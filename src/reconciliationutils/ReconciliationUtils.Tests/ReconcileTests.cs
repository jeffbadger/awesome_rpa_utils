using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>End-to-end: definition + two JSON inputs through ReconcileJson, checked against the published snapshot.</summary>
    public sealed class ReconcileTests
    {
        // ---------------------------------------------------------------- fixtures

        /// <summary>Keys company + invoiceNumber (same names both sides); compares amount (decimal, tolerance 0) and currency (text).</summary>
        private static ReconciliationUtils Invoices(string tolerance = "0")
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Company", "/company", "/company", out string m), m);
            Assert.True(c.AddKeyMappingSimple("Invoice", "/invoiceNumber", "/invoiceNumber", out m), m);
            Assert.True(c.AddDecimalComparison("Amount", "/amount", "/amount", tolerance, ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddTextComparisonSimple("Currency", "/currency", "/currency", out m), m);
            return c;
        }

        private static string Row(string company, string invoice, string amount, string currency = "\"USD\"") =>
            "{\"company\":\"" + company + "\",\"invoiceNumber\":\"" + invoice + "\",\"amount\":" + amount + ",\"currency\":" + currency + "}";

        private static string Arr(params string[] rows) => "[" + string.Join(",", rows) + "]";

        private static ReconciliationSnapshot Run(ReconciliationUtils c, string left, string right)
        {
            Assert.True(c.ReconcileJson(left, right, out int count, out string message), message);
            Assert.Null(message);
            ReconciliationSnapshot snapshot = c.Snapshot;
            Assert.NotNull(snapshot);
            Assert.Equal(snapshot.Summary.ExceptionCount, count);
            return snapshot;
        }

        private static ReconciliationResult ById(ReconciliationSnapshot s, string id) => s.Results.Single(r => r.Id == id);

        private static string Describe(ReconciliationResult r) =>
            r.Kind + "|" + (r.NormalizedKey == null ? "-" : string.Join("/", r.NormalizedKey)) + "|" + string.Join(",", r.Members.Select(m => m.Side[0] + m.Row.ToString()));

        // ---------------------------------------------------------------- the plan's worked example

        private static ReconciliationSnapshot WorkedExample(out ReconciliationUtils component)
        {
            component = Invoices();
            string left = Arr(
                Row("A", "INV-100", "125.00"),
                Row("A", "INV-101", "250.00"),
                Row("A", "INV-102", "80.00"),
                Row("A", "INV-104", "10.00"),
                Row("A", "INV-104", "11.00"),
                Row("A", "INV-105", "\"unknown\""));
            string right = Arr(
                Row("A", "INV-100", "125.00"),
                Row("A", "INV-101", "249.00"),
                Row("A", "INV-103", "45.00"),
                Row("A", "INV-104", "10.00"),
                Row("A", "INV-105", "10.00"));
            return Run(component, left, right);
        }

        [Fact]
        public void TheWorkedExample_ProducesExactlyTheDocumentedResults_InTheDocumentedOrder()
        {
            ReconciliationSnapshot s = WorkedExample(out ReconciliationUtils c);
            using (c)
            {
                Assert.Equal(new[]
                {
                    "r000001 Matched|A/INV-100|L0,R0",
                    "r000002 Different|A/INV-101|L1,R1",
                    "r000003 OnlyLeft|A/INV-102|L2",
                    "r000004 DuplicateKey|A/INV-104|L3,L4,R3",          // reported once, at its first left member, holding all three rows
                    "r000005 InvalidComparison|A/INV-105|L5,R4",
                    "r000006 OnlyRight|A/INV-103|R2"                     // right-only results come after every left row was visited
                }, s.Results.Select(r => r.Id + " " + Describe(r)));
            }
        }

        [Fact]
        public void TheWorkedExample_HasTheDocumentedCounts_AndTheAccountingHolds()
        {
            ReconciliationSnapshot s = WorkedExample(out ReconciliationUtils c);
            using (c)
            {
                ReconciliationSummary x = s.Summary;
                Assert.Equal((6, 5), (x.LeftRowCount, x.RightRowCount));
                Assert.Equal((1, 1, 1), (x.MatchedPairCount, x.DifferentPairCount, x.InvalidPairCount));
                Assert.Equal((1, 1), (x.OnlyLeftCount, x.OnlyRightCount));
                Assert.Equal((0, 0), (x.InvalidLeftRowCount, x.InvalidRightRowCount));
                Assert.Equal((1, 2, 1), (x.AmbiguousKeyCount, x.AmbiguousLeftRowCount, x.AmbiguousRightRowCount));
                Assert.Equal((6, 5, 2), (x.ResultCount, x.ExceptionCount, x.DifferenceCount));   // two field problems: INV-101's amount, INV-105's amount
                Assert.False(x.AllMatched);
                Assert.False(x.BothInputsEmpty);
                Assert.Null(x.CheckInvariants());
            }
        }

        [Fact]
        public void TheWorkedExample_ExplainsEachResult()
        {
            ReconciliationSnapshot s = WorkedExample(out ReconciliationUtils c);
            using (c)
            {
                ReconciliationResult different = ById(s, "r000002");
                Assert.Equal("DecimalMismatch", different.ReasonCode);
                ComparisonOutcome amount = Assert.Single(different.Differences);
                Assert.Equal("Amount", amount.RuleName);
                Assert.Equal("-1", amount.Delta);                              // 249 - 250

                ReconciliationResult invalid = ById(s, "r000005");
                Assert.Equal("InvalidDecimal", invalid.ReasonCode);
                Assert.Equal("\"unknown\"", Assert.Single(invalid.Differences).LeftValueJson);

                ReconciliationResult dup = ById(s, "r000004");
                Assert.Equal("DuplicateNormalizedKey", dup.ReasonCode);
                Assert.Empty(dup.Differences);                                  // duplicate groups are never compared
                Assert.Equal(-1, dup.LeftRowIndex);                             // several left rows
                Assert.Equal(3, dup.RightRowIndex);                             // exactly one right row
                Assert.Equal(1, ById(s, "r000002").LeftRowIndex);
                Assert.Equal(-1, ById(s, "r000003").RightRowIndex);            // none on that side
            }
        }

        // ---------------------------------------------------------------- pairing rules

        [Fact]
        public void APairThatAgreesOnEveryRule_IsMatched_WithNoDetails()
        {
            using ReconciliationUtils c = Invoices();
            ReconciliationSnapshot s = Run(c, Arr(Row("A", "1", "10.00")), Arr(Row("A", "1", "10")));
            ReconciliationResult r = Assert.Single(s.Results);
            Assert.Equal(ResultKind.Matched, r.Kind);
            Assert.Empty(r.Differences);
            Assert.Null(r.ReasonCode);
            Assert.True(s.Summary.AllMatched);
        }

        [Fact]
        public void EveryRuleIsEvaluated_AndOnePairWithSeveralDifferencesIsOneResult()
        {
            using ReconciliationUtils c = Invoices();
            ReconciliationSnapshot s = Run(c, Arr(Row("A", "1", "10.00", "\"USD\"")), Arr(Row("A", "1", "99.00", "\"EUR\"")));
            ReconciliationResult r = Assert.Single(s.Results);
            Assert.Equal(ResultKind.Different, r.Kind);
            Assert.Equal(new[] { "Amount", "Currency" }, r.Differences.Select(d => d.RuleName));       // in rule order, none skipped
            Assert.Equal(1, s.Summary.DifferentPairCount);
            Assert.Equal(1, s.Summary.ExceptionCount);                                                 // one exception, however many fields differ
            Assert.Equal(2, s.Summary.DifferenceCount);
        }

        [Fact]
        public void AnInvalidComparisonOutranksADifference_ButTheUnequalFieldsAreKept()
        {
            using ReconciliationUtils c = Invoices();
            // amount is unusable on the left (invalid); currency differs (unequal)
            ReconciliationSnapshot s = Run(c, Arr(Row("A", "1", "\"abc\"", "\"USD\"")), Arr(Row("A", "1", "5", "\"EUR\"")));
            ReconciliationResult r = Assert.Single(s.Results);
            Assert.Equal(ResultKind.InvalidComparison, r.Kind);
            Assert.Equal("InvalidDecimal", r.ReasonCode);                                               // the invalid one names the reason
            Assert.Equal(new[] { ComparisonState.Invalid, ComparisonState.Different }, r.Differences.Select(d => d.State));
            Assert.Equal((0, 0, 1), (s.Summary.MatchedPairCount, s.Summary.DifferentPairCount, s.Summary.InvalidPairCount));
        }

        [Fact]
        public void WithNoComparisons_ItIsAPresenceOnlyReconciliation()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            ReconciliationSnapshot s = Run(c, "[{\"id\":\"a\",\"x\":1},{\"id\":\"b\"}]", "[{\"id\":\"a\",\"x\":2},{\"id\":\"c\"}]");
            Assert.Equal(new[] { "Matched|a|L0,R0", "OnlyLeft|b|L1", "OnlyRight|c|R1" }, s.Results.Select(Describe));   // the differing x is not compared
        }

        [Fact]
        public void TheKeyIsMatchedByNormalization_AndBothOriginalsAreKept()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMapping("Id", "/id", "/id", true, true, out string m), m);
            ReconciliationSnapshot s = Run(c, "[{\"id\":\"  Abc \"}]", "[{\"id\":\"ABC\"}]");
            ReconciliationResult r = Assert.Single(s.Results);
            Assert.Equal(ResultKind.Matched, r.Kind);
            Assert.Equal(new[] { "Abc" }, r.NormalizedKey);                                            // the first (left) row's normalized text
            Assert.Equal("  Abc ", r.Members.Single(x => x.Side == "Left").OriginalKey[0]);
            Assert.Equal("ABC", r.Members.Single(x => x.Side == "Right").OriginalKey[0]);
        }

        [Fact]
        public void IntegerAndStringKeys_MatchWhenTheirTextIsTheSame_ButLeadingZerosDoNot()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            ReconciliationSnapshot s = Run(c, "[{\"id\":123},{\"id\":7}]", "[{\"id\":\"123\"},{\"id\":\"007\"}]");
            Assert.Equal(new[] { "Matched|123|L0,R0", "OnlyLeft|7|L1", "OnlyRight|007|R1" }, s.Results.Select(Describe));
        }

        [Fact]
        public void CompositeKeys_AreTuples_SoDelimiterLikeContentNeverCollides()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("X", "/x", "/x", out string m), m);
            Assert.True(c.AddKeyMappingSimple("Y", "/y", "/y", out m), m);
            ReconciliationSnapshot s = Run(c, "[{\"x\":\"a|b\",\"y\":\"c\"}]", "[{\"x\":\"a\",\"y\":\"b|c\"}]");
            Assert.Equal(2, s.Results.Count);                              // two different keys that a "|"-joined string would have merged
            Assert.Equal(ResultKind.OnlyLeft, s.Results[0].Kind);
            Assert.Equal(ResultKind.OnlyRight, s.Results[1].Kind);
        }

        [Fact]
        public void EachSideUsesItsOwnKeyPointers()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Key", "/erpId", "/payRef", out string m), m);
            ReconciliationSnapshot s = Run(c, "[{\"erpId\":\"k1\"}]", "[{\"payRef\":\"k1\"}]");
            Assert.Equal(ResultKind.Matched, Assert.Single(s.Results).Kind);
        }

        // ---------------------------------------------------------------- duplicate keys

        [Theory]
        [InlineData("left")]
        [InlineData("right")]
        [InlineData("both")]
        public void ADuplicateOnEitherSide_MakesOneGroup_NeverAPairOrAFirstRowPick(string where)
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            string one = "[{\"id\":\"k\"}]", two = "[{\"id\":\"k\"},{\"id\":\"k\"}]";
            ReconciliationSnapshot s = Run(c, where == "right" ? one : two, where == "left" ? one : two);
            ReconciliationResult r = Assert.Single(s.Results);
            Assert.Equal(ResultKind.DuplicateKey, r.Kind);
            Assert.Equal(where == "right" ? 1 : 2, r.Members.Count(x => x.Side == "Left"));
            Assert.Equal(where == "left" ? 1 : 2, r.Members.Count(x => x.Side == "Right"));
            Assert.Equal(1, s.Summary.AmbiguousKeyCount);
            Assert.Equal((0, 0, 0, 0), (s.Summary.MatchedPairCount, s.Summary.DifferentPairCount, s.Summary.OnlyLeftCount, s.Summary.OnlyRightCount));
            Assert.Null(s.Summary.CheckInvariants());
        }

        [Fact]
        public void ADuplicateOnlyOnTheRight_IsReportedOnceAtItsFirstRightMember_AfterTheLeftPass()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            ReconciliationSnapshot s = Run(c, "[{\"id\":\"a\"}]", "[{\"id\":\"z\"},{\"id\":\"z\"},{\"id\":\"a\"},{\"id\":\"z\"}]");
            Assert.Equal(new[] { "Matched|a|L0,R2", "DuplicateKey|z|R0,R1,R3" }, s.Results.Select(Describe));
        }

        [Fact]
        public void DuplicatesCreatedByNormalization_ShowTheirOriginalKeys_SoTheCollisionIsVisible()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMapping("Id", "/id", "/id", true, true, out string m), m);
            ReconciliationSnapshot s = Run(c, "[{\"id\":\"ab\"},{\"id\":\" AB \"}]", "[]");
            ReconciliationResult r = Assert.Single(s.Results);
            Assert.Equal(ResultKind.DuplicateKey, r.Kind);
            Assert.Equal(new[] { "ab", " AB " }, r.Members.Select(x => x.OriginalKey[0]));
        }

        [Fact]
        public void TwoInvalidRowsWithTheSameBadKey_AreNotDuplicates_TheyAreEachInvalid()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            ReconciliationSnapshot s = Run(c, "[{},{}]", "[{\"id\":null},{\"id\":null}]");
            Assert.All(s.Results, r => Assert.Equal(ResultKind.InvalidRecord, r.Kind));
            Assert.Equal(4, s.Results.Count);
            Assert.Equal((2, 2, 0), (s.Summary.InvalidLeftRowCount, s.Summary.InvalidRightRowCount, s.Summary.AmbiguousKeyCount));
        }

        // ---------------------------------------------------------------- invalid rows

        [Fact]
        public void InvalidRows_AreReportedWithTheirSide_Reason_MappingAndPointer_AndNeverEnterTheIndex()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/rid", out string m), m);
            ReconciliationSnapshot s = Run(c,
                "[5,{\"id\":true},{\"x\":1},{\"id\":\"\"},{\"id\":1.5},{\"id\":\"ok\"}]",
                "[{\"rid\":\"ok\"},{\"rid\":[1]},null,\"text\"]");
            var byRow = s.Results.Where(r => r.Kind == ResultKind.InvalidRecord).ToDictionary(r => r.Members[0].Side[0] + r.Members[0].Row.ToString());
            Assert.Equal("NonObjectRow", byRow["L0"].ReasonCode);
            Assert.Equal("InvalidKeyType", byRow["L1"].ReasonCode);
            Assert.Equal("MissingKey", byRow["L2"].ReasonCode);
            Assert.Equal("EmptyKey", byRow["L3"].ReasonCode);
            Assert.Equal("InvalidKeyType", byRow["L4"].ReasonCode);                 // a fraction is not an integer key
            Assert.Equal("InvalidKeyType", byRow["R1"].ReasonCode);
            Assert.Equal("NonObjectRow", byRow["R2"].ReasonCode);
            Assert.Equal("NonObjectRow", byRow["R3"].ReasonCode);
            Assert.Equal(("Id", "/id"), (byRow["L1"].MappingName, byRow["L1"].Pointer));
            Assert.Equal(("Id", "/rid"), (byRow["R1"].MappingName, byRow["R1"].Pointer));   // the right side's own pointer
            Assert.Equal(1, s.Results.Count(r => r.Kind == ResultKind.Matched));
            Assert.Equal((5, 3), (s.Summary.InvalidLeftRowCount, s.Summary.InvalidRightRowCount));
            Assert.Null(s.Summary.CheckInvariants());
        }

        [Fact]
        public void AnInvalidRowExplanation_NamesThePartButNeverQuotesTheValue()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            ReconciliationSnapshot s = Run(c, "[{\"id\":true,\"secret\":\"do-not-copy\"}]", "[]");
            ReconciliationResult r = Assert.Single(s.Results);
            Assert.Contains("'Id'", r.Explanation);
            Assert.Contains("/id", r.Explanation);
            Assert.DoesNotContain("do-not-copy", r.Explanation);
        }

        // ---------------------------------------------------------------- empty inputs

        [Fact]
        public void BothInputsEmpty_Succeeds_AsAllMatched_AndSaysSo()
        {
            using ReconciliationUtils c = Invoices();
            ReconciliationSnapshot s = Run(c, "[]", "[]");
            Assert.Empty(s.Results);
            Assert.True(s.Summary.AllMatched);
            Assert.True(s.Summary.BothInputsEmpty);
            Assert.Equal((0, 0, 0), (s.Summary.ResultCount, s.Summary.ExceptionCount, s.Summary.DifferenceCount));
        }

        [Fact]
        public void OneEmptySide_MakesEveryRecordOnTheOtherSideLoneAndTheRunNotAllMatched()
        {
            using ReconciliationUtils c = Invoices();
            ReconciliationSnapshot s = Run(c, Arr(Row("A", "1", "1"), Row("A", "2", "2")), "[]");
            Assert.All(s.Results, r => Assert.Equal(ResultKind.OnlyLeft, r.Kind));
            Assert.False(s.Summary.AllMatched);
            Assert.False(s.Summary.BothInputsEmpty);
            Assert.Equal(2, s.Summary.ExceptionCount);
        }

        // ---------------------------------------------------------------- determinism

        [Fact]
        public void ARunIsDeterministic_SameInputsGiveTheSameIdsOrderAndCounts()
        {
            ReconciliationSnapshot a = WorkedExample(out ReconciliationUtils c1);
            ReconciliationSnapshot b = WorkedExample(out ReconciliationUtils c2);
            using (c1) using (c2)
            {
                Assert.Equal(a.Results.Select(r => r.Id + " " + Describe(r) + " " + r.ReasonCode + " " + r.Explanation), b.Results.Select(r => r.Id + " " + Describe(r) + " " + r.ReasonCode + " " + r.Explanation));
            }
        }

        [Fact]
        public void RunningAgain_ReplacesTheSnapshotWholeAndRenumbersFromOne()
        {
            using ReconciliationUtils c = Invoices();
            ReconciliationSnapshot first = Run(c, Arr(Row("A", "1", "1")), Arr(Row("A", "1", "1")));
            ReconciliationSnapshot second = Run(c, Arr(Row("A", "1", "1"), Row("A", "2", "2")), Arr(Row("A", "1", "1")));
            Assert.NotSame(first, second);
            Assert.Single(first.Results);                                   // the old snapshot is untouched (immutable)
            Assert.Equal(new[] { "r000001", "r000002" }, second.Results.Select(r => r.Id));
        }
    }
}
