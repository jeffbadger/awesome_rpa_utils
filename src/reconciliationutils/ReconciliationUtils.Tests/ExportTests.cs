using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class ExportTests
    {
        // id 1 matches; 2 has a null note on the left and none on the right (MissingField); 3 and 6 are lone; 4 is a duplicate group; 5 has an unusable amount; 7 is not an object
        private const string Left = "[{\"id\":\"1\",\"amt\":\"10.00\",\"note\":\"a\"},{\"id\":\"2\",\"amt\":\"20.00\",\"note\":null},{\"id\":\"3\",\"amt\":\"5\"},"
                                  + "{\"id\":\"4\",\"amt\":\"1\"},{\"id\":\"4\",\"amt\":\"2\"},{\"id\":\"5\",\"amt\":\"7\"},7]";
        private const string Right = "[{\"id\":\"1\",\"amt\":\"10.00\",\"note\":\"a\"},{\"id\":\"2\",\"amt\":\"21.50\"},{\"id\":\"4\",\"amt\":\"3\"},{\"id\":\"5\",\"amt\":\"oops\"},{\"id\":\"6\",\"amt\":\"9\"}]";

        private static ReconciliationUtils Ran(string left = Left, string right = Right)
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.AddDecimalComparisonSimple("Amount", "/amt", "/amt", out m), m);
            Assert.True(c.AddTextComparison("Note", "/note", "/note", false, false, ComparisonNullPolicy.AllowBothNull, out m), m);
            Assert.True(c.ReconcileJson(left, right, out _, out m), m);
            return c;
        }

        private static string Export(ReconciliationUtils c, string label = "run-1")
        {
            Assert.True(c.ExportResultsJson(label, out string json, out string m), m);
            Assert.Null(m);
            return json;
        }

        [Fact]
        public void TheReport_HasTheVersionedEnvelope_AndItsPartsAreExactlyWhatTheOtherMethodsReturn()
        {
            using var c = Ran();
            string report = Export(c, "invoice-close-2026-09-25");
            using JsonDocument doc = JsonDocument.Parse(report);
            JsonElement root = doc.RootElement;
            Assert.Equal(new[] { "schemaVersion", "runLabel", "definition", "summary", "results" }, root.EnumerateObject().Select(p => p.Name));
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("invoice-close-2026-09-25", root.GetProperty("runLabel").GetString());

            Assert.True(c.GetDefinitionJson(out string definition, out string m), m);
            Assert.Equal(definition, root.GetProperty("definition").GetRawText());              // the exact effective definition, defaults and limits included
            Assert.True(c.GetSummaryJson(out string summary, out m), m);
            Assert.Equal(summary, root.GetProperty("summary").GetRawText());

            JsonElement results = root.GetProperty("results");
            Assert.Equal(root.GetProperty("summary").GetProperty("resultCount").GetInt32(), results.GetArrayLength());
            int i = 0;
            foreach (JsonElement result in results.EnumerateArray())
            {
                string id = "r" + (++i).ToString("D6");
                Assert.True(c.GetResultJson(id, out string expected, out m), m);
                Assert.Equal(expected, result.GetRawText());                                    // each result is what GetResultJson gives, in result order
            }
            Assert.Equal(new[] { "Matched", "InvalidComparison", "OnlyLeft", "DuplicateKey", "InvalidComparison", "InvalidRecord", "OnlyRight" },
                results.EnumerateArray().Select(r => r.GetProperty("kind").GetString()));      // matched pairs are exported too
        }

        [Fact]
        public void TheReport_IsDeterministic_SameInputsSameDefinitionSameLabelSameBytes()
        {
            using var a = Ran();
            using var b = Ran();
            string first = Export(a);
            Assert.Equal(first, Export(a));                                                     // repeated
            Assert.Equal(first, Export(b));                                                     // independent component, same inputs
            string other = Export(a, "run-2");
            Assert.NotEqual(first, other);
            Assert.Equal(first.Replace("run-1", "run-2"), other);                               // only the label differs
            Assert.DoesNotMatch("[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:", first);                 // no timestamp
            Assert.DoesNotMatch("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-", first);                 // no GUID
        }

        [Fact]
        public void MissingNullAndPresent_StayDistinguishable_InTheReport()
        {
            using var c = Ran();
            using JsonDocument doc = JsonDocument.Parse(Export(c));
            JsonElement idTwo = doc.RootElement.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("key").ValueKind == JsonValueKind.Array && r.GetProperty("key")[0].GetString() == "2");
            JsonElement noteDiff = idTwo.GetProperty("differences").EnumerateArray().Single(d => d.GetProperty("rule").GetString() == "Note");        // left null, right missing
            Assert.True(noteDiff.GetProperty("leftPresent").GetBoolean());
            Assert.False(noteDiff.GetProperty("rightPresent").GetBoolean());
            Assert.Equal(JsonValueKind.Null, noteDiff.GetProperty("leftValue").ValueKind);
            Assert.Equal(JsonValueKind.Null, noteDiff.GetProperty("rightValue").ValueKind);                // both read null in JSON; the presence flags tell them apart
            Assert.Equal("MissingField", noteDiff.GetProperty("reasonCode").GetString());
        }

        [Fact]
        public void TheRunLabel_IsCopiedAsGiven_NullMeansEmpty_AndBadLabelsAreRefusedWithoutBeingEchoed()
        {
            using var c = Ran();
            foreach (string label in new[] { "", "with \"quotes\" and \\ backslash", "line1\nline2\ttab", "éè 日本語 🚀", "<b>&'</b>", new string('x', 256) })
            {
                using JsonDocument doc = JsonDocument.Parse(Export(c, label));
                Assert.Equal(label, doc.RootElement.GetProperty("runLabel").GetString());
            }
            using (JsonDocument doc = JsonDocument.Parse(Export(c, null))) Assert.Equal("", doc.RootElement.GetProperty("runLabel").GetString());

            Assert.False(c.ExportResultsJson(new string('x', 257), out string json, out string m));
            Assert.Null(json);
            Assert.Contains("longer than 256", m);
            Assert.False(c.ExportResultsJson("bad \ud800 label", out json, out m));
            Assert.Contains("not valid", m);
            Assert.DoesNotContain("bad", m);
            Assert.True(c.GetSummary(out _, out _, out _, out _, out _));                       // a refused label leaves the results alone
        }

        [Fact]
        public void ExportNeedsResults_AndAnyDiscardEndsIt()
        {
            using var fresh = new ReconciliationUtils();
            Assert.False(fresh.ExportResultsJson("x", out string json, out string m)); Assert.Null(json); Assert.Contains("no results", m); Assert.Contains("ExportResultsJson", m);

            using var c = Ran();
            Assert.True(c.ClearResults(out m));
            Assert.False(c.ExportResultsJson("x", out _, out m));

            using var failed = Ran();
            Assert.False(failed.ReconcileJson("nope", "[]", out _, out _));
            Assert.False(failed.ExportResultsJson("x", out _, out _));

            using var changed = Ran();
            Assert.True(changed.AddTextComparisonSimple("Extra", "/x", "/x", out m), m);
            Assert.False(changed.ExportResultsJson("x", out _, out _));

            var gone = Ran(); gone.Dispose();
            Assert.False(gone.ExportResultsJson("x", out _, out m)); Assert.Contains("disposed", m);
        }

        [Fact]
        public void ExportDoesNotDisturbTheCursorsOrTheResults()
        {
            using var c = Ran();
            Assert.True(c.TryReadNextException(out _, out string first, out _, out _, out _, out _, out _, out _, out _));
            Export(c);
            Assert.True(c.TryReadNextException(out bool has, out string second, out _, out _, out _, out _, out _, out _, out _));
            Assert.True(has);
            Assert.NotEqual(first, second);                                                    // the cursor moved on from where it was, not back to the start
            Assert.True(c.GetSummary(out _, out _, out int matched, out int exceptions, out _));
            Assert.Equal((1, 6), (matched, exceptions));
        }

        // ------------------------------------------------------------------ the output limit

        [Fact]
        public void TheOutputLimit_IsValidated_AndItsDocumentedBoundsAreTheRealOnes()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.ConfigureOutputLimit(1, out string m), m);
            Assert.True(c.ConfigureOutputLimit(64000000, out m), m);
            Assert.False(c.ConfigureOutputLimit(64000001, out m)); Assert.Contains("maximumOutputCharacters", m); Assert.Contains("64000000", m);
            Assert.False(c.ConfigureOutputLimit(0, out m)); Assert.Contains("at least 1", m);
            Assert.False(c.ConfigureOutputLimit(-5, out _));
            Assert.False(c.ConfigureOutputLimit(int.MinValue, out _));
            Assert.Equal(16000000, ReconciliationLimits.DefaultOutputCharacters);
            var gone = new ReconciliationUtils(); gone.Dispose();
            Assert.False(gone.ConfigureOutputLimit(10, out m)); Assert.Contains("disposed", m);
        }

        [Fact]
        public void TheLimit_IsExact_ItRefusesOneCharacterOver_AndLeavesTheResultsIntact()
        {
            using var c = Ran();
            int size = Export(c).Length;
            Assert.True(c.ConfigureOutputLimit(size, out string m), m);
            Assert.Equal(size, Export(c).Length);                                                // exactly at the limit: fine
            Assert.True(c.ConfigureOutputLimit(size - 1, out m), m);
            Assert.False(c.ExportResultsJson("run-1", out string json, out m));                  // one over: refused, no report
            Assert.Null(json);
            Assert.Contains("longer than " + (size - 1) + " characters", m);
            Assert.Contains("ConfigureOutputLimit", m);
            Assert.DoesNotContain("INV", m);

            Assert.True(c.GetSummary(out _, out _, out _, out int exceptions, out m), m);        // the run is intact
            Assert.Equal(6, exceptions);
            Assert.True(c.GetResultJson("r000002", out _, out m), m);
            Assert.True(c.TryReadNextException(out bool has, out _, out _, out _, out _, out _, out _, out _, out _)); Assert.True(has);

            Assert.True(c.ConfigureOutputLimit(size, out m), m);                                 // raising the limit does not cost a re-run
            Assert.Equal(size, Export(c).Length);
        }

        [Fact]
        public void TheLimit_CountsCharacters_NotBytes_ForNonAsciiText()
        {
            // Values are written unescaped (a value keeps its own characters), so the UTF-8 form is longer than the character count.
            string text = string.Concat(Enumerable.Repeat("é日🚀", 50));
            string left = "[{\"id\":\"1\",\"amt\":\"1\",\"note\":\"" + text + "\"}]";
            string right = "[{\"id\":\"1\",\"amt\":\"1\",\"note\":\"" + text + "x\"}]";
            using var c = Ran(left, right);
            string report = Export(c);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(report) > report.Length);
            Assert.True(c.ConfigureOutputLimit(report.Length, out string m), m);
            Assert.Equal(report, Export(c));
            Assert.True(c.ConfigureOutputLimit(report.Length - 1, out m), m);
            Assert.False(c.ExportResultsJson("run-1", out _, out _));
        }

        [Fact]
        public void ALargeResult_IsStoppedWhileWriting_NotAfterTheWholeReportExists()
        {
            // one duplicate group with 3,000 members: the writer must stop inside that result
            string rows = "[" + string.Join(",", Enumerable.Range(0, 3000).Select(i => "{\"id\":\"k\",\"amt\":\"" + i + "\"}")) + "]";
            using var c = Ran(rows, "[]");
            Assert.True(c.ConfigureOutputLimit(2000, out string m), m);
            Assert.False(c.ExportResultsJson("x", out _, out m));
            Assert.Contains("longer than 2000", m);
            Assert.True(c.ConfigureOutputLimit(16000000, out m), m);
            string full = Export(c);
            Assert.True(full.Length > 100000);
            using var stream = new System.IO.MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                Assert.Throws<ResultJson.OutputLimitExceededException>(() => ResultJson.WriteResult(w, c.Snapshot.Results[0], 500));
                Assert.True(w.BytesCommitted + w.BytesPending < 5000);                            // it stopped near the cap, long before the ~100,000 characters of the full result
            }
        }

        [Fact]
        public void TheOutputLimit_DoesNotDiscardResults_AndClearDefinitionRestoresTheDefault()
        {
            using var c = Ran();
            Assert.True(c.ConfigureOutputLimit(10, out string m), m);
            Assert.True(c.GetSummary(out _, out _, out _, out _, out _));                       // unlike a definition change, this keeps the results
            Assert.False(c.ExportResultsJson("x", out _, out _));
            Assert.True(c.ClearDefinition(out m), m);                                           // back to the default limit (and no definition, so re-run)
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out m), m);
            Assert.True(c.ReconcileJson(Left, Right, out _, out m), m);
            Assert.True(c.ExportResultsJson("x", out string json, out m), m);
            Assert.NotEmpty(json);

            Assert.True(c.ConfigureOutputLimit(10, out m), m);
            Assert.True(c.LoadDefinitionJson("{\"schemaVersion\":1,\"keys\":[{\"name\":\"Id\",\"leftPointer\":\"/id\",\"rightPointer\":\"/id\"}]}", out m), m);   // loading a definition leaves the output limit
            Assert.True(c.ReconcileJson(Left, Right, out _, out m), m);
            Assert.False(c.ExportResultsJson("x", out _, out _));
        }

        // ------------------------------------------------------------------ other sources and rules

        [Fact]
        public void EveryRuleKind_AndDataTableRuns_AreExported()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Id", out string m), m);
            Assert.True(c.AddMoneyComparison("Total", "/Total", "/Total", "/Cur", "/Cur", "0", ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddBooleanComparisonSimple("Paid", "/Paid", "/Paid", out m), m);
            Assert.True(c.AddCalendarDateComparison("Due", "/Due", "/Due", "dd/MM/yyyy", "yyyy-MM-dd", 0, ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddInstantComparisonSimple("Seen", "/Seen", "/Seen", out m), m);
            DataTable Table() { var t = new DataTable(); foreach (string n in new[] { "Id", "Cur", "Due", "Seen" }) t.Columns.Add(n, typeof(string)); t.Columns.Add("Total", typeof(decimal)); t.Columns.Add("Paid", typeof(bool)); return t; }
            DataTable left = Table(), right = Table();
            left.Rows.Add("a", "USD", "29/02/2024", "2024-03-01T12:00:00Z", 10m, true);
            right.Rows.Add("a", "EUR", "2024-03-01", "2024-03-01T12:00:05Z", 10m, false);
            Assert.True(c.ReconcileDataTables(left, right, out int count, out m), m);
            Assert.Equal(1, count);
            using JsonDocument doc = JsonDocument.Parse(Export(c));
            JsonElement result = doc.RootElement.GetProperty("results")[0];
            Assert.Equal(new[] { "Total", "Paid", "Due", "Seen" }, result.GetProperty("differences").EnumerateArray().Select(d => d.GetProperty("rule").GetString()));
            Assert.Equal(new[] { "CurrencyMismatch", "BooleanMismatch", "DateMismatch", "DateMismatch" }, result.GetProperty("differences").EnumerateArray().Select(d => d.GetProperty("reasonCode").GetString()));
            Assert.Equal(new[] { "Money", "Boolean", "CalendarDate", "Instant" }, doc.RootElement.GetProperty("definition").GetProperty("comparisons").EnumerateArray().Select(k => k.GetProperty("kind").GetString()));
            Assert.Equal("EUR", result.GetProperty("differences")[0].GetProperty("rightCurrency").GetString());
        }

        [Fact]
        public void ExportingAnEmptyRun_IsAValidReport()
        {
            using var c = Ran("[]", "[]");
            using JsonDocument doc = JsonDocument.Parse(Export(c));
            Assert.Equal(0, doc.RootElement.GetProperty("results").GetArrayLength());
            Assert.True(doc.RootElement.GetProperty("summary").GetProperty("bothInputsEmpty").GetBoolean());
        }

        [Fact]
        public void ConcurrentReRunsAndExports_AlwaysGiveAConsistentReport()
        {
            using var c = Ran();
            var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var writer = System.Threading.Tasks.Task.Run(() => { for (int i = 0; i < 100; i++) if (!c.ReconcileJson(Left, Right, out _, out string m)) errors.Enqueue(m); });
            var reader = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < 300; i++)
                {
                    if (!c.ExportResultsJson("x", out string json, out _)) continue;              // a rerun may be in progress, so no results
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.GetProperty("summary").GetProperty("resultCount").GetInt32() != doc.RootElement.GetProperty("results").GetArrayLength()) errors.Enqueue("inconsistent report");
                }
            });
            System.Threading.Tasks.Task.WaitAll(writer, reader);
            Assert.Empty(errors);
        }

        [Fact]
        public void ATinyLimit_StopsBeforeAHugeValueIsWritten_SoMemoryStaysNearTheLimit()
        {
            string huge = new string('v', 5000000);                                              // one 5-million-character value on each side
            using var c = Ran("[{\"id\":\"1\",\"amt\":\"1\",\"note\":\"" + huge + "\"}]", "[{\"id\":\"1\",\"amt\":\"1\",\"note\":\"" + huge + "x\"}]");
            Assert.True(c.ConfigureOutputLimit(5000, out string m), m);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.False(c.ExportResultsJson("x", out string json, out m));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Null(json);
            Assert.Contains("longer than 5000", m);
            Assert.True(allocated < 1500000, "allocated " + allocated + " bytes to refuse a 5,000-character report (a 5,000,000-character value is 10 MB as text)");
            Assert.True(c.GetSummary(out _, out _, out _, out _, out _));                        // still intact
        }

        [Fact]
        public void ALimitOfOneCharacter_IsRefusedAtTheEnvelope_BeforeTheDefinitionOrAnyResultIsWritten()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            for (int i = 0; i < 100; i++) Assert.True(c.AddTextComparisonSimple("Rule" + i, "/" + new string('p', 900) + i, "/" + new string('q', 900) + i, out m), m);   // a large definition
            Assert.True(c.ReconcileJson("[]", "[]", out _, out m), m);
            Assert.True(c.GetDefinitionJson(out string definition, out m), m);
            Assert.True(definition.Length > 150000);
            Assert.True(c.ConfigureOutputLimit(1, out m), m);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.False(c.ExportResultsJson("run", out _, out m));
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 100000, "the definition should not have been built for a one-character limit");
            Assert.Contains("longer than 1 characters", m);
        }

        [Fact]
        public void ALargeLabelOrDefinition_ThatDoesNotFit_IsRefusedWithoutWritingIt()
        {
            using var c = Ran();
            int size = Export(c, new string('x', 200)).Length;
            Assert.True(c.ConfigureOutputLimit(size - 1, out string m), m);
            Assert.False(c.ExportResultsJson(new string('x', 200), out _, out m));
            Assert.Contains("longer than", m);
            Assert.True(c.ExportResultsJson("", out string small, out m), m);                    // a shorter label fits under the same limit
            Assert.True(small.Length <= size - 1);
        }
    }
}
