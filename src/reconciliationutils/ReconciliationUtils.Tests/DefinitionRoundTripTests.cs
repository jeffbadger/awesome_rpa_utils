using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>Every accepted definition must be savable (GetDefinitionJson) and loadable again (LoadDefinitionJson), whatever characters and sizes it has.</summary>
    public sealed class DefinitionRoundTripTests
    {
        private const int Limit = ReconciliationDefinition.MaxDefinitionJsonCharacters;

        private static string Pointer(char c, int length) => "/" + new string(c, length - 1);

        private static void AssertRoundTrips(ReconciliationUtils c)
        {
            Assert.True(c.GetDefinitionJson(out string canonical, out string m), m);
            Assert.True(canonical.Length <= Limit, "an accepted definition serialized to " + canonical.Length + " characters");
            using var again = new ReconciliationUtils();
            Assert.True(again.LoadDefinitionJson(canonical, out m), "the definition's own canonical form was rejected: " + m);
            Assert.True(again.GetDefinitionJson(out string second, out m), m);
            Assert.Equal(canonical, second);
            Assert.True(again.ValidateDefinitionJson(canonical, out int errors, out string report, out m), m);
            Assert.True(errors == 0, report);
        }

        [Fact]
        public void TheReportedCase_ThirtyTextRulesWith800ChineseCharacterPointers_SavesAndLoadsAgain()
        {
            string chinese = "/" + new string('中', 799);
            var rules = string.Join(",", Enumerable.Range(0, 30).Select(i => "{\"name\":\"R" + i + "\",\"kind\":\"Text\",\"leftPointer\":\"" + chinese + i + "\",\"rightPointer\":\"" + chinese + i + "\"}"));
            string input = "{\"schemaVersion\":1,\"comparisons\":[" + rules + "]}";
            Assert.True(input.Length < 60000);
            using var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson(input, out string m), m);
            Assert.True(c.GetDefinitionJson(out string canonical, out m), m);
            Assert.True(canonical.Length < 65000, "non-ASCII text should not be expanded six-fold; was " + canonical.Length);      // was 292,157 before
            Assert.Contains("中", canonical);
            AssertRoundTrips(c);
        }

        [Fact]
        public void CharactersThatAreEscapedInJson_StillRoundTrip_QuotesBackslashesControlsAndAstralCharacters()
        {
            using var c = new ReconciliationUtils();
            string name = "a\"b\\c\nd\te 🚀 é";
            Assert.True(c.AddKeyMappingSimple("Key " + name, "/p\"q\\r\ns" + "🚀", "/é中", out string m), m);
            Assert.True(c.GetDefinitionJson(out string canonical, out m), m);
            Assert.DoesNotContain("\n", canonical);                                               // a raw newline is never written into a JSON string
            using (JsonDocument doc = JsonDocument.Parse(canonical))
                Assert.Equal("Key " + name, doc.RootElement.GetProperty("keys")[0].GetProperty("name").GetString());
            AssertRoundTrips(c);
        }

        [Fact]
        public void ADefinitionSavedByAnEarlierRelease_WithUnicodeEscapes_StillLoads_AndIsNowWrittenWithoutThem()
        {
            const string old = "{\"schemaVersion\":1,\"keys\":[{\"name\":\"\\u4e2d\\u6587\",\"leftPointer\":\"/\\u00e9\",\"rightPointer\":\"/\\u00e9\",\"trim\":false,\"ignoreCase\":false}],\"comparisons\":[],\"limits\":{\"maximumRowsPerSide\":50000,\"maximumInputCharactersPerSide\":8000000,\"maximumResults\":100000,\"maximumDifferenceDetails\":100000}}";
            using var c = new ReconciliationUtils();
            Assert.True(c.LoadDefinitionJson(old, out string m), m);
            Assert.True(c.GetDefinitionJson(out string now, out m), m);
            Assert.Contains("\"name\":\"中文\"", now);
            Assert.Contains("/é", now);
            AssertRoundTrips(c);
            using var reloaded = new ReconciliationUtils();
            Assert.True(reloaded.LoadDefinitionJson(now, out m), m);
            Assert.True(reloaded.GetDefinitionJson(out string again, out m), m);
            Assert.Equal(now, again);
        }

        [Fact]
        public void BuildingUpAHugeDefinition_IsRefusedAtTheLimit_NotAcceptedAndThenUnloadable()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            int accepted = 0;
            string refusal = null;
            for (int i = 0; i < 128; i++)
            {
                // the largest allowed pieces: 128-character names and 1,024-character pointers, on both sides plus both currency pointers
                string name = "M" + i.ToString("D3") + new string('n', 124);
                if (!c.AddMoneyComparison(name, Pointer('a', 1024), Pointer('b', 1024), Pointer('c', 1024), Pointer('d', 1024), "0", ComparisonNullPolicy.RequireValue, out m)) { refusal = m; break; }
                accepted++;
                if (accepted % 16 == 0) AssertRoundTrips(c);
            }
            Assert.NotNull(refusal);                                                              // 128 of these would be about 560,000 characters
            Assert.Contains("DefinitionTooLarge", refusal);
            Assert.Contains(Limit.ToString(), refusal);
            Assert.True(accepted > 30 && accepted < 128);
            AssertRoundTrips(c);                                                                  // what was accepted before the refusal still saves and loads

            Assert.True(c.GetDefinitionJson(out string before, out m), m);
            Assert.False(c.AddMoneyComparison("Another", Pointer('a', 1024), Pointer('b', 1024), Pointer('c', 1024), Pointer('d', 1024), "0", ComparisonNullPolicy.RequireValue, out _));
            Assert.True(c.GetDefinitionJson(out string after, out m), m);
            Assert.Equal(before, after);                                                          // a refused change changes nothing
            Assert.True(c.AddBooleanComparisonSimple("Small", "/x", "/x", out m) || m.Contains("DefinitionTooLarge"));   // a tiny rule may or may not fit in what is left, but never breaks the round trip
            AssertRoundTrips(c);
        }

        [Fact]
        public void ALoadedDefinition_WhoseCanonicalFormWouldBeTooLarge_IsRejectedByLoadAndReportedByValidate()
        {
            // compact input just under the input limit; the canonical form spells out every default option and grows past the limit
            string pointer = Pointer('p', 949);
            var rules = string.Join(",", Enumerable.Range(0, 128).Select(i => "{\"name\":\"R" + i.ToString("D3") + "\",\"kind\":\"Text\",\"leftPointer\":\"" + pointer + "\",\"rightPointer\":\"" + pointer + "\"}"));
            string input = "{\"schemaVersion\":1,\"comparisons\":[" + rules + "]}";
            Assert.True(input.Length < Limit, "the input itself must be accepted by the input-size check; was " + input.Length);

            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Keep", "/k", "/k", out string m), m);
            Assert.True(c.GetDefinitionJson(out string before, out m), m);
            Assert.False(c.LoadDefinitionJson(input, out m));
            Assert.Contains("DefinitionTooLarge", m);
            Assert.True(c.GetDefinitionJson(out string after, out m), m);
            Assert.Equal(before, after);                                                          // the previous definition is untouched

            Assert.True(c.ValidateDefinitionJson(input, out int errors, out string report, out m), m);
            Assert.Equal(1, errors);
            using JsonDocument doc = JsonDocument.Parse(report);
            JsonElement finding = doc.RootElement.GetProperty("errors")[0];
            Assert.Equal("DefinitionTooLarge", finding.GetProperty("code").GetString());
            Assert.Equal("definition", finding.GetProperty("path").GetString());
        }

        [Theory]
        [InlineData('a', 1024, 100)]
        [InlineData('中', 1024, 100)]
        [InlineData('é', 700, 128)]
        [InlineData('"', 400, 128)]           // escaped as two characters
        [InlineData('\\', 400, 128)]
        [InlineData('\u0001', 200, 128)]      // a control character: six characters when escaped
        public void WhateverTheCharacters_EveryAcceptedDefinitionRoundTrips_AndTooLargeOnesAreRefused(char c, int pointerLength, int rules)
        {
            using var rec = new ReconciliationUtils();
            bool refused = false;
            for (int i = 0; i < rules; i++)
            {
                string pointer = "/" + new string(c, pointerLength - 4) + i.ToString("D3");
                if (!rec.AddTextComparisonSimple("T" + i.ToString("D3"), pointer, pointer, out string m)) { Assert.Contains("DefinitionTooLarge", m); refused = true; break; }
            }
            AssertRoundTrips(rec);
            if (c == '\u0001') Assert.True(refused);                                              // 128 x 2 x 200 control characters x 6 is well past the limit
        }

        private static ReconciliationUtils Filled(int rules, int pointerLength)
        {
            var c = new ReconciliationUtils();
            for (int i = 0; i < rules; i++)
                if (!c.AddTextComparisonSimple("F" + i.ToString("D3"), Pointer('x', pointerLength) + i.ToString("D3"), Pointer('y', pointerLength) + i.ToString("D3"), out _)) { c.Dispose(); return null; }   // too many for the limit
            return c;
        }

        /// <summary>The canonical length of a component with <paramref name="rules"/> filler rules and one final rule (a one-character-longer name, pointers "/k").</summary>
        private static int MeasuredLength(int rules, int pointerLength)
        {
            using ReconciliationUtils c = Filled(rules, pointerLength);
            Assert.NotNull(c);
            Assert.True(c.AddTextComparisonSimple("Z", "/k", "/k", out string m), m);
            Assert.True(c.GetDefinitionJson(out string json, out m), m);
            return json.Length;
        }

        /// <summary>
        /// A component whose canonical form is exactly <paramref name="target"/> characters once one tuned rule is added (or refused when the target is past the limit).
        /// The canonical length is linear in the rule count, the pointer length and the tuned rule's pointer, so three measurements give it and the fixture is found by
        /// arithmetic; only the one component that is returned is actually built.
        /// </summary>
        private static ReconciliationUtils WithCanonicalLength(int target, out bool accepted, out string message)
        {
            int empty = MeasuredLength(0, 1000);                                              // the frame and the tuned rule alone
            int perRule1000 = MeasuredLength(1, 1000) - empty;                                // one filler rule at 1,000-character pointers
            for (int pointerLength = 1000; pointerLength >= 900; pointerLength--)
            {
                int perRule = perRule1000 + 2 * (pointerLength - 1000);                       // two pointers per rule
                for (int rules = 127; rules >= 60; rules--)
                    for (int nameLength = 1; nameLength <= 2; nameLength++)
                    {
                        int extra = target - (empty + (nameLength - 1) + rules * perRule);   // what the tuned rule's two pointers must add
                        if (extra < 0 || extra % 2 != 0 || extra / 2 > 1020) continue;
                        ReconciliationUtils c = Filled(rules, pointerLength);
                        Assert.NotNull(c);                                                    // the arithmetic said the fillers alone fit
                        string pointer = "/k" + new string('k', extra / 2);
                        accepted = c.AddTextComparisonSimple(new string('Z', nameLength), pointer, pointer, out message);
                        return c;
                    }
            }
            throw new InvalidOperationException("could not tune the fixture to " + target);
        }

        [Fact]
        public void TheLimitIsInclusive_ExactlyTheLimitIsAccepted_OneCharacterMoreIsRefused()
        {
            using ReconciliationUtils atLimit = WithCanonicalLength(Limit, out bool accepted, out string message);
            Assert.True(accepted, message);
            Assert.True(atLimit.GetDefinitionJson(out string canonical, out message), message);
            Assert.Equal(Limit, canonical.Length);
            AssertRoundTrips(atLimit);                                                            // 256,000 characters loads back (the loader's own limit is inclusive too)

            using ReconciliationUtils over = WithCanonicalLength(Limit + 2, out accepted, out message);
            Assert.False(accepted);
            Assert.Contains("DefinitionTooLarge", message);
            Assert.Contains((Limit + 2).ToString(), message);
        }

        [Fact]
        public void AsciiIsEscapedExactlyAsBefore_HtmlSensitiveCharactersStayEscaped_AndAnAsciiDefinitionIsByteIdenticalToTheDefaultEncoding()
        {
            using var c = new ReconciliationUtils();
            const string special = "</script> & 'q' + `t` \"d\" \\ \t";
            Assert.True(c.AddKeyMappingSimple("Key " + special, "/a" + special, "/b" + special, out string m), m);
            Assert.True(c.AddTextComparisonSimple("Text " + special, "/t" + special, "/u", out m), m);
            Assert.True(c.GetDefinitionJson(out string canonical, out m), m);
            foreach (char raw in new[] { '<', '>', '&', '\'', '+', '`' }) Assert.DoesNotContain(raw.ToString(), canonical);      // never written raw
            Assert.Contains("\\u003C", canonical);
            Assert.Contains("\\u0026", canonical);
            Assert.Contains("\\u0027", canonical);

            // the same definition written with the default encoder (what earlier releases did) is the identical text: rewrite the parsed document with it
            using JsonDocument doc = JsonDocument.Parse(canonical);
            Assert.Equal(canonical, JsonSerializer.Serialize(doc.RootElement));
            AssertRoundTrips(c);

            // and a non-ASCII definition differs from the default encoding only in the non-ASCII characters
            using var d = new ReconciliationUtils();
            Assert.True(d.AddKeyMappingSimple("\u4e2d<\u6587>", "/\u00e9&", "/x", out m), m);
            Assert.True(d.GetDefinitionJson(out string unicode, out m), m);
            Assert.Contains("\"name\":\"\u4e2d\\u003C\u6587\\u003E\"", unicode);
            Assert.Contains("\"leftPointer\":\"/\u00e9\\u0026\"", unicode);
        }
    }
}
