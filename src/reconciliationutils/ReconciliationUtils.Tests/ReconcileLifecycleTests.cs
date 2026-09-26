using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>What ReconcileJson does to the component's state: failures, staleness, limits, disposal and concurrency.</summary>
    public sealed class ReconcileLifecycleTests
    {
        private static ReconciliationUtils WithKey()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.AddDecimalComparisonSimple("N", "/n", "/n", out m), m);
            return c;
        }

        private static string Rows(int count, Func<int, string> row) => "[" + string.Join(",", Enumerable.Range(0, count).Select(row)) + "]";

        private static string Same(int i) => "{\"id\":\"k" + i + "\",\"n\":" + i + "}";

        // ---------------------------------------------------------------- failures

        [Fact]
        public void ARunWithNoKeyMapping_Fails_WithAnActionableMessage()
        {
            using var c = new ReconciliationUtils();
            Assert.False(c.ReconcileJson("[]", "[]", out int count, out string message));
            Assert.Equal(0, count);
            Assert.Contains("no key mapping", message);
            Assert.Contains("AddKeyMapping", message);
            Assert.Null(c.Snapshot);
        }

        [Theory]
        [InlineData(null, "[]", "Left input is required")]
        [InlineData("[]", null, "Right input is required")]
        [InlineData("{}", "[]", "Left input must be a JSON array")]
        [InlineData("[]", "5", "Right input must be a JSON array")]
        [InlineData("[{\"id\":\"a\"", "[]", "Left input is not valid JSON")]
        [InlineData("[]", "[{\"id\":\"a\",\"id\":\"b\"}]", "Right input has an object with a repeated property name")]
        [InlineData("", "[]", "Left input is empty")]
        public void ABadInput_FailsWithTheSideAndAReason_AndLeavesNoResults(string left, string right, string expected)
        {
            using ReconciliationUtils c = WithKey();
            Assert.False(c.ReconcileJson(left, right, out int count, out string message));
            Assert.Equal(0, count);
            Assert.StartsWith("ReconcileJson failed:", message);
            Assert.Contains(expected, message);
            Assert.Null(c.Snapshot);
        }

        [Fact]
        public void AFailureMessage_NeverEchoesTheData()
        {
            using ReconciliationUtils c = WithKey();
            Assert.False(c.ReconcileJson("[{\"id\":\"secret-4111\",\"id\":\"x\"}]", "[]", out _, out string message));
            Assert.DoesNotContain("secret", message);
            Assert.DoesNotContain("4111", message);
        }

        [Fact]
        public void ASuccessfulRun_HasNoMessage_AndExceptionCountZeroWhenEverythingMatches()
        {
            using ReconciliationUtils c = WithKey();
            Assert.True(c.ReconcileJson(Rows(3, Same), Rows(3, Same), out int count, out string message));
            Assert.Null(message);
            Assert.Equal(0, count);
            Assert.True(c.Snapshot.Summary.AllMatched);
        }

        [Fact]
        public void AFailedRun_RemovesTheResultsOfTheRunBeforeIt_SoNothingStaleCanBeMistakenForItsOutcome()
        {
            using ReconciliationUtils c = WithKey();
            Assert.True(c.ReconcileJson(Rows(3, Same), Rows(3, Same), out _, out _));
            Assert.NotNull(c.Snapshot);
            Assert.False(c.ReconcileJson("not json", "[]", out _, out _));
            Assert.Null(c.Snapshot);                                   // cleared first, and the failed run published nothing
            Assert.True(c.ReconcileJson(Rows(1, Same), Rows(1, Same), out _, out _));
            Assert.Single(c.Snapshot.Results);
        }

        [Fact]
        public void ARunOnADisposedComponent_Fails()
        {
            var c = WithKey();
            c.Dispose();
            Assert.False(c.ReconcileJson("[]", "[]", out int count, out string message));
            Assert.Equal(0, count);
            Assert.Contains("disposed", message);
            Assert.Null(c.Snapshot);
        }

        [Fact]
        public void DisposingTheComponent_ReleasesItsResults()
        {
            ReconciliationUtils c = WithKey();
            Assert.True(c.ReconcileJson(Rows(2, Same), Rows(2, Same), out _, out _));
            c.Dispose();
            Assert.Null(c.Snapshot);
        }

        // ---------------------------------------------------------------- setup changes and results

        [Fact]
        public void EverySuccessfulSetupChange_MakesTheResultsStale_ARejectedOneDoesNot()
        {
            void Refresh(ReconciliationUtils c) => Assert.True(c.ReconcileJson(Rows(2, Same), Rows(2, Same), out _, out _));
            using ReconciliationUtils c = WithKey();

            Refresh(c);
            Assert.False(c.AddKeyMappingSimple("", "/x", "/x", out _));           // rejected: nothing changes, results stay
            Assert.False(c.ConfigureLimits(0, 1, 1, 1, out _));
            Assert.False(c.LoadDefinitionJson("nope", out _));
            Assert.NotNull(c.Snapshot);

            Assert.True(c.AddKeyMappingSimple("Second", "/n", "/n", out _)); Assert.Null(c.Snapshot); Refresh(c);
            Assert.True(c.AddTextComparisonSimple("T", "/t", "/t", out _)); Assert.Null(c.Snapshot); Refresh(c);
            Assert.True(c.AddDecimalComparisonSimple("D", "/d", "/d", out _)); Assert.Null(c.Snapshot); Refresh(c);
            Assert.True(c.ConfigureLimits(10, 1000000, 10, 10, out _)); Assert.Null(c.Snapshot); Refresh(c);
            Assert.True(c.LoadDefinitionJson("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"/id\",\"rightPointer\":\"/id\"}]}", out _)); Assert.Null(c.Snapshot); Refresh(c);
            Assert.True(c.ClearDefinition(out _)); Assert.Null(c.Snapshot);
        }

        [Fact]
        public void APublishedSnapshot_IsNotAffectedByLaterSetupChanges()
        {
            using ReconciliationUtils c = WithKey();
            Assert.True(c.ReconcileJson(Rows(2, Same), Rows(2, Same), out _, out _));
            ReconciliationSnapshot held = c.Snapshot;
            Assert.True(c.AddKeyMappingSimple("More", "/id", "/id", out _));
            Assert.Equal(2, held.Results.Count);                                   // what was already published is untouched
            Assert.Equal(2, held.Summary.MatchedPairCount);
        }

        // ---------------------------------------------------------------- limits (atomic: never a partial report)

        [Fact]
        public void TheResultLimit_IsExactlyEnforced_AndAFailureLeavesNothing()
        {
            using ReconciliationUtils c = WithKey();
            Assert.True(c.ConfigureLimits(1000, 1000000, 5, 1000, out string m), m);
            Assert.True(c.ReconcileJson(Rows(5, Same), Rows(5, Same), out _, out m), m);            // exactly at the limit
            Assert.Equal(5, c.Snapshot.Results.Count);

            Assert.False(c.ReconcileJson(Rows(6, Same), Rows(6, Same), out int count, out m));     // one over
            Assert.Equal(0, count);
            Assert.Contains("more than 5 results", m);
            Assert.Contains("ConfigureLimits", m);
            Assert.Null(c.Snapshot);                                                                // no partial report, no stale one
        }

        [Fact]
        public void TheDifferenceLimit_IsExactlyEnforced()
        {
            using ReconciliationUtils c = WithKey();
            Assert.True(c.ConfigureLimits(1000, 1000000, 1000, 3, out string m), m);
            string left = Rows(3, i => "{\"id\":\"k" + i + "\",\"n\":1}");
            string differsRight = Rows(3, i => "{\"id\":\"k" + i + "\",\"n\":2}");                   // one difference per pair: 3 in all
            Assert.True(c.ReconcileJson(left, differsRight, out _, out m), m);
            Assert.Equal(3, c.Snapshot.Summary.DifferenceCount);

            string four = Rows(4, i => "{\"id\":\"k" + i + "\",\"n\":1}");
            string fourRight = Rows(4, i => "{\"id\":\"k" + i + "\",\"n\":2}");
            Assert.False(c.ReconcileJson(four, fourRight, out _, out m));
            Assert.Contains("more than 3 field differences", m);
            Assert.Null(c.Snapshot);
        }

        [Fact]
        public void TheRowAndCharacterLimits_ApplyToEachSide_AndNameTheSide()
        {
            using ReconciliationUtils c = WithKey();
            Assert.True(c.ConfigureLimits(3, 1000000, 100, 100, out string m), m);
            Assert.True(c.ReconcileJson(Rows(3, Same), Rows(3, Same), out _, out m), m);
            Assert.False(c.ReconcileJson(Rows(4, Same), Rows(3, Same), out _, out m));
            Assert.Contains("Left input has more rows than the limit of 3", m);
            Assert.False(c.ReconcileJson(Rows(3, Same), Rows(4, Same), out _, out m));
            Assert.Contains("Right input has more rows than the limit of 3", m);

            string big = Rows(3, Same);
            Assert.True(c.ConfigureLimits(100, big.Length, 100, 100, out m), m);
            Assert.True(c.ReconcileJson(big, big, out _, out m), m);
            Assert.False(c.ReconcileJson(big + " ", big, out _, out m));
            Assert.Contains("Left input is longer than the limit", m);
        }

        [Fact]
        public void TheLimitsOfTheDefinition_AreTheOnesUsed_WhenItIsLoadedFromJson()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"/id\",\"rightPointer\":\"/id\"}],\"limits\":{\"maximumResults\":2}}", out string m), m);
            Assert.True(c.ReconcileJson(Rows(2, Same), Rows(2, Same), out _, out m), m);
            Assert.False(c.ReconcileJson(Rows(3, Same), Rows(3, Same), out _, out m));
            Assert.Contains("more than 2 results", m);
        }

        // ---------------------------------------------------------------- concurrency and scale

        [Fact]
        public void ConcurrentRuns_AreSerialized_EachSeesAConsistentSnapshot()
        {
            using ReconciliationUtils c = WithKey();
            string left = Rows(200, Same), right = Rows(200, i => i % 2 == 0 ? Same(i) : "{\"id\":\"k" + i + "\",\"n\":-1}");
            var counts = new int[8];
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
            {
                for (int round = 0; round < 10; round++)
                {
                    if (!c.ReconcileJson(left, right, out int count, out string message)) failures.Add(message);
                    counts[t] = count;
                    ReconciliationSnapshot s = c.Snapshot;                               // whatever run published last, it is whole
                    if (s != null && s.Summary.CheckInvariants() != null) failures.Add("torn snapshot");
                }
            });
            Assert.Empty(failures);
            Assert.All(counts, n => Assert.Equal(100, n));                               // 100 of the 200 pairs differ
        }

        [Fact]
        public void ASetupCallMadeDuringARun_WaitsForIt_SoTheRunUsesOneDefinition()
        {
            using ReconciliationUtils c = WithKey();
            string left = Rows(2000, Same), right = Rows(2000, Same);
            var adder = new Thread(() => c.AddKeyMappingSimple("Late", "/id", "/id", out _));
            var runner = new Thread(() => c.ReconcileJson(left, right, out _, out _));
            runner.Start();
            adder.Start();
            Assert.True(runner.Join(TimeSpan.FromSeconds(30)));
            Assert.True(adder.Join(TimeSpan.FromSeconds(30)));
            // either order is fine, but the snapshot is never one built from a half-changed definition
            ReconciliationSnapshot s = c.Snapshot;
            Assert.True(s == null || s.Summary.CheckInvariants() == null);
        }

        [Fact]
        public void ALargeRun_UsesIndexedMatching_NotAQuadraticSearch()
        {
            // 40,000 rows a side: a scan of the other side per row would be 1.6 billion comparisons and never finish
            using ReconciliationUtils c = WithKey();
            Assert.True(c.ConfigureLimits(50000, 32000000, 200000, 200000, out string m), m);
            string left = Rows(40000, Same);
            string right = Rows(40000, i => Same(39999 - i));                            // reversed, so no positional shortcut helps
            Assert.True(c.ReconcileJson(left, right, out int count, out m), m);
            Assert.Equal(0, count);
            Assert.Equal(40000, c.Snapshot.Summary.MatchedPairCount);
        }
    }
}
