using System.Linq;
using System.Text.Json;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>Real functional tests for ValidateArchiveCrc/Json and HasEncryptedEntries.</summary>
    public class ArchiveUtilsValidateTests : TempDirectoryTestBase
    {
        [Fact]
        public void ValidateArchiveCrc_BenignFixture_AllEntriesValid()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));

            bool result = Archive.ValidateArchiveCrc(archivePath, out bool allValid, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.True(allValid);
        }

        [Fact]
        public void ValidateArchiveCrc_CorruptedFixture_ReportsNotAllValid()
        {
            string archivePath = ArchiveFixtures.CreateCorruptedCrcFixture(TempFilePath("corrupt.zip"), out _);

            bool result = Archive.ValidateArchiveCrc(archivePath, out bool allValid, out string message);

            Assert.True(result);
            Assert.False(allValid);
        }

        [Fact]
        public void ValidateArchiveCrcJson_CorruptedFixture_ReportsSpecificEntryAsMismatch()
        {
            string archivePath = ArchiveFixtures.CreateCorruptedCrcFixture(TempFilePath("corrupt.zip"), out string entryName);

            bool result = Archive.ValidateArchiveCrcJson(archivePath, out string json, out string message);

            Assert.True(result);
            Assert.Null(message);
            using JsonDocument doc = JsonDocument.Parse(json);
            var entries = doc.RootElement.EnumerateArray().ToList();
            Assert.Single(entries);
            Assert.Equal(entryName, entries[0].GetProperty("FullName").GetString());
            Assert.Equal("Mismatch", entries[0].GetProperty("Status").GetString());
            Assert.False(entries[0].GetProperty("IsValid").GetBoolean());
            Assert.NotEqual(entries[0].GetProperty("DeclaredCrc32").GetUInt32(), entries[0].GetProperty("ComputedCrc32").GetUInt32());
        }

        [Fact]
        public void ValidateArchiveCrcJson_BenignFixture_EveryEntryReportsValid()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));

            bool result = Archive.ValidateArchiveCrcJson(archivePath, out string json, out string message);

            Assert.True(result);
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement entry in doc.RootElement.EnumerateArray())
                Assert.Equal("Valid", entry.GetProperty("Status").GetString());
        }

        [Fact]
        public void ValidateArchiveCrcJson_EncryptedFixture_ReportsSkippedEncryptedNotMismatch()
        {
            string archivePath = ArchiveFixtures.CreateEncryptedFlagFixture(TempFilePath("encrypted.zip"), out string entryName);

            bool result = Archive.ValidateArchiveCrcJson(archivePath, out string json, out string message);

            Assert.True(result);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement entry = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("FullName").GetString() == entryName);
            Assert.Equal("SkippedEncrypted", entry.GetProperty("Status").GetString());
            Assert.False(entry.GetProperty("IsValid").GetBoolean());
        }

        [Fact]
        public void ValidateArchiveCrc_EncryptedFixture_SkippedEntryDoesNotCountAsMismatchForAllValid()
        {
            // A SkippedEncrypted entry is not a "Mismatch," so allEntriesValid should still
            // read true when every other entry validates cleanly.
            string archivePath = ArchiveFixtures.CreateEncryptedFlagFixture(TempFilePath("encrypted-only.zip"), out _);

            bool result = Archive.ValidateArchiveCrc(archivePath, out bool allValid, out string message);

            Assert.True(result);
            Assert.True(allValid);
        }

        [Fact]
        public void ValidateArchiveCrc_EmptyArchive_ReturnsTrueWithAllValidTrue()
        {
            string archivePath = ArchiveFixtures.CreateEmptyFixture(TempFilePath("empty.zip"));

            bool result = Archive.ValidateArchiveCrc(archivePath, out bool allValid, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.True(allValid);
        }

        [Fact]
        public void ValidateArchiveCrc_ArchiveDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Archive.ValidateArchiveCrc(TempFilePath("nope.zip"), out bool allValid, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- HasEncryptedEntries ---

        [Fact]
        public void HasEncryptedEntries_BenignFixture_ReturnsFalse()
        {
            string archivePath = ArchiveFixtures.CreateBenignFixture(TempFilePath("benign.zip"));

            bool result = Archive.HasEncryptedEntries(archivePath, out bool hasEncrypted, out string message);

            Assert.True(result);
            Assert.False(hasEncrypted);
        }

        [Fact]
        public void HasEncryptedEntries_EncryptedFixture_ReturnsTrue()
        {
            string archivePath = ArchiveFixtures.CreateEncryptedFlagFixture(TempFilePath("encrypted.zip"), out _);

            bool result = Archive.HasEncryptedEntries(archivePath, out bool hasEncrypted, out string message);

            Assert.True(result);
            Assert.True(hasEncrypted);
        }

        [Fact]
        public void HasEncryptedEntries_NullPath_ReturnsFalseWithMessage()
        {
            bool result = Archive.HasEncryptedEntries(null, out bool hasEncrypted, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
