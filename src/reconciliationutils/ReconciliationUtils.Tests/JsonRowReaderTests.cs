using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class JsonRowReaderTests
    {
        private static FieldValue Read(string rowJson, string pointer)
        {
            using JsonDocument doc = JsonDocument.Parse(rowJson);
            Assert.True(JsonPointer.TryParse(pointer, out string[] segments, out string error), error);
            return new JsonRowReader(doc.RootElement.Clone()).Read(segments);
        }

        [Theory]
        [InlineData("{\"a\":\"text\"}", "/a", "String", "text")]
        [InlineData("{\"a\":\"\"}", "/a", "String", "")]                 // an empty string is a real string
        [InlineData("{\"a\":7}", "/a", "Integer", "7")]
        [InlineData("{\"a\":-7}", "/a", "Integer", "-7")]
        [InlineData("{\"a\":0}", "/a", "Integer", "0")]
        [InlineData("{\"a\":-0}", "/a", "Integer", "-0")]
        [InlineData("{\"a\":12345678901234567890123}", "/a", "Integer", "12345678901234567890123")]   // never narrowed to a machine integer
        [InlineData("{\"a\":1.5}", "/a", "Number", "1.5")]
        [InlineData("{\"a\":1.0}", "/a", "Number", "1.0")]               // not an integer token: the fraction is kept
        [InlineData("{\"a\":1e2}", "/a", "Number", "1e2")]
        [InlineData("{\"a\":1E+2}", "/a", "Number", "1E+2")]
        [InlineData("{\"a\":true}", "/a", "Boolean", "true")]
        [InlineData("{\"a\":false}", "/a", "Boolean", "false")]
        [InlineData("{\"a\":null}", "/a", "Null", null)]
        [InlineData("{\"a\":{}}", "/a", "Unsupported", null)]
        [InlineData("{\"a\":[]}", "/a", "Unsupported", null)]
        [InlineData("{\"a\":[1]}", "/a", "Unsupported", null)]
        public void ALeafIsClassifiedByItsJsonType_KeepingItsExactText(string row, string pointer, string kind, string text)
        {
            FieldValue value = Read(row, pointer);
            Assert.Equal(kind, value.Kind.ToString());
            Assert.Equal(text, value.Text);
        }

        [Theory]
        [InlineData("{\"a\":1}", "/b")]                        // absent
        [InlineData("{}", "/a")]
        [InlineData("{\"a\":null}", "/a/b")]                   // walking through a null
        [InlineData("{\"a\":5}", "/a/b")]                      // ... a number
        [InlineData("{\"a\":\"s\"}", "/a/b")]                  // ... a string
        [InlineData("{\"a\":true}", "/a/b")]                   // ... a Boolean
        [InlineData("{\"a\":{\"b\":1}}", "/a/c")]              // nested but absent
        public void AMissingPath_IsMissing(string row, string pointer)
        {
            Assert.Equal(FieldKind.Missing, Read(row, pointer).Kind);
        }

        [Theory]
        [InlineData("{\"a\":[{\"b\":1}]}", "/a/b")]            // an array met while walking: unsupported data
        [InlineData("{\"a\":[{\"b\":1}]}", "/a/0")]            // and there is no array indexing
        [InlineData("{\"a\":[[]]}", "/a/0/0")]
        public void AnArrayOnThePath_IsUnsupported(string row, string pointer)
        {
            Assert.Equal(FieldKind.Unsupported, Read(row, pointer).Kind);
        }

        [Fact]
        public void NestedValues_AreFound()
        {
            FieldValue value = Read("{\"a\":{\"b\":{\"c\":\"deep\"}}}", "/a/b/c");
            Assert.Equal(FieldKind.String, value.Kind);
            Assert.Equal("deep", value.Text);
        }

        [Fact]
        public void PropertyLookup_IsCaseSensitive()
        {
            Assert.Equal(FieldKind.Missing, Read("{\"Invoice\":\"x\"}", "/invoice").Kind);
            Assert.Equal(FieldKind.String, Read("{\"Invoice\":\"x\"}", "/Invoice").Kind);
        }

        [Fact]
        public void ADotInAPropertyName_IsLiteral()
        {
            Assert.Equal("v", Read("{\"a.b\":\"v\",\"a\":{\"b\":\"w\"}}", "/a.b").Text);
            Assert.Equal("w", Read("{\"a.b\":\"v\",\"a\":{\"b\":\"w\"}}", "/a/b").Text);
        }

        [Fact]
        public void EscapedAndEmptyNames_AreFound()
        {
            Assert.Equal("slash", Read("{\"a/b\":\"slash\"}", "/a~1b").Text);
            Assert.Equal("tilde", Read("{\"a~b\":\"tilde\"}", "/a~0b").Text);
            Assert.Equal("empty", Read("{\"\":\"empty\"}", "/").Text);
            Assert.Equal("nested empty", Read("{\"\":{\"\":\"nested empty\"}}", "//").Text);
        }

        [Theory]
        [InlineData("5")]
        [InlineData("\"text\"")]
        [InlineData("null")]
        [InlineData("[1,2]")]
        [InlineData("true")]
        public void ARowThatIsNotAnObject_IsReportedAsNotAnObject_AndEveryFieldIsMissing(string row)
        {
            using JsonDocument doc = JsonDocument.Parse(row);
            var reader = new JsonRowReader(doc.RootElement.Clone());
            Assert.False(reader.IsObject);
            Assert.Equal(FieldKind.Missing, reader.Read(new[] { "a" }).Kind);
        }

        [Theory]
        [InlineData("0", true)]
        [InlineData("-0", true)]
        [InlineData("7", true)]
        [InlineData("-42", true)]
        [InlineData("1234567890", true)]
        [InlineData("1.0", false)]
        [InlineData("1e2", false)]
        [InlineData("-1.5", false)]
        [InlineData("-", false)]
        [InlineData("", false)]
        public void TheIntegerTokenRule(string token, bool expected)
        {
            Assert.Equal(expected, JsonRowReader.IsIntegerToken(token));
        }
    }
}
