using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class DateRuleTests
    {
        private static FieldValue Str(string s) => new FieldValue(FieldKind.String, s);

        private static ComparisonDef Calendar(int days = 0, string leftFormat = "yyyy-MM-dd", string rightFormat = "yyyy-MM-dd", ComparisonNullPolicy policy = ComparisonNullPolicy.RequireValue) =>
            new ComparisonDef { Name = "Due", Kind = RuleKind.CalendarDate, LeftFormat = leftFormat, RightFormat = rightFormat, DateTolerance = days, NullPolicy = policy };

        private static ComparisonDef Instant(int seconds = 0, ComparisonNullPolicy policy = ComparisonNullPolicy.RequireValue) =>
            new ComparisonDef { Name = "At", Kind = RuleKind.Instant, DateTolerance = seconds, NullPolicy = policy };

        private static ComparisonOutcome Eval(ComparisonDef rule, FieldValue l, FieldValue r) => ComparisonCore.Evaluate(rule, l, r);

        // ------------------------------------------------------------------ formats

        [Theory]
        [InlineData("yyyy-MM-dd")]
        [InlineData("dd/MM/yyyy")]
        [InlineData("MM/dd/yyyy")]
        [InlineData("dd.MM.yyyy")]
        [InlineData("yyyyMMdd")]
        [InlineData("dd MM yyyy")]
        [InlineData("yyyy, MM, dd")]
        [InlineData("yyyy 'day' dd 'of month' MM")]
        [InlineData("'Date:' yyyy-MM-dd")]
        public void ApprovedFormats_Pass(string format) => Assert.Null(DateCore.CheckFormat(format));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("yy-MM-dd")]                   // a two-digit year is ambiguous
        [InlineData("y-MM-dd")]
        [InlineData("yyyyy-MM-dd")]
        [InlineData("yyyy-M-dd")]                  // unpadded month
        [InlineData("yyyy-MMM-dd")]                // month names depend on culture
        [InlineData("yyyy-MMMM-dd")]
        [InlineData("yyyy-MM-d")]
        [InlineData("yyyy-MM-ddd")]                // day names
        [InlineData("yyyy-MM-dddd")]
        [InlineData("yyyy-MM")]                    // a date part is missing
        [InlineData("MM-dd")]
        [InlineData("yyyy")]
        [InlineData("yyyy-MM-dd-dd")]              // a part twice
        [InlineData("yyyy-yyyy-MM-dd")]
        [InlineData("yyyy-MM-dd HH:mm")]           // time tokens
        [InlineData("yyyy-MM-dd hh")]
        [InlineData("yyyy-MM-dd mm")]
        [InlineData("yyyy-MM-dd ss")]
        [InlineData("yyyy-MM-dd fff")]
        [InlineData("yyyy-MM-dd tt")]
        [InlineData("yyyy-MM-dd zzz")]             // offset tokens
        [InlineData("yyyy-MM-dd K")]
        [InlineData("yyyy-MM-dd g")]               // era
        [InlineData("yyyy:MM:dd")]                 // the time separator token
        [InlineData("yyyy-MM-dd %d")]
        [InlineData("yyyy-MM-dd \\d")]             // escape characters
        [InlineData("yyyy-MM-dd \"x\"")]
        [InlineData("yyyy-MM-dd 'unclosed")]
        [InlineData("yyyy-MM-dd ''")]              // an empty literal
        [InlineData("yyyy-MM-dd 'a\\b'")]
        [InlineData("yyyy-MM-dd T")]               // an unquoted letter
        [InlineData("yyyy-MM-dd é")]
        [InlineData("yyyy-MM-dd \ud800")]
        [InlineData("yyyy-MM-dd\t")]
        public void UnapprovedFormats_AreRefused(string format) => Assert.NotNull(DateCore.CheckFormat(format));

        [Fact]
        public void AFormatLongerThanTheLimit_IsRefused()
        {
            string literal = "'" + new string('x', DateCore.MaxFormatLength) + "'";
            Assert.Contains("longer", DateCore.CheckFormat("yyyy-MM-dd" + literal));
        }

        // ------------------------------------------------------------------ calendar dates

        [Theory]
        [InlineData("2024-02-29", true)]           // a leap day
        [InlineData("2000-02-29", true)]           // divisible by 400
        [InlineData("2023-02-29", false)]
        [InlineData("2100-02-29", false)]          // divisible by 100 but not 400
        [InlineData("2024-04-31", false)]
        [InlineData("2024-13-01", false)]
        [InlineData("2024-00-10", false)]
        [InlineData("2024-01-00", false)]
        [InlineData("2024-2-29", false)]           // must be padded
        [InlineData("24-02-29", false)]
        [InlineData("20240229", false)]
        [InlineData("2024-02-29 ", false)]         // no surrounding white space
        [InlineData(" 2024-02-29", false)]
        [InlineData("2024-02-29\n", false)]
        [InlineData("2024-02-29T00:00:00", false)]
        [InlineData("0000-01-01", false)]
        [InlineData("0001-01-01", true)]
        [InlineData("9999-12-31", true)]
        [InlineData("20230-01-01", false)]
        [InlineData("999-01-01", false)]
        [InlineData("٢٠٢٤-02-29", false)]   // non-ASCII digits
        [InlineData("2024/02/29", false)]
        [InlineData("", false)]
        public void CalendarDates_AreParsedStrictly(string text, bool valid)
        {
            Assert.Equal(valid, DateCore.TryParseDate(text, "yyyy-MM-dd", out long day, out string normalized));
            if (valid) Assert.Equal(text, normalized); else { Assert.Null(normalized); Assert.Equal(0, day); }
        }

        [Fact]
        public void EachSideHasItsOwnFormat_AndBothNormalizeToIsoDates()
        {
            ComparisonDef rule = Calendar(0, "dd/MM/yyyy", "yyyyMMdd");
            ComparisonOutcome o = Eval(rule, Str("29/02/2024"), Str("20240229"));
            Assert.Equal(ComparisonState.Equal, o.State);
            Assert.Equal(("2024-02-29", "2024-02-29"), (o.LeftInterpreted, o.RightInterpreted));
            Assert.Equal("0", o.Delta);
            Assert.Equal(ComparisonState.Invalid, Eval(rule, Str("2024-02-29"), Str("20240229")).State);    // the right format applied to the left side is wrong
        }

        [Fact]
        public void CalendarTolerance_IsInclusive_InWholeDays_AndTheDeltaIsRightMinusLeft()
        {
            ComparisonOutcome equal = Eval(Calendar(2), Str("2024-02-28"), Str("2024-03-01"));            // across a leap day: 2 days
            Assert.Equal((ComparisonState.Equal, "2"), (equal.State, equal.Delta));
            ComparisonOutcome beyond = Eval(Calendar(2), Str("2024-02-27"), Str("2024-03-01"));           // 3 days
            Assert.Equal((ComparisonState.Different, "DateMismatch", "3"), (beyond.State, beyond.ReasonCode, beyond.Delta));
            Assert.Contains("day", beyond.Explanation);
            Assert.Equal("-3", Eval(Calendar(2), Str("2024-03-01"), Str("2024-02-27")).Delta);
            Assert.Equal(ComparisonState.Different, Eval(Calendar(0), Str("2024-02-28"), Str("2024-02-29")).State);
            Assert.Equal(ComparisonState.Equal, Eval(Calendar(365), Str("2023-03-01"), Str("2024-02-29")).State);
            Assert.Equal(ComparisonState.Equal, Eval(Calendar(int.MaxValue), Str("0001-01-01"), Str("9999-12-31")).State);    // the largest tolerance never overflows
        }

        [Fact]
        public void CalendarRules_ReportTheCommonReasons()
        {
            Assert.Equal("MissingField", Eval(Calendar(), FieldValue.Missing, Str("2024-01-01")).ReasonCode);
            Assert.Equal("NullNotAllowed", Eval(Calendar(), FieldValue.Null, Str("2024-01-01")).ReasonCode);
            Assert.Equal("InvalidDate", Eval(Calendar(), Str(""), Str("2024-01-01")).ReasonCode);                 // empty date text is invalid
            Assert.Equal("InvalidDate", Eval(Calendar(), Str("2024-01-01"), Str("not a date")).ReasonCode);
            Assert.Equal("InvalidType", Eval(Calendar(), new FieldValue(FieldKind.Number, "20240101"), Str("2024-01-01")).ReasonCode);
            Assert.Equal("InvalidType", Eval(Calendar(), new FieldValue(FieldKind.Boolean, "true"), Str("2024-01-01")).ReasonCode);
            Assert.Equal("InvalidType", Eval(Calendar(), FieldValue.Unsupported, Str("2024-01-01")).ReasonCode);
            ComparisonOutcome bad = Eval(Calendar(), Str("SECRET-TEXT"), Str("2024-01-01"));
            Assert.DoesNotContain("SECRET", bad.Explanation);                                                     // explanations never quote the value
            ComparisonDef allow = Calendar(0, policy: ComparisonNullPolicy.AllowBothNull);
            Assert.Equal(ComparisonState.Equal, Eval(allow, FieldValue.Null, FieldValue.Null).State);
            Assert.Equal((ComparisonState.Different, "DateMismatch"), (Eval(allow, FieldValue.Null, Str("2024-01-01")).State, Eval(allow, FieldValue.Null, Str("2024-01-01")).ReasonCode));
            Assert.Equal("InvalidDate", Eval(allow, Str("nope"), FieldValue.Null).ReasonCode);
        }

        // ------------------------------------------------------------------ instants

        [Theory]
        [InlineData("2024-03-01T12:00:00Z", "2024-03-01T12:00:00Z")]
        [InlineData("2024-03-01T12:00:00+00:00", "2024-03-01T12:00:00Z")]
        [InlineData("2024-03-01T12:00:00-00:00", "2024-03-01T12:00:00Z")]
        [InlineData("2024-03-01T12:00:00-04:00", "2024-03-01T16:00:00Z")]
        [InlineData("2024-03-01T12:00:00+05:30", "2024-03-01T06:30:00Z")]
        [InlineData("2024-03-01T12:00:00+14:00", "2024-02-29T22:00:00Z")]
        [InlineData("2024-03-01T12:00:00.5+02:00", "2024-03-01T10:00:00.5Z")]
        [InlineData("2024-03-01T12:00:00.1234567Z", "2024-03-01T12:00:00.1234567Z")]
        [InlineData("2024-03-01T12:00:00.100Z", "2024-03-01T12:00:00.1Z")]
        [InlineData("2024-02-29T23:59:59-01:00", "2024-03-01T00:59:59Z")]
        [InlineData("9999-12-31T23:59:59.9999999Z", "9999-12-31T23:59:59.9999999Z")]
        public void ApprovedInstantShapes_AreNormalizedToUtc(string text, string normalized)
        {
            Assert.True(DateCore.TryParseInstant(text, out _, out string actual));
            Assert.Equal(normalized, actual);
        }

        [Theory]
        [InlineData("2024-03-01T12:00:00")]            // no offset: never read in the machine's zone
        [InlineData("2024-03-01T12:00:00.5")]
        [InlineData("2024-03-01 12:00:00Z")]           // a space instead of T
        [InlineData("2024-03-01t12:00:00Z")]           // lower case
        [InlineData("2024-03-01T12:00:00z")]
        [InlineData("2024-03-01T12:00Z")]              // no seconds
        [InlineData("2024-03-01T12:00:00.Z")]          // a dot with no digits
        [InlineData("2024-03-01T12:00:00.12345678Z")]  // more than seven fraction digits
        [InlineData("2024-03-01T12:00:00+0530")]       // offset without a colon
        [InlineData("2024-03-01T12:00:00+05")]
        [InlineData("2024-03-01T12:00:00+15:00")]      // beyond the largest real offset
        [InlineData("2024-03-01T12:00:00+05:60")]
        [InlineData("2024-03-01T24:00:00Z")]
        [InlineData("2024-03-01T23:59:60Z")]           // no leap seconds
        [InlineData("2024-02-30T12:00:00Z")]
        [InlineData("2023-02-29T12:00:00Z")]
        [InlineData("2024-03-01T12:00:00Z ")]
        [InlineData(" 2024-03-01T12:00:00Z")]
        [InlineData("2024-03-01T12:00:00 Z")]
        [InlineData("0001-01-01T00:00:00+01:00")]      // before the first representable instant once converted to UTC
        [InlineData("٢٠٢٤-03-01T12:00:00Z")]
        [InlineData("")]
        [InlineData("1709294400")]
        public void EverythingElse_IsNotAnInstant(string text)
        {
            Assert.False(DateCore.TryParseInstant(text, out long ticks, out string normalized));
            Assert.Null(normalized);
            Assert.Equal(0, ticks);
        }

        [Fact]
        public void TheSameInstant_InAnyOffset_IsEqual_AndAnInstantIsNotJustItsClockTime()
        {
            Assert.Equal(ComparisonState.Equal, Eval(Instant(), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T08:00:00-04:00")).State);
            Assert.Equal(ComparisonState.Equal, Eval(Instant(), Str("2024-03-01T12:00:00+00:00"), Str("2024-03-01T17:30:00+05:30")).State);
            ComparisonOutcome sameClock = Eval(Instant(), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T12:00:00-04:00"));        // same wall clock, four hours apart
            Assert.Equal((ComparisonState.Different, "DateMismatch", "14400"), (sameClock.State, sameClock.ReasonCode, sameClock.Delta));
            Assert.Equal("-14400", Eval(Instant(), Str("2024-03-01T12:00:00-04:00"), Str("2024-03-01T12:00:00Z")).Delta);
        }

        [Fact]
        public void InstantTolerance_IsInclusive_InWholeSeconds_ExactToTheTick()
        {
            Assert.Equal(ComparisonState.Equal, Eval(Instant(2), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T12:00:02Z")).State);
            ComparisonOutcome one = Eval(Instant(1), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T12:00:02Z"));
            Assert.Equal((ComparisonState.Different, "2"), (one.State, one.Delta));
            Assert.Contains("second", one.Explanation);
            ComparisonOutcome fraction = Eval(Instant(1), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T12:00:01.5Z"));         // 1.5 s apart
            Assert.Equal((ComparisonState.Different, "1.5"), (fraction.State, fraction.Delta));
            Assert.Equal(ComparisonState.Equal, Eval(Instant(2), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T12:00:01.5Z")).State);
            ComparisonOutcome tick = Eval(Instant(1), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T12:00:01.0000001Z"));       // one tick past the tolerance
            Assert.Equal((ComparisonState.Different, "1.0000001"), (tick.State, tick.Delta));
            Assert.Equal(ComparisonState.Equal, Eval(Instant(1), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T12:00:01Z")).State);
        }

        [Fact]
        public void InstantTolerance_AtTheLargestValue_DoesNotOverflow()
        {
            // 60 years is about 1.9e9 seconds: inside the largest tolerance (2,147,483,647 s = about 68 years), so equal; 70 years is beyond it.
            Assert.Equal(ComparisonState.Equal, Eval(Instant(int.MaxValue), Str("2000-01-01T00:00:00Z"), Str("2060-01-01T00:00:00Z")).State);
            Assert.Equal(ComparisonState.Different, Eval(Instant(int.MaxValue), Str("1950-01-01T00:00:00Z"), Str("2024-01-01T00:00:00Z")).State);
        }

        [Fact]
        public void InstantRules_ReportTheCommonReasons_AndNeverGuessAZone()
        {
            Assert.Equal("MissingField", Eval(Instant(), FieldValue.Missing, Str("2024-03-01T12:00:00Z")).ReasonCode);
            Assert.Equal("NullNotAllowed", Eval(Instant(), Str("2024-03-01T12:00:00Z"), FieldValue.Null).ReasonCode);
            ComparisonOutcome noOffset = Eval(Instant(), Str("2024-03-01T12:00:00"), Str("2024-03-01T12:00:00Z"));
            Assert.Equal((ComparisonState.Invalid, "InvalidDate"), (noOffset.State, noOffset.ReasonCode));
            Assert.Contains("offset", noOffset.Explanation);
            Assert.Equal("InvalidType", Eval(Instant(), new FieldValue(FieldKind.Integer, "1709294400"), Str("2024-03-01T12:00:00Z")).ReasonCode);
            Assert.DoesNotContain("SECRET", Eval(Instant(), Str("SECRET"), Str("2024-03-01T12:00:00Z")).Explanation);
            ComparisonDef allow = Instant(0, ComparisonNullPolicy.AllowBothNull);
            Assert.Equal(ComparisonState.Equal, Eval(allow, FieldValue.Null, FieldValue.Null).State);
            Assert.Equal("DateMismatch", Eval(allow, FieldValue.Null, Str("2024-03-01T12:00:00Z")).ReasonCode);
        }

        [Theory]
        [InlineData("tr-TR")]
        [InlineData("ar-SA")]      // a Hijri calendar and Arabic-Indic digits by default
        [InlineData("th-TH")]      // a Buddhist calendar
        [InlineData("ja-JP")]
        [InlineData("de-DE")]
        public void TheOutcomeDoesNotDependOnTheMachineCulture(string culture)
        {
            CultureScope.Run(culture, () =>
            {
                Assert.Equal(ComparisonState.Equal, Eval(Calendar(0, "dd/MM/yyyy", "yyyy-MM-dd"), Str("29/02/2024"), Str("2024-02-29")).State);
                Assert.Equal(ComparisonState.Equal, Eval(Calendar(0, "dd.MM.yyyy", "MM/dd/yyyy"), Str("29.02.2024"), Str("02/29/2024")).State);
                Assert.Equal(ComparisonState.Equal, Eval(Instant(), Str("2024-03-01T12:00:00Z"), Str("2024-03-01T12:00:00+00:00")).State);
                Assert.True(DateCore.TryParseInstant("2024-03-01T12:00:00.5+02:00", out _, out string n));
                Assert.Equal("2024-03-01T10:00:00.5Z", n);
                Assert.Equal("2024-02-29", Eval(Calendar(0, "dd/MM/yyyy", "dd/MM/yyyy"), Str("29/02/2024"), Str("29/02/2024")).LeftInterpreted);
                Assert.Equal("-0.5", DateCore.SecondsText(-5000000));
            });
        }

        [Fact]
        public void TheInstantParsingIsIndependentOfTheLocalTimeZone()
        {
            // Read with AssumeUniversal, never AssumeLocal: a Z value is exactly the UTC instant whatever zone the machine is in.
            Assert.True(DateCore.TryParseInstant("2024-03-01T12:00:00Z", out long ticks, out _));
            Assert.Equal(new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).UtcTicks, ticks);
            Assert.True(DateCore.TryParseInstant("2024-03-10T07:30:00-08:00", out ticks, out _));          // a US daylight-saving change day: the supplied offset is used as given
            Assert.Equal(new DateTimeOffset(2024, 3, 10, 7, 30, 0, TimeSpan.FromHours(-8)).UtcTicks, ticks);
        }

        [Theory]
        [InlineData(0L, "0")]
        [InlineData(5L, "0.0000005")]
        [InlineData(-5L, "-0.0000005")]
        [InlineData(10000000L, "1")]
        [InlineData(-10000000L, "-1")]
        [InlineData(15000000L, "1.5")]
        [InlineData(-15000001L, "-1.5000001")]
        [InlineData(123456789L, "12.3456789")]
        [InlineData(long.MaxValue, "922337203685.4775807")]
        [InlineData(long.MinValue, "-922337203685.4775808")]
        public void SecondsText_IsExactWithoutTrailingZeros(long ticks, string expected) => Assert.Equal(expected, DateCore.SecondsText(ticks));

        // ------------------------------------------------------------------ definitions

        [Fact]
        public void TheBuilders_AddTheRules_AndRejectBadInput_WithoutChangingTheDefinition()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddCalendarDateComparisonSimple("Due", "/due", "/due", "yyyy-MM-dd", "dd/MM/yyyy", out string m), m);
            Assert.True(c.AddCalendarDateComparison("Ship", "/ship", "/ship", "yyyyMMdd", "yyyy-MM-dd", 3, ComparisonNullPolicy.AllowBothNull, out m), m);
            Assert.True(c.AddInstantComparisonSimple("Seen", "/seen", "/seen", out m), m);
            Assert.True(c.AddInstantComparison("Paid", "/paid", "/paid", 30, ComparisonNullPolicy.AllowBothNull, out m), m);
            Assert.True(c.GetDefinitionJson(out string before, out m), m);

            Assert.False(c.AddCalendarDateComparisonSimple("due", "/x", "/x", "yyyy-MM-dd", "yyyy-MM-dd", out m)); Assert.Contains("DuplicateName", m);
            Assert.False(c.AddCalendarDateComparisonSimple("A", "/x", "/x", "yyyy-MM", "yyyy-MM-dd", out m)); Assert.Contains("leftFormat", m); Assert.Contains("InvalidFormat", m);
            Assert.False(c.AddCalendarDateComparisonSimple("A", "/x", "/x", "yyyy-MM-dd", "yyyy-MM-dd HH", out m)); Assert.Contains("rightFormat", m);
            Assert.False(c.AddCalendarDateComparisonSimple("A", "/x", "/x", null, "yyyy-MM-dd", out m)); Assert.Contains("InvalidFormat", m);
            Assert.False(c.AddCalendarDateComparison("A", "/x", "/x", "yyyy-MM-dd", "yyyy-MM-dd", -1, ComparisonNullPolicy.RequireValue, out m)); Assert.Contains("toleranceDays", m);
            Assert.False(c.AddCalendarDateComparison("A", "x", "/x", "yyyy-MM-dd", "yyyy-MM-dd", 0, ComparisonNullPolicy.RequireValue, out m)); Assert.Contains("InvalidPointer", m);
            Assert.False(c.AddCalendarDateComparison("A", "/x", "/x", "yyyy-MM-dd", "yyyy-MM-dd", 0, (ComparisonNullPolicy)7, out m)); Assert.Contains("UnknownEnumValue", m);
            Assert.False(c.AddInstantComparison("A", "/x", "/x", -1, ComparisonNullPolicy.RequireValue, out m)); Assert.Contains("toleranceSeconds", m);
            Assert.False(c.AddInstantComparisonSimple("", "/x", "/x", out m)); Assert.Contains("InvalidName", m);
            Assert.True(c.GetDefinitionJson(out string after, out m), m);
            Assert.Equal(before, after);
        }

        private const string DateDefinition = "{\"schemaVersion\":1,\"keys\":[{\"name\":\"Id\",\"leftPointer\":\"/id\",\"rightPointer\":\"/id\",\"trim\":false,\"ignoreCase\":false}],\"comparisons\":["
            + "{\"name\":\"Due\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/due\",\"rightPointer\":\"/dueDate\",\"leftFormat\":\"dd/MM/yyyy\",\"rightFormat\":\"yyyy-MM-dd\",\"toleranceDays\":2,\"nullPolicy\":\"AllowBothNull\"},"
            + "{\"name\":\"Seen\",\"kind\":\"Instant\",\"leftPointer\":\"/seen\",\"rightPointer\":\"/seenAt\",\"toleranceSeconds\":30,\"nullPolicy\":\"RequireValue\"}"
            + "],\"limits\":{\"maximumRowsPerSide\":50000,\"maximumInputCharactersPerSide\":8000000,\"maximumResults\":100000,\"maximumDifferenceDetails\":100000}}";

        [Fact]
        public void TheCanonicalJson_OfBuilderMadeRules_EqualsTheCanonicalForm_AndLoadsBack()
        {
            using var built = new ReconciliationUtils();
            Assert.True(built.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(built.AddCalendarDateComparison("Due", "/due", "/dueDate", "dd/MM/yyyy", "yyyy-MM-dd", 2, ComparisonNullPolicy.AllowBothNull, out m), m);
            Assert.True(built.AddInstantComparison("Seen", "/seen", "/seenAt", 30, ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(built.GetDefinitionJson(out string canonical, out m), m);
            Assert.Equal(DateDefinition, canonical);

            using var loaded = new ReconciliationUtils();
            Assert.True(loaded.ValidateDefinitionJson(DateDefinition, out int errors, out _, out m), m);
            Assert.Equal(0, errors);
            Assert.True(loaded.LoadDefinitionJson(DateDefinition, out m), m);
            Assert.True(loaded.GetDefinitionJson(out string again, out m), m);
            Assert.Equal(canonical, again);
        }

        [Fact]
        public void OmittedDateOptions_TakeTheirDefaults_AndTheCanonicalFormSpellsThemOut()
        {
            const string json = "{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"D\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"},{\"name\":\"I\",\"kind\":\"Instant\",\"leftPointer\":\"/b\",\"rightPointer\":\"/b\"}]}";
            using var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson(json, out string m), m);
            Assert.True(c.GetDefinitionJson(out string canonical, out m), m);
            Assert.Contains("\"leftFormat\":\"yyyy-MM-dd\",\"rightFormat\":\"yyyy-MM-dd\",\"toleranceDays\":0,\"nullPolicy\":\"RequireValue\"", canonical);
            Assert.Contains("\"toleranceSeconds\":0,\"nullPolicy\":\"RequireValue\"", canonical);
        }

        [Theory]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftFormat\":\"yyyy-MM\"}", "comparisons[0].leftFormat", "InvalidFormat")]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"rightFormat\":\"yyyy-MM-dd HH:mm\"}", "comparisons[0].rightFormat", "InvalidFormat")]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftFormat\":5}", "comparisons[0].leftFormat", "InvalidType")]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceDays\":-1}", "comparisons[0].toleranceDays", "InvalidTolerance")]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceDays\":1.5}", "comparisons[0].toleranceDays", "InvalidType")]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceDays\":\"2\"}", "comparisons[0].toleranceDays", "InvalidType")]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceDays\":3000000000}", "comparisons[0].toleranceDays", "InvalidType")]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceSeconds\":1}", "comparisons[0].toleranceSeconds", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"CalendarDate\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"absoluteTolerance\":\"1\"}", "comparisons[0].absoluteTolerance", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Instant\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceSeconds\":-5}", "comparisons[0].toleranceSeconds", "InvalidTolerance")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Instant\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceSeconds\":null}", "comparisons[0].toleranceSeconds", "InvalidType")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Instant\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"leftFormat\":\"yyyy-MM-dd\"}", "comparisons[0].leftFormat", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Instant\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceDays\":1}", "comparisons[0].toleranceDays", "UnknownProperty")]
        [InlineData("{\"name\":\"T\",\"kind\":\"Text\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\",\"toleranceSeconds\":1}", "comparisons[0].toleranceSeconds", "UnknownProperty")]
        public void TheDateKinds_RejectBadAndForeignOptions_WithThePathOfTheProblem(string comparison, string path, string code)
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
            Assert.True(c.LoadDefinitionJson(DateDefinition, out string m), m);
            return c;
        }

        [Fact]
        public void EndToEnd_EveryOutcomeAppears_WithNormalizedValuesAndUnits()
        {
            using var c = Reconciler();
            const string left = "[{\"id\":\"1\",\"due\":\"29/02/2024\",\"seen\":\"2024-03-01T12:00:00Z\"},"
                              + "{\"id\":\"2\",\"due\":\"01/03/2024\",\"seen\":\"2024-03-01T12:00:00Z\"},"
                              + "{\"id\":\"3\",\"due\":\"31/12/2023\",\"seen\":\"2024-03-01T12:00:00Z\"},"
                              + "{\"id\":\"4\",\"due\":\"31/02/2024\",\"seen\":\"2024-03-01T12:00:00Z\"},"
                              + "{\"id\":\"5\",\"due\":\"01/01/2024\",\"seen\":\"2024-03-01T12:00:00\"},"
                              + "{\"id\":\"6\",\"due\":null,\"seen\":\"2024-03-01T12:00:00Z\"}]";
            const string right = "[{\"id\":\"1\",\"dueDate\":\"2024-03-02\",\"seenAt\":\"2024-03-01T08:00:20-04:00\"},"
                               + "{\"id\":\"2\",\"dueDate\":\"2024-03-01\",\"seenAt\":\"2024-03-01T12:01:00Z\"},"
                               + "{\"id\":\"3\",\"dueDate\":\"2024-01-05\",\"seenAt\":\"2024-03-01T12:00:00Z\"},"
                               + "{\"id\":\"4\",\"dueDate\":\"2024-03-01\",\"seenAt\":\"2024-03-01T12:00:00Z\"},"
                               + "{\"id\":\"5\",\"dueDate\":\"2024-01-01\",\"seenAt\":\"2024-03-01T12:00:00Z\"},"
                               + "{\"id\":\"6\",\"dueDate\":null,\"seenAt\":\"2024-03-01T12:00:00Z\"}]";
            Assert.True(c.ReconcileJson(left, right, out int count, out string m), m);
            Assert.Equal(4, count);                                                        // 1 and 6 match (id 1: 2 days and 20 s, both inside; id 6: both null)
            var seen = new List<string>();
            while (c.TryReadNextException(out bool has, out _, out string kind, out string key, out _, out _, out string reason, out _, out _) && has)
                seen.Add(key + " " + kind + " " + reason);
            Assert.Equal(new[]
            {
                "[\"2\"] Different DateMismatch",
                "[\"3\"] Different DateMismatch",
                "[\"4\"] InvalidComparison InvalidDate",
                "[\"5\"] InvalidComparison InvalidDate"
            }, seen);

            Assert.True(c.GetResultJson("r000002", out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement d = doc.RootElement.GetProperty("differences")[0];
            Assert.Equal("Seen", d.GetProperty("rule").GetString());                           // id 2 differs only in the instant: 60 s beyond a 30 s tolerance
            Assert.Equal("Instant", d.GetProperty("ruleKind").GetString());
            Assert.Equal("60", d.GetProperty("delta").GetString());
            Assert.Equal("2024-03-01T12:00:00Z", d.GetProperty("leftInterpreted").GetString());
            Assert.Equal("2024-03-01T12:01:00Z", d.GetProperty("rightInterpreted").GetString());
            Assert.Equal("\"2024-03-01T12:01:00Z\"", d.GetProperty("rightValue").GetRawText());

            Assert.True(c.GetResultJson("r000003", out json, out m), m);
            using JsonDocument dateDoc = JsonDocument.Parse(json);
            JsonElement dd = dateDoc.RootElement.GetProperty("differences")[0];
            Assert.Equal("Due", dd.GetProperty("rule").GetString());
            Assert.Equal("2023-12-31", dd.GetProperty("leftInterpreted").GetString());       // read with dd/MM/yyyy, shown as an ISO date
            Assert.Equal("2024-01-05", dd.GetProperty("rightInterpreted").GetString());
            Assert.Equal("5", dd.GetProperty("delta").GetString());
        }

        [Fact]
        public void DataTables_DatesAreTextColumns_ADateTimeCellIsNotReadAsADate()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Id", out string m), m);
            Assert.True(c.AddCalendarDateComparison("Due", "/Due", "/Due", "dd/MM/yyyy", "yyyy-MM-dd", 0, ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddInstantComparison("Seen", "/Seen", "/Seen", 0, ComparisonNullPolicy.RequireValue, out m), m);
            var left = new DataTable(); left.Columns.Add("Id", typeof(string)); left.Columns.Add("Due", typeof(string)); left.Columns.Add("Seen", typeof(string));
            var right = new DataTable(); right.Columns.Add("Id", typeof(string)); right.Columns.Add("Due", typeof(string)); right.Columns.Add("Seen", typeof(string));
            left.Rows.Add("a", "29/02/2024", "2024-03-01T12:00:00Z"); right.Rows.Add("a", "2024-02-29", "2024-03-01T08:00:00-04:00");
            Assert.True(c.ReconcileDataTables(left, right, out int count, out m), m);
            Assert.Equal(0, count);

            var typed = new DataTable(); typed.Columns.Add("Id", typeof(string)); typed.Columns.Add("Due", typeof(DateTime)); typed.Columns.Add("Seen", typeof(DateTimeOffset));
            typed.Rows.Add("a", new DateTime(2024, 2, 29), DateTimeOffset.UtcNow);
            Assert.True(c.ReconcileDataTables(typed, right, out count, out m), m);
            Assert.Equal(1, count);
            Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out string reason, out int diffs, out _));
            Assert.Equal(("InvalidType", 2), (reason, diffs));
        }
    }
}
