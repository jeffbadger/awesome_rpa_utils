using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class DefinitionJsonTests
    {
        private const string Full = """
            {
              "schemaVersion": 1,
              "keys": [
                { "name": "Company", "leftPointer": "/company", "rightPointer": "/entity", "trim": true, "ignoreCase": true },
                { "name": "Invoice", "leftPointer": "/invoiceNumber", "rightPointer": "/invoiceId", "trim": true, "ignoreCase": false }
              ],
              "comparisons": [
                { "name": "Amount", "kind": "Decimal", "leftPointer": "/amount", "rightPointer": "/paidAmount", "absoluteTolerance": "0.01", "nullPolicy": "RequireValue" },
                { "name": "Status", "kind": "Text", "leftPointer": "/status", "rightPointer": "/status", "trim": true, "ignoreCase": true, "nullPolicy": "RequireValue" }
              ]
            }
            """;

        private static string Get(ReconciliationUtils c)
        {
            Assert.True(c.GetDefinitionJson(out string json, out string message), message);
            return json;
        }

        private static string Validate(string json, out int errorCount)
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.ValidateDefinitionJson(json, out errorCount, out string report, out string message), message);
            Assert.Null(message);
            return report;
        }

        private static string FirstCode(string report) =>
            JsonDocument.Parse(report).RootElement.GetProperty("errors")[0].GetProperty("code").GetString();

        private static string[] Codes(string report) =>
            JsonDocument.Parse(report).RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToArray();

        // ---------------------------------------------------------------- loading

        [Fact]
        public void AValidDefinition_Loads_AndRoundTripsThroughTheCanonicalForm()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson(Full, out string message), message);
            Assert.Null(message);
            string canonical = Get(c);

            using var again = new ReconciliationUtils();
            Assert.True(again.LoadDefinitionJson(canonical, out message), message);
            Assert.Equal(canonical, Get(again));      // Get -> Load -> Get is stable
        }

        [Fact]
        public void ADefinitionLoadedFromJson_EqualsTheSameOneBuiltWithMethods()
        {
            using var fromJson = new ReconciliationUtils();
            Assert.True(fromJson.LoadDefinitionJson(Full, out string m), m);

            using var built = new ReconciliationUtils();
            Assert.True(built.AddKeyMapping("Company", "/company", "/entity", true, true, out m), m);
            Assert.True(built.AddKeyMapping("Invoice", "/invoiceNumber", "/invoiceId", true, false, out m), m);
            Assert.True(built.AddDecimalComparison("Amount", "/amount", "/paidAmount", "0.01", ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(built.AddTextComparison("Status", "/status", "/status", true, true, ComparisonNullPolicy.RequireValue, out m), m);

            Assert.Equal(Get(built), Get(fromJson));
        }

        [Fact]
        public void OmittedOptions_TakeTheirDocumentedDefaults()
        {
            const string minimal = """
                { "schemaVersion": 1,
                  "keys": [ { "name": "K", "leftPointer": "/k", "rightPointer": "/k" } ],
                  "comparisons": [
                    { "name": "T", "kind": "Text", "leftPointer": "/t", "rightPointer": "/t" },
                    { "name": "D", "kind": "Decimal", "leftPointer": "/d", "rightPointer": "/d" } ] }
                """;
            using var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson(minimal, out string m), m);
            using var simple = new ReconciliationUtils();
            Assert.True(simple.AddKeyMappingSimple("K", "/k", "/k", out m), m);
            Assert.True(simple.AddTextComparisonSimple("T", "/t", "/t", out m), m);
            Assert.True(simple.AddDecimalComparisonSimple("D", "/d", "/d", out m), m);
            Assert.Equal(Get(simple), Get(c));
        }

        [Fact]
        public void LimitsInTheJson_AreApplied_AndOmittedOnesKeepTheDefaults()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson("{\"schemaVersion\":1,\"limits\":{\"maximumRowsPerSide\":7,\"maximumResults\":9}}", out string m), m);
            using JsonDocument doc = JsonDocument.Parse(Get(c));
            JsonElement limits = doc.RootElement.GetProperty("limits");
            Assert.Equal(7, limits.GetProperty("maximumRowsPerSide").GetInt32());
            Assert.Equal(9, limits.GetProperty("maximumResults").GetInt32());
            Assert.Equal(8000000, limits.GetProperty("maximumInputCharactersPerSide").GetInt32());
            Assert.Equal(100000, limits.GetProperty("maximumDifferenceDetails").GetInt32());
        }

        [Fact]
        public void Loading_ReplacesTheWholeDefinition_IncludingLimitsSetEarlier()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Old", "/o", "/o", out _));
            Assert.True(c.ConfigureLimits(5, 5, 5, 5, out _));
            Assert.True(c.LoadDefinitionJson("{\"schemaVersion\":1}", out string m), m);
            using var fresh = new ReconciliationUtils();
            Assert.Equal(Get(fresh), Get(c));        // nothing of the earlier definition survives, limits included
        }

        [Fact]
        public void ADefinitionWithNoKeys_CanBeLoaded_WhileItIsBeingBuilt()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson("{\"schemaVersion\":1,\"keys\":[],\"comparisons\":[]}", out string m), m);
        }

        [Fact]
        public void CommentsAndTrailingCommas_AreNotAcceptedInADefinition()
        {
            using var c = new ReconciliationUtils();
            Assert.False(c.LoadDefinitionJson("{\"schemaVersion\":1, // note\n}", out _));
            Assert.False(c.LoadDefinitionJson("{\"schemaVersion\":1,}", out _));
        }

        // ---------------------------------------------------------------- a rejected load changes nothing

        [Theory]
        [InlineData("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\"}]}", "MissingProperty")]
        [InlineData("{}", "MissingProperty")]                                                                // schemaVersion is required
        [InlineData("{\"schemaVersion\":2}", "UnsupportedVersion")]
        [InlineData("{\"schemaVersion\":\"1\"}", "InvalidType")]
        [InlineData("{\"schemaVersion\":1.5}", "InvalidType")]
        [InlineData("[]", "NotAnObject")]
        [InlineData("\"x\"", "NotAnObject")]
        [InlineData("not json", "MalformedJson")]
        [InlineData("{\"schemaVersion\":1,\"unknown\":1}", "UnknownProperty")]
        [InlineData("{\"schemaVersion\":1,\"keys\":{}}", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[1]}", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"/k\",\"rightPointer\":\"/k\",\"extra\":1}]}", "UnknownProperty")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"/k\",\"rightPointer\":\"/k\",\"trim\":\"yes\"}]}", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"/k\",\"rightPointer\":\"/k\",\"trim\":1}]}", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"k\",\"rightPointer\":\"/k\"}]}", "InvalidPointer")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[{\"name\":5,\"leftPointer\":\"/k\",\"rightPointer\":\"/k\"}]}", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"/k\",\"rightPointer\":\"/k\"},{\"name\":\"k\",\"leftPointer\":\"/j\",\"rightPointer\":\"/j\"}]}", "DuplicateName")]
        public void AnInvalidDefinition_IsRejected_WithAStableCode_AndLeavesTheCurrentOneUntouched(string json, string code)
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Existing", "/e", "/e", out _));
            string before = Get(c);

            Assert.False(c.LoadDefinitionJson(json, out string message));
            Assert.StartsWith("LoadDefinitionJson failed:", message);
            Assert.Contains(code, message);
            Assert.Equal(before, Get(c));

            Validate(json, out int errors);
            Assert.True(errors > 0);
        }

        [Theory]
        [InlineData("\"nullPolicy\":1", "UnknownEnumValue")]                  // a number is not an enum name
        [InlineData("\"nullPolicy\":\"requirevalue\"", "UnknownEnumValue")]   // and the name is case-sensitive
        [InlineData("\"nullPolicy\":\"Sometimes\"", "UnknownEnumValue")]
        [InlineData("\"nullPolicy\":null", "UnknownEnumValue")]
        public void ANullPolicy_MustBeAKnownEnumName(string option, string code)
        {
            string json = "{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"T\",\"kind\":\"Text\",\"leftPointer\":\"/t\",\"rightPointer\":\"/t\"," + option + "}]}";
            Assert.Equal(code, FirstCode(Validate(json, out _)));
        }

        [Theory]
        [InlineData("\"kind\":\"Money\"", "UnknownKind")]              // Release 2 kinds are unknown to this version
        [InlineData("\"kind\":\"Boolean\"", "UnknownKind")]
        [InlineData("\"kind\":\"CalendarDate\"", "UnknownKind")]
        [InlineData("\"kind\":\"Instant\"", "UnknownKind")]
        [InlineData("\"kind\":\"text\"", "UnknownKind")]              // case-sensitive
        [InlineData("\"kind\":7", "InvalidType")]                  // present but not a string: a type error, not "missing"
        [InlineData("\"kind\":null", "InvalidType")]
        [InlineData("\"kind\":true", "InvalidType")]
        [InlineData("\"kind\":{}", "InvalidType")]
        public void AComparisonKind_MustBeTextOrDecimal(string kind, string code)
        {
            string json = "{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"C\"," + kind + ",\"leftPointer\":\"/c\",\"rightPointer\":\"/c\"}]}";
            Assert.Equal(code, FirstCode(Validate(json, out _)));
        }

        [Theory]
        [InlineData("\"kind\":\"Money\"")]
        [InlineData("\"kind\":7")]
        [InlineData("")]                                              // no kind at all
        public void AComparisonWhoseKindCannotBeSelected_StillHasItsOtherPropertiesChecked(string kind)
        {
            string body = "{\"name\":\"C\",\"leftPointer\":\"/c\",\"rightPointer\":\"/c\",\"bogus\":1" + (kind == "" ? "" : "," + kind) + "}";
            string report = Validate("{\"schemaVersion\":1,\"comparisons\":[" + body + "]}", out int errors);
            Assert.Equal(2, errors);                                   // the kind problem and the unknown property
            Assert.Contains("UnknownProperty", Codes(report));
            Assert.Contains("comparisons[0].bogus", report);
        }

        [Fact]
        public void AComparisonWhoseKindCannotBeSelected_ReportsARepeatedProperty()
        {
            string json = "{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"C\",\"name\":\"D\",\"kind\":\"Money\",\"leftPointer\":\"/c\",\"rightPointer\":\"/c\"}]}";
            Assert.Equal(new[] { "UnknownKind", "DuplicateProperty" }, Codes(Validate(json, out _)));
        }

        [Fact]
        public void AComparisonWithNoKind_IsRejected()
        {
            string json = "{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"C\",\"leftPointer\":\"/c\",\"rightPointer\":\"/c\"}]}";
            Assert.Equal("MissingProperty", FirstCode(Validate(json, out _)));
        }

        [Theory]
        [InlineData("Text", "\"absoluteTolerance\":\"0\"")]         // a Decimal option on a Text rule
        [InlineData("Decimal", "\"trim\":true")]                     // a Text option on a Decimal rule
        [InlineData("Decimal", "\"ignoreCase\":true")]
        public void AnOptionThatDoesNotBelongToTheRuleKind_IsRejected(string kind, string option)
        {
            string json = "{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"C\",\"kind\":\"" + kind + "\",\"leftPointer\":\"/c\",\"rightPointer\":\"/c\"," + option + "}]}";
            string report = Validate(json, out int errors);
            Assert.Equal(1, errors);
            Assert.Equal("UnknownProperty", FirstCode(report));
            Assert.Contains(kind, report);
        }

        [Theory]
        [InlineData("\"absoluteTolerance\":0.01", "InvalidType")]        // must be text, never a JSON number
        [InlineData("\"absoluteTolerance\":\"-1\"", "InvalidTolerance")]
        [InlineData("\"absoluteTolerance\":\"1e2\"", "InvalidTolerance")]
        [InlineData("\"absoluteTolerance\":\"\"", "InvalidTolerance")]
        public void ADecimalTolerance_MustBeNonNegativeDecimalText(string option, string code)
        {
            string json = "{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"D\",\"kind\":\"Decimal\",\"leftPointer\":\"/d\",\"rightPointer\":\"/d\"," + option + "}]}";
            Assert.Equal(code, FirstCode(Validate(json, out _)));
        }

        [Theory]
        [InlineData("\"maximumRowsPerSide\":0", "InvalidLimit")]
        [InlineData("\"maximumRowsPerSide\":250001", "InvalidLimit")]
        [InlineData("\"maximumResults\":-1", "InvalidLimit")]
        [InlineData("\"maximumRowsPerSide\":\"5\"", "InvalidType")]
        [InlineData("\"maximumRowsPerSide\":5.5", "InvalidType")]
        [InlineData("\"maximumRowsPerSide\":99999999999", "InvalidType")]
        [InlineData("\"bogus\":5", "UnknownProperty")]
        public void TheLimitsSection_IsValidated(string option, string code)
        {
            string json = "{\"schemaVersion\":1,\"limits\":{" + option + "}}";
            Assert.Equal(code, FirstCode(Validate(json, out _)));
        }

        [Theory]
        [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"name\":\"L\",\"leftPointer\":\"/k\",\"rightPointer\":\"/k\"}]}")]
        [InlineData("{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"C\",\"kind\":\"Text\",\"kind\":\"Text\",\"leftPointer\":\"/c\",\"rightPointer\":\"/c\"}]}")]
        [InlineData("{\"schemaVersion\":1,\"limits\":{\"maximumResults\":5,\"maximumResults\":5}}")]
        public void ARepeatedProperty_IsRejected_EvenWhenTheValuesAgree(string json)
        {
            Assert.Contains("DuplicateProperty", Codes(Validate(json, out _)));
        }

        [Fact]
        public void AComparisonWithARepeatedProperty_AndAnUnknownKind_ReportsBoth()
        {
            string json = "{\"schemaVersion\":1,\"comparisons\":[{\"name\":\"C\",\"name\":\"D\",\"kind\":\"Money\",\"leftPointer\":\"/c\",\"rightPointer\":\"/c\"}]}";
            Assert.Equal(new[] { "UnknownKind", "DuplicateProperty" }, Codes(Validate(json, out _)));
        }

        [Fact]
        public void TheKeyAndComparisonCounts_AreBounded_InJsonToo()
        {
            string Keys(int n) => string.Join(",", Enumerable.Range(0, n).Select(i => "{\"name\":\"k" + i + "\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"}"));
            string Comps(int n) => string.Join(",", Enumerable.Range(0, n).Select(i => "{\"name\":\"c" + i + "\",\"kind\":\"Text\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"}"));
            Assert.Equal(0, ErrorCount("{\"schemaVersion\":1,\"keys\":[" + Keys(16) + "]}"));
            Assert.Contains("TooManyKeys", Codes(Validate("{\"schemaVersion\":1,\"keys\":[" + Keys(17) + "]}", out _)));
            Assert.Equal(0, ErrorCount("{\"schemaVersion\":1,\"comparisons\":[" + Comps(128) + "]}"));
            Assert.Contains("TooManyComparisons", Codes(Validate("{\"schemaVersion\":1,\"comparisons\":[" + Comps(129) + "]}", out _)));
        }

        [Fact]
        public void EntriesPastTheKeyLimit_AreStillFullyValidated_TheLimitIsReportedOnce()
        {
            string Key(int i, string extra = "") => "{\"name\":\"k" + i + "\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"" + extra + "}";
            string keys = string.Join(",", Enumerable.Range(0, 16).Select(i => Key(i)))
                + "," + Key(16, ",\"bogus\":1")                                           // 17th: an unknown property
                + "," + "{\"name\":\"k17\",\"name\":\"dup\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"}"   // 18th: a repeated property
                + "," + "5"                                                                 // 19th: not an object
                + "," + "{\"name\":\"k99\",\"leftPointer\":\"no-slash\",\"rightPointer\":\"/a\"}";   // 20th: a bad pointer
            string report = Validate("{\"schemaVersion\":1,\"keys\":[" + keys + "]}", out int errors);
            string[] codes = Codes(report);
            Assert.Equal(1, codes.Count(c => c == "TooManyKeys"));
            Assert.Contains("UnknownProperty", codes);
            Assert.Contains("DuplicateProperty", codes);
            Assert.Contains("InvalidType", codes);
            Assert.Contains("InvalidPointer", codes);
            Assert.Contains("keys[16].bogus", report);
            Assert.Contains("keys[19].leftPointer", report);
        }

        [Fact]
        public void EntriesPastTheComparisonLimit_AreStillFullyValidated_TheLimitIsReportedOnce()
        {
            string Comp(int i, string extra = "") => "{\"name\":\"c" + i + "\",\"kind\":\"Text\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"" + extra + "}";
            string comps = string.Join(",", Enumerable.Range(0, 128).Select(i => Comp(i)))
                + "," + Comp(128, ",\"absoluteTolerance\":\"0\"")                       // 129th: an option of the wrong kind
                + "," + "{\"name\":\"c129\",\"kind\":\"Money\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"}"   // 130th: an unsupported kind
                + "," + "\"text\"";                                                        // 131st: not an object
            string[] codes = Codes(Validate("{\"schemaVersion\":1,\"comparisons\":[" + comps + "]}", out _));
            Assert.Equal(1, codes.Count(c => c == "TooManyComparisons"));
            Assert.Contains("UnknownProperty", codes);
            Assert.Contains("UnknownKind", codes);
            Assert.Contains("InvalidType", codes);
        }

        [Fact]
        public void AnOverLimitDefinition_IsStillRejectedWhole()
        {
            string keys = string.Join(",", Enumerable.Range(0, 17).Select(i => "{\"name\":\"k" + i + "\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"}"));
            using var c = new ReconciliationUtils();
            Assert.False(c.LoadDefinitionJson("{\"schemaVersion\":1,\"keys\":[" + keys + "]}", out string message));
            Assert.Contains("TooManyKeys", message);
        }

        private static int ErrorCount(string json) { Validate(json, out int n); return n; }

        [Fact]
        public void ANameUsedByAKeyAndAComparison_IsRejected_InJson()
        {
            string json = "{\"schemaVersion\":1,\"keys\":[{\"name\":\"X\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"}],\"comparisons\":[{\"name\":\"x\",\"kind\":\"Text\",\"leftPointer\":\"/a\",\"rightPointer\":\"/a\"}]}";
            Assert.Contains("DuplicateName", Codes(Validate(json, out _)));
        }

        [Fact]
        public void ASetupCall_MadeWhileALoadIsInProgress_WaitsForIt_AndIsAppliedAfterwards_NotOverwritten()
        {
            // Deterministic: from inside the load (after the parse, before the commit) another thread starts a setup call. It must
            // block until the load is finished and then apply to the LOADED definition. If the load did not hold the lock, that call
            // would commit first and the load would silently overwrite it.
            using var c = new ReconciliationUtils();
            System.Threading.Thread adding = null;
            bool addResult = false;
            bool addFinishedDuringLoad = true;
            c.DuringLoad = () =>
            {
                c.DuringLoad = null;
                adding = new System.Threading.Thread(() => addResult = c.AddKeyMappingSimple("Added", "/added", "/added", out _));
                adding.Start();
                Assert.True(System.Threading.SpinWait.SpinUntil(() => (adding.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)), "the setup call never blocked");
                addFinishedDuringLoad = !adding.IsAlive;
            };

            Assert.True(c.LoadDefinitionJson(Full, out string message), message);
            Assert.True(adding.Join(TimeSpan.FromSeconds(5)));

            Assert.False(addFinishedDuringLoad);      // it waited
            Assert.True(addResult);
            using JsonDocument doc = JsonDocument.Parse(Get(c));
            string[] keys = doc.RootElement.GetProperty("keys").EnumerateArray().Select(k => k.GetProperty("name").GetString()).ToArray();
            Assert.Equal(new[] { "Company", "Invoice", "Added" }, keys);   // the loaded keys, then the added one: nothing lost
        }

        [Fact]
        public void ADefinitionOverTheSizeLimit_IsRefused()
        {
            using var c = new ReconciliationUtils();
            string atLimit = "{\"schemaVersion\":1}" + new string(' ', 256000 - "{\"schemaVersion\":1}".Length);
            Assert.Equal(256000, atLimit.Length);
            Assert.True(c.LoadDefinitionJson(atLimit, out string m), m);
            Assert.False(c.LoadDefinitionJson(atLimit + " ", out m));
            Assert.Contains("longer than 256000", m);
            Assert.False(c.ValidateDefinitionJson(atLimit + " ", out int errors, out string report, out m));
            Assert.Equal(0, errors);
            Assert.Null(report);
        }

        [Fact]
        public void ANullDefinition_IsAFailure_NotAFinding()
        {
            using var c = new ReconciliationUtils();
            Assert.False(c.LoadDefinitionJson(null, out string m));
            Assert.Contains("required", m);
            Assert.False(c.ValidateDefinitionJson(null, out int errors, out string report, out m));
            Assert.Contains("required", m);
            Assert.Equal(0, errors);
            Assert.Null(report);
        }

        // ---------------------------------------------------------------- validation-only

        [Fact]
        public void Validate_OfAValidDefinition_ReportsNothing_AndReturnsTrue()
        {
            string report = Validate(Full, out int errors);
            Assert.Equal(0, errors);
            using JsonDocument doc = JsonDocument.Parse(report);
            Assert.True(doc.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal(0, doc.RootElement.GetProperty("errorCount").GetInt32());
            Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
            Assert.Equal(0, doc.RootElement.GetProperty("errors").GetArrayLength());
        }

        [Fact]
        public void Validate_OfAnInvalidDefinition_ReturnsTrue_AndListsEveryProblemWithAPathAndCode()
        {
            string json = "{\"schemaVersion\":1,\"keys\":[{\"name\":\"\",\"leftPointer\":\"a\",\"rightPointer\":\"/k\"}],\"comparisons\":[{\"name\":\"D\",\"kind\":\"Decimal\",\"leftPointer\":\"/d\",\"rightPointer\":\"/d\",\"absoluteTolerance\":\"-1\"}],\"limits\":{\"maximumResults\":0}}";
            string report = Validate(json, out int errors);
            Assert.Equal(3, errors);
            using JsonDocument doc = JsonDocument.Parse(report);
            Assert.False(doc.RootElement.GetProperty("valid").GetBoolean());
            var found = doc.RootElement.GetProperty("errors").EnumerateArray().Select(e => (e.GetProperty("path").GetString(), e.GetProperty("code").GetString())).ToArray();
            Assert.Contains(("keys[0].name", "InvalidName"), found);
            Assert.Contains(("comparisons[0].absoluteTolerance", "InvalidTolerance"), found);
            Assert.Contains(("limits.maximumResults", "InvalidLimit"), found);
            Assert.All(doc.RootElement.GetProperty("errors").EnumerateArray(), e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("message").GetString())));
        }

        [Fact]
        public void Validate_TreatsMalformedJsonAsAFinding_NotAFailure()
        {
            string report = Validate("{ nope", out int errors);
            Assert.Equal(1, errors);
            Assert.Equal("MalformedJson", FirstCode(report));
        }

        [Fact]
        public void Validate_NeverChangesTheDefinition()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Existing", "/e", "/e", out _));
            string before = Get(c);
            Assert.True(c.ValidateDefinitionJson(Full, out _, out _, out _));
            Assert.True(c.ValidateDefinitionJson("nope", out _, out _, out _));
            Assert.Equal(before, Get(c));
        }

        [Fact]
        public void TheValidationReport_IsBoundedAtOneHundredFindings_ButCountsThemAll()
        {
            // 128 comparisons, each with several problems (blank name, two bad pointers): far more than 100 findings
            string comparisons = string.Join(",", Enumerable.Range(0, 128).Select(_ => "{\"name\":\"\",\"kind\":\"Text\",\"leftPointer\":\"a\",\"rightPointer\":\"b\"}"));
            string report = Validate("{\"schemaVersion\":1,\"comparisons\":[" + comparisons + "]}", out int errors);
            using JsonDocument doc = JsonDocument.Parse(report);
            Assert.True(errors > 100, "expected more than 100 findings, got " + errors);
            Assert.Equal(errors, doc.RootElement.GetProperty("errorCount").GetInt32());
            Assert.Equal(100, doc.RootElement.GetProperty("reportedCount").GetInt32());
            Assert.Equal(100, doc.RootElement.GetProperty("errors").GetArrayLength());
            Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        }

        [Fact]
        public void ALoadFailure_MentionsTheFirstProblem_AndHowManyMoreThereAre()
        {
            using var c = new ReconciliationUtils();
            Assert.False(c.LoadDefinitionJson("{\"schemaVersion\":9,\"bogus\":1,\"keys\":5}", out string message));
            Assert.Contains("more problem(s)", message);
            Assert.Contains("ValidateDefinitionJson", message);
        }

        [Fact]
        public void TheMessages_QuoteTheDefinition_ButAreSafeForPointersAndNames()
        {
            using var c = new ReconciliationUtils();
            Assert.False(c.AddKeyMappingSimple("K", "no-slash", "/a", out string message));
            Assert.Contains("start with '/'", message);
        }
    }
}
