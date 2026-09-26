using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class BooleanMoneyTests
    {
        private static readonly FieldValue True = new FieldValue(FieldKind.Boolean, "true");
        private static readonly FieldValue False = new FieldValue(FieldKind.Boolean, "false");
        private static FieldValue Str(string s) => new FieldValue(FieldKind.String, s);
        private static FieldValue Num(string s) => new FieldValue(s.Contains('.') ? FieldKind.Number : FieldKind.Integer, s);

        private static ComparisonDef Boolean(ComparisonNullPolicy policy = ComparisonNullPolicy.RequireValue) =>
            new ComparisonDef { Name = "Paid", Kind = RuleKind.Boolean, NullPolicy = policy };

        private static ComparisonDef Money(string tolerance = "0", ComparisonNullPolicy policy = ComparisonNullPolicy.RequireValue) =>
            new ComparisonDef { Name = "Total", Kind = RuleKind.Money, AbsoluteTolerance = tolerance, NullPolicy = policy };

        private static ComparisonOutcome Eval(ComparisonDef rule, FieldValue l, FieldValue r) => ComparisonCore.Evaluate(rule, l, r);
        private static ComparisonOutcome Pay(ComparisonDef rule, FieldValue la, FieldValue ra, FieldValue lc, FieldValue rc) => ComparisonCore.EvaluateMoney(rule, la, ra, lc, rc);

        // ------------------------------------------------------------------ Boolean

        [Fact]
        public void Boolean_EqualAndDifferent()
        {
            Assert.Equal(ComparisonState.Equal, Eval(Boolean(), True, True).State);
            Assert.Equal(ComparisonState.Equal, Eval(Boolean(), False, False).State);
            ComparisonOutcome d = Eval(Boolean(), True, False);
            Assert.Equal((ComparisonState.Different, "BooleanMismatch"), (d.State, d.ReasonCode));
            Assert.Equal(("true", "false"), (d.LeftValueJson, d.RightValueJson));
            Assert.Equal(("true", "false"), (d.LeftInterpreted, d.RightInterpreted));
        }

        [Theory]
        [InlineData("string", "yes")]
        [InlineData("string", "true")]
        [InlineData("integer", "1")]
        [InlineData("integer", "0")]
        [InlineData("number", "1.0")]
        public void Boolean_RequiresAJsonBoolean_NothingConvertsImplicitly(string kind, string text)
        {
            FieldValue bad = kind == "string" ? Str(text) : Num(text);
            foreach (ComparisonOutcome o in new[] { Eval(Boolean(), True, bad), Eval(Boolean(), bad, True) })
            {
                Assert.Equal((ComparisonState.Invalid, "InvalidType"), (o.State, o.ReasonCode));
                Assert.DoesNotContain("\"" + text + "\"", o.Explanation);           // explanations never quote the value
            }
            Assert.Equal("InvalidType", Eval(Boolean(ComparisonNullPolicy.AllowBothNull), bad, FieldValue.Null).ReasonCode);   // a bad type stays invalid under AllowBothNull
        }

        [Fact]
        public void Boolean_MissingNullAndObjects_FollowTheCommonPolicy()
        {
            Assert.Equal("MissingField", Eval(Boolean(), FieldValue.Missing, True).ReasonCode);
            Assert.Equal("MissingField", Eval(Boolean(ComparisonNullPolicy.AllowBothNull), FieldValue.Missing, FieldValue.Missing).ReasonCode);   // missing is always invalid
            Assert.Equal("NullNotAllowed", Eval(Boolean(), FieldValue.Null, True).ReasonCode);
            Assert.Equal("NullNotAllowed", Eval(Boolean(), FieldValue.Null, FieldValue.Null).ReasonCode);
            Assert.Equal(ComparisonState.Equal, Eval(Boolean(ComparisonNullPolicy.AllowBothNull), FieldValue.Null, FieldValue.Null).State);
            ComparisonOutcome d = Eval(Boolean(ComparisonNullPolicy.AllowBothNull), FieldValue.Null, False);
            Assert.Equal((ComparisonState.Different, "BooleanMismatch"), (d.State, d.ReasonCode));
            Assert.Equal("InvalidType", Eval(Boolean(), FieldValue.Unsupported, True).ReasonCode);
        }

        // ------------------------------------------------------------------ Money

        [Fact]
        public void Money_SameCurrency_ComparesTheAmountsExactly_WithAnInclusiveTolerance()
        {
            Assert.Equal(ComparisonState.Equal, Pay(Money(), Num("10.00"), Num("10"), Str("USD"), Str("USD")).State);
            ComparisonOutcome d = Pay(Money(), Str("10.00"), Num("10.01"), Str("USD"), Str("USD"));
            Assert.Equal((ComparisonState.Different, "DecimalMismatch", "0.01"), (d.State, d.ReasonCode, d.Delta));
            Assert.Equal(ComparisonState.Equal, Pay(Money("0.01"), Str("10.00"), Str("10.01"), Str("USD"), Str("USD")).State);          // at the tolerance
            Assert.Equal(ComparisonState.Different, Pay(Money("0.01"), Str("10.00"), Str("10.02"), Str("USD"), Str("USD")).State);      // just beyond
            Assert.Equal(ComparisonState.Different, Pay(Money("0.1"), Num("0.1"), Num("0.3"), Str("EUR"), Str("EUR")).State);            // exactly 0.2 apart
        }

        [Theory]
        [InlineData("usd", "USD")]
        [InlineData(" Usd ", "USD")]
        [InlineData("\tUSD\n", "usd")]
        [InlineData("eur", "EUR")]
        public void Money_CurrencyIsTrimmedAndUppercased_BeforeItIsCompared(string left, string right)
        {
            ComparisonOutcome o = Pay(Money(), Str("5"), Str("5"), Str(left), Str(right));
            Assert.Equal(ComparisonState.Equal, o.State);
            Assert.EndsWith(" " + left.Trim().ToUpperInvariant(), o.LeftInterpreted);
        }

        [Fact]
        public void Money_DifferentCurrencies_AreAMismatch_AndTheAmountsAreNeverCompared()
        {
            ComparisonOutcome o = Pay(Money("1000000"), Str("10"), Str("10"), Str("USD"), Str("EUR"));   // equal amounts, and a huge tolerance: still a mismatch
            Assert.Equal((ComparisonState.Different, "CurrencyMismatch"), (o.State, o.ReasonCode));
            Assert.Null(o.Delta);                                                                          // no misleading amount delta
            Assert.Equal(("\"USD\"", "\"EUR\""), (o.LeftCurrencyJson, o.RightCurrencyJson));
            Assert.DoesNotContain("USD", o.Explanation);
            Assert.DoesNotContain("EUR", o.Explanation);
        }

        [Theory]
        [InlineData("US")]
        [InlineData("USDD")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("U$D")]
        [InlineData("U D")]
        [InlineData("US1")]
        [InlineData("ÉUR")]        // a non-ASCII letter
        [InlineData("ıSD")]        // dotless i
        public void Money_AMalformedCurrency_IsInvalidCurrency_OnEitherSide(string bad)
        {
            foreach (ComparisonOutcome o in new[] { Pay(Money(), Str("1"), Str("1"), Str(bad), Str("USD")), Pay(Money(), Str("1"), Str("1"), Str("USD"), Str(bad)) })
                Assert.Equal((ComparisonState.Invalid, "InvalidCurrency"), (o.State, o.ReasonCode));
        }

        [Fact]
        public void Money_ACurrencyThatIsNotAString_IsInvalidCurrency_AndNullIsNotAllowedEvenWithAllowBothNull()
        {
            ComparisonDef allowNull = Money("0", ComparisonNullPolicy.AllowBothNull);
            Assert.Equal("InvalidCurrency", Pay(allowNull, Str("1"), Str("1"), Num("840"), Str("USD")).ReasonCode);
            Assert.Equal("InvalidCurrency", Pay(allowNull, Str("1"), Str("1"), True, Str("USD")).ReasonCode);
            Assert.Equal("InvalidCurrency", Pay(allowNull, Str("1"), Str("1"), FieldValue.Null, Str("USD")).ReasonCode);
            Assert.Equal("InvalidCurrency", Pay(allowNull, FieldValue.Null, FieldValue.Null, FieldValue.Null, FieldValue.Null).ReasonCode);
            Assert.Equal("InvalidCurrency", Pay(allowNull, Str("1"), Str("1"), FieldValue.Unsupported, Str("USD")).ReasonCode);
        }

        [Fact]
        public void Money_ReasonPriority_MissingThenCurrencyThenAmount()
        {
            Assert.Equal("MissingField", Pay(Money(), Str("1"), Str("1"), FieldValue.Missing, Str("USD")).ReasonCode);
            Assert.Equal("MissingField", Pay(Money(), FieldValue.Missing, Str("1"), Str("USD"), Str("USD")).ReasonCode);
            Assert.Equal("MissingField", Pay(Money(), FieldValue.Missing, Str("1"), Str("US"), Str("USD")).ReasonCode);          // missing outranks a bad currency
            Assert.Equal("InvalidCurrency", Pay(Money(), Str("abc"), Str("1"), Str("US"), Str("USD")).ReasonCode);               // a bad currency outranks a bad amount
            Assert.Equal("InvalidDecimal", Pay(Money(), Str("abc"), Str("1"), Str("USD"), Str("EUR")).ReasonCode);               // an unusable amount outranks a currency mismatch
            Assert.Equal("InvalidType", Pay(Money(), True, Str("1"), Str("USD"), Str("USD")).ReasonCode);
            Assert.Equal("NullNotAllowed", Pay(Money(), FieldValue.Null, Str("1"), Str("USD"), Str("EUR")).ReasonCode);          // RequireValue: a null amount is invalid even across currencies
            ComparisonOutcome both = Pay(Money(), FieldValue.Missing, Str("1"), Str("US"), Str("USD"));
            Assert.Contains("amount", both.Explanation);
            Assert.Contains("currency", both.Explanation);                                                                       // every problem is described
        }

        [Fact]
        public void Money_AllowBothNull_AppliesToAmountsOnlyOnceTheCurrenciesAgree()
        {
            ComparisonDef allowNull = Money("0", ComparisonNullPolicy.AllowBothNull);
            Assert.Equal(ComparisonState.Equal, Pay(allowNull, FieldValue.Null, FieldValue.Null, Str("USD"), Str("usd")).State);
            ComparisonOutcome oneNull = Pay(allowNull, FieldValue.Null, Str("5"), Str("USD"), Str("USD"));
            Assert.Equal((ComparisonState.Different, "DecimalMismatch"), (oneNull.State, oneNull.ReasonCode));
            ComparisonOutcome mismatch = Pay(allowNull, FieldValue.Null, FieldValue.Null, Str("USD"), Str("EUR"));
            Assert.Equal((ComparisonState.Different, "CurrencyMismatch"), (mismatch.State, mismatch.ReasonCode));       // two nulls do not agree across currencies
            Assert.Equal("InvalidType", Pay(allowNull, True, FieldValue.Null, Str("USD"), Str("USD")).ReasonCode);
        }

        [Fact]
        public void Money_TheRowReaderOverload_ReadsTheCurrencyFields_AndTheFieldValueOverloadNeverThrows()
        {
            var rule = new ComparisonDef { Name = "T", Kind = RuleKind.Money, LeftSegments = new[] { "amt" }, RightSegments = new[] { "amt" }, LeftCurrencySegments = new[] { "cur" }, RightCurrencySegments = new[] { "cur" } };
            using JsonDocument left = JsonDocument.Parse("{\"amt\":\"5\",\"cur\":\"usd\"}"), right = JsonDocument.Parse("{\"amt\":5,\"cur\":\"USD\"}");
            Assert.Equal(ComparisonState.Equal, ComparisonCore.Evaluate(rule, new JsonRowReader(left.RootElement), new JsonRowReader(right.RootElement)).State);
            Assert.Equal(ComparisonState.Invalid, ComparisonCore.Evaluate(rule, Str("5"), Str("5")).State);
        }

        // ------------------------------------------------------------------ definitions

        [Fact]
        public void TheBuilders_AddTheRules_AndRejectBadInput_WithoutChangingTheDefinition()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddBooleanComparisonSimple("Paid", "/paid", "/paid", out string m), m);
            Assert.True(c.AddBooleanComparison("Flag", "/f", "/f", ComparisonNullPolicy.AllowBothNull, out m), m);
            Assert.True(c.AddMoneyComparisonSimple("Total", "/t", "/t", "/cur", "/cur", out m), m);
            Assert.True(c.AddMoneyComparison("Fee", "/fee", "/fee", "/cur", "/curr", "0.01", ComparisonNullPolicy.AllowBothNull, out m), m);
            Assert.True(c.GetDefinitionJson(out string before, out m), m);

            Assert.False(c.AddBooleanComparisonSimple("paid", "/x", "/x", out m)); Assert.Contains("DuplicateName", m);            // names are unique ignoring case
            Assert.False(c.AddBooleanComparison("B", "x", "/x", ComparisonNullPolicy.RequireValue, out m)); Assert.Contains("InvalidPointer", m);
            Assert.False(c.AddBooleanComparison("B", "/x", "/x", (ComparisonNullPolicy)9, out m)); Assert.Contains("UnknownEnumValue", m);
            Assert.False(c.AddMoneyComparisonSimple("M", "/a", "/a", "cur", "/c", out m)); Assert.Contains("leftCurrencyPointer", m);
            Assert.False(c.AddMoneyComparisonSimple("M", "/a", "/a", "/c", "", out m)); Assert.Contains("rightCurrencyPointer", m);
            Assert.False(c.AddMoneyComparisonSimple("M", "/a", "/a", null, "/c", out m)); Assert.Contains("InvalidPointer", m);
            Assert.False(c.AddMoneyComparison("M", "/a", "/a", "/c", "/c", "-1", ComparisonNullPolicy.RequireValue, out m)); Assert.Contains("absoluteTolerance", m);
            Assert.False(c.AddMoneyComparison("M", "/a", "/a", "/c", "/c", "1e2", ComparisonNullPolicy.RequireValue, out m)); Assert.Contains("InvalidTolerance", m);
            Assert.False(c.AddMoneyComparison("M", "/a", "/a", "/c", "/c", null, ComparisonNullPolicy.RequireValue, out m)); Assert.Contains("InvalidTolerance", m);
            Assert.True(c.GetDefinitionJson(out string after, out m), m);
            Assert.Equal(before, after);
        }

        private const string RuleDefinition = "{\"schemaVersion\":1,\"keys\":[{\"name\":\"Id\",\"leftPointer\":\"/id\",\"rightPointer\":\"/id\",\"trim\":false,\"ignoreCase\":false}],\"comparisons\":["
            + "{\"name\":\"Paid\",\"kind\":\"Boolean\",\"leftPointer\":\"/paid\",\"rightPointer\":\"/paid\",\"nullPolicy\":\"RequireValue\"},"
            + "{\"name\":\"Total\",\"kind\":\"Money\",\"leftPointer\":\"/total\",\"rightPointer\":\"/total\",\"leftCurrencyPointer\":\"/cur\",\"rightCurrencyPointer\":\"/currency\",\"absoluteTolerance\":\"0.05\",\"nullPolicy\":\"AllowBothNull\"}"
            + "],\"limits\":{\"maximumRowsPerSide\":50000,\"maximumInputCharactersPerSide\":8000000,\"maximumResults\":100000,\"maximumDifferenceDetails\":100000}}";

        [Fact]
        public void TheCanonicalJson_OfBuilderMadeRules_EqualsTheCanonicalForm_AndLoadsBack()
        {
            using var built = new ReconciliationUtils();
            Assert.True(built.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(built.AddBooleanComparisonSimple("Paid", "/paid", "/paid", out m), m);
            Assert.True(built.AddMoneyComparison("Total", "/total", "/total", "/cur", "/currency", "0.05", ComparisonNullPolicy.AllowBothNull, out m), m);
            Assert.True(built.GetDefinitionJson(out string canonical, out m), m);
            Assert.Equal(RuleDefinition, canonical);

            using var loaded = new ReconciliationUtils();
            Assert.True(loaded.ValidateDefinitionJson(RuleDefinition, out int errors, out _, out m), m);
            Assert.Equal(0, errors);
            Assert.True(loaded.LoadDefinitionJson(RuleDefinition, out m), m);
            Assert.True(loaded.GetDefinitionJson(out string again, out m), m);
            Assert.Equal(canonical, again);
        }

        [Theory]
        [InlineData("{\"name\":\"T\",\"kind\":\"Money\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"rightCurrencyPointer\":\"/c\"}", "comparisons[0].leftCurrencyPointer", "MissingProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Money\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftCurrencyPointer\":\"/c\"}", "comparisons[0].rightCurrencyPointer", "MissingProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Money\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftCurrencyPointer\":5,\"rightCurrencyPointer\":\"/c\"}", "comparisons[0].leftCurrencyPointer", "InvalidType")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Money\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftCurrencyPointer\":\"c\",\"rightCurrencyPointer\":\"/c\"}", "comparisons[0].leftCurrencyPointer", "InvalidPointer")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Money\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftCurrencyPointer\":\"/c\",\"rightCurrencyPointer\":\"/c\",\"trim\":true}", "comparisons[0].trim", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Money\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftCurrencyPointer\":\"/c\",\"rightCurrencyPointer\":\"/c\",\"absoluteTolerance\":\"-1\"}", "comparisons[0].absoluteTolerance", "InvalidTolerance")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Boolean\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"absoluteTolerance\":\"0\"}", "comparisons[0].absoluteTolerance", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Boolean\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftCurrencyPointer\":\"/c\"}", "comparisons[0].leftCurrencyPointer", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Boolean\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"trim\":true}", "comparisons[0].trim", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Decimal\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftCurrencyPointer\":\"/c\"}", "comparisons[0].leftCurrencyPointer", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Boolean\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"nullPolicy\":1}", "comparisons[0].nullPolicy", "UnknownEnumValue")]
        public void TheNewKinds_RejectMissingWrongAndForeignOptions_WithThePathOfTheProblem(string comparison, string path, string code)
        {
            string json = "{\"schemaVersion\":1,\"comparisons\":[" + comparison + "]}";
            using var c = new ReconciliationUtils();
            Assert.True(c.ValidateDefinitionJson(json, out int errors, out string report, out string m), m);
            Assert.True(errors > 0);
            using JsonDocument doc = JsonDocument.Parse(report);
            Assert.Contains(doc.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetProperty("path").GetString() == path && e.GetProperty("code").GetString() == code);
            Assert.False(c.LoadDefinitionJson(json, out _));
        }

        // ------------------------------------------------------------------ end to end

        private static ReconciliationUtils Reconciler()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson(RuleDefinition, out string m), m);
            return c;
        }

        [Fact]
        public void EndToEnd_EveryOutcomeAppears_WithCurrencyDetailInTheResultJson()
        {
            using var c = Reconciler();
            const string left = "[{\"id\":\"1\",\"paid\":true,\"total\":\"10.00\",\"cur\":\"USD\"},"
                              + "{\"id\":\"2\",\"paid\":true,\"total\":\"10.00\",\"cur\":\"USD\"},"
                              + "{\"id\":\"3\",\"paid\":true,\"total\":\"10.00\",\"cur\":\"USD\"},"
                              + "{\"id\":\"4\",\"paid\":\"yes\",\"total\":\"10.00\",\"cur\":\"USD\"},"
                              + "{\"id\":\"5\",\"paid\":true,\"total\":\"10.00\",\"cur\":\"USD\"}]";
            const string right = "[{\"id\":\"1\",\"paid\":true,\"total\":10.04,\"currency\":\" usd\"},"
                               + "{\"id\":\"2\",\"paid\":false,\"total\":10.00,\"currency\":\"USD\"},"
                               + "{\"id\":\"3\",\"paid\":true,\"total\":10.00,\"currency\":\"EUR\"},"
                               + "{\"id\":\"4\",\"paid\":true,\"total\":10.00,\"currency\":\"USD\"},"
                               + "{\"id\":\"5\",\"paid\":true,\"total\":10.00,\"currency\":\"US\"}]";
            Assert.True(c.ReconcileJson(left, right, out int count, out string m), m);
            Assert.Equal(4, count);                                                        // id 1 matches: same currency once trimmed, within 0.05
            var seen = new List<string>();
            while (c.TryReadNextException(out bool has, out _, out string kind, out string key, out _, out _, out string reason, out int diffs, out _) && has)
                seen.Add(key + " " + kind + " " + reason + " d" + diffs);
            Assert.Equal(new[]
            {
                "[\"2\"] Different BooleanMismatch d1",
                "[\"3\"] Different CurrencyMismatch d1",
                "[\"4\"] InvalidComparison InvalidType d1",
                "[\"5\"] InvalidComparison InvalidCurrency d1"
            }, seen);

            Assert.True(c.GetResultJson("r000003", out string json, out m), m);            // the currency mismatch: both codes visible, no delta
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement d = doc.RootElement.GetProperty("differences")[0];
            Assert.Equal("Total", d.GetProperty("rule").GetString());
            Assert.Equal("Money", d.GetProperty("ruleKind").GetString());
            Assert.Equal("USD", d.GetProperty("leftCurrency").GetString());
            Assert.Equal("EUR", d.GetProperty("rightCurrency").GetString());
            Assert.Equal(JsonValueKind.Null, d.GetProperty("delta").ValueKind);
            Assert.Equal("10 USD", d.GetProperty("leftInterpreted").GetString());

            Assert.True(c.GetResultJson("r000002", out json, out m), m);                    // a Boolean difference has no currency properties
            using JsonDocument boolDoc = JsonDocument.Parse(json);
            Assert.False(boolDoc.RootElement.GetProperty("differences")[0].TryGetProperty("leftCurrency", out _));
        }

        [Fact]
        public void EndToEnd_ACurrencyFieldMissingOnOneSide_IsMissingField()
        {
            using var c = Reconciler();
            Assert.True(c.ReconcileJson("[{\"id\":\"1\",\"paid\":true,\"total\":1,\"cur\":\"USD\"}]", "[{\"id\":\"1\",\"paid\":true,\"total\":1}]", out int count, out string m), m);
            Assert.Equal(1, count);
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out string reason, out _, out _));
            Assert.Equal("MissingField", reason);
            Assert.True(c.TryReadNextDifference(out _, out _, out _, out _, out string rightValueJson, out _, out _));
            Assert.Equal("1", rightValueJson);
        }

        // ------------------------------------------------------------------ DataTables

        [Fact]
        public void DataTables_CurrencyColumnsAreColumns_AndBooleanCellsAreBooleans()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Id", out string m), m);
            Assert.True(c.AddMoneyComparison("Total", "/Total", "/Total", "/Cur", "/Currency", "0", ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddBooleanComparisonSimple("Paid", "/Paid", "/Paid", out m), m);
            var left = new DataTable(); left.Columns.Add("Id", typeof(string)); left.Columns.Add("Total", typeof(decimal)); left.Columns.Add("Cur", typeof(string)); left.Columns.Add("Paid", typeof(bool));
            var right = new DataTable(); right.Columns.Add("Id", typeof(string)); right.Columns.Add("Total", typeof(decimal)); right.Columns.Add("Currency", typeof(string)); right.Columns.Add("Paid", typeof(bool));
            left.Rows.Add("a", 10.5m, "USD", true); left.Rows.Add("b", 10.5m, "USD", true);
            right.Rows.Add("a", 10.50m, "usd", true); right.Rows.Add("b", 10.5m, "GBP", false);
            Assert.True(c.ReconcileDataTables(left, right, out int count, out m), m);
            Assert.Equal(1, count);
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out string reason, out int diffs, out _));
            Assert.Equal(("CurrencyMismatch", 2), (reason, diffs));                          // the currency mismatch and the Boolean difference are both kept

            using var deep = new ReconciliationUtils();
            Assert.True(deep.AddKeyMappingSimple("Id", "/Id", "/Id", out m), m);
            Assert.True(deep.AddMoneyComparisonSimple("Total", "/Total", "/Total", "/Cur", "/a/b", out m), m);
            Assert.False(deep.ReconcileDataTables(left, right, out _, out m));
            Assert.Contains("exactly one column", m);

            var noCurrency = new DataTable(); noCurrency.Columns.Add("Id", typeof(string)); noCurrency.Columns.Add("Total", typeof(decimal)); noCurrency.Rows.Add("a", 1m);
            Assert.True(c.ReconcileDataTables(left, noCurrency, out count, out m), m);      // a missing column is missing on every row, as in JSON
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out reason, out _, out _));
            Assert.Equal("MissingField", reason);
        }
    }
}
