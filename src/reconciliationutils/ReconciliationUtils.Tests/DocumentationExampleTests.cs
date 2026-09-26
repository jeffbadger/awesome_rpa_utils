using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>
    /// Runs the worked examples from Documentation/ so a page cannot drift from the behavior it describes. The fixtures are read from the pages
    /// themselves (JSON blocks, the limits table, the printed helper), so editing a documented example changes what these tests execute or fails them.
    /// </summary>
    public sealed class DocumentationExampleTests
    {
        // ---- reading the pages

        private static string ComponentDirectory()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "reconciliationutils");
                if (File.Exists(Path.Combine(candidate, "ReconciliationUtils.csproj"))) return candidate;
            }
            throw new FileNotFoundException("Could not find src/reconciliationutils above " + AppContext.BaseDirectory);
        }

        private static string Page(string name) => File.ReadAllText(Path.Combine(ComponentDirectory(), "Documentation", name));

        /// <summary>The bodies of the fenced code blocks of one language, in page order.</summary>
        private static List<string> Blocks(string page, string language) =>
            Regex.Matches(page.Replace("\r\n", "\n"), "```" + language + "\n(.*?)\n```", RegexOptions.Singleline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        /// <summary>Code with comments, using-lines, the private modifier and all white space removed, for comparing a page's snippet with real code.</summary>
        private static string Normalize(string code) =>
            Regex.Replace(Regex.Replace(Regex.Replace(code, "//[^\n]*", ""), "^\\s*using [^\n]*;", "", RegexOptions.Multiline).Replace("private static", "static"), "\\s+", "");

        /// <summary>The two arrays exactly as the page's C# assigns them to leftJson and rightJson (raw string literals).</summary>
        private static (string left, string right) QuickStartInputs()
        {
            string code = string.Join("\n", Blocks(Page("QuickStart.md"), "csharp"));
            string Literal(string variable)
            {
                Match m = Regex.Match(code, "string " + variable + " = \"\"\"\n(.*?)\n\"\"\";", RegexOptions.Singleline);
                Assert.True(m.Success, variable + " is not assigned a raw string literal on the QuickStart page");
                using JsonDocument valid = JsonDocument.Parse(m.Groups[1].Value);         // the page's input must be real JSON
                return m.Groups[1].Value;
            }
            return (Literal("leftJson"), Literal("rightJson"));
        }

        [Fact]
        public void QuickStart_ProducesTheDocumentedCountsExceptionsAndDifferences()
        {
            (string quickLeft, string quickRight) = QuickStartInputs();

            // the C# on the page calls these methods in this order
            var calls = Blocks(Page("QuickStart.md"), "csharp").SelectMany(b => Regex.Matches(b, "recon\\.(\\w+)\\(").Cast<Match>().Select(m => m.Groups[1].Value)).ToList();
            Assert.Equal(new[] { "AddKeyMappingSimple", "AddDecimalComparisonSimple", "AddTextComparison", "ReconcileJson", "GetSummary", "TryReadNextException", "TryReadNextDifference" }, calls);

            using var recon = new ReconciliationUtils();
            Assert.True(recon.AddKeyMappingSimple("Invoice", "/invoice", "/invoiceId", out string message), message);
            Assert.True(recon.AddDecimalComparisonSimple("Amount", "/amount", "/paid", out message), message);
            Assert.True(recon.AddTextComparison("Status", "/status", "/status", true, true, ComparisonNullPolicy.RequireValue, out message), message);
            Assert.True(recon.ReconcileJson(quickLeft, quickRight, out int exceptionCount, out message), message);
            Assert.Equal(3, exceptionCount);

            Assert.True(recon.GetSummary(out int leftRows, out int rightRows, out int matchedPairs, out int exceptions, out message), message);
            Assert.Equal((3, 3, 1, 3), (leftRows, rightRows, matchedPairs, exceptions));

            var seen = new List<string>();
            while (true)
            {
                Assert.True(recon.TryReadNextException(out bool hasItem, out _, out string kind, out string keyJson, out int leftRow, out int rightRow, out _, out int differenceCount, out message), message);
                if (!hasItem) break;
                seen.Add(kind + " " + keyJson + " " + leftRow + "/" + rightRow + " d" + differenceCount);
                while (true)
                {
                    Assert.True(recon.TryReadNextDifference(out bool more, out string rule, out string code, out string leftValue, out string rightValue, out _, out message), message);
                    if (!more) break;
                    seen.Add("  " + rule + " " + code + " " + leftValue + " " + rightValue);
                }
            }
            Assert.Equal(new[]
            {
                "Different [\"INV-101\"] 1/1 d1", "  Amount DecimalMismatch \"20.00\" \"21.50\"",
                "OnlyLeft [\"INV-102\"] 2/-1 d0",
                "OnlyRight [\"INV-103\"] -1/2 d0"
            }, seen);
        }

        [Fact]
        public void TheConfigurationPageDefinition_LoadsValidates_AndRoundTripsThroughTheCanonicalForm()
        {
            string json = Blocks(Page("Configuration.md"), "json").First();
            using var c = new ReconciliationUtils();
            Assert.True(c.ValidateDefinitionJson(json, out int errors, out _, out string m), m);
            Assert.Equal(0, errors);
            Assert.True(c.LoadDefinitionJson(json, out m), m);
            Assert.True(c.GetDefinitionJson(out string canonical, out m), m);
            using var again = new ReconciliationUtils();
            Assert.True(again.LoadDefinitionJson(canonical, out m), m);
            Assert.True(again.GetDefinitionJson(out string second, out m), m);
            Assert.Equal(canonical, second);
        }

        [Fact]
        public void ADefinitionWithKeysAndNoComparisons_ReportsPresenceOnly()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.ReconcileJson("[{\"id\":\"1\",\"x\":1},{\"id\":\"2\"}]", "[{\"id\":\"1\",\"x\":2}]", out int exceptions, out m), m);
            Assert.Equal(1, exceptions);
            Assert.True(c.GetSummary(out _, out _, out int matched, out _, out _));
            Assert.Equal(1, matched);
        }

        // ---- BEGIN helper (the QueueHandoff page prints exactly this; a test compares the two)
        private static string ExceptionPayload(string resultId, string kind, string keyJson, int leftRow, int rightRow, string reason)
        {
            using var stream = new System.IO.MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteString("resultId", resultId);
                w.WriteString("kind", kind);
                if (keyJson == null) w.WriteNull("key");
                else { w.WritePropertyName("key"); w.WriteRawValue(keyJson); }
                w.WriteNumber("leftRow", leftRow);
                w.WriteNumber("rightRow", rightRow);
                w.WriteString("reason", reason);
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        // ---- END helper

        [Fact]
        public void TheQueueHandoffPage_PrintsTheHelperThatIsCompiledAndTestedHere()
        {
            string source = File.ReadAllText(Path.Combine(ComponentDirectory(), "ReconciliationUtils.Tests", "DocumentationExampleTests.cs"));
            int a = source.IndexOf("// ---- BEGIN helper", StringComparison.Ordinal), b = source.IndexOf("// ---- END helper", StringComparison.Ordinal);
            string compiled = source.Substring(source.IndexOf('\n', a) + 1, b - source.IndexOf('\n', a) - 1);
            string printed = Blocks(Page("QueueHandoff.md"), "csharp").First();
            Assert.Equal(Normalize(compiled), Normalize(printed));
        }

        [Fact]
        public void QueueHandoff_BuildsAValidPayloadForEveryKindOfException()
        {
            using var recon = new ReconciliationUtils();
            Assert.True(recon.AddKeyMappingSimple("Invoice", "/invoice", "/invoiceId", out string message), message);
            Assert.True(recon.AddDecimalComparisonSimple("Amount", "/amount", "/paid", out message), message);
            const string left = "[{\"invoice\":\"A\",\"amount\":\"1\"},{\"invoice\":\"B\",\"amount\":\"1\"},{\"invoice\":\"B\",\"amount\":\"2\"},{\"invoice\":\"C\",\"amount\":\"1\"},{\"invoice\":\"D\",\"amount\":\"x\"},7]";
            const string right = "[{\"invoiceId\":\"A\",\"paid\":\"2\"},{\"invoiceId\":\"B\",\"paid\":\"1\"},{\"invoiceId\":\"E\",\"paid\":\"1\"},{\"invoiceId\":\"D\",\"paid\":\"1\"}]";
            Assert.True(recon.ReconcileJson(left, right, out int count, out message), message);

            var payloads = new List<JsonElement>();
            while (true)
            {
                Assert.True(recon.TryReadNextException(out bool has, out string id, out string kind, out string key, out int l, out int r, out string reason, out _, out message), message);
                if (!has) break;
                payloads.Add(JsonDocument.Parse(ExceptionPayload(id, kind, key, l, r, reason)).RootElement.Clone());
            }
            Assert.Equal(count, payloads.Count);
            Assert.Equal(new[] { "Different", "DuplicateKey", "OnlyLeft", "InvalidComparison", "InvalidRecord", "OnlyRight" }, payloads.Select(p => p.GetProperty("kind").GetString()));
            Assert.Equal(JsonValueKind.Null, payloads.Last(p => p.GetProperty("kind").GetString() == "InvalidRecord").GetProperty("key").ValueKind);
        }

        [Fact]
        public void TheLimitsPage_TheDocumentedDefaultsAndMaximums_AreTheRealOnes()
        {
            // read the table from the page: | `name` | default | maximum | ...
            var rows = Regex.Matches(Page("Limits.md"), "^\\| `(\\w+)` \\| ([\\d,]+) \\| ([\\d,]+) \\|", RegexOptions.Multiline).Cast<Match>()
                .ToDictionary(m => m.Groups[1].Value, m => (def: int.Parse(m.Groups[2].Value.Replace(",", "")), max: int.Parse(m.Groups[3].Value.Replace(",", ""))));
            string[] names = { "maximumRowsPerSide", "maximumInputCharactersPerSide", "maximumResults", "maximumDifferenceDetails" };
            Assert.Equal(names.OrderBy(n => n), rows.Keys.OrderBy(n => n));

            using var c = new ReconciliationUtils();
            int[] maxima = names.Select(n => rows[n].max).ToArray();
            Assert.True(c.ConfigureLimits(maxima[0], maxima[1], maxima[2], maxima[3], out string m), m);          // the documented maximums are accepted
            for (int i = 0; i < 4; i++)
            {
                int[] over = (int[])maxima.Clone();
                over[i]++;
                Assert.False(c.ConfigureLimits(over[0], over[1], over[2], over[3], out _), names[i] + " maximum is not the documented one");
            }
            Assert.False(c.ConfigureLimits(0, 1, 1, 1, out _));
            Assert.True(c.ClearDefinition(out m), m);
            Assert.True(c.GetDefinitionJson(out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement limits = doc.RootElement.GetProperty("limits");
            Assert.Equal(names.Select(n => rows[n].def), names.Select(n => limits.GetProperty(n).GetInt32()));      // the documented defaults
        }

        [Fact]
        public void TheDataTablesPage_CallsTheDocumentedMethods_AndItsLimitTableIsTheRealOne()
        {
            string page = Page("DataTables.md");
            var calls = Blocks(page, "csharp").SelectMany(b => Regex.Matches(b, "recon\\.(\\w+)\\(").Cast<Match>().Select(m => m.Groups[1].Value)).ToList();
            Assert.Equal(new[] { "AddKeyMappingSimple", "AddDecimalComparisonSimple", "ReconcileDataTables" }, calls);

            var rows = Regex.Matches(page, "^\\| `(\\w+)` \\| ([\\d,]+) \\| ([\\d,]+) \\|", RegexOptions.Multiline).Cast<Match>()
                .ToDictionary(m => m.Groups[1].Value, m => (def: int.Parse(m.Groups[2].Value.Replace(",", "")), max: int.Parse(m.Groups[3].Value.Replace(",", ""))));
            string[] names = { "maximumColumns", "maximumCells", "maximumValueCharacters" };
            Assert.Equal(names.OrderBy(n => n), rows.Keys.OrderBy(n => n));

            var defaults = new TableLimits();
            Assert.Equal(new[] { defaults.MaximumColumns, defaults.MaximumCells, defaults.MaximumValueCharacters }, names.Select(n => rows[n].def));
            using var c = new ReconciliationUtils();
            int[] max = names.Select(n => rows[n].max).ToArray();
            Assert.True(c.ConfigureTableLimits(max[0], max[1], max[2], out string m), m);
            for (int i = 0; i < 3; i++)
            {
                int[] over = (int[])max.Clone();
                over[i]++;
                Assert.False(c.ConfigureTableLimits(over[0], over[1], over[2], out _), names[i] + " maximum is not the documented one");
            }
        }

        [Fact]
        public void TheDataTablesPage_TheExampleShapeWorks_AndItsMappingTableMatchesTheCode()
        {
            // the page's snippet, with real tables
            using var recon = new ReconciliationUtils();
            Assert.True(recon.AddKeyMappingSimple("Invoice", "/Invoice", "/InvoiceId", out string message), message);
            Assert.True(recon.AddDecimalComparisonSimple("Amount", "/Amount", "/Paid", out message), message);
            var erp = new System.Data.DataTable(); erp.Columns.Add("Invoice", typeof(string)); erp.Columns.Add("Amount", typeof(decimal)); erp.Rows.Add("A", 1m);
            var bank = new System.Data.DataTable(); bank.Columns.Add("InvoiceId", typeof(string)); bank.Columns.Add("Paid", typeof(decimal)); bank.Rows.Add("A", 2m);
            Assert.True(recon.ReconcileDataTables(erp, bank, out int exceptionCount, out message), message);
            Assert.Equal(1, exceptionCount);

            // every type the page lists as supported maps to the kind the page states
            string page = Page("DataTables.md");
            Assert.Contains("`DBNull` | null", page);
            Assert.Equal(FieldKind.Null, DataTableInput.Map(DBNull.Value).Kind);
            foreach (string integral in new[] { "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong" }) Assert.Contains("`" + integral + "`", page);
            Assert.Contains("`NaN` and infinity are unsupported", page);
        }

        [Fact]
        public void TheComparisonRulesPage_ListsEveryReasonCodeTheRulesCanProduce()
        {
            string source = File.ReadAllText(Path.Combine(ComponentDirectory(), "ComparisonCore.cs"));
            var codes = Regex.Matches(source, "\"((?:Missing|Null|Invalid|Text|Decimal|Boolean|Currency)[A-Za-z]+)\"").Cast<Match>().Select(m => m.Groups[1].Value)
                .Where(c => c != "InvalidOperation").Distinct().ToList();
            Assert.Contains("CurrencyMismatch", codes);
            Assert.Contains("BooleanMismatch", codes);
            string page = Page("ComparisonRules.md");
            foreach (string code in codes)
                Assert.True(Regex.IsMatch(page, "^\\| `" + code + "` \\|", RegexOptions.Multiline), code + " is produced by the rules but has no row in the ComparisonRules reason-code table");
        }

        [Fact]
        public void TheConfigurationPage_MoneyExample_IsAValidDefinitionRule()
        {
            string json = Blocks(Page("Configuration.md"), "json").Single(b => b.Contains("\"kind\": \"Money\""));
            using var c = new ReconciliationUtils();
            Assert.True(c.ValidateDefinitionJson("{\"schemaVersion\":1,\"comparisons\":[" + json + "]}", out int errors, out string report, out string m), m);
            Assert.True(errors == 0, report);
        }

        [Fact]
        public void TheConfigurationPage_DateExample_IsAValidDefinitionRule_AndTheDocumentedInstantsAndFormatsBehaveAsStated()
        {
            string json = Blocks(Page("Configuration.md"), "json").Single(b => b.Contains("\"kind\": \"CalendarDate\""));
            using var c = new ReconciliationUtils();
            Assert.True(c.ValidateDefinitionJson("{\"schemaVersion\":1,\"comparisons\":[" + json + "]}", out int errors, out string report, out string m), m);
            Assert.True(errors == 0, report);

            // the examples ComparisonRules.md gives in prose
            string page = Page("ComparisonRules.md");
            Assert.Contains("2024-03-01T12:00:00.5Z", page);
            Assert.True(DateCore.TryParseInstant("2024-03-01T12:00:00.5Z", out _, out _));
            Assert.False(DateCore.TryParseInstant("2024-03-01T12:00:00", out _, out _));
            Assert.False(DateCore.TryParseInstant("2024-03-01T23:59:60Z", out _, out _));
            Assert.False(DateCore.TryParseInstant("2024-03-01T12:00:00+0530", out _, out _));
            Assert.True(DateCore.TryParseDate("2000-02-29", "yyyy-MM-dd", out _, out _));
            Assert.False(DateCore.TryParseDate("2100-02-29", "yyyy-MM-dd", out _, out _));
            Assert.Null(DateCore.CheckFormat("yyyy 'day' dd MM"));
            Assert.NotNull(DateCore.CheckFormat("yyyy-MM-dd HH:mm"));
        }
    }
}
