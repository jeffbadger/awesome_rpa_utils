using System.IO;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>
    /// Direct unit tests for the zip-slip guard, exercised independently of any real
    /// archive extraction - every traversal variant this component's design identified,
    /// plus a false-positive check for a legitimate nested entry.
    /// </summary>
    public class ArchiveSafetyTests
    {
        private readonly string _destDir = Path.Combine(Path.GetTempPath(), "archivesafety-tests-dest");

        [Theory]
        [InlineData("../evil.txt")]
        [InlineData("../../evil.txt")]
        [InlineData("../../../../evil.txt")]
        [InlineData("subdir/../../evil.txt")]
        public void TryResolveSafeExtractionPath_RelativeTraversal_Rejected(string entryName)
        {
            bool result = ArchiveSafety.TryResolveSafeExtractionPath(_destDir, entryName, out string safePath, out string error);

            Assert.False(result);
            Assert.Null(safePath);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("/evil.txt")]
        [InlineData("/etc/passwd")]
        public void TryResolveSafeExtractionPath_UnixRootedPath_Rejected(string entryName)
        {
            bool result = ArchiveSafety.TryResolveSafeExtractionPath(_destDir, entryName, out string safePath, out string error);

            Assert.False(result);
            Assert.Null(safePath);
            Assert.Contains("rooted", error);
        }

        [Fact]
        public void TryResolveSafeExtractionPath_WindowsRootedPath_Rejected()
        {
            // Path.IsPathRooted("C:\evil.txt") is only true under Windows path rules -
            // on Linux it's (correctly) treated as a harmless relative file name, since
            // this suite's target runtime (Robot Studio) is Windows-only.
            if (!System.OperatingSystem.IsWindows())
                return;

            bool result = ArchiveSafety.TryResolveSafeExtractionPath(_destDir, "C:\\evil.txt", out string safePath, out string error);

            Assert.False(result);
            Assert.Null(safePath);
            Assert.Contains("rooted", error);
        }

        [Fact]
        public void TryResolveSafeExtractionPath_SiblingDirectoryLookingLikePrefix_NotFalselyAccepted()
        {
            // Regression guard for a bare StartsWith(destDirFull) bug: a directory named
            // like "<dest>2" must not pass a "<dest>" prefix check.
            string dest = Path.Combine(Path.GetTempPath(), "archivesafety-prefix-dest");
            // An entry that resolves to a sibling directory sharing dest's name as a prefix.
            bool result = ArchiveSafety.TryResolveSafeExtractionPath(dest, "../" + Path.GetFileName(dest) + "2/evil.txt", out string safePath, out string error);

            Assert.False(result);
            Assert.Null(safePath);
        }

        [Theory]
        [InlineData("readme.txt")]
        [InlineData("data/values.csv")]
        [InlineData("a/b/c/d.txt")]
        public void TryResolveSafeExtractionPath_LegitimateNestedEntry_Accepted(string entryName)
        {
            bool result = ArchiveSafety.TryResolveSafeExtractionPath(_destDir, entryName, out string safePath, out string error);

            Assert.True(result);
            Assert.Null(error);
            Assert.StartsWith(Path.GetFullPath(_destDir), safePath);
        }

        [Fact]
        public void TryResolveSafeExtractionPath_NullOrEmptyEntryName_Rejected()
        {
            bool result = ArchiveSafety.TryResolveSafeExtractionPath(_destDir, "", out string safePath, out string error);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void TryResolveSafeExtractionPath_NullOrEmptyDestination_Rejected()
        {
            bool result = ArchiveSafety.TryResolveSafeExtractionPath("", "readme.txt", out string safePath, out string error);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
