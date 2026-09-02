using TerminalAutomation;
using Xunit;

namespace TerminalAutomation.Tests
{
    /// <summary>
    /// Pure string-logic tests for <see cref="FieldSplitting"/> - no P/Invoke, no live
    /// console, runs anywhere.
    /// </summary>
    public class FieldSplittingTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\t")]
        public void SplitFields_NullOrWhitespaceOnly_ReturnsEmptyArray(string rowText)
        {
            string[] fields = FieldSplitting.SplitFields(rowText);

            Assert.Empty(fields);
        }

        [Fact]
        public void SplitFields_SingleWord_ReturnsOneField()
        {
            string[] fields = FieldSplitting.SplitFields("hello");

            Assert.Equal(new[] { "hello" }, fields);
        }

        [Fact]
        public void SplitFields_SingleSpaceBetweenWords_DoesNotSplit()
        {
            string[] fields = FieldSplitting.SplitFields("word1 word2 word3");

            Assert.Equal(new[] { "word1 word2 word3" }, fields);
        }

        [Fact]
        public void SplitFields_TwoSpacesBetweenWords_Splits()
        {
            string[] fields = FieldSplitting.SplitFields("word1  word2");

            Assert.Equal(new[] { "word1", "word2" }, fields);
        }

        [Fact]
        public void SplitFields_ManySpacesBetweenWords_SplitsAsOneBoundary()
        {
            string[] fields = FieldSplitting.SplitFields("word1     word2");

            Assert.Equal(new[] { "word1", "word2" }, fields);
        }

        [Fact]
        public void SplitFields_LeadingAndTrailingWhitespace_IsTrimmedBeforeSplitting()
        {
            string[] fields = FieldSplitting.SplitFields("   word1  word2   ");

            Assert.Equal(new[] { "word1", "word2" }, fields);
        }

        [Fact]
        public void SplitFields_SingleTabBetweenWords_DoesNotSplit()
        {
            string[] fields = FieldSplitting.SplitFields("word1\tword2");

            Assert.Equal(new[] { "word1\tword2" }, fields);
        }

        [Fact]
        public void SplitFields_TwoTabsBetweenWords_Splits()
        {
            string[] fields = FieldSplitting.SplitFields("word1\t\tword2");

            Assert.Equal(new[] { "word1", "word2" }, fields);
        }

        [Fact]
        public void SplitFields_MixedSpaceAndTabRun_CountsAsOneBoundary()
        {
            string[] fields = FieldSplitting.SplitFields("word1 \t word2");

            Assert.Equal(new[] { "word1", "word2" }, fields);
        }

        [Fact]
        public void SplitFields_ThreeColumns_SplitsIntoThreeFields()
        {
            string[] fields = FieldSplitting.SplitFields("NAME       STATUS      PID");

            Assert.Equal(new[] { "NAME", "STATUS", "PID" }, fields);
        }
    }
}
