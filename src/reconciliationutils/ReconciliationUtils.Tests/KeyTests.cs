using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class KeyTests
    {
        private static KeyMappingDef Map(string name, string leftPointer, string rightPointer = null, bool trim = false, bool ignoreCase = false)
        {
            rightPointer ??= leftPointer;
            JsonPointer.TryParse(leftPointer, out string[] l, out _);
            JsonPointer.TryParse(rightPointer, out string[] r, out _);
            return new KeyMappingDef { Name = name, LeftPointer = leftPointer, RightPointer = rightPointer, LeftSegments = l, RightSegments = r, Trim = trim, IgnoreCase = ignoreCase };
        }

        private static KeyExtraction Extract(string rowJson, params KeyMappingDef[] mappings)
        {
            using JsonDocument doc = JsonDocument.Parse(rowJson);
            return ReconciliationKey.Extract(new JsonRowReader(doc.RootElement.Clone()), mappings, left: true);
        }

        // ---------------------------------------------------------------- extraction

        [Fact]
        public void ACompositeKey_IsAnOrderedTupleOfItsParts()
        {
            KeyExtraction key = Extract("{\"company\":\"A\",\"invoice\":\"INV-101\"}", Map("Company", "/company"), Map("Invoice", "/invoice"));
            Assert.True(key.IsValid);
            Assert.Equal(new[] { "A", "INV-101" }, key.Normalized);
            Assert.Equal(new[] { "A", "INV-101" }, key.Original);
        }

        [Fact]
        public void EachSideUsesItsOwnPointer()
        {
            using JsonDocument doc = JsonDocument.Parse("{\"company\":\"L\",\"entity\":\"R\"}");
            var row = new JsonRowReader(doc.RootElement.Clone());
            KeyMappingDef[] maps = { Map("Company", "/company", "/entity") };
            Assert.Equal("L", ReconciliationKey.Extract(row, maps, left: true).Normalized[0]);
            Assert.Equal("R", ReconciliationKey.Extract(row, maps, left: false).Normalized[0]);
        }

        [Theory]
        [InlineData("{\"k\":\"007\"}", "007")]              // leading zeros in a string are preserved
        [InlineData("{\"k\":7}", "7")]                      // an integer token is used as its exact text
        [InlineData("{\"k\":-7}", "-7")]
        [InlineData("{\"k\":0}", "0")]
        [InlineData("{\"k\":12345678901234567890123}", "12345678901234567890123")]
        [InlineData("{\"k\":\"123\"}", "123")]
        [InlineData("{\"k\":123}", "123")]                  // ... so the number 123 and the string "123" are the same key text
        [InlineData("{\"k\":\"  padded  \"}", "  padded  ")] // not trimmed unless the mapping says so
        [InlineData("{\"k\":\"   \"}", "   ")]              // whitespace-only is a literal key when trimming is off
        public void AStringOrIntegerKeyPart_IsUsedAsItsExactText(string row, string expected)
        {
            KeyExtraction key = Extract(row, Map("K", "/k"));
            Assert.True(key.IsValid);
            Assert.Equal(expected, key.Normalized[0]);
        }

        [Fact]
        public void TheNumberSevenAndTheStringZeroZeroSeven_AreDifferentKeys()
        {
            KeyMappingDef[] maps = { Map("K", "/k") };
            var comparer = ReconciliationKey.ComparerFor(maps);
            string[] seven = Extract("{\"k\":7}", maps).Normalized;
            string[] padded = Extract("{\"k\":\"007\"}", maps).Normalized;
            Assert.False(comparer.Equals(seven, padded));
        }

        [Theory]
        [InlineData("{}", "MissingKey")]
        [InlineData("{\"other\":1}", "MissingKey")]
        [InlineData("{\"k\":null}", "MissingKey")]
        [InlineData("{\"k\":1.5}", "InvalidKeyType")]       // a fraction
        [InlineData("{\"k\":1.0}", "InvalidKeyType")]       // even a whole value written with a fraction
        [InlineData("{\"k\":1e2}", "InvalidKeyType")]       // an exponent
        [InlineData("{\"k\":true}", "InvalidKeyType")]
        [InlineData("{\"k\":false}", "InvalidKeyType")]
        [InlineData("{\"k\":{}}", "InvalidKeyType")]
        [InlineData("{\"k\":[]}", "InvalidKeyType")]
        [InlineData("{\"k\":[\"a\"]}", "InvalidKeyType")]
        [InlineData("{\"k\":\"\"}", "EmptyKey")]
        public void ABadKeyPart_MakesTheRowInvalid_WithAStableReason(string row, string reason)
        {
            KeyExtraction key = Extract(row, Map("K", "/k"));
            Assert.False(key.IsValid);
            Assert.Equal(reason, key.ReasonCode);
            Assert.Equal("K", key.MappingName);
            Assert.Equal("/k", key.Pointer);
        }

        [Fact]
        public void EmptyAfterTrimming_IsAnEmptyKey_ButNotWhenTrimmingIsOff()
        {
            Assert.Equal("EmptyKey", Extract("{\"k\":\"   \"}", Map("K", "/k", trim: true)).ReasonCode);
            Assert.True(Extract("{\"k\":\"   \"}", Map("K", "/k", trim: false)).IsValid);
        }

        [Fact]
        public void Trimming_RemovesUnicodeWhitespace_AndKeepsTheOriginal()
        {
            KeyExtraction key = Extract("{\"k\":\"\\u00a0 ab \\t\"}", Map("K", "/k", trim: true));
            Assert.True(key.IsValid);
            Assert.Equal("ab", key.Normalized[0]);
            Assert.Equal("\u00a0 ab \t", key.Original[0]);
        }

        [Fact]
        public void TheFailingPartIsNamed_EvenWhenEarlierPartsWereFine()
        {
            KeyExtraction key = Extract("{\"a\":\"ok\",\"b\":true}", Map("First", "/a"), Map("Second", "/b"));
            Assert.False(key.IsValid);
            Assert.Equal("Second", key.MappingName);
            Assert.Equal("InvalidKeyType", key.ReasonCode);
        }

        [Fact]
        public void AFailingPartOnTheRightSide_ReportsTheRightPointer()
        {
            using JsonDocument doc = JsonDocument.Parse("{\"entity\":true}");
            KeyExtraction key = ReconciliationKey.Extract(new JsonRowReader(doc.RootElement.Clone()), new[] { Map("Company", "/company", "/entity") }, left: false);
            Assert.Equal("/entity", key.Pointer);
        }

        // ---------------------------------------------------------------- equality and hashing

        [Fact]
        public void CompositeKeys_AreNeverDelimiterConcatenated()
        {
            // "a|b" + "c" must not equal "a" + "b|c", whatever delimiter one might have chosen.
            KeyMappingDef[] maps = { Map("X", "/x"), Map("Y", "/y") };
            var comparer = ReconciliationKey.ComparerFor(maps);
            foreach (string delimiter in new[] { "|", ",", "/", "\u0000", "\u001f", "::", " " })
            {
                string[] first = { "a" + delimiter + "b", "c" };
                string[] second = { "a", "b" + delimiter + "c" };
                Assert.False(comparer.Equals(first, second), "delimiter '" + delimiter + "'");
            }
        }

        [Fact]
        public void EqualKeys_HaveEqualHashCodes_UnderTheCaseRules()
        {
            KeyMappingDef[] maps = { Map("Company", "/c", ignoreCase: true), Map("Invoice", "/i", ignoreCase: false) };
            var comparer = ReconciliationKey.ComparerFor(maps);
            string[] a = { "Acme", "INV-1" };
            string[] b = { "ACME", "INV-1" };       // differs from a only in a case-insensitive part
            string[] c = { "Acme", "inv-1" };       // differs in a case-sensitive part
            Assert.True(comparer.Equals(a, b));
            Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
            Assert.False(comparer.Equals(a, c));
        }

        [Fact]
        public void TheHashAndEqualityContract_HoldsForManyGeneratedKeys()
        {
            KeyMappingDef[] maps = { Map("A", "/a", ignoreCase: true), Map("B", "/b") };
            var comparer = ReconciliationKey.ComparerFor(maps);
            var random = new Random(12345);
            string alphabet = "aAbB01|_ ";
            string Piece() => new string(Enumerable.Range(0, random.Next(0, 4)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var keys = Enumerable.Range(0, 300).Select(_ => new[] { Piece(), Piece() }).ToList();
            foreach (string[] x in keys)
                foreach (string[] y in keys.Take(60))
                    if (comparer.Equals(x, y))
                        Assert.Equal(comparer.GetHashCode(x), comparer.GetHashCode(y));
        }

        [Fact]
        public void AKeyIndex_GroupsRowsBySameKey_UnderTheRules()
        {
            KeyMappingDef[] maps = { Map("K", "/k", trim: true, ignoreCase: true) };
            var index = new Dictionary<string[], List<int>>(ReconciliationKey.ComparerFor(maps));
            string[] rows = { "{\"k\":\"abc\"}", "{\"k\":\" ABC \"}", "{\"k\":\"abd\"}" };
            for (int i = 0; i < rows.Length; i++)
            {
                string[] key = Extract(rows[i], maps).Normalized;
                if (!index.TryGetValue(key, out List<int> list)) index[key] = list = new List<int>();
                list.Add(i);
            }
            Assert.Equal(2, index.Count);
            Assert.Equal(new[] { 0, 1 }, index[new[] { "abc" }]);
        }

        // ---------------------------------------------------------------- display

        [Theory]
        [InlineData(new[] { "A", "INV-101" }, "[\"A\",\"INV-101\"]")]
        [InlineData(new[] { "he said \"hi\"" }, "[\"he said \\\"hi\\\"\"]")]
        [InlineData(new[] { "back\\slash" }, "[\"back\\\\slash\"]")]
        [InlineData(new[] { "tab\there" }, "[\"tab\\u0009here\"]")]
        [InlineData(new[] { "日本" }, "[\"日本\"]")]
        public void TheDisplayKey_IsAJsonArrayOfNormalizedParts(string[] parts, string expected)
        {
            Assert.Equal(expected, ReconciliationKey.ToDisplayJson(parts));
            using JsonDocument roundTrip = JsonDocument.Parse(ReconciliationKey.ToDisplayJson(parts));   // and it is always valid JSON
            Assert.Equal(parts, roundTrip.RootElement.EnumerateArray().Select(e => e.GetString()).ToArray());
        }
    }
}
