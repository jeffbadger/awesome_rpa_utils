using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>The method-call way of building a definition, and the rules it shares with JSON loading.</summary>
    public sealed class DefinitionBuilderTests
    {
        private static string Json(ReconciliationUtils c)
        {
            Assert.True(c.GetDefinitionJson(out string json, out string message), message);
            return json;
        }

        private static ReconciliationUtils Built()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMapping("Company", "/company", "/entity", true, true, out string m), m);
            Assert.True(c.AddKeyMappingSimple("Invoice", "/invoiceNumber", "/invoiceId", out m), m);
            Assert.True(c.AddDecimalComparison("Amount", "/amount", "/paidAmount", "0.01", ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddTextComparison("Status", "/status", "/status", true, true, ComparisonNullPolicy.AllowBothNull, out m), m);
            return c;
        }

        [Fact]
        public void ANewComponent_HasAnEmptyDefinition_WithTheDefaultLimits()
        {
            using var c = new ReconciliationUtils();
            using JsonDocument doc = JsonDocument.Parse(Json(c));
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(0, doc.RootElement.GetProperty("keys").GetArrayLength());
            Assert.Equal(0, doc.RootElement.GetProperty("comparisons").GetArrayLength());
            JsonElement limits = doc.RootElement.GetProperty("limits");
            Assert.Equal(50000, limits.GetProperty("maximumRowsPerSide").GetInt32());
            Assert.Equal(8000000, limits.GetProperty("maximumInputCharactersPerSide").GetInt32());
            Assert.Equal(100000, limits.GetProperty("maximumResults").GetInt32());
            Assert.Equal(100000, limits.GetProperty("maximumDifferenceDetails").GetInt32());
        }

        [Fact]
        public void TheBuiltDefinition_IsCanonicalJson_WithEveryOptionSpelledOut()
        {
            using ReconciliationUtils c = Built();
            Assert.Equal(
                "{\"schemaVersion\":1," +
                "\"keys\":[{\"name\":\"Company\",\"leftPointer\":\"/company\",\"rightPointer\":\"/entity\",\"trim\":true,\"ignoreCase\":true}," +
                "{\"name\":\"Invoice\",\"leftPointer\":\"/invoiceNumber\",\"rightPointer\":\"/invoiceId\",\"trim\":false,\"ignoreCase\":false}]," +
                "\"comparisons\":[{\"name\":\"Amount\",\"kind\":\"Decimal\",\"leftPointer\":\"/amount\",\"rightPointer\":\"/paidAmount\",\"absoluteTolerance\":\"0.01\",\"nullPolicy\":\"RequireValue\"}," +
                "{\"name\":\"Status\",\"kind\":\"Text\",\"leftPointer\":\"/status\",\"rightPointer\":\"/status\",\"trim\":true,\"ignoreCase\":true,\"nullPolicy\":\"AllowBothNull\"}]," +
                "\"limits\":{\"maximumRowsPerSide\":50000,\"maximumInputCharactersPerSide\":8000000,\"maximumResults\":100000,\"maximumDifferenceDetails\":100000}}",
                Json(c));
        }

        [Fact]
        public void TheSimpleForms_UseTheDocumentedDefaults()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("K", "/k", "/k", out string m), m);
            Assert.True(c.AddTextComparisonSimple("T", "/t", "/t", out m), m);
            Assert.True(c.AddDecimalComparisonSimple("D", "/d", "/d", out m), m);

            using var full = new ReconciliationUtils();
            Assert.True(full.AddKeyMapping("K", "/k", "/k", false, false, out m), m);
            Assert.True(full.AddTextComparison("T", "/t", "/t", false, false, ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(full.AddDecimalComparison("D", "/d", "/d", "0", ComparisonNullPolicy.RequireValue, out m), m);

            Assert.Equal(Json(full), Json(c));
        }

        [Fact]
        public void ASuccessfulCall_HasNoMessage()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("K", "/k", "/k", out string message));
            Assert.Null(message);
        }

        // ---------------------------------------------------------------- rejection is atomic

        [Theory]
        [InlineData(null, "/a", "/a", "InvalidName")]
        [InlineData("", "/a", "/a", "InvalidName")]
        [InlineData("   ", "/a", "/a", "InvalidName")]
        [InlineData("K", null, "/a", "InvalidPointer")]
        [InlineData("K", "", "/a", "InvalidPointer")]
        [InlineData("K", "a", "/a", "InvalidPointer")]
        [InlineData("K", "/a", "b", "InvalidPointer")]
        [InlineData("K", "/a~", "/a", "InvalidPointer")]
        public void ARejectedKeyMapping_ChangesNothing_AndNamesTheProblem(string name, string left, string right, string code)
        {
            using ReconciliationUtils c = Built();
            string before = Json(c);
            Assert.False(c.AddKeyMapping(name, left, right, false, false, out string message));
            Assert.Contains(code, message);
            Assert.StartsWith("AddKeyMapping failed:", message);
            Assert.Equal(before, Json(c));
        }

        [Theory]
        [InlineData("high")]
        [InlineData("low")]
        [InlineData("trailing-high")]
        public void AnUnpairedSurrogateInANameOrPointer_IsRejected_SoTheDefinitionCanAlwaysBeWritten(string which)
        {
            string bad = JsonInputTests.Unpaired(which);
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Good", "/a", "/a", out string m), m);
            string before = Json(c);

            Assert.False(c.AddKeyMappingSimple("n" + bad, "/a", "/a", out m)); Assert.Contains("InvalidName", m);
            Assert.False(c.AddKeyMappingSimple("N", "/a" + bad, "/a", out m)); Assert.Contains("InvalidPointer", m);
            Assert.False(c.AddKeyMappingSimple("N", "/a", "/" + bad, out m)); Assert.Contains("InvalidPointer", m);
            Assert.False(c.AddTextComparisonSimple("n" + bad, "/a", "/a", out m)); Assert.Contains("InvalidName", m);
            Assert.False(c.AddDecimalComparisonSimple("N", "/" + bad, "/a", out m)); Assert.Contains("InvalidPointer", m);
            Assert.DoesNotContain("failed unexpectedly", m);

            Assert.Equal(before, Json(c));            // nothing changed, and the canonical JSON is still writable
        }

        [Fact]
        public void AProperSurrogatePair_InANameOrPointer_IsAccepted_AndWritten()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("\U0001F389 party", "/\U0001F389", "/\U0001F389", out string m), m);
            Assert.Contains("party", Json(c));
        }

        [Fact]
        public void ANameTooLong_IsRejected_AtItsBoundary()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple(new string('n', 128), "/a", "/a", out string m), m);
            Assert.False(c.AddKeyMappingSimple(new string('n', 129), "/a", "/a", out m));
            Assert.Contains("longer than 128", m);
        }

        [Fact]
        public void Names_AreUniqueAcrossKeysAndComparisons_IgnoringCase()
        {
            using ReconciliationUtils c = Built();
            string before = Json(c);
            Assert.False(c.AddKeyMappingSimple("company", "/x", "/x", out string message));       // same as a key, different case
            Assert.Contains("DuplicateName", message);
            Assert.False(c.AddTextComparisonSimple("INVOICE", "/x", "/x", out message));           // a comparison named like a key
            Assert.Contains("DuplicateName", message);
            Assert.False(c.AddKeyMappingSimple("amount", "/x", "/x", out message));                // a key named like a comparison
            Assert.Contains("DuplicateName", message);
            Assert.False(c.AddDecimalComparisonSimple("STATUS", "/x", "/x", out message));
            Assert.Contains("DuplicateName", message);
            Assert.Equal(before, Json(c));
        }

        [Fact]
        public void TheKeyAndComparisonCounts_AreBounded()
        {
            using var c = new ReconciliationUtils();
            for (int i = 0; i < 16; i++) Assert.True(c.AddKeyMappingSimple("k" + i, "/a", "/a", out string m), m);
            Assert.False(c.AddKeyMappingSimple("k16", "/a", "/a", out string message));
            Assert.Contains("TooManyKeys", message);
            for (int i = 0; i < 128; i++) Assert.True(c.AddTextComparisonSimple("c" + i, "/a", "/a", out message), message);
            Assert.False(c.AddTextComparisonSimple("c128", "/a", "/a", out message));
            Assert.Contains("TooManyComparisons", message);
        }

        [Theory]
        [InlineData("0", true)]
        [InlineData("0.01", true)]
        [InlineData("10", true)]
        [InlineData("00.5", true)]
        [InlineData("123456789.123456789", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData(" ", false)]
        [InlineData("-0.01", false)]      // a tolerance is never negative
        [InlineData("+0.01", false)]
        [InlineData("1e-2", false)]
        [InlineData("1,5", false)]
        [InlineData("1.", false)]
        [InlineData(".5", false)]
        [InlineData("0.01 ", false)]
        [InlineData("abc", false)]
        public void TheToleranceGrammar(string tolerance, bool accepted)
        {
            using var c = new ReconciliationUtils();
            bool ok = c.AddDecimalComparison("D", "/a", "/a", tolerance, ComparisonNullPolicy.RequireValue, out string message);
            Assert.Equal(accepted, ok);
            if (!accepted) Assert.Contains("InvalidTolerance", message);
        }

        [Fact]
        public void AToleranceOverTheLengthBound_IsRejected()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddDecimalComparison("A", "/a", "/a", new string('1', 256), ComparisonNullPolicy.RequireValue, out string m), m);
            Assert.False(c.AddDecimalComparison("B", "/a", "/a", new string('1', 257), ComparisonNullPolicy.RequireValue, out m));
            Assert.Contains("longer than 256", m);
        }

        [Fact]
        public void AnUndefinedNullPolicyValue_IsRejected()
        {
            using var c = new ReconciliationUtils();
            Assert.False(c.AddTextComparison("T", "/a", "/a", false, false, (ComparisonNullPolicy)7, out string message));
            Assert.Contains("UnknownEnumValue", message);
        }

        // ---------------------------------------------------------------- lifecycle

        [Fact]
        public void ClearDefinition_RestoresTheDefaults()
        {
            using ReconciliationUtils c = Built();
            Assert.True(c.ConfigureLimits(10, 20, 30, 40, out string m), m);
            Assert.True(c.ClearDefinition(out m), m);
            using var fresh = new ReconciliationUtils();
            Assert.Equal(Json(fresh), Json(c));
        }

        [Fact]
        public void ClearDefinition_OnAnEmptyDefinition_Succeeds()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.ClearDefinition(out string message));
            Assert.Null(message);
        }

        [Fact]
        public void TheDefinitionCanBeBuiltAgain_AfterItIsCleared()
        {
            using ReconciliationUtils c = Built();
            string built = Json(c);
            Assert.True(c.ClearDefinition(out _));
            Assert.True(c.AddKeyMapping("Company", "/company", "/entity", true, true, out _));
            Assert.True(c.AddKeyMappingSimple("Invoice", "/invoiceNumber", "/invoiceId", out _));
            Assert.True(c.AddDecimalComparison("Amount", "/amount", "/paidAmount", "0.01", ComparisonNullPolicy.RequireValue, out _));
            Assert.True(c.AddTextComparison("Status", "/status", "/status", true, true, ComparisonNullPolicy.AllowBothNull, out _));
            Assert.Equal(built, Json(c));
        }

        // ---------------------------------------------------------------- limits

        [Fact]
        public void ConfigureLimits_SetsAllFour_AndTheyAppearInTheDefinition()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.ConfigureLimits(1, 2, 3, 4, out string message), message);
            using JsonDocument doc = JsonDocument.Parse(Json(c));
            JsonElement limits = doc.RootElement.GetProperty("limits");
            Assert.Equal(new[] { 1, 2, 3, 4 }, new[] { "maximumRowsPerSide", "maximumInputCharactersPerSide", "maximumResults", "maximumDifferenceDetails" }.Select(n => limits.GetProperty(n).GetInt32()));
        }

        [Fact]
        public void ConfigureLimits_AcceptsTheMaximums()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.ConfigureLimits(250000, 32000000, 500000, 500000, out string message), message);
        }

        [Theory]
        [InlineData(0, 1, 1, 1, "maximumRowsPerSide")]
        [InlineData(1, 0, 1, 1, "maximumInputCharactersPerSide")]
        [InlineData(1, 1, 0, 1, "maximumResults")]
        [InlineData(1, 1, 1, 0, "maximumDifferenceDetails")]
        [InlineData(-5, 1, 1, 1, "maximumRowsPerSide")]
        [InlineData(250001, 1, 1, 1, "maximumRowsPerSide")]
        [InlineData(1, 32000001, 1, 1, "maximumInputCharactersPerSide")]
        [InlineData(1, 1, 500001, 1, "maximumResults")]
        [InlineData(1, 1, 1, 500001, "maximumDifferenceDetails")]
        [InlineData(int.MaxValue, 1, 1, 1, "maximumRowsPerSide")]
        public void AnInvalidLimit_IsRejected_AndNothingChanges(int rows, int characters, int results, int differences, string named)
        {
            using ReconciliationUtils c = Built();
            string before = Json(c);
            Assert.False(c.ConfigureLimits(rows, characters, results, differences, out string message));
            Assert.Contains(named, message);
            Assert.Equal(before, Json(c));
        }

        // ---------------------------------------------------------------- disposal

        [Fact]
        public void EveryDefinitionMethod_FailsAfterDisposal()
        {
            var c = new ReconciliationUtils();
            c.Dispose();
            Assert.False(c.AddKeyMappingSimple("K", "/a", "/a", out string m)); Assert.Contains("disposed", m);
            Assert.False(c.ClearDefinition(out m)); Assert.Contains("disposed", m);
            Assert.False(c.ConfigureLimits(1, 1, 1, 1, out m)); Assert.Contains("disposed", m);
            Assert.False(c.GetDefinitionJson(out string json, out m)); Assert.Contains("disposed", m); Assert.Null(json);
            Assert.False(c.LoadDefinitionJson("{}", out m)); Assert.Contains("disposed", m);
            Assert.False(c.ValidateDefinitionJson("{}", out int errors, out string report, out m)); Assert.Contains("disposed", m); Assert.Equal(0, errors); Assert.Null(report);
        }
    }
}
