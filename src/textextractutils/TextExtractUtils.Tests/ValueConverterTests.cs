using System;
using System.Linq;
using Xunit;

namespace TextExtractAutomation.Tests
{
    public sealed class ValueConverterTests
    {
        private static FieldDef Field(FieldType type, DecimalStyle style = DecimalStyle.DotDecimal, params string[] dateFormats) =>
            new FieldDef { Name = "F", Kind = FieldKind.Label, Type = type, DecimalStyle = style, DateFormats = dateFormats.Length == 0 ? new[] { "yyyy-MM-dd" } : dateFormats };

        private static string Value(FieldType type, string span, DecimalStyle style = DecimalStyle.DotDecimal, params string[] formats)
        {
            Converted c = ValueConverter.Convert(Field(type, style, formats), span);
            Assert.True(c.Ok, type + " '" + span + "': " + c.Reason + " " + c.Detail);
            Assert.Null(c.Reason);
            return c.Value;
        }

        private static string Captured(FieldType type, string span, DecimalStyle style = DecimalStyle.DotDecimal)
        {
            Converted c = ValueConverter.Convert(Field(type, style), span);
            Assert.True(c.Ok);
            return span.Substring(c.Start, c.End - c.Start);
        }

        private static void Invalid(FieldType type, string span, DecimalStyle style = DecimalStyle.DotDecimal, params string[] formats)
        {
            Converted c = ValueConverter.Convert(Field(type, style, formats), span);
            Assert.False(c.Ok, type + " '" + span + "' should be invalid but gave " + c.Value);
            Assert.Equal("InvalidValue", c.Reason);
            Assert.Null(c.Value);
            Assert.False(string.IsNullOrEmpty(c.Detail));
            Assert.DoesNotContain(span.Trim(), c.Detail);                    // a reason never quotes the text
        }

        [Theory]
        [InlineData(FieldType.Text)]
        [InlineData(FieldType.Amount)]
        [InlineData(FieldType.Date)]
        public void AnEmptySpan_IsMissingValue_ForEveryType(FieldType type)
        {
            foreach (string span in new[] { null, "", "   " })
                Assert.Equal("MissingValue", ValueConverter.Convert(Field(type), span).Reason);
        }

        // ------------------------------------------------------------------ text and code

        [Fact]
        public void Text_IsTheWholeSpanTrimmed()
        {
            Assert.Equal("Net 30 days", Value(FieldType.Text, "  Net 30 days "));
            Assert.Equal("Net 30 days", Captured(FieldType.Text, "  Net 30 days "));
        }

        [Theory]
        [InlineData("INV-2026-0042", "INV-2026-0042")]
        [InlineData("INV-2026-0042.", "INV-2026-0042")]            // the sentence's full stop
        [InlineData("PX/00_12.A (reissued)", "PX/00_12.A")]
        [InlineData("\"AB123\"", "AB123")]
        [InlineData("88731 dated today", "88731")]
        [InlineData("ABC/", "ABC/")]                                // - / _ belong to a code, even at its end
        [InlineData("ABC_ next", "ABC_")]
        [InlineData("ABC- next", "ABC-")]
        [InlineData("ABC/.", "ABC/")]
        public void Code_IsTheFirstToken(string span, string value) => Assert.Equal(value, Value(FieldType.Code, span));

        [Theory]
        [InlineData("---")]
        [InlineData("? unknown")]
        public void Code_NeedsALetterOrDigitFirst(string span) => Invalid(FieldType.Code, span);

        // ------------------------------------------------------------------ numbers

        [Theory]
        [InlineData("1234", "1234")]
        [InlineData("1,234", "1234")]
        [InlineData("1,234,567 units", "1234567")]
        [InlineData("-42", "-42")]
        [InlineData("+42", "42")]
        [InlineData("1'234", "1234")]
        [InlineData("1 234", "1234")]
        [InlineData("qty 12", "12")]
        [InlineData("2O26", "2026")]                       // OCR: O next to digits
        [InlineData("1l0", "110")]
        [InlineData("l00", "100")]
        [InlineData("O 12", "12")]                         // a look-alike on its own is a letter, not a digit
        [InlineData("I 5", "5")]
        [InlineData("No. 7", "7")]
        [InlineData("5 12/03", "5")]                       // a space before a run that is not three digits separates two numbers
        [InlineData("1 23", "1")]
        public void Integer_DotStyle(string span, string value) => Assert.Equal(value, Value(FieldType.Integer, span));

        [Theory]
        [InlineData("1.234", "1234")]
        [InlineData("1.234.567", "1234567")]
        [InlineData("1 234", "1234")]
        public void Integer_CommaStyle(string span, string value) => Assert.Equal(value, Value(FieldType.Integer, span, DecimalStyle.CommaDecimal));

        [Theory]
        [InlineData("12.50")]              // a fraction
        [InlineData("none")]
        [InlineData("ORDER")]              // look-alike letters alone are not digits
        [InlineData("12abc")]
        [InlineData("1,23")]               // grouping of two: probably the other style, so refused
        [InlineData("1,2345")]
        [InlineData("12,34,567")]
        [InlineData("1'2345")]             // an apostrophe is only ever grouping
        [InlineData("1'23")]
        public void Integer_Invalid(string span) => Invalid(FieldType.Integer, span);

        [Theory]
        [InlineData("1,234.56", DecimalStyle.DotDecimal, "1234.56")]
        [InlineData("1234.5600", DecimalStyle.DotDecimal, "1234.5600")]       // decimals kept as written, never rounded
        [InlineData(".50", DecimalStyle.DotDecimal, "0.50")]
        [InlineData("-0.001", DecimalStyle.DotDecimal, "-0.001")]
        [InlineData("1.234,56", DecimalStyle.CommaDecimal, "1234.56")]
        [InlineData("0,5", DecimalStyle.CommaDecimal, "0.5")]
        [InlineData(",5", DecimalStyle.CommaDecimal, "0.5")]
        [InlineData("1 234,56", DecimalStyle.CommaDecimal, "1234.56")]
        [InlineData("12.5 kg", DecimalStyle.DotDecimal, "12.5")]
        [InlineData("1,234.", DecimalStyle.DotDecimal, "1234")]                // a trailing point belongs to the sentence
        [InlineData("3.14l5", DecimalStyle.DotDecimal, "3.1415")]
        [InlineData(".O5", DecimalStyle.DotDecimal, "0.05")]                     // OCR O after a leading point
        [InlineData(",O5", DecimalStyle.CommaDecimal, "0.05")]
        [InlineData("0000000000000000000000000000000012.5", DecimalStyle.DotDecimal, "0000000000000000000000000000000012.5")]   // redundant zeros cost nothing
        [InlineData("1.0000000000000000000000000001", DecimalStyle.DotDecimal, "1.0000000000000000000000000001")]   // 29 digits that decimal still holds exactly
        [InlineData("1.50000000000000000000000000000000000", DecimalStyle.DotDecimal, "1.50000000000000000000000000000000000")]
        [InlineData("79228162514264337593543950335", DecimalStyle.DotDecimal, "79228162514264337593543950335")]                 // the largest decimal
        [InlineData("0.0000000000000000000000000001", DecimalStyle.DotDecimal, "0.0000000000000000000000000001")]                 // 28 decimals
        public void Decimal_ReadsItsStyle(string span, DecimalStyle style, string value) => Assert.Equal(value, Value(FieldType.Decimal, span, style));

        [Theory]
        [InlineData("1.234,56", DecimalStyle.DotDecimal)]      // the wrong style is refused, not read as 1.234
        [InlineData("12.5", DecimalStyle.CommaDecimal)]
        [InlineData("99999999999999999999999999999999", DecimalStyle.DotDecimal)]   // beyond what can be held exactly
        [InlineData(",5", DecimalStyle.DotDecimal)]                                   // a leading separator of the other style
        [InlineData(".5", DecimalStyle.CommaDecimal)]
        [InlineData("about ,O5", DecimalStyle.DotDecimal)]
        [InlineData("0.12345678901234567890123456789", DecimalStyle.DotDecimal)]
        [InlineData("79228162514264337593543950336", DecimalStyle.DotDecimal)]      // one past the largest decimal
        [InlineData("0.00000000000000000000000000001", DecimalStyle.DotDecimal)]    // 29 significant decimals would be rounded
        [InlineData("8.0000000000000000000000000001", DecimalStyle.DotDecimal)]     // 29 significant digits past decimal's mantissa: the last would be rounded
        public void Decimal_Invalid(string span, DecimalStyle style) => Invalid(FieldType.Decimal, span, style);

        [Theory]
        [InlineData("12.5%", "12.5")]
        [InlineData("12.5 %", "12.5")]
        [InlineData("-3% change", "-3")]
        public void Percentage_NeedsThePercentSign(string span, string value) => Assert.Equal(value, Value(FieldType.Percentage, span));

        [Fact]
        public void Percentage_WithoutTheSign_IsInvalid() => Invalid(FieldType.Percentage, "12.5 percent");

        // ------------------------------------------------------------------ amounts

        [Theory]
        [InlineData("$1,234.50", "1234.50", "$")]
        [InlineData("$ 1,234.50", "1234.50", "$")]
        [InlineData("1,234.50 USD", "1234.50", "USD")]
        [InlineData("USD 1,234.50", "1234.50", "USD")]
        [InlineData("£12", "12", "£")]
        [InlineData("-$12.00", "-12.00", "$")]
        [InlineData("$-12.00", "-12.00", "$")]
        [InlineData("($12.00)", "-12.00", "$")]
        [InlineData("(12.00)", "-12.00", null)]
        [InlineData("12.00-", "-12.00", null)]
        [InlineData("12.00- EUR", "-12.00", "EUR")]          // a currency after the trailing minus
        [InlineData("12.00-$", "-12.00", "$")]
        [InlineData("1,045.00", "1045.00", null)]
        [InlineData("-0.00", "0.00", null)]                 // no negative zero
        [InlineData("Total due 99.95 by Friday", "99.95", null)]
        public void Amount_DotStyle(string span, string value, string currency)
        {
            Converted c = ValueConverter.Convert(Field(FieldType.Amount), span);
            Assert.True(c.Ok, c.Detail);
            Assert.Equal(value, c.Value);
            Assert.Equal(currency, c.Currency);
        }

        [Theory]
        [InlineData("€1.234,50", "1234.50", "€")]
        [InlineData("1.234,50 €", "1234.50", "€")]
        [InlineData("1.234,50 EUR", "1234.50", "EUR")]
        [InlineData("-1.234,50 EUR", "-1234.50", "EUR")]
        public void Amount_CommaStyle(string span, string value, string currency)
        {
            Converted c = ValueConverter.Convert(Field(FieldType.Amount, DecimalStyle.CommaDecimal), span);
            Assert.True(c.Ok, c.Detail);
            Assert.Equal((value, currency), (c.Value, c.Currency));
        }

        [Fact]
        public void ACurrencyCodeNeedsABoundaryOnBothSides()
        {
            Converted c = ValueConverter.Convert(Field(FieldType.Amount), "12 USD3");
            Assert.True(c.Ok);
            Assert.Null(c.Currency);                                       // USD3 is not a currency code
            Assert.Equal("12", c.Value);
            Assert.Equal("USD", ValueConverter.Convert(Field(FieldType.Amount), "12 USD.").Currency);
        }

        [Fact]
        public void Amount_CapturesTheSymbolSignAndParentheses_InTheRawPart()
        {
            Assert.Equal("($12.00)", Captured(FieldType.Amount, "Credit ($12.00) applied"));
            Assert.Equal("1,234.50 USD", Captured(FieldType.Amount, "1,234.50 USD incl. tax"));
            Assert.Equal("12.00-", Captured(FieldType.Amount, "12.00- CR"));
            Assert.Equal("12.00- EUR", Captured(FieldType.Amount, "12.00- EUR due"));
            Assert.Equal("12", Captured(FieldType.Amount, "(see note) 12"));                // an unmatched ( is not a negative
            Assert.Equal("12", Value(FieldType.Amount, "(see note) 12"));
        }

        [Theory]
        [InlineData("n/a")]
        [InlineData("€1.234,50")]            // comma style text under DotDecimal
        [InlineData("USD")]
        public void Amount_Invalid(string span) => Invalid(FieldType.Amount, span);

        // ------------------------------------------------------------------ dates

        [Theory]
        [InlineData("2026-09-26", "2026-09-26")]
        [InlineData("due 2026-09-26 at noon", "2026-09-26")]
        [InlineData("2O26-O9-26", "2026-09-26")]            // OCR O for 0
        [InlineData("2024-02-29", "2024-02-29")]
        public void Date_DefaultFormat(string span, string value) => Assert.Equal(value, Value(FieldType.Date, span));

        [Fact]
        public void Date_TriesEachFormat_AndTheFirstDateInTheSpanWins()
        {
            string[] formats = { "dd/MM/yyyy", "yyyy-MM-dd" };
            Assert.Equal("2026-09-26", Value(FieldType.Date, "26/09/2026", DecimalStyle.DotDecimal, formats));
            Assert.Equal("2026-09-26", Value(FieldType.Date, "2026-09-26", DecimalStyle.DotDecimal, formats));
            Assert.Equal("2026-01-02", Value(FieldType.Date, "from 02/01/2026 to 2026-12-31", DecimalStyle.DotDecimal, formats));
            Assert.Equal("2026-02-01", Value(FieldType.Date, "02/01/2026", DecimalStyle.DotDecimal, "MM/dd/yyyy"));      // the format decides, never the culture
        }

        [Theory]
        [InlineData("2023-02-29")]            // not a leap year
        [InlineData("2026-13-01")]
        [InlineData("26/09/2026")]            // not the field's format
        [InlineData("2026-09-261")]           // a longer token is not a date
        [InlineData("tomorrow")]
        public void Date_Invalid(string span) => Invalid(FieldType.Date, span);

        [Fact]
        public void FormatLengths_AreFixed() =>
            Assert.Equal(new[] { 10, 8, 16 }, new[] { "yyyy-MM-dd", "yyyyMMdd", "'Date:' dd.MM.yyyy" }.Select(ValueConverter.FormatLength));

        // ------------------------------------------------------------------ email and IBAN

        [Theory]
        [InlineData("jane.doe@Example.COM", "jane.doe@example.com")]
        [InlineData("<jane+ap@mail.acme.co.uk>", "jane+ap@mail.acme.co.uk")]
        [InlineData("contact: ap@acme.com.", "ap@acme.com")]
        public void Email_FirstAddress_DomainLowerCased(string span, string value) => Assert.Equal(value, Value(FieldType.Email, span));

        [Theory]
        [InlineData("jane.doe@localhost")]
        [InlineData(".jane@acme.com")]
        [InlineData("jane..doe@acme.com")]
        [InlineData("no address")]
        public void Email_Invalid(string span) => Invalid(FieldType.Email, span);

        [Theory]
        [InlineData("DE89 3704 0044 0532 0130 00", "DE89370400440532013000")]
        [InlineData("DE89370400440532013000", "DE89370400440532013000")]
        [InlineData("gb82 west 1234 5698 7654 32", "GB82WEST12345698765432")]
        [InlineData("IBAN DE89 3704 0044 0532 0130 00 BIC COBADEFFXXX", "DE89370400440532013000")]
        [InlineData("NL91ABNA0417164300.", "NL91ABNA0417164300")]
        [InlineData("ref AB12 then GB82 WEST 1234 5698 7654 32", "GB82WEST12345698765432")]   // an IBAN-shaped word before the real one
        [InlineData("DE00 3704 0044 0532 0130 00 or DE89 3704 0044 0532 0130 00", "DE89370400440532013000")]
        public void Iban_IsChecked_AndNormalized(string span, string value) => Assert.Equal(value, Value(FieldType.Iban, span));

        [Theory]
        [InlineData("DE89 3704 0044 0532 0130 01")]         // one digit wrong
        [InlineData("DE00 3704 0044 0532 0130 00")]
        [InlineData("1234 5678")]
        [InlineData("DE89")]
        public void Iban_Invalid(string span) => Invalid(FieldType.Iban, span);

        [Fact]
        public void Mod97_MatchesTheStandardExamples()
        {
            Assert.Equal(1, ValueConverter.Mod97("GB82WEST12345698765432"));
            Assert.Equal(1, ValueConverter.Mod97("FR1420041010050500013M02606"));
            Assert.NotEqual(1, ValueConverter.Mod97("GB82WEST12345698765433"));
        }

        // ------------------------------------------------------------------ culture

        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("tr-TR")]
        [InlineData("ar-SA")]
        public void TheMachineCulture_ChangesNothing(string culture)
        {
            string[] Run() => new[]
            {
                Value(FieldType.Amount, "$1,234.50"), Value(FieldType.Amount, "1.234,50 EUR", DecimalStyle.CommaDecimal), Value(FieldType.Decimal, "0.5"),
                Value(FieldType.Date, "26/09/2026", DecimalStyle.DotDecimal, "dd/MM/yyyy"), Value(FieldType.Email, "Info@EXAMPLE.com"), Value(FieldType.Iban, "gb82 west 1234 5698 7654 32")
            };
            string[] invariant = Run(), local = null;
            CultureScope.Run(culture, () => local = Run());
            Assert.Equal(invariant, local ?? invariant);
        }
    }
}
