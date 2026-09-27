using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TextExtractAutomation
{
    /// <summary>One problem with a template: where, a stable code and an explanation that never quotes input text.</summary>
    internal sealed class Finding
    {
        internal Finding(string path, string code, string message)
        {
            Path = path;
            Code = code;
            Message = message;
        }

        internal string Path { get; }
        internal string Code { get; }
        internal string Message { get; }

        /// <summary>The finding as one sentence, for a public failure message.</summary>
        internal string Format() => Path + ": " + Message + " (" + Code + ").";
    }

    internal enum FieldKind
    {
        Label,
        Pattern
    }

    /// <summary>One field of a template. Immutable once added: templates are copied before every change, and the copies share these objects.</summary>
    internal sealed class FieldDef
    {
        internal string Name;
        internal FieldKind Kind;
        internal string[] Labels;                 // Label fields: the alternatives, trimmed, in the order given
        internal ValuePosition Position;          // Label fields
        internal string Pattern;                  // Pattern fields
        internal Regex CompiledPattern;           // Pattern fields: compiled once, culture-invariant, with a match timeout
        internal FieldType Type;
        internal DecimalStyle DecimalStyle;       // Decimal, Amount and Percentage fields
        internal string[] DateFormats;            // Date fields: the formats, tried in order (at least one)
        internal bool Required = true;
        internal Occurrence Occurrence = Occurrence.RequireUnique;
    }

    /// <summary>One column of a table: found by its header text on a header line, read as its type in every row.</summary>
    internal sealed class ColumnDef
    {
        internal string Header;                   // as written in the template; also the column's name for GetRowValue
        internal FieldType Type;
        internal DecimalStyle DecimalStyle;
        internal string[] DateFormats;            // Date columns only

        /// <summary>The column as a field definition, so the value types read a cell exactly as they read a field's value.</summary>
        internal FieldDef AsField() => new FieldDef { Name = Header, Kind = FieldKind.Label, Type = Type, DecimalStyle = DecimalStyle, DateFormats = DateFormats };
    }

    /// <summary>A table: rows under a header line that holds every column's header.</summary>
    internal sealed class TableDef
    {
        internal string Name;
        internal List<ColumnDef> Columns = new List<ColumnDef>();

        internal TableDef With(ColumnDef column) => new TableDef { Name = Name, Columns = new List<ColumnDef>(Columns) { column } };
    }

    /// <summary>The fields and limits that define an extraction. Copied before every change, so a rejected change leaves the current template untouched.</summary>
    internal sealed class Template
    {
        internal const int SchemaVersion = 1;

        internal List<FieldDef> Fields = new List<FieldDef>();
        internal List<TableDef> Tables = new List<TableDef>();
        internal TemplateLimits Limits = new TemplateLimits();

        internal Template Clone() => new Template { Fields = new List<FieldDef>(Fields), Tables = new List<TableDef>(Tables), Limits = Limits.Clone() };

        /// <summary>Field and table names share one namespace (ignoring case), so a result is never ambiguous.</summary>
        internal IEnumerable<string> AllNames() => Fields.Select(f => f.Name).Concat(Tables.Select(t => t.Name));

        // ------------------------------------------------------------------ shared rules (builders and JSON loading)

        internal static Finding CheckName(string name, IEnumerable<string> taken, string path)
        {
            if (string.IsNullOrWhiteSpace(name)) return new Finding(path, "InvalidName", "a name is required");
            if (name.Length > TemplateLimits.MaxNameLength) return new Finding(path, "InvalidName", "the name is longer than " + TemplateLimits.MaxNameLength + " characters");
            if (TextCheck.HasUnpairedSurrogate(name)) return new Finding(path, "InvalidName", "the name contains text that is not valid (an unpaired surrogate character)");
            if (taken.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
                return new Finding(path, "DuplicateName", "the name '" + name + "' is already used; field and table names are unique, ignoring case");
            return null;
        }

        /// <summary>Splits the builder form <c>Invoice No|Invoice Number</c> into its alternatives and checks them.</summary>
        internal static Finding SplitLabels(string labels, string path, out string[] result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(labels)) return new Finding(path, "InvalidLabel", "at least one label is required, for example Invoice No|Invoice Number");
            return CheckLabels(labels.Split('|'), path, out result);
        }

        internal static Finding CheckLabels(IReadOnlyList<string> labels, string path, out string[] result)
        {
            result = null;
            if (labels.Count == 0) return new Finding(path, "InvalidLabel", "at least one label is required");
            if (labels.Count > TemplateLimits.MaxLabelsPerField) return new Finding(path, "InvalidLabel", "a field may have at most " + TemplateLimits.MaxLabelsPerField + " labels");
            var trimmed = new List<string>();
            for (int i = 0; i < labels.Count; i++)
            {
                string label = labels[i]?.Trim();
                string where = labels.Count == 1 ? "the label" : "label " + (i + 1);
                if (string.IsNullOrEmpty(label)) return new Finding(path, "InvalidLabel", where + " is empty");
                if (label.Length > TemplateLimits.MaxLabelLength) return new Finding(path, "InvalidLabel", where + " is longer than " + TemplateLimits.MaxLabelLength + " characters");
                if (TextCheck.HasUnpairedSurrogate(label)) return new Finding(path, "InvalidLabel", where + " contains text that is not valid (an unpaired surrogate character)");
                if (label.Contains('|')) return new Finding(path, "InvalidLabel", where + " contains |, which separates alternatives");
                if (label.Any(char.IsControl)) return new Finding(path, "InvalidLabel", where + " contains a control character (a line break or tab)");
                if (!label.Any(char.IsLetterOrDigit)) return new Finding(path, "InvalidLabel", where + " has no letter or digit, so it cannot be told apart from punctuation");
                if (trimmed.Any(t => string.Equals(t, label, StringComparison.OrdinalIgnoreCase))) return new Finding(path, "InvalidLabel", where + " repeats an earlier label (ignoring case)");
                trimmed.Add(label);
            }
            result = trimmed.ToArray();
            return null;
        }

        internal static bool UsesDecimalStyle(FieldType type) => type == FieldType.Decimal || type == FieldType.Amount || type == FieldType.Percentage;

        /// <summary>
        /// Checks the date formats of a field. A Date field gets its formats (default yyyy-MM-dd when none are given); any other type must be given none,
        /// so a format meant for another field is not silently ignored.
        /// </summary>
        internal static Finding CheckDateFormats(FieldType type, IReadOnlyList<string> formats, string path, out string[] resolved)
        {
            resolved = null;
            if (type != FieldType.Date)
            {
                if (formats.Count == 0) return null;
                return new Finding(path, "InvalidFormat", "date formats apply only to Date fields; leave them empty for a " + type + " field");
            }
            if (formats.Count == 0) { resolved = new[] { DateCore.DefaultFormat }; return null; }
            if (formats.Count > TemplateLimits.MaxDateFormats) return new Finding(path, "InvalidFormat", "a Date field may have at most " + TemplateLimits.MaxDateFormats + " date formats");
            for (int i = 0; i < formats.Count; i++)
            {
                string problem = DateCore.CheckFormat(formats[i]);
                if (problem != null) return new Finding(path, "InvalidFormat", (formats.Count == 1 ? "the date format: " : "date format " + (i + 1) + ": ") + problem);
            }
            if (formats.Distinct(StringComparer.Ordinal).Count() != formats.Count) return new Finding(path, "InvalidFormat", "a date format is repeated");
            resolved = formats.ToArray();
            return null;
        }

        /// <summary>The builder form of date formats: alternatives separated by |, trimmed; null or white space means none.</summary>
        internal static string[] SplitDateFormats(string dateFormats) =>
            string.IsNullOrWhiteSpace(dateFormats) ? Array.Empty<string>() : dateFormats.Split('|').Select(f => f.Trim()).ToArray();

        /// <summary>
        /// Checks and compiles a pattern: a .NET regular expression with one named group <c>value</c>. It is compiled culture-invariant with a match
        /// timeout, so a pattern that backtracks badly on some text stops (and is reported) instead of hanging the robot.
        /// </summary>
        internal static Finding CompilePattern(string pattern, string path, out Regex regex)
        {
            regex = null;
            if (string.IsNullOrEmpty(pattern)) return new Finding(path, "InvalidPattern", "a pattern is required");
            if (pattern.Length > TemplateLimits.MaxPatternLength) return new Finding(path, "InvalidPattern", "the pattern is longer than " + TemplateLimits.MaxPatternLength + " characters");
            if (TextCheck.HasUnpairedSurrogate(pattern)) return new Finding(path, "InvalidPattern", "the pattern contains text that is not valid (an unpaired surrogate character)");
            try
            {
                regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(TemplateLimits.PatternTimeoutMilliseconds));
            }
            catch (RegexParseException ex)
            {
                return new Finding(path, "InvalidPattern", "the pattern is not a valid regular expression (" + ex.Error + " at position " + ex.Offset + ")");
            }
            catch (ArgumentException)
            {
                return new Finding(path, "InvalidPattern", "the pattern is not a valid regular expression");
            }
            if (!regex.GetGroupNames().Contains("value"))
            {
                regex = null;
                return new Finding(path, "InvalidPattern", "the pattern needs a named group called value, for example Ref:\\s*(?<value>\\S+)");
            }
            return null;
        }

        /// <summary>
        /// Adds a column to a table, creating the table (after the existing ones) when this is its first column. The header is matched on the header
        /// line like a label (case, spacing and OCR slips as for labels) and is also the column's name for GetRowValue.
        /// </summary>
        internal Finding TryAddTableColumn(string table, string header, FieldType type, DecimalStyle decimalStyle, string dateFormats)
        {
            TableDef existing = Tables.FirstOrDefault(t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                if (Tables.Count >= TemplateLimits.MaxTables) return new Finding("tables", "TooManyTables", "a template may have at most " + TemplateLimits.MaxTables + " tables");
                Finding nameProblem = CheckName(table, AllNames(), "table");
                if (nameProblem != null) return nameProblem;
            }
            else if (existing.Name != table)
                return new Finding("table", "DuplicateName", "the table is called '" + existing.Name + "'; use the same spelling for all its columns");
            Finding f = CheckColumn(existing, header, "header", out string trimmed);
            if (f != null) return f;
            f = CheckEnums(ValuePosition.SameLine, type, decimalStyle, Occurrence.RequireUnique);
            if (f != null) return f;
            f = CheckDateFormats(type, SplitDateFormats(dateFormats), "dateFormats", out string[] formats);
            if (f != null) return f;
            var column = new ColumnDef { Header = trimmed, Type = type, DecimalStyle = decimalStyle, DateFormats = formats };
            if (existing == null) Tables.Add(new TableDef { Name = table, Columns = { column } });
            else Tables[Tables.IndexOf(existing)] = existing.With(column);          // tables are replaced, never edited, so earlier copies stay intact
            return null;
        }

        /// <summary>A header is a single label: trimmed, with a letter or digit, no line break or |, at most 128 characters, unique in its table ignoring case.</summary>
        internal static Finding CheckColumn(TableDef table, string header, string path, out string trimmed)
        {
            trimmed = null;
            if (table != null && table.Columns.Count >= TemplateLimits.MaxColumnsPerTable) return new Finding(path, "TooManyColumns", "a table may have at most " + TemplateLimits.MaxColumnsPerTable + " columns");
            if (string.IsNullOrWhiteSpace(header)) return new Finding(path, "InvalidLabel", "a column header is required");
            if (header.Contains('|')) return new Finding(path, "InvalidLabel", "a column has one header; | is not allowed");
            Finding f = CheckLabels(new[] { header }, path, out string[] checkedHeader);
            if (f != null) return f;
            trimmed = checkedHeader[0];
            string candidate = trimmed;
            if (table != null && table.Columns.Any(c => string.Equals(c.Header, candidate, StringComparison.OrdinalIgnoreCase)))
                return new Finding(path, "DuplicateName", "the table already has a column '" + candidate + "'; headers are unique in a table, ignoring case");
            return null;
        }

        private static Finding CheckEnums(ValuePosition position, FieldType type, DecimalStyle decimalStyle, Occurrence occurrence)
        {
            if (!Enum.IsDefined(typeof(DecimalStyle), decimalStyle)) return new Finding("decimalStyle", "UnknownEnumValue", "decimalStyle " + (int)decimalStyle + " is not a known DecimalStyle");
            if (!Enum.IsDefined(typeof(ValuePosition), position)) return new Finding("position", "UnknownEnumValue", "position " + (int)position + " is not a known ValuePosition");
            if (!Enum.IsDefined(typeof(FieldType), type)) return new Finding("type", "UnknownEnumValue", "type " + (int)type + " is not a known FieldType");
            if (!Enum.IsDefined(typeof(Occurrence), occurrence)) return new Finding("occurrence", "UnknownEnumValue", "occurrence " + (int)occurrence + " is not a known Occurrence");
            return null;
        }

        // ------------------------------------------------------------------ builders (each works on a copy; null = accepted)

        internal Finding TryAddLabelField(string name, string labels, ValuePosition position, FieldType type, DecimalStyle decimalStyle, string dateFormats, bool required, Occurrence occurrence)
        {
            if (Fields.Count >= TemplateLimits.MaxFields) return new Finding("fields", "TooManyFields", "a template may have at most " + TemplateLimits.MaxFields + " fields");
            Finding f = CheckName(name, AllNames(), "name");
            if (f != null) return f;
            f = SplitLabels(labels, "labels", out string[] split);
            if (f != null) return f;
            f = CheckEnums(position, type, decimalStyle, occurrence);
            if (f != null) return f;
            f = CheckDateFormats(type, SplitDateFormats(dateFormats), "dateFormats", out string[] formats);
            if (f != null) return f;
            Fields.Add(new FieldDef { Name = name, Kind = FieldKind.Label, Labels = split, Position = position, Type = type, DecimalStyle = decimalStyle, DateFormats = formats, Required = required, Occurrence = occurrence });
            return null;
        }

        internal Finding TryAddPatternField(string name, string pattern, FieldType type, DecimalStyle decimalStyle, string dateFormats)
        {
            if (Fields.Count >= TemplateLimits.MaxFields) return new Finding("fields", "TooManyFields", "a template may have at most " + TemplateLimits.MaxFields + " fields");
            Finding f = CheckName(name, AllNames(), "name");
            if (f != null) return f;
            f = CompilePattern(pattern, "pattern", out Regex regex);
            if (f != null) return f;
            f = CheckEnums(ValuePosition.SameLine, type, decimalStyle, Occurrence.RequireUnique);
            if (f != null) return f;
            f = CheckDateFormats(type, SplitDateFormats(dateFormats), "dateFormats", out string[] formats);
            if (f != null) return f;
            Fields.Add(new FieldDef { Name = name, Kind = FieldKind.Pattern, Pattern = pattern, CompiledPattern = regex, Type = type, DecimalStyle = decimalStyle, DateFormats = formats, Required = true, Occurrence = Occurrence.RequireUnique });
            return null;
        }

        // ------------------------------------------------------------------ canonical JSON

        /// <summary>
        /// Non-ASCII text stays as it is (a label in Chinese is one character per character, not a six-character escape); ASCII is escaped exactly as by
        /// the standard encoder, so &lt; &gt; &amp; and quotes stay escaped and the text is safe to embed in HTML or script.
        /// </summary>
        private static readonly System.Text.Encodings.Web.JavaScriptEncoder CanonicalEncoder =
            System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All);

        /// <summary>
        /// A template is only acceptable if its canonical form (what GetTemplateJson returns) fits the limit that LoadTemplateJson applies to its input,
        /// so every accepted template can be saved and loaded again.
        /// </summary>
        internal Finding CheckCanonicalSize()
        {
            int length = ToCanonicalJson().Length;
            if (length <= TemplateLimits.MaxTemplateJsonCharacters) return null;
            return new Finding("template", "TemplateTooLarge",
                "the template would be " + length + " characters when saved with GetTemplateJson, over the limit of " + TemplateLimits.MaxTemplateJsonCharacters +
                " that LoadTemplateJson accepts; use fewer or shorter labels, names or patterns");
        }

        /// <summary>
        /// The template as canonical JSON: fixed property order, every option spelled out (defaults included), compact. The same template always
        /// produces the same text, whether it was built with methods or loaded from JSON.
        /// </summary>
        internal string ToCanonicalJson()
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = CanonicalEncoder }))
                {
                    w.WriteStartObject();
                    w.WriteNumber("schemaVersion", SchemaVersion);
                    w.WriteStartArray("fields");
                    foreach (FieldDef f in Fields)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", f.Name);
                        w.WriteString("kind", f.Kind.ToString());
                        if (f.Kind == FieldKind.Label)
                        {
                            w.WriteStartArray("labels");
                            foreach (string label in f.Labels) w.WriteStringValue(label);
                            w.WriteEndArray();
                            w.WriteString("position", f.Position.ToString());
                        }
                        else
                        {
                            w.WriteString("pattern", f.Pattern);
                        }
                        w.WriteString("type", f.Type.ToString());
                        if (UsesDecimalStyle(f.Type)) w.WriteString("decimalStyle", f.DecimalStyle.ToString());
                        if (f.Type == FieldType.Date)
                        {
                            w.WriteStartArray("dateFormats");
                            foreach (string format in f.DateFormats) w.WriteStringValue(format);
                            w.WriteEndArray();
                        }
                        w.WriteBoolean("required", f.Required);
                        w.WriteString("occurrence", f.Occurrence.ToString());
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    if (Tables.Count > 0)                    // written only when present, so a template without tables keeps its phase 1 text
                    {
                        w.WriteStartArray("tables");
                        foreach (TableDef t in Tables)
                        {
                            w.WriteStartObject();
                            w.WriteString("name", t.Name);
                            w.WriteStartArray("columns");
                            foreach (ColumnDef c in t.Columns)
                            {
                                w.WriteStartObject();
                                w.WriteString("header", c.Header);
                                w.WriteString("type", c.Type.ToString());
                                if (UsesDecimalStyle(c.Type)) w.WriteString("decimalStyle", c.DecimalStyle.ToString());
                                if (c.Type == FieldType.Date)
                                {
                                    w.WriteStartArray("dateFormats");
                                    foreach (string format in c.DateFormats) w.WriteStringValue(format);
                                    w.WriteEndArray();
                                }
                                w.WriteEndObject();
                            }
                            w.WriteEndArray();
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                    }
                    w.WriteStartObject("limits");
                    w.WriteNumber("maximumTextCharacters", Limits.MaximumTextCharacters);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
