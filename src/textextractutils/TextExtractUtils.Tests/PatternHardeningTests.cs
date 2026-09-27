using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TextExtractAutomation.Tests
{
    /// <summary>Pattern fields are the escape hatch, so they must never hang the robot, flood it, or turn an unusual text into a failure of the whole call.</summary>
    public sealed class PatternHardeningTests
    {
        private static TextExtractUtils WithPattern(string pattern, Occurrence occurrence = Occurrence.RequireUnique, FieldType type = FieldType.Text)
        {
            var c = new TextExtractUtils();
            Assert.True(c.LoadTemplateJson("{\"schemaVersion\":1,\"fields\":[{\"name\":\"P\",\"kind\":\"Pattern\",\"pattern\":" + JsonSerializer.Serialize(pattern)
                + ",\"type\":\"" + type + "\",\"occurrence\":\"" + occurrence + "\"},{\"name\":\"Total\",\"kind\":\"Label\",\"labels\":[\"Total\"],\"type\":\"Amount\"}]}", out string m), m);
            return c;
        }

        private static (string reason, int occurrences, string value) Result(TextExtractUtils c)
        {
            Assert.True(c.GetResultJson(out string json, out string m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement p = doc.RootElement.GetProperty("fields")[0];
            return (p.GetProperty("reason").GetString(), p.GetProperty("occurrences").GetInt32(), p.GetProperty("value").GetString());
        }

        [Theory]
        [InlineData(@"^(?<value>(a+)+)$")]              // nested repeats
        [InlineData(@"^(?<value>(a|a)*)$")]             // alternation of the same thing
        [InlineData(@"^(?<value>(a|aa)+)$")]
        [InlineData(@"^(?<value>(\w+\s?)*)$")]          // the classic "words" catastrophe
        [InlineData(@"(?<value>(.*a){20})")]
        public void CatastrophicPatterns_StopQuickly_AsPatternTimeout_AndTheOtherFieldsStillWork(string pattern)
        {
            using TextExtractUtils c = WithPattern(pattern);
            string text = new string('a', 5000) + "!\nTotal 5.00\n" + string.Concat(Enumerable.Repeat("aaaa bbbb ", 500)) + "!";
            var clock = Stopwatch.StartNew();
            Assert.True(c.ExtractFromText(text, out int found, out _, out string m), m);
            clock.Stop();
            Assert.True(clock.ElapsedMilliseconds < 5000, "took " + clock.ElapsedMilliseconds + " ms");
            Assert.Equal(1, found);
            string reason = Result(c).reason;
            Assert.True(reason == "PatternTimeout" || reason == "MissingLabel", reason);             // it either gives up in time or finds nothing: never hangs
            Assert.True(c.GetField("Total", out bool totalFound, out string total, out _, out _, out _, out m), m);
            Assert.Equal((true, "5.00"), (totalFound, total));
        }

        [Fact]
        public void ManyMatchesOnALargeText_AreReadWellInsideTheBudget()
        {
            // line numbers are counted incrementally, so 10,000 matches spread over a large text do not cost 10,000 scans from its start
            var template = new Template();
            Assert.Null(template.TryAddPatternField("P", @"ID(?<value>\d)", FieldType.Integer, DecimalStyle.DotDecimal, null));
            string text = string.Concat(Enumerable.Range(0, 10000).Select(i => "ID7 " + new string('.', 300) + "\n"));
            var clock = Stopwatch.StartNew();
            ExtractionSnapshot snapshot = Extraction.Run(template, text);
            clock.Stop();
            Assert.Null(snapshot.Fields[0].Reason);
            Assert.Equal(10000, snapshot.Fields[0].Occurrences);
            Assert.True(clock.ElapsedMilliseconds < 1000, "took " + clock.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void AZeroBudget_ReportsPatternTimeout_WithoutReadingAnything()
        {
            var template = new Template();
            Assert.Null(template.TryAddPatternField("P", @"(?<value>\d+)", FieldType.Integer, DecimalStyle.DotDecimal, null));
            string text = string.Join(" ", Enumerable.Range(0, 5000));
            ExtractionSnapshot snapshot = Extraction.Run(template, text, patternBudgetMilliseconds: -1);
            Assert.Equal("PatternTimeout", snapshot.Fields[0].Reason);
            Assert.Equal(0, snapshot.Fields[0].Occurrences);
        }

        [Fact]
        public void EmptyValueMatches_AreNotOccurrences()
        {
            using TextExtractUtils c = WithPattern(@"(?<value>\d*)", Occurrence.First, FieldType.Integer);
            Assert.True(c.ExtractFromText("no digits here, then 42 and 7", out _, out _, out string m), m);
            Assert.Equal((null, 1, "42"), Result(c));                                                   // not an empty match at position 0
            using TextExtractUtils never = WithPattern(@"(?<value>x*)");
            Assert.True(never.ExtractFromText("abc", out _, out _, out m), m);
            Assert.Equal("MissingLabel", Result(never).reason);
        }

        [Fact]
        public void MoreThanTenThousandMatches_IsTooManyMatches_UnlessTheFieldOnlyWantsTheFirst()
        {
            string text = string.Concat(Enumerable.Repeat("x ", 10001));
            using TextExtractUtils unique = WithPattern(@"(?<value>x)");
            Assert.True(unique.ExtractFromText(text, out _, out _, out string m), m);
            (string reason, int occurrences, string value) = Result(unique);
            Assert.Equal("TooManyMatches", reason);
            Assert.Equal(10000, occurrences);
            Assert.Null(value);

            using TextExtractUtils atCap = WithPattern(@"(?<value>x)");
            Assert.True(atCap.ExtractFromText(string.Concat(Enumerable.Repeat("x ", 10000)), out _, out _, out m), m);
            Assert.Equal((null, 10000, "x"), Result(atCap));                                            // exactly at the cap is fine (the same value repeated)

            using TextExtractUtils first = WithPattern(@"(?<value>x)", Occurrence.First);
            Assert.True(first.ExtractFromText(text, out _, out _, out m), m);
            Assert.Equal((null, 1, "x"), Result(first));                                                // First stops at the first match
        }

        [Fact]
        public void ALastField_ReadsEveryMatch_UpToTheCap()
        {
            using TextExtractUtils c = WithPattern(@"#(?<value>\d+)", Occurrence.Last, FieldType.Integer);
            Assert.True(c.ExtractFromText("#1 #2 #3", out _, out _, out string m), m);
            Assert.Equal((null, 3, "3"), Result(c));
        }

        [Fact]
        public void AHostileEmailText_IsAnInvalidValue_NotAFailedCall()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("Mail", "Email", FieldType.Email, out string m), m);
            string hostile = "Email: a@" + string.Concat(Enumerable.Repeat("a-", 200000)) + "!";
            var clock = Stopwatch.StartNew();
            Assert.True(c.ExtractFromText(hostile, out int found, out _, out m), m);
            Assert.True(clock.ElapsedMilliseconds < 5000, "took " + clock.ElapsedMilliseconds + " ms");
            Assert.Equal(0, found);
            Assert.True(c.GetField("Mail", out _, out _, out _, out string reason, out _, out m), m);
            Assert.Equal("InvalidValue", reason);
        }

        [Fact]
        public void PatternsRunOnTheOriginalText_WithInlineOptionsAvailable()
        {
            using TextExtractUtils caseless = WithPattern(@"(?i)ref:\s*(?<value>\w+)");
            Assert.True(caseless.ExtractFromText("REF: ab12", out _, out _, out string m), m);
            Assert.Equal("ab12", Result(caseless).value);
            using TextExtractUtils perLine = WithPattern(@"(?m)^Code (?<value>\w+)\r?$");
            Assert.True(perLine.ExtractFromText("x\r\nCode Q7\r\ny", out _, out _, out m), m);
            Assert.Equal("Q7", Result(perLine).value);
            using TextExtractUtils plain = WithPattern(@"^Code (?<value>\w+)");
            Assert.True(plain.ExtractFromText("x\r\nCode Q7", out _, out _, out m), m);
            Assert.Equal("MissingLabel", Result(plain).reason);                                       // without (?m), ^ is the start of the whole text
        }

        [Fact]
        public void TheIncrementalLineCounter_AgreesWithTheSimpleOne()
        {
            string text = "a\r\nb\rc\nd\r\n\r\ne f\n";
            var counter = new Extraction.LineCounter(text);
            for (int offset = 0; offset < text.Length; offset++)
                if (text[offset] != '\r' && text[offset] != '\n')
                    Assert.Equal(Extraction.LineOf(text, offset), counter.LineAt(offset));
        }

        [Fact]
        public void AnEmailMatchThatRunsPastItsLimit_IsAnInvalidValue()
        {
            // The built-in email pattern is linear on hostile input, so the guard is proven with a pattern that is not.
            var slow = new System.Text.RegularExpressions.Regex(@"^(a+)+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(10));
            Converted c = ValueConverter.ReadEmail(new string('a', 40) + "!", slow);
            Assert.False(c.Ok);
            Assert.Equal("InvalidValue", c.Reason);
            Assert.Contains("too long or unusual", c.Detail);
        }
    }
}
