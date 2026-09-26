using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ReconciliationAutomation
{
    /// <summary>The findings of parsing a definition, bounded so a hostile document cannot produce an unbounded report.</summary>
    internal sealed class DefinitionFindings
    {
        internal const int MaxReported = 100;

        private readonly List<Finding> reported = new List<Finding>();

        /// <summary>Every problem found, including those beyond the reported ones.</summary>
        internal int Total { get; private set; }

        internal IReadOnlyList<Finding> Reported => reported;

        internal bool Truncated => Total > reported.Count;

        internal void Add(string path, string code, string message)
        {
            Total++;
            if (reported.Count < MaxReported) reported.Add(new Finding(path, code, message));
        }

        internal void Add(Finding finding)
        {
            if (finding != null) Add(finding.Path, finding.Code, finding.Message);
        }
    }

    /// <summary>
    /// Parses and validates a definition from JSON, collecting every problem rather than stopping at the first. A definition with
    /// any problem yields no result. Strict on purpose: unknown properties, repeated properties, wrong types, numeric enum
    /// values and options that do not belong to a rule kind are all errors, because a silently ignored misspelling would be a
    /// plausible-looking wrong configuration.
    /// </summary>
    internal static class DefinitionParser
    {
        private static readonly string[] RootProperties = { "schemaVersion", "keys", "comparisons", "limits" };
        private static readonly string[] KeyProperties = { "name", "leftPointer", "rightPointer", "trim", "ignoreCase" };
        /// <summary>The options every comparison has, whatever its kind: used to check a comparison whose kind cannot be selected.</summary>
        private static readonly string[] CommonComparisonProperties = { "name", "kind", "leftPointer", "rightPointer", "nullPolicy" };
        private static readonly string[] TextProperties = { "name", "kind", "leftPointer", "rightPointer", "trim", "ignoreCase", "nullPolicy" };
        private static readonly string[] DecimalProperties = { "name", "kind", "leftPointer", "rightPointer", "absoluteTolerance", "nullPolicy" };
        private static readonly string[] BooleanProperties = { "name", "kind", "leftPointer", "rightPointer", "nullPolicy" };
        private static readonly string[] MoneyProperties = { "name", "kind", "leftPointer", "rightPointer", "leftCurrencyPointer", "rightCurrencyPointer", "absoluteTolerance", "nullPolicy" };
        private static readonly string[] CalendarDateProperties = { "name", "kind", "leftPointer", "rightPointer", "leftFormat", "rightFormat", "toleranceDays", "nullPolicy" };
        private static readonly string[] InstantProperties = { "name", "kind", "leftPointer", "rightPointer", "toleranceSeconds", "nullPolicy" };
        private static readonly string[] LimitProperties = { "maximumRowsPerSide", "maximumInputCharactersPerSide", "maximumResults", "maximumDifferenceDetails" };

        /// <summary>Parses <paramref name="json"/>. Returns the definition, or null when there is any finding (see <paramref name="findings"/>).</summary>
        internal static ReconciliationDefinition Parse(string json, DefinitionFindings findings)
        {
            // Text with an unpaired surrogate character cannot be read reliably (the JSON parser throws), so it is a finding, not a crash.
            if (TextCheck.HasUnpairedSurrogate(json))
            {
                findings.Add("$", "InvalidText", "the definition contains text that is not valid (an unpaired surrogate character)");
                return null;
            }

            // The same depth bound as input documents (64 levels, the plan's fixed JSON depth). A real definition is at most three
            // levels deep, so this only ever trips on hostile or broken text, and it gets its own finding instead of "malformed".
            if (ExceedsDepth(json))
            {
                findings.Add("$", "DepthLimit", "the definition is nested deeper than the limit of " + JsonInput.MaxDepth + " levels");
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = JsonInput.MaxDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            }
            catch (JsonException ex)
            {
                string where = ex.LineNumber.HasValue && ex.BytePositionInLine.HasValue ? " at line " + (ex.LineNumber.Value + 1) + ", byte " + (ex.BytePositionInLine.Value + 1) : string.Empty;
                findings.Add("$", "MalformedJson", "the definition is not valid JSON" + where);
                return null;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    findings.Add("$", "NotAnObject", "the definition must be a JSON object");
                    return null;
                }

                // Every repeated property name anywhere in the document (including inside values the schema does not know) and any text
                // that cannot be decoded are found first, in one pass, with their paths. The schema readers below keep the first
                // occurrence of a repeated name and do not report repeats again.
                if (!ScanDocument(json, findings)) return null;

                var definition = new ReconciliationDefinition();
                var props = ReadObject(root, "$", RootProperties, null, findings);

                // One set of names for the whole document: keys and comparisons share a namespace.
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ReadVersion(props, findings);
                ReadKeys(props, definition, seenNames, findings);
                ReadComparisons(props, definition, seenNames, findings);
                ReadLimits(props, definition, findings);

                return findings.Total == 0 ? definition : null;
            }
        }

        private sealed class ScanFrame
        {
            internal bool IsArray;
            internal string Path;              // "$" for the root, otherwise the path of this object/array
            internal HashSet<string> Names;    // objects only
            internal string CurrentName;       // objects only: the property whose value is being read
            internal int NextIndex;            // arrays only
        }

        /// <summary>
        /// Walks the whole (already syntactically valid) document once. Reports <c>DuplicateProperty</c> for every repeated name in every
        /// object, wherever it is, and <c>InvalidText</c> (and stops) for a string or name that is not valid text: an escaped lone
        /// surrogate such as <c>\uD800</c> is legal JSON syntax but cannot be turned into a string. Returns false when it must stop.
        /// </summary>
        private static bool ScanDocument(string json, DefinitionFindings findings)
        {
            var stack = new Stack<ScanFrame>();
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json),
                new JsonReaderOptions { MaxDepth = JsonInput.MaxDepth + 1, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            string decoding = "$";                 // where the token being decoded is, kept current so a decoding failure can name it
            bool decodingAName = false;
            try
            {
                while (reader.Read())
                {
                    ScanFrame top = stack.Count > 0 ? stack.Peek() : null;
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                        {
                            decoding = top.Path;      // the name itself cannot be decoded, so point at the object that holds it
                            decodingAName = true;
                            string name = reader.GetString();
                            top.CurrentName = name;
                            if (!top.Names.Add(name))
                                findings.Add(top.Path == "$" ? "$." + name : top.Path + "." + name, "DuplicateProperty", "the property '" + name + "' appears more than once");
                            break;
                        }
                        case JsonTokenType.StartObject:
                        case JsonTokenType.StartArray:
                            stack.Push(new ScanFrame
                            {
                                IsArray = reader.TokenType == JsonTokenType.StartArray,
                                Path = ChildPath(top),
                                Names = reader.TokenType == JsonTokenType.StartObject ? new HashSet<string>(StringComparer.Ordinal) : null
                            });
                            break;
                        case JsonTokenType.EndObject:
                        case JsonTokenType.EndArray:
                            stack.Pop();
                            if (stack.Count > 0 && stack.Peek().IsArray) stack.Peek().NextIndex++;
                            break;
                        case JsonTokenType.String:
                            decoding = ChildPath(top);
                            decodingAName = false;
                            reader.GetString(); // throws InvalidOperationException for text that is not valid UTF-16
                            if (top != null && top.IsArray) top.NextIndex++;
                            break;
                        default:
                            if (top != null && top.IsArray) top.NextIndex++;
                            break;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                findings.Add(decoding, "InvalidText", decodingAName
                    ? "a property name in this object is not valid text (an unpaired surrogate escape such as \\uD800)"
                    : "the value is not valid text (an unpaired surrogate escape such as \\uD800)");
                return false;
            }
            catch (JsonException)
            {
                // cannot happen for a document the parser already accepted; nothing more to report here
            }
            return true;
        }

        private static string ChildPath(ScanFrame parent)
        {
            if (parent == null) return "$";
            if (parent.IsArray) return (parent.Path == "$" ? string.Empty : parent.Path) + "[" + parent.NextIndex + "]";
            return parent.Path == "$" ? parent.CurrentName : parent.Path + "." + parent.CurrentName;
        }

        /// <summary>
        /// True when the text nests objects/arrays deeper than <see cref="JsonInput.MaxDepth"/>. Decided by token depth, never by the
        /// parser's (localized) exception text. Text that is malformed in some other way returns false here and is reported by the parse.
        /// </summary>
        private static bool ExceedsDepth(string json)
        {
            try
            {
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json),
                    new JsonReaderOptions { MaxDepth = JsonInput.MaxDepth + 1, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
                while (reader.Read())
                    if ((reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray) && reader.CurrentDepth + 1 > JsonInput.MaxDepth)
                        return true;
            }
            catch (JsonException) { /* malformed: left for the parse to report */ }
            return false;
        }

        // ------------------------------------------------------------------ sections

        private static void ReadVersion(Dictionary<string, JsonElement> props, DefinitionFindings findings)
        {
            if (!props.TryGetValue("schemaVersion", out JsonElement v))
            {
                findings.Add("schemaVersion", "MissingProperty", "schemaVersion is required (the current version is " + ReconciliationDefinition.SchemaVersion + ")");
                return;
            }
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int version))
            {
                findings.Add("schemaVersion", "InvalidType", "schemaVersion must be the number " + ReconciliationDefinition.SchemaVersion);
                return;
            }
            if (version != ReconciliationDefinition.SchemaVersion)
                findings.Add("schemaVersion", "UnsupportedVersion", "schemaVersion " + version + " is not supported; this component reads version " + ReconciliationDefinition.SchemaVersion);
        }

        /// <summary>The fields every entry has, read and checked independently of one another so each problem is reported.</summary>
        private sealed class CommonFields
        {
            internal string Name, Left, Right;
            internal string[] LeftSegments, RightSegments;
            /// <summary>True when name and both pointers are present and valid (a duplicate name counts as invalid).</summary>
            internal bool Ok;
        }

        /// <summary>
        /// Reads name, leftPointer and rightPointer and checks each on its own. <paramref name="seen"/> holds every well-formed name met so far,
        /// whether or not its entry was otherwise valid or fell past a limit, so a later entry that reuses one is always reported.
        /// </summary>
        private static CommonFields ReadCommon(Dictionary<string, JsonElement> p, string path, HashSet<string> seen, DefinitionFindings findings)
        {
            var fields = new CommonFields
            {
                Name = ReadString(p, "name", path, true, findings),
                Left = ReadString(p, "leftPointer", path, true, findings),
                Right = ReadString(p, "rightPointer", path, true, findings)
            };
            fields.Ok = fields.Name != null && fields.Left != null && fields.Right != null;

            if (fields.Name != null)
            {
                Finding f = ReconciliationDefinition.CheckName(fields.Name, seen, path + ".name");
                if (f != null) { findings.Add(f); fields.Ok = false; }
                if (!string.IsNullOrWhiteSpace(fields.Name) && fields.Name.Length <= ReconciliationDefinition.MaxNameLength) seen.Add(fields.Name);
            }
            if (fields.Left != null)
            {
                Finding f = ReconciliationDefinition.CheckPointer(fields.Left, path + ".leftPointer", out fields.LeftSegments);
                if (f != null) { findings.Add(f); fields.Ok = false; }
            }
            if (fields.Right != null)
            {
                Finding f = ReconciliationDefinition.CheckPointer(fields.Right, path + ".rightPointer", out fields.RightSegments);
                if (f != null) { findings.Add(f); fields.Ok = false; }
            }
            return fields;
        }

        private static void ReadKeys(Dictionary<string, JsonElement> props, ReconciliationDefinition definition, HashSet<string> seen, DefinitionFindings findings)
        {
            if (!props.TryGetValue("keys", out JsonElement keys)) return;
            if (keys.ValueKind != JsonValueKind.Array)
            {
                findings.Add("keys", "InvalidType", "keys must be an array");
                return;
            }
            int index = 0;
            bool reportedOverflow = false;
            foreach (JsonElement item in keys.EnumerateArray())
            {
                string path = "keys[" + index++ + "]";
                // Entries past the limit are still checked in full so the report stays complete; they are just never added to
                // the definition. The limit itself is reported once.
                // Counted by position in the array, not by how many entries were valid: an invalid early entry must not let an
                // over-long list escape the limit (the limit would only appear once the invalid entry was fixed).
                bool overflow = index > ReconciliationDefinition.MaxKeys;
                if (overflow && !reportedOverflow)
                {
                    findings.Add("keys", "TooManyKeys", "a definition may have at most " + ReconciliationDefinition.MaxKeys + " key mappings");
                    reportedOverflow = true;
                }
                if (item.ValueKind != JsonValueKind.Object)
                {
                    findings.Add(path, "InvalidType", "each key mapping must be an object");
                    continue;
                }
                var p = ReadObject(item, path, KeyProperties, null, findings);
                CommonFields common = ReadCommon(p, path, seen, findings);
                bool trim = ReadBool(p, "trim", path, findings);
                bool ignoreCase = ReadBool(p, "ignoreCase", path, findings);
                if (!common.Ok || overflow) continue;

                definition.Keys.Add(new KeyMappingDef
                {
                    Name = common.Name, LeftPointer = common.Left, RightPointer = common.Right,
                    LeftSegments = common.LeftSegments, RightSegments = common.RightSegments,
                    Trim = trim, IgnoreCase = ignoreCase
                });
            }
        }

        private static void ReadComparisons(Dictionary<string, JsonElement> props, ReconciliationDefinition definition, HashSet<string> seen, DefinitionFindings findings)
        {
            if (!props.TryGetValue("comparisons", out JsonElement comparisons)) return;
            if (comparisons.ValueKind != JsonValueKind.Array)
            {
                findings.Add("comparisons", "InvalidType", "comparisons must be an array");
                return;
            }
            int index = 0;
            bool reportedOverflow = false;
            foreach (JsonElement item in comparisons.EnumerateArray())
            {
                string path = "comparisons[" + index++ + "]";
                bool overflow = index > ReconciliationDefinition.MaxComparisons;
                if (overflow && !reportedOverflow)
                {
                    findings.Add("comparisons", "TooManyComparisons", "a definition may have at most " + ReconciliationDefinition.MaxComparisons + " comparisons");
                    reportedOverflow = true;
                }
                if (item.ValueKind != JsonValueKind.Object)
                {
                    findings.Add(path, "InvalidType", "each comparison must be an object");
                    continue;
                }

                // The kind decides which options are allowed, so read it first. It comes from the same collected properties as every other
                // field (first occurrence wins; a repeated kind is reported), never from a separate lookup that could pick another
                // occurrence. Absent, wrongly typed and unsupported are three different problems and are reported as such.
                Dictionary<string, JsonElement> raw = Collect(item, path, findings);
                bool hasKind = raw.TryGetValue("kind", out JsonElement kindElement);
                string kindText = hasKind && kindElement.ValueKind == JsonValueKind.String ? kindElement.GetString() : null;
                string[] allowed;
                RuleKind kind;
                if (kindText == "Text") { kind = RuleKind.Text; allowed = TextProperties; }
                else if (kindText == "Decimal") { kind = RuleKind.Decimal; allowed = DecimalProperties; }
                else if (kindText == "Boolean") { kind = RuleKind.Boolean; allowed = BooleanProperties; }
                else if (kindText == "Money") { kind = RuleKind.Money; allowed = MoneyProperties; }
                else if (kindText == "CalendarDate") { kind = RuleKind.CalendarDate; allowed = CalendarDateProperties; }
                else if (kindText == "Instant") { kind = RuleKind.Instant; allowed = InstantProperties; }
                else
                {
                    if (!hasKind) findings.Add(path + ".kind", "MissingProperty", "kind is required: Text, Decimal, Boolean, Money, CalendarDate or Instant");
                    else if (kindElement.ValueKind != JsonValueKind.String) findings.Add(path + ".kind", "InvalidType", "kind must be a string: Text, Decimal, Boolean, Money, CalendarDate or Instant");
                    else findings.Add(path + ".kind", "UnknownKind", "the kind '" + kindText + "' is not supported by this version; use Text, Decimal, Boolean, Money, CalendarDate or Instant");
                    // The kind cannot be selected, but the fields every comparison has can still be checked, so those problems are
                    // reported too (and the name still counts for duplicate detection).
                    RejectUnknown(raw, path, CommonComparisonProperties, null, findings);
                    ReadCommon(raw, path, seen, findings);
                    ReadNullPolicy(raw, path, findings);
                    continue;
                }

                RejectUnknown(raw, path, allowed, kind.ToString(), findings);
                var p = raw;
                CommonFields fields = ReadCommon(p, path, seen, findings);
                ComparisonNullPolicy policy = ReadNullPolicy(p, path, findings);
                bool trim = kind == RuleKind.Text && ReadBool(p, "trim", path, findings);
                bool ignoreCase = kind == RuleKind.Text && ReadBool(p, "ignoreCase", path, findings);
                bool hasTolerance = kind == RuleKind.Decimal || kind == RuleKind.Money;
                string tolerance = hasTolerance ? ReadTolerance(p, path, findings) : null;
                string[] leftCurrency = null, rightCurrency = null;
                string leftCurrencyPointer = null, rightCurrencyPointer = null;
                bool currencyOk = true;
                if (kind == RuleKind.Money)
                {
                    currencyOk &= ReadPointer(p, "leftCurrencyPointer", path, findings, out leftCurrencyPointer, out leftCurrency);
                    currencyOk &= ReadPointer(p, "rightCurrencyPointer", path, findings, out rightCurrencyPointer, out rightCurrency);
                }
                string leftFormat = DateCore.DefaultFormat, rightFormat = DateCore.DefaultFormat;
                int dateTolerance = 0;
                bool dateOk = true;
                if (kind == RuleKind.CalendarDate)
                {
                    dateOk &= ReadFormat(p, "leftFormat", path, findings, out leftFormat);
                    dateOk &= ReadFormat(p, "rightFormat", path, findings, out rightFormat);
                    dateOk &= ReadDateTolerance(p, "toleranceDays", path, findings, out dateTolerance);
                }
                else if (kind == RuleKind.Instant)
                {
                    dateOk &= ReadDateTolerance(p, "toleranceSeconds", path, findings, out dateTolerance);
                }
                if (!fields.Ok || overflow || (hasTolerance && tolerance == null) || !currencyOk || !dateOk) continue;

                definition.Comparisons.Add(new ComparisonDef
                {
                    Name = fields.Name, Kind = kind, LeftPointer = fields.Left, RightPointer = fields.Right,
                    LeftSegments = fields.LeftSegments, RightSegments = fields.RightSegments,
                    LeftCurrencyPointer = leftCurrencyPointer, RightCurrencyPointer = rightCurrencyPointer,
                    LeftCurrencySegments = leftCurrency, RightCurrencySegments = rightCurrency,
                    LeftFormat = leftFormat, RightFormat = rightFormat, DateTolerance = dateTolerance,
                    Trim = trim, IgnoreCase = ignoreCase,
                    AbsoluteTolerance = tolerance ?? "0", NullPolicy = policy
                });
            }
        }

        private static void ReadLimits(Dictionary<string, JsonElement> props, ReconciliationDefinition definition, DefinitionFindings findings)
        {
            if (!props.TryGetValue("limits", out JsonElement limits)) return;
            if (limits.ValueKind != JsonValueKind.Object)
            {
                findings.Add("limits", "InvalidType", "limits must be an object");
                return;
            }
            var p = ReadObject(limits, "limits", LimitProperties, null, findings);
            ReadLimit(p, "maximumRowsPerSide", ReconciliationLimits.MaxRowsPerSide, v => definition.Limits.MaximumRowsPerSide = v, findings);
            ReadLimit(p, "maximumInputCharactersPerSide", ReconciliationLimits.MaxInputCharactersPerSide, v => definition.Limits.MaximumInputCharactersPerSide = v, findings);
            ReadLimit(p, "maximumResults", ReconciliationLimits.MaxResults, v => definition.Limits.MaximumResults = v, findings);
            ReadLimit(p, "maximumDifferenceDetails", ReconciliationLimits.MaxDifferenceDetails, v => definition.Limits.MaximumDifferenceDetails = v, findings);
        }

        private static void ReadLimit(Dictionary<string, JsonElement> p, string name, int maximum, Action<int> set, DefinitionFindings findings)
        {
            if (!p.TryGetValue(name, out JsonElement v)) return;
            string path = "limits." + name;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int value))
            {
                findings.Add(path, "InvalidType", name + " must be a whole number");
                return;
            }
            string problem = ReconciliationLimits.Check(name, value, maximum);
            if (problem != null) findings.Add(path, "InvalidLimit", problem);
            else set(value);
        }

        // ------------------------------------------------------------------ typed readers

        /// <summary>
        /// Collects an object's properties (first occurrence wins) and, when <paramref name="allowed"/> is given, reports names that do not belong. <paramref name="kind"/> only sharpens the wording for a rule kind.
        /// </summary>
        private static Dictionary<string, JsonElement> ReadObject(JsonElement obj, string path, string[] allowed, string kind, DefinitionFindings findings)
        {
            Dictionary<string, JsonElement> result = Collect(obj, path, findings);
            if (allowed != null) RejectUnknown(result, path, allowed, kind, findings);
            return result;
        }

        /// <summary>
        /// The properties of an object with the FIRST occurrence of each name. Repeated names are reported by <see cref="ScanDocument"/>,
        /// which covers every object in the document, so they are not reported again here.
        /// </summary>
        private static Dictionary<string, JsonElement> Collect(JsonElement obj, string path, DefinitionFindings findings)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in obj.EnumerateObject())
                if (!result.ContainsKey(property.Name)) result[property.Name] = property.Value;
            return result;
        }

        private static void RejectUnknown(Dictionary<string, JsonElement> properties, string path, string[] allowed, string kind, DefinitionFindings findings)
        {
            foreach (string name in properties.Keys)
            {
                if (allowed.Contains(name)) continue;
                string where = kind == null ? "here" : "in a " + kind + " comparison";
                findings.Add(path + "." + name, "UnknownProperty", "'" + name + "' is not an option " + where + " (expected: " + string.Join(", ", allowed) + ")");
            }
        }

        private static string ReadString(Dictionary<string, JsonElement> p, string name, string path, bool required, DefinitionFindings findings)
        {
            if (!p.TryGetValue(name, out JsonElement v))
            {
                if (required) findings.Add(path + "." + name, "MissingProperty", name + " is required");
                return null;
            }
            if (v.ValueKind != JsonValueKind.String)
            {
                findings.Add(path + "." + name, "InvalidType", name + " must be a string");
                return null;
            }
            return v.GetString();
        }

        private static bool ReadBool(Dictionary<string, JsonElement> p, string name, string path, DefinitionFindings findings)
        {
            if (!p.TryGetValue(name, out JsonElement v)) return false; // omitted options take their default: false
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            findings.Add(path + "." + name, "InvalidType", name + " must be true or false");
            return false;
        }

        private static ComparisonNullPolicy ReadNullPolicy(Dictionary<string, JsonElement> p, string path, DefinitionFindings findings)
        {
            if (!p.TryGetValue("nullPolicy", out JsonElement v)) return ComparisonNullPolicy.RequireValue;
            string text = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (text == nameof(ComparisonNullPolicy.RequireValue)) return ComparisonNullPolicy.RequireValue;
            if (text == nameof(ComparisonNullPolicy.AllowBothNull)) return ComparisonNullPolicy.AllowBothNull;
            findings.Add(path + ".nullPolicy", "UnknownEnumValue", "nullPolicy must be the string RequireValue or AllowBothNull (numbers are not accepted)");
            return ComparisonNullPolicy.RequireValue;
        }

        /// <summary>Reads a required pointer property. False (with a finding) when it is absent, not a string, or not a valid pointer.</summary>
        private static bool ReadPointer(Dictionary<string, JsonElement> p, string name, string path, DefinitionFindings findings, out string pointer, out string[] segments)
        {
            segments = null;
            pointer = ReadString(p, name, path, true, findings);
            if (pointer == null) return false;
            Finding f = ReconciliationDefinition.CheckPointer(pointer, path + "." + name, out segments);
            if (f != null) { findings.Add(f); return false; }
            return true;
        }

        /// <summary>Reads an optional date format (default yyyy-MM-dd). False, with a finding, when it is not a string or not an allowed format.</summary>
        private static bool ReadFormat(Dictionary<string, JsonElement> p, string name, string path, DefinitionFindings findings, out string format)
        {
            format = DateCore.DefaultFormat;
            if (!p.TryGetValue(name, out JsonElement v)) return true;
            if (v.ValueKind != JsonValueKind.String) { findings.Add(path + "." + name, "InvalidType", name + " must be a string, for example yyyy-MM-dd"); return false; }
            string text = v.GetString();
            Finding f = ReconciliationDefinition.CheckFormat(text, path + "." + name);
            if (f != null) { findings.Add(f); return false; }
            format = text;
            return true;
        }

        /// <summary>Reads an optional whole-number tolerance (default 0). False, with a finding, when it is not a whole number of at least 0.</summary>
        private static bool ReadDateTolerance(Dictionary<string, JsonElement> p, string name, string path, DefinitionFindings findings, out int tolerance)
        {
            tolerance = 0;
            if (!p.TryGetValue(name, out JsonElement v)) return true;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int value)) { findings.Add(path + "." + name, "InvalidType", name + " must be a whole number of at least 0"); return false; }
            Finding f = ReconciliationDefinition.CheckDateTolerance(value, path + "." + name);
            if (f != null) { findings.Add(f); return false; }
            tolerance = value;
            return true;
        }

        private static string ReadTolerance(Dictionary<string, JsonElement> p, string path, DefinitionFindings findings)
        {
            if (!p.TryGetValue("absoluteTolerance", out JsonElement v)) return "0"; // omitted: an exact comparison
            if (v.ValueKind != JsonValueKind.String)
            {
                findings.Add(path + ".absoluteTolerance", "InvalidType", "absoluteTolerance must be a string of invariant decimal text, for example \"0.01\"");
                return null;
            }
            Finding f = ReconciliationDefinition.CheckTolerance(v.GetString(), path + ".absoluteTolerance");
            if (f != null)
            {
                findings.Add(f);
                return null;
            }
            return v.GetString();
        }
    }
}
