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
        private static readonly string[] LimitProperties = { "maximumRowsPerSide", "maximumInputCharactersPerSide", "maximumResults", "maximumDifferenceDetails" };

        /// <summary>Parses <paramref name="json"/>. Returns the definition, or null when there is any finding (see <paramref name="findings"/>).</summary>
        internal static ReconciliationDefinition Parse(string json, DefinitionFindings findings)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
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

                var definition = new ReconciliationDefinition();
                var props = ReadObject(root, "$", RootProperties, null, findings);

                ReadVersion(props, findings);
                ReadKeys(props, definition, findings);
                ReadComparisons(props, definition, findings);
                ReadLimits(props, definition, findings);

                return findings.Total == 0 ? definition : null;
            }
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

        private static void ReadKeys(Dictionary<string, JsonElement> props, ReconciliationDefinition definition, DefinitionFindings findings)
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
                // Entries past the limit are still checked in full (shape, properties, names, pointers) so the report stays complete;
                // they are just never added to the definition. The limit itself is reported once.
                bool overflow = definition.Keys.Count >= ReconciliationDefinition.MaxKeys;
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
                string name = ReadString(p, "name", path, true, findings);
                string left = ReadString(p, "leftPointer", path, true, findings);
                string right = ReadString(p, "rightPointer", path, true, findings);
                bool trim = ReadBool(p, "trim", path, findings);
                bool ignoreCase = ReadBool(p, "ignoreCase", path, findings);
                if (name == null || left == null || right == null) continue;

                var probe = definition.Clone();
                Finding f = probe.TryAddKey(name, left, right, trim, ignoreCase, enforceLimit: !overflow);
                if (f != null) findings.Add(path + "." + f.Path, f.Code, f.Message);
                else if (!overflow) definition.Keys.Add(probe.Keys[probe.Keys.Count - 1]);
            }
        }

        private static void ReadComparisons(Dictionary<string, JsonElement> props, ReconciliationDefinition definition, DefinitionFindings findings)
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
                bool overflow = definition.Comparisons.Count >= ReconciliationDefinition.MaxComparisons;
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

                // The kind decides which options are allowed, so read it first from the raw object. Absent, wrongly typed and
                // unsupported are three different problems and are reported as such.
                bool hasKind = item.TryGetProperty("kind", out JsonElement kindElement);
                string kindText = hasKind && kindElement.ValueKind == JsonValueKind.String ? kindElement.GetString() : null;
                string[] allowed;
                RuleKind kind;
                if (kindText == "Text") { kind = RuleKind.Text; allowed = TextProperties; }
                else if (kindText == "Decimal") { kind = RuleKind.Decimal; allowed = DecimalProperties; }
                else
                {
                    if (!hasKind) findings.Add(path + ".kind", "MissingProperty", "kind is required: Text or Decimal");
                    else if (kindElement.ValueKind != JsonValueKind.String) findings.Add(path + ".kind", "InvalidType", "kind must be a string: Text or Decimal");
                    else findings.Add(path + ".kind", "UnknownKind", "the kind '" + kindText + "' is not supported by this version; use Text or Decimal");
                    // Still check the rest against the options every comparison has, so a repeated or misspelled property is reported too.
                    ReadObject(item, path, CommonComparisonProperties, null, findings);
                    continue;
                }

                var p = ReadObject(item, path, allowed, kind.ToString(), findings);
                string name = ReadString(p, "name", path, true, findings);
                string left = ReadString(p, "leftPointer", path, true, findings);
                string right = ReadString(p, "rightPointer", path, true, findings);
                ComparisonNullPolicy policy = ReadNullPolicy(p, path, findings);
                bool trim = kind == RuleKind.Text && ReadBool(p, "trim", path, findings);
                bool ignoreCase = kind == RuleKind.Text && ReadBool(p, "ignoreCase", path, findings);
                string tolerance = kind == RuleKind.Decimal ? ReadTolerance(p, path, findings) : null;
                if (name == null || left == null || right == null || (kind == RuleKind.Decimal && tolerance == null)) continue;

                var probe = definition.Clone();
                Finding f = kind == RuleKind.Text
                    ? probe.TryAddText(name, left, right, trim, ignoreCase, policy, enforceLimit: !overflow)
                    : probe.TryAddDecimal(name, left, right, tolerance, policy, enforceLimit: !overflow);
                if (f != null) findings.Add(path + "." + f.Path, f.Code, f.Message);
                else if (!overflow) definition.Comparisons.Add(probe.Comparisons[probe.Comparisons.Count - 1]);
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
        /// Collects an object's properties (first occurrence wins), reporting repeated names and, when <paramref name="allowed"/> is
        /// given, names that do not belong. <paramref name="kind"/> only sharpens the wording for a rule kind.
        /// </summary>
        private static Dictionary<string, JsonElement> ReadObject(JsonElement obj, string path, string[] allowed, string kind, DefinitionFindings findings)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in obj.EnumerateObject())
            {
                if (result.ContainsKey(property.Name))
                {
                    findings.Add(path + "." + property.Name, "DuplicateProperty", "the property '" + property.Name + "' appears more than once");
                    continue;
                }
                result[property.Name] = property.Value;
                if (allowed != null && !allowed.Contains(property.Name))
                {
                    string where = kind == null ? "here" : "in a " + kind + " comparison";
                    findings.Add(path + "." + property.Name, "UnknownProperty", "'" + property.Name + "' is not an option " + where + " (expected: " + string.Join(", ", allowed) + ")");
                }
            }
            return result;
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
