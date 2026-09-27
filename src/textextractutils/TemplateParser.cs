using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TextExtractAutomation
{
    /// <summary>The findings of parsing a template, bounded so a hostile document cannot produce an unbounded report.</summary>
    internal sealed class TemplateFindings
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
    /// Parses and validates a template from JSON, collecting every problem rather than stopping at the first. A template with any problem yields no
    /// result. Strict on purpose (the rules and helpers are those of ReconciliationUtils' definition parser): unknown properties, repeated properties,
    /// wrong types, numeric enum values and options that do not belong to a field kind are all errors, because a silently ignored misspelling would
    /// be a plausible-looking wrong configuration.
    /// </summary>
    internal static class TemplateParser
    {
        internal const int MaxDepth = 64;

        private static readonly string[] RootProperties = { "schemaVersion", "fields", "limits" };
        private static readonly string[] CommonFieldProperties = { "name", "kind", "type", "decimalStyle", "dateFormats", "required", "occurrence" };
        private static readonly string[] LabelFieldProperties = { "name", "kind", "labels", "position", "type", "decimalStyle", "dateFormats", "required", "occurrence" };
        private static readonly string[] PatternFieldProperties = { "name", "kind", "pattern", "type", "decimalStyle", "dateFormats", "required", "occurrence" };
        private static readonly string[] LimitProperties = { "maximumTextCharacters" };

        /// <summary>Parses <paramref name="json"/>. Returns the template, or null when there is any finding (see <paramref name="findings"/>).</summary>
        internal static Template Parse(string json, TemplateFindings findings)
        {
            // Text with an unpaired surrogate character cannot be read reliably (the JSON parser throws), so it is a finding, not a crash.
            if (TextCheck.HasUnpairedSurrogate(json))
            {
                findings.Add("$", "InvalidText", "the template contains text that is not valid (an unpaired surrogate character)");
                return null;
            }
            if (ExceedsDepth(json))
            {
                findings.Add("$", "DepthLimit", "the template is nested deeper than the limit of " + MaxDepth + " levels");
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            }
            catch (JsonException ex)
            {
                string where = ex.LineNumber.HasValue && ex.BytePositionInLine.HasValue ? " at line " + (ex.LineNumber.Value + 1) + ", byte " + (ex.BytePositionInLine.Value + 1) : string.Empty;
                findings.Add("$", "MalformedJson", "the template is not valid JSON" + where);
                return null;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    findings.Add("$", "NotAnObject", "the template must be a JSON object");
                    return null;
                }
                if (!ScanDocument(json, findings)) return null;

                var template = new Template();
                var props = ReadObject(root, "$", RootProperties, null, findings);
                ReadVersion(props, findings);
                ReadFields(props, template, findings);
                ReadLimits(props, template, findings);

                if (findings.Total != 0) return null;
                Finding tooLarge = template.CheckCanonicalSize();          // an accepted template must also fit once it is saved again
                if (tooLarge != null) { findings.Add(tooLarge); return null; }
                return template;
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
        private static bool ScanDocument(string json, TemplateFindings findings)
        {
            var stack = new Stack<ScanFrame>();
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json),
                new JsonReaderOptions { MaxDepth = MaxDepth + 1, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
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
        /// True when the text nests objects/arrays deeper than <see cref="MaxDepth"/>. Decided by token depth, never by the
        /// parser's (localized) exception text. Text that is malformed in some other way returns false here and is reported by the parse.
        /// </summary>
        private static bool ExceedsDepth(string json)
        {
            try
            {
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json),
                    new JsonReaderOptions { MaxDepth = MaxDepth + 1, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
                while (reader.Read())
                    if ((reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray) && reader.CurrentDepth + 1 > MaxDepth)
                        return true;
            }
            catch (JsonException) { /* malformed: left for the parse to report */ }
            return false;
        }

        // ------------------------------------------------------------------ sections

        private static void ReadVersion(Dictionary<string, JsonElement> props, TemplateFindings findings)
        {
            if (!props.TryGetValue("schemaVersion", out JsonElement v))
            {
                findings.Add("schemaVersion", "MissingProperty", "schemaVersion is required (the current version is " + Template.SchemaVersion + ")");
                return;
            }
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int version))
            {
                findings.Add("schemaVersion", "InvalidType", "schemaVersion must be the number " + Template.SchemaVersion);
                return;
            }
            if (version != Template.SchemaVersion)
                findings.Add("schemaVersion", "UnsupportedVersion", "schemaVersion " + version + " is not supported; this version reads " + Template.SchemaVersion);
        }

        private static void ReadFields(Dictionary<string, JsonElement> props, Template template, TemplateFindings findings)
        {
            if (!props.TryGetValue("fields", out JsonElement fields)) return;
            if (fields.ValueKind != JsonValueKind.Array)
            {
                findings.Add("fields", "InvalidType", "fields must be an array");
                return;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            bool reportedOverflow = false;
            foreach (JsonElement item in fields.EnumerateArray())
            {
                string path = "fields[" + index++ + "]";
                bool overflow = index > TemplateLimits.MaxFields;
                if (overflow && !reportedOverflow)
                {
                    findings.Add("fields", "TooManyFields", "a template may have at most " + TemplateLimits.MaxFields + " fields");
                    reportedOverflow = true;
                }
                if (item.ValueKind != JsonValueKind.Object)
                {
                    findings.Add(path, "InvalidType", "each field must be an object");
                    continue;
                }

                Dictionary<string, JsonElement> p = Collect(item);
                bool hasKind = p.TryGetValue("kind", out JsonElement kindElement);
                string kindText = hasKind && kindElement.ValueKind == JsonValueKind.String ? kindElement.GetString() : null;
                FieldKind kind;
                if (kindText == "Label") { kind = FieldKind.Label; RejectUnknown(p, path, LabelFieldProperties, "Label", findings); }
                else if (kindText == "Pattern") { kind = FieldKind.Pattern; RejectUnknown(p, path, PatternFieldProperties, "Pattern", findings); }
                else
                {
                    if (!hasKind) findings.Add(path + ".kind", "MissingProperty", "kind is required: Label or Pattern");
                    else if (kindElement.ValueKind != JsonValueKind.String) findings.Add(path + ".kind", "InvalidType", "kind must be a string: Label or Pattern");
                    else findings.Add(path + ".kind", "UnknownKind", "the kind '" + kindText + "' is not supported by this version; use Label or Pattern");
                    RejectUnknown(p, path, CommonFieldProperties, null, findings);
                    ReadName(p, path, seen, findings);
                    continue;
                }

                string name = ReadName(p, path, seen, findings);
                bool ok = name != null;
                string[] labels = null;
                Regex regex = null;
                string pattern = null;
                ValuePosition position = ValuePosition.SameLine;
                if (kind == FieldKind.Label)
                {
                    ok &= ReadLabels(p, path, findings, out labels);
                    ok &= ReadEnum(p, "position", path, ValuePosition.SameLine, findings, out position);
                }
                else
                {
                    pattern = ReadString(p, "pattern", path, true, findings);
                    if (pattern == null) ok = false;
                    else
                    {
                        Finding f = Template.CompilePattern(pattern, path + ".pattern", out regex);
                        if (f != null) { findings.Add(f); ok = false; }
                    }
                }

                FieldType type = FieldType.Text;
                bool typeOk = false;
                if (!p.ContainsKey("type")) findings.Add(path + ".type", "MissingProperty", "type is required: " + string.Join(", ", Enum.GetNames(typeof(FieldType))));
                else typeOk = ReadEnum(p, "type", path, FieldType.Text, findings, out type);
                ok &= typeOk;

                // decimalStyle belongs to Decimal, Amount and Percentage fields and dateFormats to Date fields; on any other type they are errors,
                // because an option that is silently ignored is a plausible-looking wrong configuration.
                DecimalStyle decimalStyle = DecimalStyle.DotDecimal;
                if (p.ContainsKey("decimalStyle"))
                {
                    if (typeOk && !Template.UsesDecimalStyle(type)) { findings.Add(path + ".decimalStyle", "UnknownProperty", "decimalStyle applies only to Decimal, Amount and Percentage fields"); ok = false; }
                    else ok &= ReadEnum(p, "decimalStyle", path, DecimalStyle.DotDecimal, findings, out decimalStyle);
                }
                string[] dateFormats = null;
                if (typeOk)
                {
                    if (p.ContainsKey("dateFormats") && type != FieldType.Date) { findings.Add(path + ".dateFormats", "UnknownProperty", "dateFormats applies only to Date fields"); ok = false; }
                    else if (type == FieldType.Date)
                    {
                        if (!ReadStringArray(p, "dateFormats", path, findings, out string[] given)) ok = false;
                        else
                        {
                            Finding f = Template.CheckDateFormats(type, given, path + ".dateFormats", out dateFormats);
                            if (f != null) { findings.Add(f); ok = false; }
                        }
                    }
                }
                ok &= ReadBool(p, "required", path, true, findings, out bool required);
                ok &= ReadEnum(p, "occurrence", path, Occurrence.RequireUnique, findings, out Occurrence occurrence);

                if (!ok || overflow) continue;
                template.Fields.Add(new FieldDef
                {
                    Name = name, Kind = kind, Labels = labels, Position = position, Pattern = pattern, CompiledPattern = regex,
                    Type = type, DecimalStyle = decimalStyle, DateFormats = dateFormats, Required = required, Occurrence = occurrence
                });
            }
        }

        /// <summary>Reads and checks the name. Every well-formed name counts toward duplicate detection, whether or not its field is otherwise valid.</summary>
        private static string ReadName(Dictionary<string, JsonElement> p, string path, HashSet<string> seen, TemplateFindings findings)
        {
            string name = ReadString(p, "name", path, true, findings);
            if (name == null) return null;
            Finding f = Template.CheckName(name, seen, path + ".name");
            if (!string.IsNullOrWhiteSpace(name) && name.Length <= TemplateLimits.MaxNameLength) seen.Add(name);
            if (f != null) { findings.Add(f); return null; }
            return name;
        }

        private static bool ReadLabels(Dictionary<string, JsonElement> p, string path, TemplateFindings findings, out string[] labels)
        {
            labels = null;
            if (!p.TryGetValue("labels", out JsonElement v)) { findings.Add(path + ".labels", "MissingProperty", "labels is required: an array of label texts such as [\"Invoice No\", \"Invoice Number\"]"); return false; }
            if (v.ValueKind != JsonValueKind.Array) { findings.Add(path + ".labels", "InvalidType", "labels must be an array of strings"); return false; }
            var list = new List<string>();
            int i = 0;
            foreach (JsonElement e in v.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String) { findings.Add(path + ".labels[" + i + "]", "InvalidType", "each label must be a string"); return false; }
                list.Add(e.GetString());
                i++;
            }
            Finding f = Template.CheckLabels(list, path + ".labels", out labels);
            if (f != null) { findings.Add(f); return false; }
            return true;
        }

        private static void ReadLimits(Dictionary<string, JsonElement> props, Template template, TemplateFindings findings)
        {
            if (!props.TryGetValue("limits", out JsonElement limits)) return;
            if (limits.ValueKind != JsonValueKind.Object)
            {
                findings.Add("limits", "InvalidType", "limits must be an object");
                return;
            }
            var p = ReadObject(limits, "limits", LimitProperties, null, findings);
            if (!p.TryGetValue("maximumTextCharacters", out JsonElement v)) return;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int value))
            {
                findings.Add("limits.maximumTextCharacters", "InvalidType", "maximumTextCharacters must be a whole number");
                return;
            }
            string problem = TemplateLimits.Check("maximumTextCharacters", value, TemplateLimits.MaxTextCharacters);
            if (problem != null) findings.Add("limits.maximumTextCharacters", "InvalidLimit", problem);
            else template.Limits.MaximumTextCharacters = value;
        }

        // ------------------------------------------------------------------ typed readers

        private static Dictionary<string, JsonElement> ReadObject(JsonElement obj, string path, string[] allowed, string kind, TemplateFindings findings)
        {
            Dictionary<string, JsonElement> result = Collect(obj);
            if (allowed != null) RejectUnknown(result, path, allowed, kind, findings);
            return result;
        }

        /// <summary>The properties of an object with the FIRST occurrence of each name (repeats are reported by <see cref="ScanDocument"/>).</summary>
        private static Dictionary<string, JsonElement> Collect(JsonElement obj)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in obj.EnumerateObject())
                if (!result.ContainsKey(property.Name)) result[property.Name] = property.Value;
            return result;
        }

        private static void RejectUnknown(Dictionary<string, JsonElement> properties, string path, string[] allowed, string kind, TemplateFindings findings)
        {
            foreach (string name in properties.Keys)
            {
                if (allowed.Contains(name)) continue;
                string where = kind == null ? "here" : "in a " + kind + " field";
                findings.Add((path == "$" ? "" : path + ".") + name, "UnknownProperty", "'" + name + "' is not an option " + where + " (expected: " + string.Join(", ", allowed) + ")");
            }
        }

        private static string ReadString(Dictionary<string, JsonElement> p, string name, string path, bool required, TemplateFindings findings)
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

        /// <summary>Reads an optional array of strings (absent means empty). False, with a finding, when it is not an array of strings.</summary>
        private static bool ReadStringArray(Dictionary<string, JsonElement> p, string name, string path, TemplateFindings findings, out string[] values)
        {
            values = Array.Empty<string>();
            if (!p.TryGetValue(name, out JsonElement v)) return true;
            if (v.ValueKind != JsonValueKind.Array) { findings.Add(path + "." + name, "InvalidType", name + " must be an array of strings"); return false; }
            var list = new List<string>();
            int i = 0;
            foreach (JsonElement e in v.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String) { findings.Add(path + "." + name + "[" + i + "]", "InvalidType", "each entry must be a string"); return false; }
                list.Add(e.GetString());
                i++;
            }
            values = list.ToArray();
            return true;
        }

        private static bool ReadBool(Dictionary<string, JsonElement> p, string name, string path, bool fallback, TemplateFindings findings, out bool value)
        {
            value = fallback;
            if (!p.TryGetValue(name, out JsonElement v)) return true;
            if (v.ValueKind == JsonValueKind.True) { value = true; return true; }
            if (v.ValueKind == JsonValueKind.False) { value = false; return true; }
            findings.Add(path + "." + name, "InvalidType", name + " must be true or false");
            return false;
        }

        /// <summary>Reads an optional enum given by its exact name. Numbers and unknown names are refused (a number would silently change meaning if the enum grew).</summary>
        private static bool ReadEnum<T>(Dictionary<string, JsonElement> p, string name, string path, T fallback, TemplateFindings findings, out T value) where T : struct, Enum
        {
            value = fallback;
            if (!p.TryGetValue(name, out JsonElement v)) return true;
            string text = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (text != null && Enum.GetNames(typeof(T)).Contains(text, StringComparer.Ordinal))
            {
                value = (T)Enum.Parse(typeof(T), text);
                return true;
            }
            findings.Add(path + "." + name, "UnknownEnumValue", name + " must be the string " + string.Join(", ", Enum.GetNames(typeof(T))) + " (numbers are not accepted)");
            return false;
        }
    }
}
