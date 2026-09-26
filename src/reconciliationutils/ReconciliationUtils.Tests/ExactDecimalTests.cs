using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class ExactDecimalTests
    {
        private static ExactDecimal Text(string text)
        {
            Assert.True(ExactDecimal.TryParseText(text, out ExactDecimal value, out string error), text + ": " + error);
            return value;
        }

        private static ExactDecimal Json(string token)
        {
            Assert.True(ExactDecimal.TryParseJsonNumber(token, out ExactDecimal value, out string error), token + ": " + error);
            return value;
        }

        // ---------------------------------------------------------------- text grammar

        [Theory]
        [InlineData("0", "0")]
        [InlineData("-0", "0")]                     // signed zero is zero, and prints without a sign
        [InlineData("+0", "0")]
        [InlineData("0.000", "0")]
        [InlineData("00012", "12")]                 // leading zeros
        [InlineData("1.50", "1.5")]                 // trailing fractional zeros are not significant
        [InlineData("100", "100")]
        [InlineData("-125.00", "-125")]
        [InlineData("+7.25", "7.25")]
        [InlineData("0.01", "0.01")]
        [InlineData("0.0000000000000000000000000001", "0.0000000000000000000000000001")]      // 28 decimal places: the most a decimal holds
        [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]        // decimal.MaxValue
        [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335")]
        [InlineData("7.9228162514264337593543950335", "7.9228162514264337593543950335")]      // 29 significant digits, 28 places
        [InlineData("1.2345678901234567890123456789", "1.2345678901234567890123456789")]
        public void ValidText_ParsesExactly_AndPrintsInPlainInvariantForm(string text, string expected)
        {
            Assert.Equal(expected, Text(text).ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData(" 1")]
        [InlineData("1 ")]
        [InlineData("1,000")]                       // no grouping separators
        [InlineData("1 000")]
        [InlineData("1,5")]                         // and no comma decimal
        [InlineData("$1")]
        [InlineData("1$")]
        [InlineData("1e3")]                         // text has no exponent
        [InlineData("1E3")]
        [InlineData(".5")]
        [InlineData("5.")]
        [InlineData("+")]
        [InlineData("-")]
        [InlineData("--1")]
        [InlineData("+-1")]
        [InlineData("1.2.3")]
        [InlineData("abc")]
        [InlineData("１２３")]                       // full-width digits are not ASCII digits
        [InlineData("٣")]                            // Arabic-Indic digit
        [InlineData("0x10")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("1_000")]
        public void InvalidText_IsRejected_WithAReason(string text)
        {
            Assert.False(ExactDecimal.TryParseText(text, out _, out string error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void NullText_IsRejected()
        {
            Assert.False(ExactDecimal.TryParseText(null, out _, out string error));
            Assert.Contains("empty", error);
        }

        // ---------------------------------------------------------------- never rounds, never overflows

        [Theory]
        [InlineData("0.00000000000000000000000000001", "decimal places")]                         // 29 decimal places
        [InlineData("1.00000000000000000000000000001", "decimal places")]                         // 29 places, so it would have to be rounded
        [InlineData("79228162514264337593543950336", "too large")]                                // decimal.MaxValue + 1
        [InlineData("-79228162514264337593543950336", "too large")]
        [InlineData("100000000000000000000000000000", "too large")]                               // 1e29
        [InlineData("79228162514264337593543950335.5", "too large")]                              // one place past the largest value
        [InlineData("1.23456789012345678901234567891", "decimal places")]                         // 29 decimal places, the last not a zero
        [InlineData("12345678901234567890123456789012345678901234567890", "too large")]
        public void AValueThatCannotBeHeldExactly_IsRejected_NotRounded(string text, string expectedReason)
        {
            Assert.False(ExactDecimal.TryParseText(text, out _, out string error));
            Assert.Contains(expectedReason, error);       // and it says which limit it hit
        }

        [Fact]
        public void TrailingZerosBeyondThePrecision_AreNotSignificant_SoTheValueIsStillAccepted()
        {
            // 30 fractional digits, but the last two are zeros: exactly 1.2345678901234567890123456789
            Assert.Equal("1.2345678901234567890123456789", Text("1.234567890123456789012345678900").ToString());
            Assert.Equal("1", Text("1." + new string('0', 200)).ToString());
        }

        [Fact]
        public void TheTextLengthIsBounded()
        {
            Assert.False(ExactDecimal.TryParseText(new string('1', 257), out _, out string error));
            Assert.Contains("longer than 256", error);
            Assert.True(ExactDecimal.TryParseText("0." + new string('0', 254), out ExactDecimal zero, out _));   // 256 characters, all zeros
            Assert.True(zero.IsZero);
        }

        // ---------------------------------------------------------------- JSON numbers (exponents only when exact)

        [Theory]
        [InlineData("0", "0")]
        [InlineData("-0", "0")]
        [InlineData("-0.0", "0")]
        [InlineData("12", "12")]
        [InlineData("-12.5", "-12.5")]
        [InlineData("1e2", "100")]
        [InlineData("1E2", "100")]
        [InlineData("1e+2", "100")]
        [InlineData("1.5e1", "15")]
        [InlineData("15e-1", "1.5")]
        [InlineData("1e-2", "0.01")]
        [InlineData("125e-2", "1.25")]
        [InlineData("1.25e2", "125")]
        [InlineData("0.5e1", "5")]
        [InlineData("100e-2", "1")]
        [InlineData("1e28", "10000000000000000000000000000")]
        [InlineData("7.9228162514264337593543950335e28", "79228162514264337593543950335")]
        [InlineData("1e-28", "0.0000000000000000000000000001")]
        [InlineData("0e999999", "0")]                         // zero is zero whatever the exponent
        [InlineData("0.0e-999999", "0")]
        public void AJsonNumber_IsParsedExactly_ExponentsIncluded(string token, string expected)
        {
            Assert.Equal(expected, Json(token).ToString());
        }

        [Theory]
        [InlineData("1e29")]                        // too large
        [InlineData("1e-29")]                       // too many places
        [InlineData("1.5e-28")]                     // 29 places
        [InlineData("1e999999")]                    // an absurd exponent is refused without computing it
        [InlineData("1e-999999")]
        [InlineData("1e99999999999999999999999999999999999999")]   // an exponent that does not fit any integer type
        [InlineData("123456789012345678901234567891e-5")]         // 30 significant digits, the last one not a zero
        [InlineData("79228162514264337593543950336e0")]
        public void AJsonNumberThatCannotBeHeldExactly_IsRejected(string token)
        {
            Assert.False(ExactDecimal.TryParseJsonNumber(token, out _, out string error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Theory]
        [InlineData("01")]                          // a leading zero before another digit is not JSON
        [InlineData("-01")]
        [InlineData("00")]
        [InlineData("-00")]
        [InlineData("00.5")]
        [InlineData("007")]
        [InlineData("01e2")]
        [InlineData("-0123.5")]
        public void ALeadingZero_IsNotAJsonNumber(string token)
        {
            Assert.False(ExactDecimal.TryParseJsonNumber(token, out _, out string error));
            Assert.Contains("leading zero", error);
        }

        [Theory]
        [InlineData("0", "0")]
        [InlineData("-0", "0")]
        [InlineData("0.5", "0.5")]
        [InlineData("-0.05", "-0.05")]
        [InlineData("10", "10")]                    // a zero that is not leading is fine
        [InlineData("100.05", "100.05")]
        [InlineData("0e5", "0")]
        [InlineData("0.0", "0")]
        [InlineData("0.00e1", "0")]
        [InlineData("10e-1", "1")]
        public void ZerosThatAreNotLeadingDigits_AreFine(string token, string expected)
        {
            Assert.Equal(expected, Json(token).ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("+1")]                          // JSON has no leading plus
        [InlineData("1.")]
        [InlineData(".5")]
        [InlineData("1e")]
        [InlineData("1e+")]
        [InlineData("e5")]
        [InlineData("1.2.3")]
        [InlineData("--1")]
        [InlineData("1f")]
        public void ATokenThatIsNotAJsonNumber_IsRejected(string token)
        {
            Assert.False(ExactDecimal.TryParseJsonNumber(token, out _, out _));
        }

        // ---------------------------------------------------------------- arithmetic and comparison

        [Theory]
        [InlineData("250.00", "249", "-1")]
        [InlineData("249", "250", "1")]
        [InlineData("0.1", "0.3", "0.2")]                      // exact: no binary floating-point error
        [InlineData("0.3", "0.1", "-0.2")]
        [InlineData("100", "100.00", "0")]
        [InlineData("-5", "5", "10")]
        [InlineData("5", "-5", "-10")]
        [InlineData("0.0000000000000000000000000001", "0", "-0.0000000000000000000000000001")]
        [InlineData("1234.5678", "0.0001", "-1234.5677")]
        public void Subtract_IsExact(string left, string right, string expected)
        {
            // Subtract(a, b) is a - b; the delta reported for a pair is right - left
            Assert.Equal(expected, ExactDecimal.Subtract(Text(right), Text(left)).ToString());
        }

        [Fact]
        public void ADeltaBeyondTheRangeOfADecimal_IsStillExact()
        {
            ExactDecimal max = Text("79228162514264337593543950335");
            ExactDecimal min = Text("-79228162514264337593543950335");
            // max - min = 2 * 79228162514264337593543950335, which no decimal can hold
            Assert.Equal("158456325028528675187087900670", ExactDecimal.Subtract(max, min).ToString());
            Assert.Equal("-158456325028528675187087900670", ExactDecimal.Subtract(min, max).ToString());
            Assert.Equal("158456325028528675187087900670", ExactDecimal.Subtract(min, max).Abs().ToString());
        }

        [Fact]
        public void ADeltaWithManyPlacesAndALargeMagnitude_IsExact()
        {
            ExactDecimal a = Text("79228162514264337593543950335");
            ExactDecimal b = Text("0.0000000000000000000000000001");
            Assert.Equal("79228162514264337593543950334.9999999999999999999999999999", ExactDecimal.Subtract(a, b).ToString());
        }

        [Theory]
        [InlineData("1", "1", 0)]
        [InlineData("1.0", "1.00", 0)]
        [InlineData("1", "2", -1)]
        [InlineData("2", "1", 1)]
        [InlineData("-1", "1", -1)]
        [InlineData("-2", "-1", -1)]
        [InlineData("0.1", "0.10", 0)]
        [InlineData("0.0999999999999999999999999999", "0.1", -1)]
        [InlineData("-0", "0", 0)]
        public void CompareTo_OrdersByExactValue(string a, string b, int expected)
        {
            Assert.Equal(expected, Math.Sign(Text(a).CompareTo(Text(b))));
        }

        [Fact]
        public void ATextValueAndTheSameJsonNumber_AreEqual()
        {
            Assert.Equal(0, Text("12.5").CompareTo(Json("12.50")));
            Assert.Equal(0, Text("100").CompareTo(Json("1e2")));
            Assert.Equal(0, Text("0.01").CompareTo(Json("1E-2")));
        }

        // ---------------------------------------------------------------- agreement with System.Decimal, over many values

        [Fact]
        public void ForManyGeneratedValues_TheResultAgreesWithSystemDecimal()
        {
            var random = new Random(20260925);
            string RandomDecimalText()
            {
                int intDigits = random.Next(1, 17);          // at most 16 + 12 = 28 digits, always exactly representable
                int fracDigits = random.Next(0, 12);
                string integer = string.Concat(Enumerable.Range(0, intDigits).Select(_ => random.Next(10)));
                string fraction = string.Concat(Enumerable.Range(0, fracDigits).Select(_ => random.Next(10)));
                return (random.Next(2) == 0 ? "-" : "") + integer + (fracDigits > 0 ? "." + fraction : "");
            }

            for (int i = 0; i < 3000; i++)
            {
                string a = RandomDecimalText(), b = RandomDecimalText();
                decimal da = decimal.Parse(a, CultureInfo.InvariantCulture), db = decimal.Parse(b, CultureInfo.InvariantCulture);
                ExactDecimal ea = Text(a), eb = Text(b);

                Assert.Equal(Math.Sign(da.CompareTo(db)), Math.Sign(ea.CompareTo(eb)));
                Assert.Equal(Normalized(db - da), ExactDecimal.Subtract(eb, ea).ToString());     // right minus left
                Assert.Equal(Normalized(da), ea.ToString());
            }
        }

        /// <summary>System.Decimal's invariant text without trailing fractional zeros and without a sign on zero: the ExactDecimal form.</summary>
        private static string Normalized(decimal value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (text.Contains('.')) text = text.TrimEnd('0').TrimEnd('.');
            return text == "-0" || text == "" ? "0" : text;
        }

        // ---------------------------------------------------------------- culture independence

        [Theory]
        [InlineData("de-DE")]     // comma decimal separator, dot grouping
        [InlineData("fr-FR")]     // narrow no-break space grouping
        [InlineData("ar-SA")]     // Arabic digits and calendar
        [InlineData("tr-TR")]
        [InlineData("fa-IR")]
        public void ParsingAndPrinting_DoNotDependOnTheCurrentCulture(string cultureName)
        {
            CultureScope.Run(cultureName, () =>
            {
                Assert.Equal("1234.5", Text("1234.50").ToString());
                Assert.Equal("-0.25", Text("-0.25").ToString());
                Assert.Equal("100", Json("1e2").ToString());
                Assert.Equal("0.2", ExactDecimal.Subtract(Text("0.3"), Text("0.1")).ToString());
                Assert.False(ExactDecimal.TryParseText("1,5", out _, out _));       // a comma is never a decimal point, whatever the culture
                Assert.False(ExactDecimal.TryParseText("\u0661\u0662\u0663", out _, out _));   // Arabic-Indic digits are not accepted, whatever the culture
            });
        }

        [Fact]
        public void TheCultureHelper_PutsBackBothCultures_EvenWhenTheyDiffer()
        {
            CultureInfo formatting = CultureInfo.CurrentCulture;
            CultureInfo ui = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = new CultureInfo("de-DE");                 // deliberately different from the formatting culture
                CultureScope.Run("tr-TR", () => Assert.Equal("tr-TR", CultureInfo.CurrentUICulture.Name));
                Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
                Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);               // and the UI culture is not overwritten with the formatting one
            }
            catch (CultureNotFoundException) { /* a host without these cultures has nothing to prove */ }
            finally
            {
                CultureInfo.CurrentCulture = formatting;
                CultureInfo.CurrentUICulture = ui;
            }
        }
    }
}
