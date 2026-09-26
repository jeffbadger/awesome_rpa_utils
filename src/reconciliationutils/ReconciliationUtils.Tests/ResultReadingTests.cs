using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class ResultReadingTests
    {
        // The plan's worked example: results r000001 Matched, r000002 Different, r000003 OnlyLeft, r000004 DuplicateKey, r000005 InvalidComparison, r000006 OnlyRight.
        private const string Left = "[{\"id\":\"INV-100\",\"amt\":\"10.00\",\"name\":\"A\"},{\"id\":\"INV-101\",\"amt\":\"20.00\",\"name\":\"B\"},{\"id\":\"INV-102\",\"amt\":\"5\",\"name\":\"C\"},{\"id\":\"INV-104\",\"amt\":\"1\",\"name\":\"D\"},{\"id\":\"INV-104\",\"amt\":\"2\",\"name\":\"D\"},{\"id\":\"INV-105\",\"amt\":\"7\",\"name\":\"E\"}]";
        private const string Right = "[{\"id\":\"INV-100\",\"amt\":\"10.00\",\"name\":\"A\"},{\"id\":\"INV-101\",\"amt\":\"21.50\",\"name\":\"B2\"},{\"id\":\"INV-104\",\"amt\":\"3\",\"name\":\"D\"},{\"id\":\"INV-105\",\"amt\":\"oops\",\"name\":\"E\"},{\"id\":\"INV-103\",\"amt\":\"9\",\"name\":\"F\"}]";

        private static ReconciliationUtils Ran()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.AddDecimalComparisonSimple("Amount", "/amt", "/amt", out m), m);
            Assert.True(c.AddTextComparisonSimple("Name", "/name", "/name", out m), m);
            Assert.True(c.ReconcileJson(Left, Right, out _, out m), m);
            return c;
        }

        private static List<(string id, string kind, string key, int l, int r, string reason, int diffs)> ReadAllExceptions(ReconciliationUtils c)
        {
            var all = new List<(string, string, string, int, int, string, int)>();
            while (true)
            {
                Assert.True(c.TryReadNextException(out bool has, out string id, out string kind, out string key, out int l, out int r, out string reason, out int diffs, out string m), m);
                if (!has) return all;
                all.Add((id, kind, key, l, r, reason, diffs));
            }
        }

        [Fact]
        public void Summary_ReportsTheHeadlineCounts_AndJsonCarriesEveryCount()
        {
            using var c = Ran();
            Assert.True(c.GetSummary(out int l, out int r, out int matched, out int exceptions, out string m), m);
            Assert.Equal((6, 5, 1, 5), (l, r, matched, exceptions));
            Assert.Null(m);

            Assert.True(c.GetSummaryJson(out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement e = doc.RootElement;
            Assert.Equal(6, e.GetProperty("leftRowCount").GetInt32());
            Assert.Equal(1, e.GetProperty("differentPairCount").GetInt32());
            Assert.Equal(1, e.GetProperty("invalidPairCount").GetInt32());
            Assert.Equal(1, e.GetProperty("ambiguousKeyCount").GetInt32());
            Assert.Equal(2, e.GetProperty("ambiguousLeftRowCount").GetInt32());
            Assert.Equal(6, e.GetProperty("resultCount").GetInt32());
            Assert.Equal(3, e.GetProperty("differenceCount").GetInt32());     // two unequal fields on r000002, one invalid field on r000005
            Assert.False(e.GetProperty("allMatched").GetBoolean());
            Assert.False(e.GetProperty("bothInputsEmpty").GetBoolean());
        }

        [Fact]
        public void TheExceptionCursor_ReadsEveryExceptionInOrder_SkipsMatched_ThenStaysExhausted()
        {
            using var c = Ran();
            var all = ReadAllExceptions(c);
            Assert.Equal(new[] { "r000002", "r000003", "r000004", "r000005", "r000006" }, all.Select(x => x.id));
            Assert.Equal(new[] { "Different", "OnlyLeft", "DuplicateKey", "InvalidComparison", "OnlyRight" }, all.Select(x => x.kind));
            Assert.Equal("[\"INV-101\"]", all[0].key);
            Assert.Equal((1, 1), (all[0].l, all[0].r));
            Assert.Equal((2, -1), (all[1].l, all[1].r));                       // only left
            Assert.Equal((-1, 2), (all[2].l, all[2].r));                       // duplicate group: several left rows (-1), one right row (its index)
            Assert.Equal((-1, 4), (all[4].l, all[4].r));                       // only right
            Assert.Equal(new[] { 2, 0, 0, 1, 0 }, all.Select(x => x.diffs));

            // exhausted: true with hasItem false and sentinels, again and again
            for (int i = 0; i < 3; i++)
            {
                Assert.True(c.TryReadNextException(out bool has, out string id, out string kind, out string key, out int l, out int r, out string reason, out int diffs, out string m));
                Assert.False(has); Assert.Null(id); Assert.Null(kind); Assert.Null(key); Assert.Equal(-1, l); Assert.Equal(-1, r); Assert.Null(reason); Assert.Equal(0, diffs); Assert.Null(m);
            }
        }

        [Fact]
        public void TheDifferenceCursor_ReadsTheCurrentExceptionsDifferences_AndRestartsWithTheNextException()
        {
            using var c = Ran();
            Assert.True(c.TryReadNextException(out _, out string id, out _, out _, out _, out _, out _, out int diffs, out _));
            Assert.Equal("r000002", id);
            Assert.Equal(2, diffs);

            var seen = new List<string>();
            while (true)
            {
                Assert.True(c.TryReadNextDifference(out bool has, out string rule, out string code, out string lv, out string rv, out string why, out string m), m);
                if (!has) { Assert.Null(rule); Assert.Null(code); Assert.Null(lv); Assert.Null(rv); Assert.Null(why); break; }
                seen.Add(rule + "|" + code + "|" + lv + "|" + rv);
                Assert.False(string.IsNullOrEmpty(why));
            }
            Assert.Equal(new[] { "Amount|DecimalMismatch|\"20.00\"|\"21.50\"", "Name|TextMismatch|\"B\"|\"B2\"" }, seen);

            // still exhausted until the next exception is read
            Assert.True(c.TryReadNextDifference(out bool again, out _, out _, out _, out _, out _, out _));
            Assert.False(again);

            // an exception with no field differences has an immediately exhausted inner cursor
            Assert.True(c.TryReadNextException(out _, out id, out string kind, out _, out _, out _, out _, out diffs, out _));
            Assert.Equal(("r000003", "OnlyLeft", 0), (id, kind, diffs));
            Assert.True(c.TryReadNextDifference(out bool none, out _, out _, out _, out _, out _, out _));
            Assert.False(none);

            // the invalid comparison reports the unusable side's value as found
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));       // r000004 duplicate
            Assert.True(c.TryReadNextException(out _, out id, out kind, out _, out _, out _, out string reason, out diffs, out _));
            Assert.Equal(("r000005", "InvalidComparison"), (id, kind));
            Assert.Equal(1, diffs);
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.True(c.TryReadNextDifference(out bool has2, out string rule2, out _, out string l2, out string r2, out _, out _));
            Assert.True(has2);
            Assert.Equal(("Amount", "\"7\"", "\"oops\""), (rule2, l2, r2));
        }

        [Fact]
        public void Reset_RestartsBothCursors()
        {
            using var c = Ran();
            var first = ReadAllExceptions(c);
            Assert.True(c.ResetResultCursor(out string m), m);
            Assert.Null(m);
            Assert.Equal(first, ReadAllExceptions(c));

            Assert.True(c.ResetResultCursor(out m));
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));
            Assert.True(c.ResetResultCursor(out m));
            Assert.True(c.TryReadNextDifference(out bool has, out _, out _, out _, out _, out _, out _));
            Assert.False(has);                                              // reset also forgets the current exception
        }

        [Fact]
        public void ADifferenceReadBeforeAnyException_IsSimplyExhausted()
        {
            using var c = Ran();
            Assert.True(c.TryReadNextDifference(out bool has, out _, out _, out _, out _, out _, out string m), m);
            Assert.False(has);
        }

        [Fact]
        public void GetResultJson_DescribesAnyResult_IncludingEveryMemberOfADuplicateGroup()
        {
            using var c = Ran();

            Assert.True(c.GetResultJson("r000004", out string json, out string m), m);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement e = doc.RootElement;
                Assert.Equal("DuplicateKey", e.GetProperty("kind").GetString());
                Assert.Equal("INV-104", e.GetProperty("key")[0].GetString());
                Assert.Equal(new[] { "Left:3", "Left:4", "Right:2" }, e.GetProperty("members").EnumerateArray().Select(x => x.GetProperty("side").GetString() + ":" + x.GetProperty("row").GetInt32()));
                Assert.Equal(3, e.GetProperty("members").GetArrayLength());
                Assert.Equal(0, e.GetProperty("differences").GetArrayLength());
            }

            Assert.True(c.GetResultJson("r000002", out json, out m), m);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement d = doc.RootElement.GetProperty("differences")[0];
                Assert.Equal("Amount", d.GetProperty("rule").GetString());
                Assert.Equal("20.00", d.GetProperty("leftValue").GetString());
                Assert.Equal("1.5", d.GetProperty("delta").GetString());
                Assert.True(d.GetProperty("leftPresent").GetBoolean());
            }

            Assert.True(c.GetResultJson("r000001", out json, out m), m);       // a matched pair can be inspected too
            Assert.Contains("\"Matched\"", json);
        }

        [Fact]
        public void GetResultJson_WritesAMissingValueAsNull_AndAJsonNullAsNull_WithThePresenceFlagsTellingThemApart()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.AddTextComparisonSimple("Note", "/note", "/note", out m), m);
            Assert.True(c.ReconcileJson("[{\"id\":\"1\"}]", "[{\"id\":\"1\",\"note\":null}]", out _, out m), m);
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));
            Assert.True(c.TryReadNextDifference(out bool has, out _, out _, out string lv, out string rv, out _, out _));
            Assert.True(has);
            Assert.Null(lv);                       // missing
            Assert.Equal("null", rv);              // present JSON null
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("r000099")]
        [InlineData("R000001")]
        [InlineData("r0000001")]
        [InlineData("r-00001")]
        [InlineData("r+00001")]
        [InlineData("x000001")]
        [InlineData("r1")]
        [InlineData("r000001234567890123456789")]
        public void ABadResultId_Fails_WithoutDisturbingTheResultsOrCursors(string id)
        {
            using var c = Ran();
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));
            Assert.False(c.GetResultJson(id, out string json, out string m));
            Assert.Null(json);
            Assert.Contains("GetResultJson", m);
            Assert.Contains("no result", m);
            Assert.True(c.TryReadNextException(out bool has, out string next, out _, out _, out _, out _, out _, out _, out _));
            Assert.True(has);
            Assert.Equal("r000003", next);                                  // the cursor was not advanced or reset
            Assert.True(c.GetSummary(out _, out _, out _, out _, out _));
        }

        [Fact]
        public void BeforeAnyRun_EveryReader_FailsWithAnActionableMessage_AndClearResultsSucceeds()
        {
            using var c = new ReconciliationUtils();
            Assert.False(c.GetSummary(out _, out _, out _, out _, out string m)); Assert.Contains("call ReconcileJson first", m);
            Assert.False(c.GetSummaryJson(out string json, out m)); Assert.Null(json); Assert.Contains("no results", m);
            Assert.False(c.ResetResultCursor(out m)); Assert.Contains("no results", m);
            Assert.False(c.TryReadNextException(out bool has, out _, out _, out _, out _, out _, out _, out _, out m)); Assert.False(has); Assert.Contains("no results", m);
            Assert.False(c.TryReadNextDifference(out has, out _, out _, out _, out _, out _, out m)); Assert.Contains("no results", m);
            Assert.False(c.GetResultJson("r000001", out json, out m)); Assert.Contains("no results", m);
            Assert.True(c.ClearResults(out m)); Assert.Null(m);
            Assert.True(c.ClearResults(out m)); Assert.Null(m);              // idempotent
        }

        [Fact]
        public void ClearResults_DiscardsTheResultsAndCursors()
        {
            using var c = Ran();
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));
            Assert.True(c.ClearResults(out string m), m);
            Assert.False(c.GetSummary(out _, out _, out _, out _, out _));
            Assert.False(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void AFailedRun_LeavesNothingToRead()
        {
            using var c = Ran();
            Assert.False(c.ReconcileJson("not json", "[]", out _, out _));
            Assert.False(c.GetSummary(out _, out _, out _, out _, out _));
            Assert.False(c.GetResultJson("r000001", out _, out _));
            Assert.False(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void ALimitFailure_LeavesNothingToRead()
        {
            using var c = Ran();
            Assert.True(c.ConfigureLimits(50000, 32000000, 2, 200000, out string m), m);      // also discards the results (a setup change)
            Assert.False(c.GetSummary(out _, out _, out _, out _, out _));
            Assert.False(c.ReconcileJson(Left, Right, out _, out _));                         // more than 2 results
            Assert.False(c.GetSummary(out _, out _, out _, out _, out _));
        }

        [Fact]
        public void ASuccessfulRerun_ResetsTheCursors_AndReplacesTheResults()
        {
            using var c = Ran();
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _));
            Assert.True(c.ReconcileJson("[{\"id\":\"9\",\"amt\":\"1\",\"name\":\"x\"}]", "[]", out int count, out string m), m);
            Assert.Equal(1, count);
            Assert.True(c.TryReadNextException(out bool has, out string id, out string kind, out _, out _, out _, out _, out _, out _));
            Assert.Equal((true, "r000001", "OnlyLeft"), (has, id, kind));
            Assert.False(c.GetResultJson("r000002", out _, out _));
        }

        [Fact]
        public void ASetupChange_DiscardsTheResultsAndCursors_ButARejectedOneDoesNot()
        {
            using var c = Ran();
            Assert.False(c.AddDecimalComparisonSimple("", "/a", "/a", out _));               // rejected
            Assert.True(c.GetSummary(out _, out _, out _, out _, out _));
            Assert.True(c.AddTextComparisonSimple("Other", "/x", "/x", out string m), m);   // accepted
            Assert.False(c.GetSummary(out _, out _, out _, out _, out _));
        }

        [Fact]
        public void EverythingMatching_HasNoExceptions_AndAMessageFreeExhaustedCursor()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.ReconcileJson("[{\"id\":\"1\"}]", "[{\"id\":\"1\"}]", out int count, out m), m);
            Assert.Equal(0, count);
            Assert.True(c.TryReadNextException(out bool has, out _, out _, out _, out _, out _, out _, out _, out m));
            Assert.False(has);
            Assert.Null(m);
            Assert.True(c.GetSummaryJson(out string json, out m));
            Assert.Contains("\"allMatched\":true", json);
        }

        [Fact]
        public void NoMessage_EverContainsSourceFieldContents()
        {
            const string secret = "TOP-SECRET-VALUE";
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.AddDecimalComparisonSimple("Amount", "/amt", "/amt", out m), m);
            Assert.True(c.ReconcileJson("[{\"id\":\"" + secret + "\",\"amt\":\"" + secret + "\"},{\"id\":5.5}]", "[{\"id\":\"" + secret + "\",\"amt\":\"1\"},{\"id\":\"" + secret + "\"}]", out _, out m), m);
            var messages = new List<string> { m };
            while (true)
            {
                c.TryReadNextException(out bool has, out string id, out _, out _, out _, out _, out string reason, out _, out string em);
                messages.Add(em); messages.Add(reason);
                if (!has) break;
                c.GetResultJson(id + "9", out _, out string bad); messages.Add(bad);
                while (true)
                {
                    c.TryReadNextDifference(out bool more, out _, out string code, out _, out _, out string why, out string dm);
                    messages.Add(dm); messages.Add(code); messages.Add(why);
                    if (!more) break;
                }
            }
            Assert.All(messages.Where(x => x != null), x => Assert.DoesNotContain(secret, x));
        }

        [Fact]
        public void ReadersAndCursors_AfterDisposal_FailWithTheStandardMessage()
        {
            var c = Ran();
            c.Dispose();
            Assert.False(c.GetSummary(out _, out _, out _, out _, out string m)); Assert.Contains("disposed", m);
            Assert.False(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out m)); Assert.Contains("disposed", m);
            Assert.False(c.ClearResults(out m)); Assert.Contains("disposed", m);
        }

        [Fact]
        public void ReadingWhileAnotherThreadReruns_NeverSeesATornSnapshot()
        {
            using var c = Ran();
            var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var writer = System.Threading.Tasks.Task.Run(() => { for (int i = 0; i < 200; i++) if (!c.ReconcileJson(Left, Right, out _, out string m)) errors.Enqueue(m); });
            var reader = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    if (c.GetSummary(out int l, out int r, out int matched, out int exc, out _) && (l != 6 || r != 5 || matched != 1 || exc != 5)) errors.Enqueue("torn summary");
                    if (c.GetSummaryJson(out string json, out _)) JsonDocument.Parse(json).Dispose();
                    c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out _);
                }
            });
            System.Threading.Tasks.Task.WaitAll(writer, reader);
            Assert.Empty(errors);
        }
    }
}
