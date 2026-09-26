using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>
    /// Generated datasets checked against an independent, deliberately naive oracle (quadratic, written separately from the component's
    /// matching code), plus the accounting and permutation/swap properties from the design plan.
    /// </summary>
    public sealed class ReconcileGeneratedTests
    {
        // A row is described abstractly first, so the test can both render it as JSON and compute the expected outcome from it.
        private sealed class GenRow
        {
            public string Kind;            // "obj", "nonobject"
            public string KeyText;         // null = missing, "<null>" = JSON null, "<bool>" = true, "<frac>" = 1.5
            public int Amount;
            public bool KeyAsInteger;

            public string Json()
            {
                if (Kind == "nonobject") return "5";
                string key = KeyText == null ? "" : ",\"id\":" + KeyValueJson();
                return "{" + "\"amount\":" + Amount + key + "}";
            }

            private string KeyValueJson() =>
                KeyText == "<null>" ? "null" : KeyText == "<bool>" ? "true" : KeyText == "<frac>" ? "1.5"
                : KeyAsInteger ? KeyText : "\"" + KeyText + "\"";
        }

        private static readonly string[] KeyPool = { "a", "A", " a ", "b", "B", "c", "12", "007", "7", "d" };

        private static List<GenRow> Generate(Random random, int count)
        {
            var rows = new List<GenRow>();
            for (int i = 0; i < count; i++)
            {
                int dice = random.Next(100);
                var row = new GenRow { Amount = random.Next(0, 5) };
                if (dice < 6) row.Kind = "nonobject";
                else
                {
                    row.Kind = "obj";
                    if (dice < 10) row.KeyText = null;                           // missing key
                    else if (dice < 13) row.KeyText = "<null>";
                    else if (dice < 15) row.KeyText = "<bool>";
                    else if (dice < 17) row.KeyText = "<frac>";
                    else if (dice < 19) row.KeyText = "";                        // empty
                    else
                    {
                        row.KeyText = KeyPool[random.Next(KeyPool.Length)];
                        row.KeyAsInteger = (row.KeyText == "12" || row.KeyText == "7") && random.Next(2) == 0;   // an integer token sometimes
                    }
                }
                rows.Add(row);
            }
            return rows;
        }

        private static string Render(List<GenRow> rows) => "[" + string.Join(",", rows.Select(r => r.Json())) + "]";

        /// <summary>The oracle's view of a row's key: null when the row is invalid, else the normalized text (trim + ordinal ignore case, so lower-cased).</summary>
        private static string OracleKey(GenRow row)
        {
            if (row.Kind == "nonobject") return null;
            if (row.KeyText == null || row.KeyText == "<null>" || row.KeyText == "<bool>" || row.KeyText == "<frac>") return null;
            string trimmed = row.KeyText.Trim();
            return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
        }

        private static ReconciliationUtils Component()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMapping("Id", "/id", "/id", true, true, out string m), m);
            Assert.True(c.AddDecimalComparison("Amount", "/amount", "/amount", "1", ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.ConfigureLimits(50000, 32000000, 200000, 200000, out m), m);
            return c;
        }

        // ---------------------------------------------------------------- against the oracle

        [Fact]
        public void ForManyGeneratedDatasets_TheResultsAgreeWithAnIndependentQuadraticOracle()
        {
            for (int seed = 1; seed <= 300; seed++)
            {
                var random = new Random(seed);
                List<GenRow> left = Generate(random, random.Next(0, 25)), right = Generate(random, random.Next(0, 25));
                using ReconciliationUtils c = Component();
                Assert.True(c.ReconcileJson(Render(left), Render(right), out _, out string message), "seed " + seed + ": " + message);
                ReconciliationSnapshot s = c.Snapshot;

                // expected, computed the slow obvious way
                var expected = new Dictionary<string, (ResultKind kind, int[] l, int[] r)>();
                int invalidLeft = left.Count(r => OracleKey(r) == null), invalidRight = right.Count(r => OracleKey(r) == null);
                foreach (string key in left.Select(OracleKey).Concat(right.Select(OracleKey)).Where(k => k != null).Distinct())
                {
                    int[] l = Enumerable.Range(0, left.Count).Where(i => OracleKey(left[i]) == key).ToArray();
                    int[] r = Enumerable.Range(0, right.Count).Where(i => OracleKey(right[i]) == key).ToArray();
                    ResultKind kind;
                    if (l.Length > 1 || r.Length > 1) kind = ResultKind.DuplicateKey;
                    else if (r.Length == 0) kind = ResultKind.OnlyLeft;
                    else if (l.Length == 0) kind = ResultKind.OnlyRight;
                    else kind = Math.Abs(left[l[0]].Amount - right[r[0]].Amount) <= 1 ? ResultKind.Matched : ResultKind.Different;   // tolerance 1, inclusive
                    expected[key] = (kind, l, r);
                }

                // every valid key has exactly one result of the expected kind holding exactly the expected rows
                Assert.Equal(expected.Count + invalidLeft + invalidRight, s.Results.Count);
                foreach (ReconciliationResult result in s.Results.Where(x => x.Kind != ResultKind.InvalidRecord))
                {
                    string key = result.NormalizedKey[0].ToLowerInvariant();
                    var want = expected[key];
                    Assert.True(want.kind == result.Kind, "seed " + seed + " key '" + key + "': expected " + want.kind + " got " + result.Kind);
                    Assert.Equal(want.l, result.Members.Where(x => x.Side == "Left").Select(x => x.Row));
                    Assert.Equal(want.r, result.Members.Where(x => x.Side == "Right").Select(x => x.Row));
                }
                Assert.Equal(invalidLeft, s.Summary.InvalidLeftRowCount);
                Assert.Equal(invalidRight, s.Summary.InvalidRightRowCount);
            }
        }

        // ---------------------------------------------------------------- accounting

        [Fact]
        public void EveryRowIsAccountedForExactlyOnce_AndTheCountEquationsHold_ForGeneratedDatasets()
        {
            for (int seed = 1000; seed < 1300; seed++)
            {
                var random = new Random(seed);
                List<GenRow> left = Generate(random, random.Next(0, 40)), right = Generate(random, random.Next(0, 40));
                using ReconciliationUtils c = Component();
                Assert.True(c.ReconcileJson(Render(left), Render(right), out int count, out string message), message);
                ReconciliationSnapshot s = c.Snapshot;
                ReconciliationSummary x = s.Summary;

                Assert.Null(x.CheckInvariants());
                Assert.Equal(left.Count, s.Results.SelectMany(r => r.Members).Count(m => m.Side == "Left"));       // each row appears in one result
                Assert.Equal(right.Count, s.Results.SelectMany(r => r.Members).Count(m => m.Side == "Right"));
                Assert.Equal(left.Count, s.Results.SelectMany(r => r.Members).Where(m => m.Side == "Left").Select(m => m.Row).Distinct().Count());
                Assert.Equal(right.Count, s.Results.SelectMany(r => r.Members).Where(m => m.Side == "Right").Select(m => m.Row).Distinct().Count());
                Assert.Equal(s.Results.Count, x.ResultCount);
                Assert.Equal(s.Results.Count - s.Results.Count(r => r.Kind == ResultKind.Matched), x.ExceptionCount);
                Assert.Equal(x.ExceptionCount, count);
                Assert.Equal(s.Results.Sum(r => r.Differences.Count), x.DifferenceCount);
                Assert.Equal(Enumerable.Range(1, s.Results.Count).Select(i => "r" + i.ToString("D6")), s.Results.Select(r => r.Id));
                Assert.Equal(x.AllMatched, x.ExceptionCount == 0);
                Assert.Equal(x.BothInputsEmpty, left.Count == 0 && right.Count == 0);
            }
        }

        // ---------------------------------------------------------------- permutation and swap properties

        private static (int matched, int different, int onlyLeft, int onlyRight, int dup, int invalid, int ambLeft, int ambRight) Counts(ReconciliationSummary x) =>
            (x.MatchedPairCount, x.DifferentPairCount, x.OnlyLeftCount, x.OnlyRightCount, x.AmbiguousKeyCount, x.InvalidLeftRowCount + x.InvalidRightRowCount, x.AmbiguousLeftRowCount, x.AmbiguousRightRowCount);

        [Fact]
        public void ReorderingTheInputs_ChangesIndicesAndOrder_ButNotTheClassificationOfAnyKey()
        {
            for (int seed = 2000; seed < 2150; seed++)
            {
                var random = new Random(seed);
                List<GenRow> left = Generate(random, random.Next(1, 30)), right = Generate(random, random.Next(1, 30));
                List<GenRow> leftShuffled = left.OrderBy(_ => random.Next()).ToList(), rightShuffled = right.OrderBy(_ => random.Next()).ToList();

                using ReconciliationUtils a = Component();
                using ReconciliationUtils b = Component();
                Assert.True(a.ReconcileJson(Render(left), Render(right), out _, out _));
                Assert.True(b.ReconcileJson(Render(leftShuffled), Render(rightShuffled), out _, out _));

                Assert.Equal(Counts(a.Snapshot.Summary), Counts(b.Snapshot.Summary));
                Dictionary<string, ResultKind> KindByKey(ReconciliationSnapshot s) =>
                    s.Results.Where(r => r.Kind != ResultKind.InvalidRecord).ToDictionary(r => r.NormalizedKey[0].ToLowerInvariant(), r => r.Kind);
                // a pair's Matched/Different can depend on WHICH amounts pair up only when there are no duplicates, and then it does not change
                Assert.Equal(KindByKey(a.Snapshot).OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value), KindByKey(b.Snapshot).OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value));
            }
        }

        [Fact]
        public void SwappingLeftAndRight_SwapsTheMissingSideCounts_AndTheDeltaSigns_AndKeepsTheRest()
        {
            for (int seed = 3000; seed < 3150; seed++)
            {
                var random = new Random(seed);
                List<GenRow> left = Generate(random, random.Next(1, 30)), right = Generate(random, random.Next(1, 30));
                using ReconciliationUtils forward = Component();
                using ReconciliationUtils swapped = Component();               // its pointers are symmetric, so swapping the inputs is a true mirror
                Assert.True(forward.ReconcileJson(Render(left), Render(right), out _, out _));
                Assert.True(swapped.ReconcileJson(Render(right), Render(left), out _, out _));

                ReconciliationSummary f = forward.Snapshot.Summary, w = swapped.Snapshot.Summary;
                Assert.Equal((f.OnlyLeftCount, f.OnlyRightCount), (w.OnlyRightCount, w.OnlyLeftCount));
                Assert.Equal((f.InvalidLeftRowCount, f.InvalidRightRowCount), (w.InvalidRightRowCount, w.InvalidLeftRowCount));
                Assert.Equal((f.AmbiguousLeftRowCount, f.AmbiguousRightRowCount), (w.AmbiguousRightRowCount, w.AmbiguousLeftRowCount));
                Assert.Equal((f.MatchedPairCount, f.DifferentPairCount, f.AmbiguousKeyCount, f.DifferenceCount), (w.MatchedPairCount, w.DifferentPairCount, w.AmbiguousKeyCount, w.DifferenceCount));

                Dictionary<string, string> Deltas(ReconciliationSnapshot s) => s.Results.Where(r => r.Kind == ResultKind.Different)
                    .ToDictionary(r => r.NormalizedKey[0].ToLowerInvariant(), r => r.Differences.Single().Delta);
                Dictionary<string, string> forwardDeltas = Deltas(forward.Snapshot), swappedDeltas = Deltas(swapped.Snapshot);
                Assert.Equal(forwardDeltas.Keys.OrderBy(k => k), swappedDeltas.Keys.OrderBy(k => k));
                foreach (KeyValuePair<string, string> d in forwardDeltas)
                    Assert.Equal(d.Value.StartsWith("-") ? d.Value.Substring(1) : "-" + d.Value, swappedDeltas[d.Key]);        // right minus left flips sign
            }
        }
    }
}
