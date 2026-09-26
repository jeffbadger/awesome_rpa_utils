using System.Globalization;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class ComparisonCoreTests
    {
        // ---------------------------------------------------------------- helpers

        private static ComparisonDef Text(bool trim = false, bool ignoreCase = false, ComparisonNullPolicy policy = ComparisonNullPolicy.RequireValue)
        {
            var d = new ReconciliationDefinition();
            Assert.Null(d.TryAddText("Rule", "/l", "/r", trim, ignoreCase, policy));
            return d.Comparisons[0];
        }

        private static ComparisonDef Dec(string tolerance = "0", ComparisonNullPolicy policy = ComparisonNullPolicy.RequireValue)
        {
            var d = new ReconciliationDefinition();
            Assert.Null(d.TryAddDecimal("Rule", "/l", "/r", tolerance, policy));
            return d.Comparisons[0];
        }

        private static FieldValue S(string s) => new FieldValue(FieldKind.String, s);
        private static FieldValue I(string token) => new FieldValue(FieldKind.Integer, token);
        private static FieldValue N(string token) => new FieldValue(FieldKind.Number, token);
        private static FieldValue B(bool b) => new FieldValue(FieldKind.Boolean, b ? "true" : "false");
        private static readonly FieldValue Missing = FieldValue.Missing;
        private static readonly FieldValue Null = FieldValue.Null;
        private static readonly FieldValue Obj = FieldValue.UnsupportedBecause("an object");
        private static readonly FieldValue Arr = FieldValue.UnsupportedBecause("an array");

        private static ComparisonOutcome Eval(ComparisonDef rule, FieldValue left, FieldValue right) => ComparisonCore.Evaluate(rule, left, right);

        private static void AssertEqual(ComparisonOutcome o) { Assert.Equal(ComparisonState.Equal, o.State); Assert.Null(o.ReasonCode); Assert.Null(o.Explanation); }
        private static void AssertDifferent(ComparisonOutcome o, string code) { Assert.Equal(ComparisonState.Different, o.State); Assert.Equal(code, o.ReasonCode); Assert.False(string.IsNullOrWhiteSpace(o.Explanation)); }
        private static void AssertInvalid(ComparisonOutcome o, string code) { Assert.Equal(ComparisonState.Invalid, o.State); Assert.Equal(code, o.ReasonCode); Assert.False(string.IsNullOrWhiteSpace(o.Explanation)); }

        // ---------------------------------------------------------------- text

        [Fact]
        public void Text_IsExactByDefault()
        {
            AssertEqual(Eval(Text(), S("Paid"), S("Paid")));
            AssertDifferent(Eval(Text(), S("Paid"), S("paid")), "TextMismatch");           // case-sensitive
            AssertDifferent(Eval(Text(), S("Paid"), S(" Paid")), "TextMismatch");          // no trimming
            AssertDifferent(Eval(Text(), S("Paid"), S("Paid ")), "TextMismatch");
        }

        [Fact]
        public void Text_TrimAndIgnoreCase_AreIndependentOptions()
        {
            AssertEqual(Eval(Text(trim: true), S("  Paid\t"), S("Paid")));
            AssertDifferent(Eval(Text(trim: true), S("  Paid "), S("paid")), "TextMismatch");        // trimming does not ignore case
            AssertEqual(Eval(Text(ignoreCase: true), S("PAID"), S("paid")));
            AssertDifferent(Eval(Text(ignoreCase: true), S(" PAID"), S("paid")), "TextMismatch");     // ignoring case does not trim
            AssertEqual(Eval(Text(trim: true, ignoreCase: true), S(" PAID "), S("paid")));
        }

        [Fact]
        public void Text_IsOrdinal_NoNormalizationNoCollationNoCollapsing()
        {
            AssertDifferent(Eval(Text(), S("\u00e9"), S("e\u0301")), "TextMismatch");                  // precomposed vs combining: different code points
            AssertDifferent(Eval(Text(), S("a  b"), S("a b")), "TextMismatch");                        // whitespace is not collapsed
            AssertDifferent(Eval(Text(), S("a-b"), S("ab")), "TextMismatch");                          // punctuation is not removed
            AssertDifferent(Eval(Text(ignoreCase: true), S("STRASSE"), S("stra\u00dfe")), "TextMismatch");   // no locale case mapping (ss vs sharp s)
        }

        [Theory]
        [InlineData("tr-TR")]
        [InlineData("az-Latn-AZ")]
        [InlineData("de-DE")]
        [InlineData("ar-SA")]
        public void Text_DoesNotDependOnTheCurrentCulture(string cultureName)
        {
            CultureScope.Run(cultureName, () =>
            {
                ComparisonDef rule = Text(ignoreCase: true);
                AssertEqual(Eval(rule, S("TITLE"), S("title")));                                 // the Turkish-I trap: I and i still match
                AssertEqual(Eval(rule, S("I"), S("i")));
                AssertDifferent(Eval(rule, S("\u0130"), S("i")), "TextMismatch");               // dotted capital I is not i
                AssertDifferent(Eval(rule, S("I"), S("\u0131")), "TextMismatch");               // dotless i is not I
            });
        }

        [Fact]
        public void Text_AnEmptyString_IsARealValue_NotNullNotMissing()
        {
            AssertEqual(Eval(Text(), S(""), S("")));
            AssertDifferent(Eval(Text(), S(""), S("x")), "TextMismatch");
            AssertInvalid(Eval(Text(), S(""), Missing), "MissingField");
            AssertInvalid(Eval(Text(), S(""), Null), "NullNotAllowed");
            AssertDifferent(Eval(Text(trim: true), S("   "), S("x")), "TextMismatch");               // blank after trimming is "" , still a value
            AssertEqual(Eval(Text(trim: true), S("   "), S("")));
        }

        [Theory]
        [InlineData("integer")]
        [InlineData("number")]
        [InlineData("boolean")]
        [InlineData("object")]
        [InlineData("array")]
        public void Text_RequiresStrings_NothingConvertsImplicitly(string kind)
        {
            FieldValue wrong = kind switch { "integer" => I("5"), "number" => N("5.5"), "boolean" => B(true), "object" => Obj, _ => Arr };
            ComparisonOutcome o = Eval(Text(), S("5"), wrong);
            AssertInvalid(o, "InvalidType");
            Assert.Contains("needs a string", o.Explanation);
            AssertInvalid(Eval(Text(), wrong, S("5")), "InvalidType");
        }

        [Fact]
        public void Text_TheExplanation_NeverQuotesTheValues()
        {
            ComparisonOutcome o = Eval(Text(), S("secret-A"), S("secret-B"));
            Assert.DoesNotContain("secret", o.Explanation);
        }

        // ---------------------------------------------------------------- null policy (both rule kinds)

        [Theory]
        [InlineData("Text")]
        [InlineData("Decimal")]
        public void RequireValue_ANullOnEitherSide_IsInvalid(string kind)
        {
            ComparisonDef rule = kind == "Text" ? Text() : Dec();
            FieldValue value = kind == "Text" ? S("5") : I("5");
            AssertInvalid(Eval(rule, Null, value), "NullNotAllowed");
            AssertInvalid(Eval(rule, value, Null), "NullNotAllowed");
            AssertInvalid(Eval(rule, Null, Null), "NullNotAllowed");
        }

        [Theory]
        [InlineData("Text")]
        [InlineData("Decimal")]
        public void AllowBothNull_TwoNullsAreEqual_ANullAgainstAValueIsADifference(string kind)
        {
            ComparisonDef rule = kind == "Text" ? Text(policy: ComparisonNullPolicy.AllowBothNull) : Dec(policy: ComparisonNullPolicy.AllowBothNull);
            FieldValue value = kind == "Text" ? S("5") : I("5");
            string code = kind == "Text" ? "TextMismatch" : "DecimalMismatch";
            AssertEqual(Eval(rule, Null, Null));
            ComparisonOutcome left = Eval(rule, Null, value);
            AssertDifferent(left, code);
            Assert.Contains("null", left.Explanation);
            AssertDifferent(Eval(rule, value, Null), code);
        }

        [Theory]
        [InlineData("Text")]
        [InlineData("Decimal")]
        public void AMissingField_IsAlwaysInvalid_EvenWhenBothAreMissing_AndUnderAnyNullPolicy(string kind)
        {
            foreach (ComparisonNullPolicy policy in new[] { ComparisonNullPolicy.RequireValue, ComparisonNullPolicy.AllowBothNull })
            {
                ComparisonDef rule = kind == "Text" ? Text(policy: policy) : Dec(policy: policy);
                FieldValue value = kind == "Text" ? S("5") : I("5");
                AssertInvalid(Eval(rule, Missing, value), "MissingField");
                AssertInvalid(Eval(rule, value, Missing), "MissingField");
                AssertInvalid(Eval(rule, Missing, Missing), "MissingField");
                AssertInvalid(Eval(rule, Missing, Null), "MissingField");       // missing is not null, even next to a null
                AssertInvalid(Eval(rule, Null, Missing), "MissingField");
            }
        }

        [Fact]
        public void AllowBothNull_ANullNextToAnInvalidValue_IsInvalid_NotADifference()
        {
            AssertInvalid(Eval(Text(policy: ComparisonNullPolicy.AllowBothNull), Null, I("5")), "InvalidType");
            AssertInvalid(Eval(Dec(policy: ComparisonNullPolicy.AllowBothNull), Null, S("abc")), "InvalidDecimal");
            AssertInvalid(Eval(Dec(policy: ComparisonNullPolicy.AllowBothNull), B(true), Null), "InvalidType");
        }

        [Fact]
        public void NullMissingEmptyAndZero_AreNeverTreatedAsEquivalent()
        {
            ComparisonDef allow = Dec(policy: ComparisonNullPolicy.AllowBothNull);
            AssertDifferent(Eval(allow, Null, I("0")), "DecimalMismatch");            // null is not zero
            AssertInvalid(Eval(allow, Null, Missing), "MissingField");                  // null is not missing
            AssertInvalid(Eval(allow, S(""), I("0")), "InvalidDecimal");                // empty is not zero
            ComparisonDef text = Text(policy: ComparisonNullPolicy.AllowBothNull);
            AssertDifferent(Eval(text, Null, S("")), "TextMismatch");                   // null is not empty
        }

        [Fact]
        public void BothSidesInvalid_TheLeftProblemNamesTheReason_AndTheExplanationCoversBoth()
        {
            ComparisonOutcome o = Eval(Text(), Missing, B(true));
            AssertInvalid(o, "MissingField");
            Assert.Contains("left field is missing", o.Explanation);
            Assert.Contains("right value cannot be compared", o.Explanation);
            // a missing field outranks a wrong type, and a wrong type outranks a null; within one kind of problem the left side names the reason
            AssertInvalid(Eval(Text(), B(true), Missing), "MissingField");
            AssertInvalid(Eval(Text(), Null, B(true)), "InvalidType");
            AssertInvalid(Eval(Text(), B(true), Null), "InvalidType");
            AssertInvalid(Eval(Text(), Null, Missing), "MissingField");
            AssertInvalid(Eval(Dec(), S("abc"), B(true)), "InvalidDecimal");           // both unusable: the left one names it
            AssertInvalid(Eval(Dec(), B(true), S("abc")), "InvalidType");
        }

        // ---------------------------------------------------------------- what is reported about the values

        [Fact]
        public void TheOriginalValues_AreReportedAsJsonFragments_SoMissingNullEmptyAndZeroStayDistinct()
        {
            ComparisonOutcome o = Eval(Dec(policy: ComparisonNullPolicy.AllowBothNull), Missing, Null);
            Assert.False(o.LeftPresent);
            Assert.True(o.RightPresent);
            Assert.Null(o.LeftValueJson);                        // missing: a null string
            Assert.Equal("null", o.RightValueJson);              // present JSON null: the text null

            Assert.Equal("\"\"", Eval(Text(), S(""), S("x")).LeftValueJson);                 // empty string
            Assert.Equal("0", Eval(Dec(), I("0"), I("1")).LeftValueJson);                    // zero
            Assert.Equal("1.50", Eval(Dec(), N("1.50"), I("2")).LeftValueJson);              // the original token, not the normalized number
            Assert.Equal("\"1.50\"", Eval(Dec(), S("1.50"), I("2")).LeftValueJson);          // a numeric string stays a string
            Assert.Equal("true", Eval(Dec(), B(true), I("2")).LeftValueJson);
            Assert.Equal("\"a\\\"b\\\\c\"", Eval(Text(), S("a\"b\\c"), S("x")).LeftValueJson);   // properly escaped JSON
            Assert.Equal("\"(an object)\"", Eval(Text(), Obj, S("x")).LeftValueJson);          // a structure is described, never copied
            Assert.Equal("\"(an array)\"", Eval(Text(), Arr, S("x")).LeftValueJson);
        }

        [Fact]
        public void TheInterpretedValues_AreTheTrimmedTextOrTheNormalizedNumber()
        {
            ComparisonOutcome t = Eval(Text(trim: true), S("  Paid "), S("Paid"));
            Assert.Equal("Paid", t.LeftInterpreted);
            Assert.Equal("Paid", t.RightInterpreted);

            ComparisonOutcome d = Eval(Dec(), N("1.50"), S("+2.500"));
            Assert.Equal("1.5", d.LeftInterpreted);
            Assert.Equal("2.5", d.RightInterpreted);

            Assert.Null(Eval(Dec(), S("abc"), I("1")).LeftInterpreted);                       // nothing to interpret
        }

        // ---------------------------------------------------------------- decimal

        [Fact]
        public void Decimal_TheToleranceIsInclusive_AtTheBoundary()
        {
            ComparisonDef rule = Dec("0.01");
            AssertEqual(Eval(rule, S("100.00"), S("100.01")));                     // exactly the tolerance: equal
            AssertEqual(Eval(rule, S("100.01"), S("100.00")));
            AssertDifferent(Eval(rule, S("100.00"), S("100.02")), "DecimalMismatch");    // one unit of the last place beyond
            AssertDifferent(Eval(rule, S("100.02"), S("100.00")), "DecimalMismatch");
            ComparisonOutcome tiny = Eval(rule, S("1.00"), S("1.0100000000000000000000000001"));     // the smallest representable amount beyond the tolerance
            AssertDifferent(tiny, "DecimalMismatch");
            Assert.Equal("0.0100000000000000000000000001", tiny.Delta);
            AssertEqual(Eval(rule, S("100.00"), S("100.00")));
        }

        [Fact]
        public void Decimal_ZeroTolerance_MeansExact()
        {
            AssertEqual(Eval(Dec(), I("125"), N("125.00")));
            AssertDifferent(Eval(Dec(), N("125.00"), N("125.01")), "DecimalMismatch");
            AssertDifferent(Eval(Dec(), N("0.0000000000000000000000000001"), I("0")), "DecimalMismatch");
        }

        [Fact]
        public void Decimal_TheDelta_IsRightMinusLeft_AndExact()
        {
            ComparisonOutcome o = Eval(Dec(), S("250.00"), S("249"));
            AssertDifferent(o, "DecimalMismatch");
            Assert.Equal("-1", o.Delta);
            Assert.Equal("1", Eval(Dec(), S("249"), S("250.00")).Delta);
            Assert.Equal("0.2", Eval(Dec(), S("0.1"), S("0.3")).Delta);                       // no binary floating-point residue
            Assert.Equal("0", Eval(Dec("1"), S("5"), S("5.0")).Delta);
            Assert.Contains("differ by -1", o.Explanation);
            Assert.Contains("tolerance 0", o.Explanation);
        }

        [Fact]
        public void Decimal_TheDeltaIsReportedEvenWhenTheValuesAreWithinTolerance()
        {
            ComparisonOutcome o = Eval(Dec("1"), S("10.00"), S("10.50"));
            AssertEqual(o);
            Assert.Equal("0.5", o.Delta);
        }

        [Fact]
        public void Decimal_NumbersAndNumericStrings_CompareEqually()
        {
            AssertEqual(Eval(Dec(), N("12.5"), S("12.50")));
            AssertEqual(Eval(Dec(), I("100"), S("100")));
            AssertEqual(Eval(Dec(), N("1e2"), I("100")));                   // exponent form
            AssertEqual(Eval(Dec(), N("1E-2"), S("0.01")));
            AssertEqual(Eval(Dec(), N("-0.0"), I("0")));                     // signed zero
            AssertEqual(Eval(Dec(), S("-0"), S("0")));
            AssertEqual(Eval(Dec(), S("+5"), I("5")));
        }

        [Fact]
        public void Decimal_NegativeValues_AndTheirTolerance()
        {
            ComparisonDef rule = Dec("0.5");
            AssertEqual(Eval(rule, S("-10.0"), S("-10.5")));
            AssertDifferent(Eval(rule, S("-10.0"), S("-10.6")), "DecimalMismatch");
            AssertDifferent(Eval(rule, S("-1"), S("1")), "DecimalMismatch");
            Assert.Equal("2", Eval(rule, S("-1"), S("1")).Delta);
        }

        [Fact]
        public void Decimal_OpposingExtremes_DoNotOverflow_AndReportAnExactDelta()
        {
            const string max = "79228162514264337593543950335";
            ComparisonOutcome o = Eval(Dec(), S("-" + max), S(max));                                // right - left = 2 * max, beyond any decimal
            AssertDifferent(o, "DecimalMismatch");
            Assert.Equal("158456325028528675187087900670", o.Delta);
            Assert.Equal("-158456325028528675187087900670", Eval(Dec(), S(max), S("-" + max)).Delta);
            AssertEqual(Eval(Dec(max), S("-" + max), S("0")));                                       // tolerance may itself be the maximum
            AssertEqual(Eval(Dec(), S(max), S(max)));
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData(" 1")]
        [InlineData("1,000")]
        [InlineData("$5")]
        [InlineData("1e3")]                                       // text has no exponent
        [InlineData("0.00000000000000000000000000001")]           // 29 places: would have to be rounded
        [InlineData("79228162514264337593543950336")]             // out of range
        public void Decimal_AnUnusableString_IsInvalid_NeverRounded(string text)
        {
            ComparisonOutcome o = Eval(Dec(), S(text), I("1"));
            AssertInvalid(o, "InvalidDecimal");
            Assert.Contains("left value cannot be compared", o.Explanation);
            AssertInvalid(Eval(Dec(), I("1"), S(text)), "InvalidDecimal");
        }

        [Theory]
        [InlineData("1e29")]
        [InlineData("1e-29")]
        [InlineData("1e999999")]
        [InlineData("1.00000000000000000000000000001")]
        [InlineData("123456789012345678901234567890")]
        public void Decimal_AJsonNumberThatCannotBeHeldExactly_IsInvalid(string token)
        {
            AssertInvalid(Eval(Dec(), N(token), I("1")), "InvalidDecimal");
        }

        [Theory]
        [InlineData("boolean")]
        [InlineData("object")]
        [InlineData("array")]
        public void Decimal_OtherTypes_AreInvalidType(string kind)
        {
            FieldValue wrong = kind switch { "boolean" => B(true), "object" => Obj, _ => Arr };
            ComparisonOutcome o = Eval(Dec(), I("1"), wrong);
            AssertInvalid(o, "InvalidType");
            Assert.Contains("needs a number or numeric text", o.Explanation);
        }

        [Theory]
        [InlineData("01")]
        [InlineData("-01")]
        [InlineData("00.5")]
        public void Decimal_AMalformedNumberToken_FromAnyRowSource_IsInvalid_NotComparedAsAValidNumber(string token)
        {
            // JsonRowReader only hands over well-formed tokens, but a row reader for another source (a DataTable) might not
            ComparisonOutcome o = Eval(Dec(), N(token), I("1"));
            AssertInvalid(o, "InvalidDecimal");
            Assert.Contains("leading zero", o.Explanation);
        }

        [Fact]
        public void Decimal_TheToleranceIsParsedOnce_ButAChangedToleranceIsHonored()
        {
            ComparisonDef rule = Dec("0.01");
            AssertEqual(Eval(rule, S("1.00"), S("1.01")));
            AssertEqual(Eval(rule, S("1.00"), S("1.01")));         // the cached value is reused
            rule.AbsoluteTolerance = "0";
            AssertDifferent(Eval(rule, S("1.00"), S("1.01")), "DecimalMismatch");   // the cache is keyed on the text
            rule.AbsoluteTolerance = "1e3";
            AssertInvalid(Eval(rule, S("1.00"), S("1.01")), "InvalidDecimal");
            rule.AbsoluteTolerance = "0.5";
            AssertEqual(Eval(rule, S("1.00"), S("1.50")));         // and it recovers
        }

        [Fact]
        public void Decimal_ARuleWithAnUnusableTolerance_IsInvalid_NotAnException()
        {
            // not reachable through the public API (tolerances are validated when added), but the core must not throw for a broken rule
            ComparisonDef broken = Dec();
            broken.AbsoluteTolerance = "1e3";
            AssertInvalid(Eval(broken, I("1"), I("1")), "InvalidDecimal");
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("ar-SA")]
        [InlineData("tr-TR")]
        public void Decimal_DoesNotDependOnTheCurrentCulture(string cultureName)
        {
            CultureScope.Run(cultureName, () =>
            {
                ComparisonOutcome o = Eval(Dec("0.05"), S("1234.50"), S("1234.56"));
                AssertDifferent(o, "DecimalMismatch");
                Assert.Equal("0.06", o.Delta);                                    // a dot, never a locale comma
                Assert.Equal("1234.5", o.LeftInterpreted);
                AssertInvalid(Eval(Dec(), S("1,5"), S("1.5")), "InvalidDecimal");  // a comma is never a decimal point
                AssertInvalid(Eval(Dec(), S("\u0661\u0662\u0663"), I("123")), "InvalidDecimal");   // Arabic-Indic digits are not digits here
            });
        }

        [Fact]
        public void Decimal_TheExplanation_NamesTheDeltaAndTheTolerance()
        {
            ComparisonOutcome o = Eval(Dec("0.01"), S("100.00"), S("100.25"));
            Assert.Contains("0.25", o.Explanation);
            Assert.Contains("0.01", o.Explanation);
        }

        // ---------------------------------------------------------------- against real JSON rows

        private static ComparisonOutcome EvalJson(ComparisonDef rule, string leftRow, string rightRow)
        {
            using JsonDocument l = JsonDocument.Parse(leftRow);
            using JsonDocument r = JsonDocument.Parse(rightRow);
            return ComparisonCore.Evaluate(rule, new JsonRowReader(l.RootElement.Clone()), new JsonRowReader(r.RootElement.Clone()));
        }

        [Fact]
        public void TheRuleReadsItsOwnPointerOnEachSide_FromRealRows()
        {
            var d = new ReconciliationDefinition();
            Assert.Null(d.TryAddDecimal("Amount", "/invoice/amount", "/paid", "0.01", ComparisonNullPolicy.RequireValue));
            ComparisonDef rule = d.Comparisons[0];

            AssertEqual(EvalJson(rule, "{\"invoice\":{\"amount\":125.00}}", "{\"paid\":\"125.01\"}"));
            ComparisonOutcome o = EvalJson(rule, "{\"invoice\":{\"amount\":250.00}}", "{\"paid\":249}");
            AssertDifferent(o, "DecimalMismatch");
            Assert.Equal("250.00", o.LeftValueJson);
            Assert.Equal("-1", o.Delta);
            AssertInvalid(EvalJson(rule, "{\"invoice\":{}}", "{\"paid\":1}"), "MissingField");
            AssertInvalid(EvalJson(rule, "{\"invoice\":{\"amount\":\"unknown\"}}", "{\"paid\":1}"), "InvalidDecimal");
            AssertInvalid(EvalJson(rule, "{\"invoice\":{\"amount\":null}}", "{\"paid\":1}"), "NullNotAllowed");
        }

        [Fact]
        public void ARowThatIsNotAnObject_IsMissingEveryField()
        {
            AssertInvalid(EvalJson(Text(), "5", "{\"r\":\"x\"}"), "MissingField");
        }

        [Fact]
        public void AFieldHoldingAStructure_IsInvalidType_AndIsDescribedNotCopied()
        {
            ComparisonOutcome o = EvalJson(Text(), "{\"l\":{\"secret\":\"do-not-copy\"}}", "{\"r\":\"x\"}");
            AssertInvalid(o, "InvalidType");
            Assert.DoesNotContain("do-not-copy", o.LeftValueJson + o.Explanation);
            Assert.Equal("\"(an object)\"", o.LeftValueJson);
        }

        [Fact]
        public void AnEscapedLoneSurrogateValue_IsInvalidType_NeverAnException()
        {
            ComparisonOutcome o = EvalJson(Text(), "{\"l\":\"\\uD800\"}", "{\"r\":\"x\"}");
            AssertInvalid(o, "InvalidType");
            Assert.Equal("\"(text that is not valid)\"", o.LeftValueJson);
        }

        [Fact]
        public void EveryRuleKindAndOutcome_HasTheRuleNameAndKind()
        {
            ComparisonOutcome o = Eval(Dec(), I("1"), I("2"));
            Assert.Equal("Rule", o.RuleName);
            Assert.Equal(RuleKind.Decimal, o.Kind);
            Assert.Equal(RuleKind.Text, Eval(Text(), S("a"), S("a")).Kind);
        }
    }
}
