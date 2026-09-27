using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TextExtractAutomation.Tests
{
    public sealed class TemplateBuilderTests
    {
        private static string Canonical(TextExtractUtils c)
        {
            Assert.True(c.GetTemplateJson(out string json, out string m), m);
            return json;
        }

        private static JsonElement Field(TextExtractUtils c, int index)
        {
            using JsonDocument doc = JsonDocument.Parse(Canonical(c));
            return doc.RootElement.GetProperty("fields")[index].Clone();
        }

        [Fact]
        public void TheSimpleForm_UsesTheSafeDefaults()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("InvoiceNumber", "Invoice No|Invoice Number|Invoice #", FieldType.Code, out string m), m);
            Assert.Null(m);
            JsonElement f = Field(c, 0);
            Assert.Equal("Label", f.GetProperty("kind").GetString());
            Assert.Equal(new[] { "Invoice No", "Invoice Number", "Invoice #" }, f.GetProperty("labels").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal("SameLine", f.GetProperty("position").GetString());
            Assert.Equal("Code", f.GetProperty("type").GetString());
            Assert.Equal("", f.GetProperty("format").GetString());
            Assert.True(f.GetProperty("required").GetBoolean());
            Assert.Equal("RequireUnique", f.GetProperty("occurrence").GetString());
        }

        [Fact]
        public void TheFullForm_KeepsEveryOption()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelField("Due", "Due Date|Payment Due", ValuePosition.Below, FieldType.Date, "dd/MM/yyyy|yyyy-MM-dd", false, Occurrence.Last, out string m), m);
            JsonElement f = Field(c, 0);
            Assert.Equal("Below", f.GetProperty("position").GetString());
            Assert.Equal("dd/MM/yyyy|yyyy-MM-dd", f.GetProperty("format").GetString());
            Assert.False(f.GetProperty("required").GetBoolean());
            Assert.Equal("Last", f.GetProperty("occurrence").GetString());
        }

        [Theory]
        [InlineData(FieldType.Amount, null, "DotDecimal")]
        [InlineData(FieldType.Amount, "", "DotDecimal")]
        [InlineData(FieldType.Amount, " CommaDecimal ", "CommaDecimal")]
        [InlineData(FieldType.Decimal, "DotDecimal", "DotDecimal")]
        [InlineData(FieldType.Percentage, "CommaDecimal", "CommaDecimal")]
        [InlineData(FieldType.Date, null, "yyyy-MM-dd")]
        [InlineData(FieldType.Date, "dd.MM.yyyy | MM/dd/yyyy", "dd.MM.yyyy|MM/dd/yyyy")]
        [InlineData(FieldType.Text, null, "")]
        [InlineData(FieldType.Iban, "  ", "")]
        public void Formats_AreResolvedToTheirCanonicalForm(FieldType type, string format, string resolved)
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelField("F", "Label", ValuePosition.SameLine, type, format, true, Occurrence.RequireUnique, out string m), m);
            Assert.Equal(resolved, Field(c, 0).GetProperty("format").GetString());
        }

        [Theory]
        [InlineData(FieldType.Amount, "dotdecimal")]            // exact names only
        [InlineData(FieldType.Amount, "de-DE")]                 // never a culture
        [InlineData(FieldType.Decimal, "yyyy-MM-dd")]
        [InlineData(FieldType.Date, "yyyy-MM")]                  // a date part is missing
        [InlineData(FieldType.Date, "yyyy-MM-dd HH:mm")]         // time tokens are refused
        [InlineData(FieldType.Date, "yyyy-MM-dd|yyyy-MM-dd")]    // repeated
        [InlineData(FieldType.Date, "yyyy-MM-dd|")]              // an empty alternative
        [InlineData(FieldType.Code, "anything")]                 // this type takes no format
        [InlineData(FieldType.Email, "x")]
        public void BadFormats_AreRefused(FieldType type, string format)
        {
            using var c = new TextExtractUtils();
            Assert.False(c.AddLabelField("F", "Label", ValuePosition.SameLine, type, format, true, Occurrence.RequireUnique, out string m));
            Assert.Contains("InvalidFormat", m);
            Assert.Contains("AddLabelField", m);
        }

        [Fact]
        public void ADateFieldAcceptsUpToTenFormats()
        {
            string[] ten = { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd.MM.yyyy", "yyyyMMdd", "dd-MM-yyyy", "MM-dd-yyyy", "yyyy/MM/dd", "dd MM yyyy", "yyyy.MM.dd" };
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelField("A", "L", ValuePosition.SameLine, FieldType.Date, string.Join("|", ten), true, Occurrence.RequireUnique, out string m), m);
            Assert.False(c.AddLabelField("B", "L", ValuePosition.SameLine, FieldType.Date, string.Join("|", ten) + "|MM.dd.yyyy", true, Occurrence.RequireUnique, out m));
            Assert.Contains("at most 10", m);
        }

        [Theory]
        [InlineData(null, "at least one label")]
        [InlineData("", "at least one label")]
        [InlineData("   ", "at least one label")]
        [InlineData("Invoice||Number", "is empty")]
        [InlineData("Invoice|", "is empty")]
        [InlineData("Total|total", "repeats")]
        [InlineData("---", "no letter or digit")]
        [InlineData("Line\nbreak", "control character")]
        [InlineData("Tab\there", "control character")]
        public void BadLabels_AreRefused(string labels, string expected)
        {
            using var c = new TextExtractUtils();
            Assert.False(c.AddLabelFieldSimple("F", labels, FieldType.Text, out string m));
            Assert.Contains("InvalidLabel", m);
            Assert.Contains(expected, m);
        }

        [Fact]
        public void TextThatIsNotValidUnicode_IsRefusedInLabelsNamesAndPatterns()
        {
            // (Not a theory case: xUnit's attribute data would replace the lone surrogate with U+FFFD before the test saw it.)
            using var c = new TextExtractUtils();
            Assert.False(c.AddLabelFieldSimple("F", "Bad \ud800 text", FieldType.Text, out string m)); Assert.Contains("InvalidLabel", m); Assert.Contains("not valid", m);
            Assert.False(c.AddLabelFieldSimple("F\udc00", "L", FieldType.Text, out m)); Assert.Contains("InvalidName", m);
            Assert.False(c.AddPatternField("P", "(?<value>\ud800)", FieldType.Text, null, out m)); Assert.Contains("InvalidPattern", m);
            Assert.True(c.AddLabelFieldSimple("Rocket", "Launch \ud83d\ude80", FieldType.Text, out m), m);   // a proper pair is fine
        }

        [Fact]
        public void LabelLimits_AreEnforcedAtTheBoundary()
        {
            using var c = new TextExtractUtils();
            string twenty = string.Join("|", Enumerable.Range(0, 20).Select(i => "L" + i));
            Assert.True(c.AddLabelFieldSimple("A", twenty, FieldType.Text, out string m), m);
            Assert.False(c.AddLabelFieldSimple("B", twenty + "|L20", FieldType.Text, out m));
            Assert.Contains("at most 20", m);
            Assert.True(c.AddLabelFieldSimple("C", new string('a', 128), FieldType.Text, out m), m);
            Assert.False(c.AddLabelFieldSimple("D", new string('a', 129), FieldType.Text, out m));
            Assert.Contains("longer than 128", m);
            Assert.True(c.AddLabelFieldSimple("E", "  Spaced Label  ", FieldType.Text, out m), m);        // trimmed, not refused
            Assert.Equal("Spaced Label", Field(c, 2).GetProperty("labels")[0].GetString());
        }

        [Fact]
        public void Names_AreRequired_Bounded_AndUniqueIgnoringCase_AcrossLabelAndPatternFields()
        {
            using var c = new TextExtractUtils();
            Assert.False(c.AddLabelFieldSimple(null, "L", FieldType.Text, out string m)); Assert.Contains("InvalidName", m);
            Assert.False(c.AddLabelFieldSimple(" ", "L", FieldType.Text, out m)); Assert.Contains("InvalidName", m);
            Assert.False(c.AddLabelFieldSimple(new string('n', 129), "L", FieldType.Text, out m)); Assert.Contains("InvalidName", m);
            Assert.True(c.AddLabelFieldSimple(new string('n', 128), "L", FieldType.Text, out m), m);
            Assert.True(c.AddLabelFieldSimple("Total", "Total", FieldType.Amount, out m), m);
            Assert.False(c.AddLabelFieldSimple("TOTAL", "Sum", FieldType.Amount, out m)); Assert.Contains("DuplicateName", m);
            Assert.False(c.AddPatternField("total", "(?<value>\\d+)", FieldType.Integer, null, out m)); Assert.Contains("DuplicateName", m);
        }

        [Fact]
        public void UnknownEnumValues_AreRefused()
        {
            using var c = new TextExtractUtils();
            Assert.False(c.AddLabelField("A", "L", (ValuePosition)9, FieldType.Text, null, true, Occurrence.RequireUnique, out string m)); Assert.Contains("UnknownEnumValue", m);
            Assert.False(c.AddLabelField("A", "L", ValuePosition.SameLine, (FieldType)99, null, true, Occurrence.RequireUnique, out m)); Assert.Contains("UnknownEnumValue", m);
            Assert.False(c.AddLabelField("A", "L", ValuePosition.SameLine, FieldType.Text, null, true, (Occurrence)(-1), out m)); Assert.Contains("UnknownEnumValue", m);
            Assert.False(c.AddLabelFieldSimple("A", "L", (FieldType)(-3), out m)); Assert.Contains("UnknownEnumValue", m);
        }

        [Fact]
        public void PatternFields_NeedAValueGroup_AValidPattern_AndAreBounded()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddPatternField("Ref", "Ref:\\s*(?<value>[A-Z]{3}\\d+)", FieldType.Code, "", out string m), m);
            JsonElement f = Field(c, 0);
            Assert.Equal("Pattern", f.GetProperty("kind").GetString());
            Assert.Equal("Ref:\\s*(?<value>[A-Z]{3}\\d+)", f.GetProperty("pattern").GetString());
            Assert.False(f.TryGetProperty("labels", out _));
            Assert.False(f.TryGetProperty("position", out _));
            Assert.True(f.GetProperty("required").GetBoolean());

            Assert.False(c.AddPatternField("A", "Ref:\\s*(\\d+)", FieldType.Code, null, out m)); Assert.Contains("named group called value", m);
            Assert.False(c.AddPatternField("A", "(?<value>[a-", FieldType.Code, null, out m)); Assert.Contains("not a valid regular expression", m);
            Assert.False(c.AddPatternField("A", "", FieldType.Code, null, out m)); Assert.Contains("InvalidPattern", m);
            Assert.False(c.AddPatternField("A", null, FieldType.Code, null, out m)); Assert.Contains("InvalidPattern", m);
            Assert.False(c.AddPatternField("A", "(?<value>x)" + new string('y', 1014), FieldType.Code, null, out m)); Assert.Contains("longer than 1024", m);
            Assert.True(c.AddPatternField("B", "(?<value>x)" + new string('y', 1013), FieldType.Code, null, out m), m);
            Assert.False(c.AddPatternField("C", "(?<value>\\d+)", FieldType.Integer, "DotDecimal", out m)); Assert.Contains("InvalidFormat", m);
        }

        [Fact]
        public void APatternIsCompiledWithATimeout_SoACatastrophicPatternCannotHangTheRobot()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddPatternField("Bad", "^(?<value>(a+)+)$", FieldType.Text, null, out string m), m);
            FieldDef field = c.CurrentTemplate.Fields.Single();
            Assert.Equal(TimeSpan.FromMilliseconds(100), field.CompiledPattern.MatchTimeout);
            Assert.True((field.CompiledPattern.Options & System.Text.RegularExpressions.RegexOptions.CultureInvariant) != 0);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(() => field.CompiledPattern.Match(new string('a', 40) + "!"));
            Assert.True(sw.ElapsedMilliseconds < 5000);
        }

        [Fact]
        public void TheFieldLimit_Is200()
        {
            using var c = new TextExtractUtils();
            for (int i = 0; i < 200; i++) Assert.True(c.AddLabelFieldSimple("F" + i, "L" + i, FieldType.Text, out string m), m);
            Assert.False(c.AddLabelFieldSimple("F200", "L", FieldType.Text, out string message));
            Assert.Contains("TooManyFields", message);
            Assert.False(c.AddPatternField("P", "(?<value>x)", FieldType.Text, null, out message));
            Assert.Contains("TooManyFields", message);
        }

        [Fact]
        public void ARefusedChange_LeavesTheTemplateUntouched_AndClearTemplateRestoresEverything()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("A", "L", FieldType.Text, out string m), m);
            Assert.True(c.ConfigureLimits(5000, out m), m);
            string before = Canonical(c);
            Assert.False(c.AddLabelFieldSimple("a", "L", FieldType.Text, out _));
            Assert.False(c.ConfigureLimits(0, out _));
            Assert.False(c.ConfigureLimits(10000001, out m)); Assert.Contains("must not exceed 10000000", m);
            Assert.Equal(before, Canonical(c));
            Assert.True(c.ClearTemplate(out m), m);
            Assert.Equal("{\"schemaVersion\":1,\"fields\":[],\"limits\":{\"maximumTextCharacters\":1000000}}", Canonical(c));
            Assert.True(c.ConfigureLimits(10000000, out m), m);
        }

        [Fact]
        public void ATemplateWhoseSavedFormWouldExceedTheLoadLimit_IsRefusedWhenBuilt()
        {
            using var c = new TextExtractUtils();
            string twenty = string.Join("|", Enumerable.Range(0, 20).Select(i => new string('x', 124) + i.ToString("D3")));   // about 2,600 characters per field
            string refusal = null;
            for (int i = 0; i < 200 && refusal == null; i++)
                if (!c.AddLabelFieldSimple("F" + i, twenty, FieldType.Text, out string m)) refusal = m;
            Assert.NotNull(refusal);
            Assert.Contains("TemplateTooLarge", refusal);
            string saved = Canonical(c);
            Assert.True(saved.Length <= 256000);
            using var again = new TextExtractUtils();
            Assert.True(again.LoadTemplateJson(saved, out string message), message);       // what was accepted still saves and loads
            Assert.Equal(saved, Canonical(again));
        }

        [Fact]
        public void CultureDoesNotChangeTheCanonicalTemplate()
        {
            string Build()
            {
                using var c = new TextExtractUtils();
                Assert.True(c.AddLabelField("İnvoice", "Fatura No|INVOICE", ValuePosition.NextLine, FieldType.Amount, "CommaDecimal", true, Occurrence.First, out string m), m);
                return Canonical(c);
            }
            string invariant = Build(), turkish = null;
            CultureScope.Run("tr-TR", () => turkish = Build());
            Assert.Equal(invariant, turkish ?? invariant);
            Assert.Contains("\"name\":\"İnvoice\"", invariant);                       // non-ASCII kept as typed, not \u escaped
        }
    }
}
