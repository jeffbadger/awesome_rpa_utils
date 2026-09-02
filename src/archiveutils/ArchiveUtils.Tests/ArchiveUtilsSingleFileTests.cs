using System.IO;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>
    /// Real functional tests for ExtractSingleFile/ExtractFirstMatchingFile, including the
    /// actual zip-slip regression test - the empirically-reproduced vulnerability this
    /// component's whole premise is built around.
    /// </summary>
    public class ArchiveUtilsSingleFileTests : TempDirectoryTestBase
    {
        // --- ExtractSingleFile: the security regression test ---

        [Fact]
        public void ExtractSingleFile_RelativeTraversalEntry_RejectedAndNothingWrittenOutsideDestination()
        {
            string archivePath = ArchiveFixtures.CreateZipSlipFixture(TempFilePath("slip.zip"));
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractSingleFile(archivePath, "../../evil-relative.txt", destination, false, 0, 0, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
            // The actual regression assertion: the file must not exist anywhere outside `destination`.
            Assert.False(File.Exists(Path.Combine(TempDir, "evil-relative.txt")));
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "evil-relative.txt")));
            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void ExtractSingleFile_RootedTraversalEntry_RejectedAndNothingWrittenOutsideDestination()
        {
            string archivePath = ArchiveFixtures.CreateZipSlipFixture(TempFilePath("slip.zip"));
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractSingleFile(archivePath, "/evil-rooted.txt", destination, false, 0, 0, out string message);

            Assert.False(result);
            Assert.Contains("rooted", message);
            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void ExtractSingleFile_BenignEntryInSameArchiveAsSlipEntries_StillExtractsSuccessfully()
        {
            // A false-positive check: the safety guard must not be so aggressive it rejects
            // legitimate entries just because the archive also contains malicious ones.
            string archivePath = ArchiveFixtures.CreateZipSlipFixture(TempFilePath("slip.zip"));
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractSingleFile(archivePath, "safe.txt", destination, false, 0, 0, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.Equal("this one is fine", File.ReadAllText(Path.Combine(destination, "safe.txt")));
        }

        // --- ExtractSingleFile: happy path / not found ---

        [Fact]
        public void ExtractSingleFile_ExactName_HappyPath()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractSingleFile(archivePath, "data/values.csv", destination, false, 0, 0, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.Equal("a,b,c\n1,2,3\n", File.ReadAllText(Path.Combine(destination, "data", "values.csv")));
        }

        [Fact]
        public void ExtractSingleFile_EntryNotFound_ReturnsFalseCleanly()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractSingleFile(archivePath, "does-not-exist.txt", destination, false, 0, 0, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ExtractSingleFile_PerEntryRatioLimitExceeded_RejectedAndNothingExtracted()
        {
            string archivePath = ArchiveFixtures.CreateHighRatioBombFixture(TempFilePath("bomb.zip"), uncompressedSize: 20_000_000);
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractSingleFile(archivePath, "bomb.bin", destination, false, 0, 100.0, out string message);

            Assert.False(result);
            Assert.Contains("ratio", message);
            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }

        [Theory]
        [InlineData(null, "dest")]
        [InlineData("archive.zip", null)]
        public void ExtractSingleFile_NullArgs_ReturnsFalseWithMessage(string archivePath, string destination)
        {
            bool result = Archive.ExtractSingleFile(archivePath, "entry.txt", destination, false, 0, 0, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- ExtractFirstMatchingFile ---

        [Fact]
        public void ExtractFirstMatchingFile_GlobPattern_HappyPathReportsMatchedName()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractFirstMatchingFile(archivePath, "*.csv", destination, false, 0, 0, out string matchedEntryName, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.Equal("data/values.csv", matchedEntryName);
        }

        [Fact]
        public void ExtractFirstMatchingFile_NoMatch_ReturnsFalseCleanly()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractFirstMatchingFile(archivePath, "*.pdf", destination, false, 0, 0, out string matchedEntryName, out string message);

            Assert.False(result);
            Assert.Null(matchedEntryName);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ExtractFirstMatchingFile_PatternMatchingSlipEntry_StillRejectedByPathGuard()
        {
            string archivePath = ArchiveFixtures.CreateZipSlipFixture(TempFilePath("slip.zip"));
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractFirstMatchingFile(archivePath, "*evil*", destination, false, 0, 0, out string matchedEntryName, out string message);

            Assert.False(result);
            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }
    }
}
