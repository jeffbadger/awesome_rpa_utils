using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class JsonPointerTests
    {
        [Theory]
        [InlineData("/company", new[] { "company" })]
        [InlineData("/a/b/c", new[] { "a", "b", "c" })]
        [InlineData("/", new[] { "" })]                        // the property whose name is empty
        [InlineData("//", new[] { "", "" })]
        [InlineData("/a//b", new[] { "a", "", "b" })]
        [InlineData("/a~1b", new[] { "a/b" })]                 // ~1 is '/'
        [InlineData("/a~0b", new[] { "a~b" })]                 // ~0 is '~'
        [InlineData("/~01", new[] { "~1" })]                   // ~0 then a literal 1, not ~1
        [InlineData("/~10", new[] { "/0" })]
        [InlineData("/a.b", new[] { "a.b" })]                  // dots mean nothing
        [InlineData("/0", new[] { "0" })]                      // digits are a property name, never an array index
        [InlineData("/ünï/日本", new[] { "ünï", "日本" })]
        [InlineData("/ padded ", new[] { " padded " })]        // spaces are part of the name
        public void AValidPointer_ParsesToItsSegments(string pointer, string[] expected)
        {
            Assert.True(JsonPointer.TryParse(pointer, out string[] segments, out string error), error);
            Assert.Null(error);
            Assert.Equal(expected, segments);
        }

        [Theory]
        [InlineData(null, "required")]
        [InlineData("", "whole row")]                          // the root pointer is unsupported
        [InlineData("company", "start with '/'")]
        [InlineData("a/b", "start with '/'")]
        [InlineData("/a~", "invalid escape")]
        [InlineData("/a~2", "invalid escape")]
        [InlineData("/a~b", "invalid escape")]
        [InlineData("/ok/~", "segment 2")]                     // the segment number is reported
        public void AnInvalidPointer_IsRejected_WithAnExplanation(string pointer, string fragment)
        {
            Assert.False(JsonPointer.TryParse(pointer, out string[] segments, out string error));
            Assert.Null(segments);
            Assert.Contains(fragment, error);
        }

        [Fact]
        public void ThePointerLength_IsBounded()
        {
            string atLimit = "/" + new string('a', JsonPointer.MaxLength - 1);
            string over = "/" + new string('a', JsonPointer.MaxLength);
            Assert.True(JsonPointer.TryParse(atLimit, out _, out _));
            Assert.False(JsonPointer.TryParse(over, out _, out string error));
            Assert.Contains("longer than 1024", error);
        }
    }
}
