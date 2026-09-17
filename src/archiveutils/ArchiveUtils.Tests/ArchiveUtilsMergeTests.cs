using System.IO;
using System.IO.Compression;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>Real functional tests for MergeArchives.</summary>
    public class ArchiveUtilsMergeTests : TempDirectoryTestBase
    {
        [Fact]
        public void MergeArchives_FirstArchiveMissing_ReturnsFalseWithMessage()
        {
            string second = TempFilePath("second.zip");
            ArchiveFixtures.CreateMutableFixture(second);

            bool result = Archive.MergeArchives(TempFilePath("missing.zip"), second, TempFilePath("out.zip"), false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void MergeArchives_OverwriteFalseAgainstExistingOutput_FailsClosed()
        {
            string first = TempFilePath("first.zip");
            string second = TempFilePath("second.zip");
            ArchiveFixtures.CreateMutableFixture(first);
            ArchiveFixtures.CreateSecondMergeFixture(second);
            string output = TempFilePath("existing.zip");
            File.WriteAllText(output, "not really a zip, just needs to exist");

            bool result = Archive.MergeArchives(first, second, output, false, out string message);

            Assert.False(result);
            Assert.Equal("not really a zip, just needs to exist", File.ReadAllText(output));
        }

        [Fact]
        public void MergeArchives_HappyPath_CombinesEntriesAndDisambiguatesCollisions()
        {
            string first = TempFilePath("first.zip");
            string second = TempFilePath("second.zip");
            ArchiveFixtures.CreateMutableFixture(first); // readme.txt, data/values.csv
            ArchiveFixtures.CreateSecondMergeFixture(second); // readme.txt (collides), extra.txt
            string output = TempFilePath("merged.zip");

            bool result = Archive.MergeArchives(first, second, output, false, out string message);

            Assert.True(result, message);
            Assert.Null(message);

            using ZipArchive archive = ZipFile.OpenRead(output);
            using (var reader = new StreamReader(archive.GetEntry("readme.txt").Open()))
                Assert.Equal("hello world", reader.ReadToEnd());
            using (var reader = new StreamReader(archive.GetEntry("readme_2.txt").Open()))
                Assert.Equal("second archive's readme, should be disambiguated", reader.ReadToEnd());
            Assert.NotNull(archive.GetEntry("extra.txt"));
            Assert.NotNull(archive.GetEntry("data/values.csv"));

            using ZipArchive firstAfter = ZipFile.OpenRead(first);
            Assert.Single(firstAfter.Entries, e => e.FullName == "readme.txt");
        }
    }
}
