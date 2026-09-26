using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>
    /// The number grammar is checked against System.Text.Json itself, not against a list of examples: every string over the JSON number
    /// alphabet up to four characters, plus a large random sample of longer ones, must be accepted by <c>IsJsonNumber</c> exactly when the
    /// real JSON parser accepts it as a number, and the value fragment for any token must always be valid JSON.
    /// </summary>
    public sealed class NumberGrammarTests
    {
        private const string Alphabet = "0123456789.eE+-";

        private static bool RealJsonSaysNumber(string token)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(token);
                return doc.RootElement.ValueKind == JsonValueKind.Number && doc.RootElement.GetRawText() == token;
            }
            catch (JsonException) { return false; }
        }

        private static IEnumerable<string> Exhaustive(int maxLength)
        {
            var chars = Alphabet.ToCharArray();
            var current = new List<string> { string.Empty };
            for (int length = 1; length <= maxLength; length++)
            {
                var next = new List<string>(current.Count * chars.Length);
                foreach (string prefix in current)
                    foreach (char c in chars) next.Add(prefix + c);
                foreach (string s in next) yield return s;
                current = next;
            }
        }

        [Fact]
        public void IsJsonNumber_AgreesWithTheRealJsonParser_ForEveryStringUpToFourCharacters()
        {
            int accepted = 0;
            foreach (string token in Exhaustive(4))
            {
                bool expected = RealJsonSaysNumber(token);
                Assert.True(expected == ComparisonCore.IsJsonNumber(token), "'" + token + "': System.Text.Json says " + expected + " but IsJsonNumber says " + !expected);
                if (expected) accepted++;
            }
            Assert.True(accepted > 100, "the sweep should have met many real numbers, met " + accepted);
        }

        [Fact]
        public void IsJsonNumber_AgreesWithTheRealJsonParser_ForRandomLongerStrings_AndNearMisses()
        {
            var random = new Random(26092026);
            string Piece(int length) => new string(Enumerable.Range(0, length).Select(_ => Alphabet[random.Next(Alphabet.Length)]).ToArray());
            string[] shapes = { "{0}", "-{0}", "{0}.{1}", "-{0}.{1}", "{0}e{1}", "{0}.{1}e{2}", "-{0}.{1}E+{2}", "{0}E-{2}", "0{0}", "-0{0}", "{0}.", ".{0}", "{0}e", "{0}e+" };
            for (int i = 0; i < 60000; i++)
            {
                string token = i % 2 == 0
                    ? Piece(random.Next(1, 14))
                    : string.Format(shapes[random.Next(shapes.Length)], DigitsOnly(random, 1 + random.Next(4)), DigitsOnly(random, 1 + random.Next(4)), DigitsOnly(random, 1 + random.Next(3)));
                Assert.True(RealJsonSaysNumber(token) == ComparisonCore.IsJsonNumber(token), "'" + token + "'");
            }
        }

        private static string DigitsOnly(Random random, int length) => new string(Enumerable.Range(0, length).Select(_ => (char)('0' + random.Next(10))).ToArray());

        [Theory]
        [InlineData("01e2")]      // a leading zero with an exponent: not a number, so it must come out quoted
        [InlineData("-01e2")]
        [InlineData("00e0")]
        [InlineData("01.5e3")]
        [InlineData("0e")]
        [InlineData("0.e1")]
        public void AMalformedTokenWithAnExponent_IsQuoted_NeverEmittedAsARawFragment(string token)
        {
            Assert.False(ComparisonCore.IsJsonNumber(token));
            foreach (FieldKind kind in new[] { FieldKind.Integer, FieldKind.Number })
            {
                string fragment = ComparisonCore.ValueJson(new FieldValue(kind, token));
                using JsonDocument doc = JsonDocument.Parse(fragment);
                Assert.Equal(JsonValueKind.String, doc.RootElement.ValueKind);
                Assert.Equal(token, doc.RootElement.GetString());
            }
        }

        [Fact]
        public void ForEveryToken_UpToFourCharacters_TheFragmentIsValidJson_AndVerbatimExactlyWhenItIsANumber()
        {
            foreach (string token in Exhaustive(4))
            {
                string fragment = ComparisonCore.ValueJson(new FieldValue(FieldKind.Number, token));
                using JsonDocument doc = JsonDocument.Parse(fragment);                                   // never malformed
                if (RealJsonSaysNumber(token)) Assert.Equal(token, fragment);                             // a real number stays as written
                else Assert.Equal(token, doc.RootElement.GetString());                                    // anything else is carried as a string
            }
        }

        [Fact]
        public void TheExactDecimalParser_NeverAcceptsAToken_ThatIsNotAJsonNumber()
        {
            // Its grammar is the same one (it then applies range limits on top), so acceptance must be a subset of real JSON numbers
            foreach (string token in Exhaustive(4))
                if (ExactDecimal.TryParseJsonNumber(token, out _, out _))
                    Assert.True(RealJsonSaysNumber(token), "'" + token + "' was accepted but is not a JSON number");
        }

        [Fact]
        public void TheExactDecimalParser_AcceptsEveryShortJsonNumberWithoutAnExponent()
        {
            int checkedCount = 0;
            foreach (string token in Exhaustive(4).Where(t => RealJsonSaysNumber(t) && t.IndexOfAny(new[] { 'e', 'E' }) < 0))
            {
                // up to four characters without an exponent is at most a few digits and places, so it always fits exactly
                Assert.True(ExactDecimal.TryParseJsonNumber(token, out _, out string error), "'" + token + "': " + error);
                checkedCount++;
            }
            Assert.True(checkedCount > 100);
        }

        [Fact]
        public void TheExactDecimalParser_RefusesAShortJsonNumberOnlyForAnExponentThatCannotBeHeldExactly()
        {
            foreach (string token in Exhaustive(4).Where(t => RealJsonSaysNumber(t) && t.IndexOfAny(new[] { 'e', 'E' }) >= 0))
                if (!ExactDecimal.TryParseJsonNumber(token, out _, out string error))
                    Assert.Contains("exact", error);        // a stated range reason ('too large' / 'decimal places'), never a grammar complaint
        }
    }
}
