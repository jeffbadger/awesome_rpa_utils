using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TextExtractAutomation.Tests
{
    /// <summary>Runs the examples printed in Documentation/ straight from the pages, so a page cannot drift from the behavior it describes.</summary>
    public sealed class DocumentationExampleTests
    {
        private static string ComponentDirectory()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "textextractutils");
                if (File.Exists(Path.Combine(candidate, "TextExtractUtils.csproj"))) return candidate;
            }
            throw new FileNotFoundException("Could not find src/textextractutils above " + AppContext.BaseDirectory);
        }

        // A Windows checkout has CRLF line endings.
        internal static string Page(string name) => File.ReadAllText(Path.Combine(ComponentDirectory(), "Documentation", name)).Replace("\r\n", "\n");

        private static List<string> Blocks(string page, string language) =>
            Regex.Matches(page, "```" + language + "\n(.*?)\n```", RegexOptions.Singleline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        /// <summary>The rows of the first Markdown table after <paramref name="heading"/> (or in the page), cells unwrapped from backticks and unescaped.</summary>
        private static List<string[]> Table(string page, string heading = null)
        {
            string part = heading == null ? page : page.Substring(page.IndexOf(heading, StringComparison.Ordinal));
            var rows = new List<string[]>();
            bool started = false;
            foreach (string line in part.Split('\n'))
            {
                if (!line.StartsWith("|", StringComparison.Ordinal)) { if (started) break; continue; }
                started = true;
                if (Regex.IsMatch(line, "^\\|[-| ]+\\|$")) continue;
                string[] cells = Regex.Split(line.Trim().Trim('|'), "(?<!\\\\)\\|").Select(c => c.Trim().Replace("\\|", "|")).Select(c => c.Length >= 2 && c.StartsWith("`") && c.EndsWith("`") ? c.Substring(1, c.Length - 2) : c).ToArray();
                rows.Add(cells);
            }
            return rows.Skip(1).ToList();                                                    // without the header
        }

        private static (bool found, string value, string raw, string reason, int line) Get(TextExtractUtils c, string name)
        {
            Assert.True(c.GetField(name, out bool found, out string value, out string raw, out string reason, out int line, out string m), m);
            return (found, value, raw, reason, line);
        }

        [Fact]
        public void QuickStart_TheDocumentedTemplateGivesTheDocumentedValues()
        {
            string page = Page("QuickStart.md");
            var calls = Blocks(page, "csharp").SelectMany(b => Regex.Matches(b, "extract\\.(\\w+)\\(").Cast<Match>().Select(m => m.Groups[1].Value)).ToList();
            Assert.Equal(new[] { "AddLabelFieldSimple", "AddLabelFieldSimple", "AddLabelField", "AddLabelField", "ExtractFromText", "GetField", "TryReadNextField" }, calls);

            using var extract = new TextExtractUtils();
            Assert.True(extract.AddLabelFieldSimple("InvoiceNumber", "Invoice Number|Invoice No", FieldType.Code, out string message), message);
            Assert.True(extract.AddLabelFieldSimple("Iban", "IBAN", FieldType.Iban, out message), message);
            Assert.True(extract.AddLabelField("Due", "Due Date|Payment Due", ValuePosition.SameLine, FieldType.Date, DecimalStyle.DotDecimal, "dd/MM/yyyy", true, Occurrence.RequireUnique, out message), message);
            Assert.True(extract.AddLabelField("Total", "Amount Due|Total", ValuePosition.SameLine, FieldType.Amount, DecimalStyle.CommaDecimal, "", true, Occurrence.RequireUnique, out message), message);
            Assert.True(extract.ExtractFromText(Blocks(page, "text").Single(), out int foundCount, out int missingRequiredCount, out message), message);
            Assert.Contains("`foundCount` is " + foundCount + " and `missingRequiredCount` is " + missingRequiredCount, page);
            foreach (string[] row in Table(page, "| Field |"))
                Assert.Equal((true, row[1], row[2], (string)null, int.Parse(row[3])), Get(extract, row[0]));
        }

        [Fact]
        public void Labels_EveryPositionExampleGivesItsValue()
        {
            List<string[]> rows = Table(Page("Labels.md"), "| Labels | Position |");
            Assert.True(rows.Count >= 7);
            foreach (string[] row in rows)
            {
                using var c = new TextExtractUtils();
                Assert.True(c.AddLabelField("F", row[0], Enum.Parse<ValuePosition>(row[1]), FieldType.Text, DecimalStyle.DotDecimal, "", true, Occurrence.RequireUnique, out string m), m);
                if (!row[0].Equals("Date", StringComparison.OrdinalIgnoreCase))
                    Assert.True(c.AddLabelField("OtherDate", "Date", ValuePosition.SameLine, FieldType.Text, DecimalStyle.DotDecimal, "", false, Occurrence.First, out m), m);   // the page's note: Date is also a field
                Assert.True(c.ExtractFromText(row[2].Replace("\\n", "\n"), out _, out _, out m), m);
                Assert.True(Get(c, "F").value == row[3], row[0] + " / " + row[1] + ": expected " + row[3] + " but got " + Get(c, "F").value + " (" + Get(c, "F").reason + ")");
            }
        }

        [Fact]
        public void Labels_TheSlipExamplesAreTheRealRules()
        {
            string page = Page("Labels.md");
            foreach (var (label, text) in new[] { ("Total", "Tota1 5"), ("Invoice No", "lnvoice N0 5") })
            {
                Assert.Contains("`" + text.Split(' ')[0], page);
                using var c = new TextExtractUtils();
                Assert.True(c.AddLabelFieldSimple("F", label, FieldType.Integer, out string m), m);
                Assert.True(c.ExtractFromText(text, out int found, out _, out m), m);
                Assert.Equal(1, found);
            }
            using var shortLabel = new TextExtractUtils();
            Assert.True(shortLabel.AddLabelFieldSimple("F", "Due", FieldType.Integer, out string msg), msg);
            Assert.True(shortLabel.ExtractFromText("Dve 5", out int none, out _, out msg), msg);
            Assert.Equal(0, none);
        }

        private static FieldDef TypeField(string type, string styleOrFormats)
        {
            var t = new Template();
            FieldType fieldType = Enum.Parse<FieldType>(type);
            bool isStyle = styleOrFormats == "DotDecimal" || styleOrFormats == "CommaDecimal";
            Assert.Null(t.TryAddLabelField("F", "L", ValuePosition.SameLine, fieldType, isStyle ? Enum.Parse<DecimalStyle>(styleOrFormats) : DecimalStyle.DotDecimal, isStyle ? "" : styleOrFormats, true, Occurrence.RequireUnique));
            return t.Fields[0];
        }

        [Fact]
        public void Types_EveryExampleGivesItsValue_AndEveryRefusalIsRefused()
        {
            string page = Page("Types.md");
            List<string[]> examples = Table(page, "## Examples");
            Assert.True(examples.Count >= 16);
            foreach (string[] row in examples)
            {
                Converted c = ValueConverter.Convert(TypeField(row[0], row[1]), row[2]);
                Assert.True(c.Ok && c.Value == row[3], row[0] + " '" + row[2] + "': expected " + row[3] + " but got " + (c.Value ?? c.Detail));
            }
            List<string[]> refused = Table(page, "## Refused");
            Assert.True(refused.Count >= 8);
            foreach (string[] row in refused)
                Assert.Equal("InvalidValue", ValueConverter.Convert(TypeField(row[0], row[1]), row[2]).Reason);
        }

        [Fact]
        public void Results_TheJsonExampleHasExactlyTheRealProperties_AndTheReasonTableIsComplete()
        {
            string page = Page("Results.md");
            using JsonDocument shown = JsonDocument.Parse(Blocks(page, "json").Single());
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelField("Total", "Amount Due", ValuePosition.SameLine, FieldType.Amount, DecimalStyle.CommaDecimal, "", true, Occurrence.RequireUnique, out string m), m);
            Assert.True(c.ExtractFromText("x\ny\nAmount Due: EUR 1.234,50", out _, out _, out m), m);
            Assert.True(c.GetResultJson(out string json, out m), m);
            using JsonDocument real = JsonDocument.Parse(json);
            Assert.Equal(real.RootElement.GetRawText(), shown.RootElement.GetRawText().Replace("\n", "").Replace("  ", "").Replace(", ", ",").Replace(": ", ":").Replace("{ ", "{").Replace(" }", "}").Replace("[ ", "[").Replace(" ]", "]"));

            string source = File.ReadAllText(Path.Combine(ComponentDirectory(), "Extraction.cs")) + File.ReadAllText(Path.Combine(ComponentDirectory(), "ValueConverter.cs"))
                + File.ReadAllText(Path.Combine(ComponentDirectory(), "TableReader.cs"));
            var codes = Regex.Matches(source, "\"(Missing[A-Za-z]+|Invalid[A-Za-z]+|Ambiguous[A-Za-z]+|Pattern[A-Za-z]+|TooMany[A-Za-z]+)\"").Cast<Match>().Select(x => x.Groups[1].Value).Distinct().ToList();
            List<string> documented = Table(page, "| Reason | Meaning |").Select(r => r[0]).ToList();
            Assert.Equal(codes.OrderBy(x => x), documented.OrderBy(x => x));
        }

        [Fact]
        public void Patterns_TheExampleWorks()
        {
            string page = Page("Patterns.md");
            Match pattern = Regex.Match(Blocks(page, "csharp").Single(), "@\"(.*?)\", FieldType");
            using var extract = new TextExtractUtils();
            Assert.True(extract.AddPatternField("Ref", pattern.Groups[1].Value, FieldType.Code, DecimalStyle.DotDecimal, "", out string message), message);
            Assert.True(extract.ExtractFromText("please see Ref# ABC-123 for details", out int found, out _, out message), message);
            Assert.Equal(1, found);
            Assert.Equal("ABC-123", Get(extract, "Ref").value);
        }

        [Fact]
        public void Limits_TheTableIsTheCode()
        {
            List<string[]> rows = Table(Page("Limits.md"));
            string Default(string start) => rows.Single(r => r[0].StartsWith(start, StringComparison.Ordinal))[1].Replace(",", "");
            string Max(string start) => rows.Single(r => r[0].StartsWith(start, StringComparison.Ordinal))[2].Replace(",", "");
            Assert.Equal(TemplateLimits.DefaultTextCharacters.ToString(), Default("Characters of text"));
            Assert.Equal(TemplateLimits.MaxTextCharacters.ToString(), Max("Characters of text"));
            Assert.Equal(TemplateLimits.MaxFields.ToString(), Max("Fields in a template"));
            Assert.Equal(TemplateLimits.MaxLabelsPerField.ToString(), Max("Labels per field"));
            Assert.Equal(TemplateLimits.MaxLabelLength.ToString(), Max("Characters in a label"));
            Assert.Equal(TemplateLimits.MaxNameLength.ToString(), Max("Characters in a label"));
            Assert.Equal(TemplateLimits.MaxPatternLength.ToString(), Max("Characters in a pattern"));
            Assert.Equal(TemplateLimits.MaxDateFormats.ToString(), Max("Date formats"));
            Assert.Equal(TemplateLimits.MaxTemplateJsonCharacters.ToString(), Max("Characters of a template"));
            Assert.Equal(TemplateLimits.PatternTimeoutMilliseconds + " ms", Max("One pattern match"));
            Assert.Equal(TemplateLimits.PatternBudgetMilliseconds / 1000 + " s", Max("All of one pattern"));
            Assert.Equal(TemplateLimits.MaxPatternMatches.ToString(), Max("Occurrences of one pattern"));
            Assert.Equal(TemplateLimits.MaxTables.ToString(), Max("Tables in a template"));
            Assert.Equal(TemplateLimits.MaxColumnsPerTable.ToString(), Max("Columns in a table"));
            Assert.Equal(TemplateLimits.MaxTableRows.ToString(), Max("Rows in a table"));
        }

        [Fact]
        public void Tables_TheBuilderCallsAndTheJsonAgree_AndTheTextGivesTheDocumentedRows()
        {
            string page = Page("Tables.md");
            string code = Blocks(page, "csharp").Single();
            using var built = new TextExtractUtils();
            foreach (Match call in Regex.Matches(code, "extract\\.(\\w+)\\(\"(\\w+)\", \"([^\"]+)\", FieldType\\.(\\w+)(?:, DecimalStyle\\.(\\w+), \"\")?, out message\\);"))
            {
                var type = Enum.Parse<FieldType>(call.Groups[4].Value);
                string m;
                if (call.Groups[1].Value == "AddLabelFieldSimple") Assert.True(built.AddLabelFieldSimple(call.Groups[2].Value, call.Groups[3].Value, type, out m), m);
                else Assert.True(built.AddTableColumn(call.Groups[2].Value, call.Groups[3].Value, type, Enum.Parse<DecimalStyle>(call.Groups[5].Value), "", out m), m);
            }
            Assert.Equal(code.Split('\n').Length, built.CurrentTemplate.Fields.Count + built.CurrentTemplate.Tables.Sum(t => t.Columns.Count));   // every line was a call that ran

            using var loaded = new TextExtractUtils();
            List<string> json = Blocks(page, "json");
            Assert.True(loaded.LoadTemplateJson(json[0], out string message), message);
            Assert.True(loaded.GetTemplateJson(out string fromJson, out message), message);
            Assert.True(built.GetTemplateJson(out string fromBuilder, out message), message);
            Assert.Contains(fromJson.Substring(fromJson.IndexOf("\"tables\"", StringComparison.Ordinal)).TrimEnd('}'), fromBuilder);          // the JSON table is the builder's table

            Assert.True(built.ExtractFromText(Blocks(page, "text")[0], out int found, out _, out message), message);
            Assert.Equal(2, found);
            Assert.Equal("136.50", Get(built, "Total").value);
            List<string[]> rows = Table(page, "| Row |");
            string[] headers = page.Split('\n').First(l => l.StartsWith("| Row |", StringComparison.Ordinal)).Trim('|').Split('|').Select(h => h.Trim()).Skip(1).ToArray();
            foreach (string[] row in rows)
            {
                Assert.True(built.TryReadNextRow("Lines", out bool hasItem, out int rowNumber, out string rowJson, out message), message);
                Assert.True(hasItem);
                Assert.Equal(int.Parse(row[0]), rowNumber);
                for (int k = 0; k < headers.Length; k++)
                {
                    Assert.True(built.GetRowValue(headers[k], out bool cellFound, out string value, out _, out string reason, out message), message);
                    Assert.Equal((true, row[k + 1], (string)null), (cellFound, value, reason));
                }
                if (rowNumber == 1)
                {
                    // the rowJson example shows the real properties and the real first cells (it is cut short after two)
                    using JsonDocument shown = JsonDocument.Parse(json[1]);
                    using JsonDocument real = JsonDocument.Parse(rowJson);
                    Assert.Equal(Names(real.RootElement), Names(shown.RootElement));
                    Assert.Equal(Names(real.RootElement.GetProperty("cells")[0]), Names(shown.RootElement.GetProperty("cells")[0]));
                    Assert.Equal(real.RootElement.GetProperty("lineNumber").GetInt32(), shown.RootElement.GetProperty("lineNumber").GetInt32());
                    foreach (JsonElement cell in shown.RootElement.GetProperty("cells").EnumerateArray())
                        Assert.Contains(real.RootElement.GetProperty("cells").EnumerateArray(), r => r.GetRawText() == cell.GetRawText().Replace(" ", "").Replace("Bluewidget", "Blue widget"));
                }
            }
            Assert.True(built.TryReadNextRow("Lines", out bool more, out _, out _, out message), message);
            Assert.False(more);
        }

        private static List<string> Names(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToList();

        [Fact]
        public void EmailIntake_TheTemplateLoads_AndTheEmailGivesTheDocumentedCounts()
        {
            string page = Page("EmailIntake.md");
            using var extract = new TextExtractUtils();
            Assert.True(extract.LoadTemplateJson(Blocks(page, "json").Single(), out string message), message);
            Assert.True(extract.ExtractFromText(Blocks(page, "text").Single(), out int foundCount, out int missingRequired, out message), message);
            Assert.Contains("`foundCount` " + foundCount + " and `missingRequiredCount` " + missingRequired, page);
            Assert.Equal("MissingLabel", Get(extract, "PurchaseOrder").reason);
            Assert.Equal("1234.50", Get(extract, "Total").value);
            Assert.Equal("ACME Supplies GmbH", Get(extract, "Supplier").value);

            // the queue call matches LocalQueueUtils.AddJson (queuePath, payloadJson, out itemId, out duplicate, out message, businessKey)
            string call = Regex.Match(Blocks(page, "csharp").Single(), "queueUtils\\.AddJson\\(([^;]*)\\);").Groups[1].Value;
            Assert.Equal(6, call.Split(',').Length);
            string readme = File.ReadAllText(Path.Combine(ComponentDirectory(), "..", "localqueueutils", "README.md"));
            Assert.Contains("bool AddJson(string queuePath, string payloadJson, out string itemId, out bool duplicate, out string message, string businessKey", readme);

            // the page states the duplicate check the way LocalQueueUtils implements it: active items only
            Assert.Contains("still waiting or being worked", page);
            string queueCode = File.ReadAllText(Path.Combine(ComponentDirectory(), "..", "localqueueutils", "LocalQueueUtils.cs"));
            Assert.Contains("ReadItems(full, \"ready\", \"delayed\", \"in-progress\")", queueCode);
        }
    }
}
