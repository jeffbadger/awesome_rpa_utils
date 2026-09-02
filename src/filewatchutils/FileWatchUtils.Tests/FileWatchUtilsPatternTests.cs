using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>Real functional tests for WaitForFileMatchingPattern against a real temp directory.</summary>
    public class FileWatchUtilsPatternTests : TempDirectoryTestBase
    {
        [Theory]
        [InlineData(null, "*.csv")]
        [InlineData("", "*.csv")]
        public void WaitForFileMatchingPattern_NullOrEmptyDirectory_ReturnsFalseWithMessage(string directoryPath, string pattern)
        {
            bool result = Fw.WaitForFileMatchingPattern(directoryPath, pattern, false, 100, 10, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForFileMatchingPattern_NullOrEmptyPattern_ReturnsFalseWithMessage(string pattern)
        {
            bool result = Fw.WaitForFileMatchingPattern(TempDir, pattern, false, 100, 10, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileMatchingPattern_DirectoryDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Fw.WaitForFileMatchingPattern(Path.Combine(TempDir, "nope"), "*.csv", false, 100, 10, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileMatchingPattern_AlreadyPresent_ReturnsTrueWithPath()
        {
            string filePath = TempFilePath("data.csv");
            File.WriteAllText(filePath, "a,b,c");

            bool result = Fw.WaitForFileMatchingPattern(TempDir, "*.csv", false, 1000, 20, out string matched, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Equal(filePath, matched);
        }

        [Fact]
        public void WaitForFileMatchingPattern_AppearsBeforeTimeout_ReturnsTrue()
        {
            string filePath = TempFilePath("export.csv");
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                File.WriteAllText(filePath, "1,2,3");
            });

            bool result = Fw.WaitForFileMatchingPattern(TempDir, "*.csv", false, 5000, 20, out string matched, out bool timedOut, out _);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Equal(filePath, matched);
        }

        [Fact]
        public void WaitForFileMatchingPattern_NoMatch_TimesOut()
        {
            bool result = Fw.WaitForFileMatchingPattern(TempDir, "*.csv", false, 150, 20, out string matched, out bool timedOut, out string message);

            Assert.False(result);
            Assert.True(timedOut);
            Assert.Null(matched);
        }

        [Fact]
        public void WaitForFileMatchingPattern_IncludeSubdirectoriesFalse_IgnoresNestedMatch()
        {
            string nested = TempSubdirectory("nested");
            File.WriteAllText(Path.Combine(nested, "data.csv"), "x");

            bool result = Fw.WaitForFileMatchingPattern(TempDir, "*.csv", false, 150, 20, out _, out bool timedOut, out _);

            Assert.False(result);
            Assert.True(timedOut);
        }

        [Fact]
        public void WaitForFileMatchingPattern_IncludeSubdirectoriesTrue_FindsNestedMatch()
        {
            string nested = TempSubdirectory("nested");
            string filePath = Path.Combine(nested, "data.csv");
            File.WriteAllText(filePath, "x");

            bool result = Fw.WaitForFileMatchingPattern(TempDir, "*.csv", true, 1000, 20, out string matched, out bool timedOut, out _);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Equal(filePath, matched);
        }

        [Fact]
        public void WaitForFileMatchingPatternSimple_DelegatesToFullOverload()
        {
            string filePath = TempFilePath("data.csv");
            File.WriteAllText(filePath, "a,b,c");

            bool result = Fw.WaitForFileMatchingPatternSimple(TempDir, "*.csv", false, 1000, 20, out string matched, out string message);

            Assert.True(result);
            Assert.Equal(filePath, matched);
            Assert.Null(message);
        }
    }
}
