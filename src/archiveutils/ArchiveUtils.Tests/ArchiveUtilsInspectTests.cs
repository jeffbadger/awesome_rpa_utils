using System.Linq;
using System.Text.Json;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>Real functional tests for ListArchiveContentsJson/TryGetArchiveMetadata.</summary>
    public class ArchiveUtilsInspectTests : TempDirectoryTestBase
    {
        [Fact]
        public void ListArchiveContentsJson_BenignFixture_ReportsEveryEntryWithFields()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));

            bool result = Archive.ListArchiveContentsJson(archivePath, out string json, out string message);

            Assert.True(result);
            Assert.Null(message);
            using JsonDocument doc = JsonDocument.Parse(json);
            var names = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("FullName").GetString()).ToList();
            Assert.Contains("readme.txt", names);
            Assert.Contains("data/values.csv", names);
            Assert.Contains("empty-dir/", names);

            JsonElement readme = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("FullName").GetString() == "readme.txt");
            Assert.Equal(11, readme.GetProperty("Length").GetInt64()); // "hello world"
            Assert.False(readme.GetProperty("IsEncrypted").GetBoolean());
            Assert.False(readme.GetProperty("IsDirectory").GetBoolean());

            JsonElement dirEntry = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("FullName").GetString() == "empty-dir/");
            Assert.True(dirEntry.GetProperty("IsDirectory").GetBoolean());
        }

        [Fact]
        public void ListArchiveContentsJson_EncryptedFixture_ReportsIsEncryptedTrue()
        {
            string archivePath = ArchiveFixtures.CreateEncryptedFlagFixture(TempFilePath("encrypted.zip"), out string entryName);

            bool result = Archive.ListArchiveContentsJson(archivePath, out string json, out string message);

            Assert.True(result);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement entry = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("FullName").GetString() == entryName);
            Assert.True(entry.GetProperty("IsEncrypted").GetBoolean());
        }

        [Fact]
        public void ListArchiveContentsJson_EmptyArchive_ReturnsEmptyJsonArray()
        {
            string archivePath = ArchiveFixtures.CreateEmptyFixture(TempFilePath("empty.zip"));

            bool result = Archive.ListArchiveContentsJson(archivePath, out string json, out string message);

            Assert.True(result);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal(0, doc.RootElement.GetArrayLength());
        }

        [Fact]
        public void ListArchiveContentsJson_ArchiveDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Archive.ListArchiveContentsJson(TempFilePath("nope.zip"), out string json, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- TryGetArchiveMetadata ---

        [Fact]
        public void TryGetArchiveMetadata_BenignFixture_ReportsCountsAndNoEncryption()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));

            bool result = Archive.TryGetArchiveMetadata(archivePath, out int entryCount, out long totalUncompressed, out long totalCompressed, out bool hasEncrypted, out string message);

            Assert.True(result);
            Assert.Equal(3, entryCount); // readme.txt, data/values.csv, empty-dir/
            Assert.True(totalUncompressed > 0);
            Assert.False(hasEncrypted);
        }

        [Fact]
        public void TryGetArchiveMetadata_EncryptedFixture_ReportsHasEncryptedTrue()
        {
            string archivePath = ArchiveFixtures.CreateEncryptedFlagFixture(TempFilePath("encrypted.zip"), out _);

            bool result = Archive.TryGetArchiveMetadata(archivePath, out _, out _, out _, out bool hasEncrypted, out string message);

            Assert.True(result);
            Assert.True(hasEncrypted);
        }

        [Fact]
        public void TryGetArchiveMetadata_EmptyArchive_ReportsZeroCounts()
        {
            string archivePath = ArchiveFixtures.CreateEmptyFixture(TempFilePath("empty.zip"));

            bool result = Archive.TryGetArchiveMetadata(archivePath, out int entryCount, out long totalUncompressed, out long totalCompressed, out bool hasEncrypted, out string message);

            Assert.True(result);
            Assert.Equal(0, entryCount);
            Assert.Equal(0, totalUncompressed);
            Assert.Equal(0, totalCompressed);
            Assert.False(hasEncrypted);
        }

        [Fact]
        public void TryGetArchiveMetadata_NullPath_ReturnsFalseWithMessage()
        {
            bool result = Archive.TryGetArchiveMetadata(null, out _, out _, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
