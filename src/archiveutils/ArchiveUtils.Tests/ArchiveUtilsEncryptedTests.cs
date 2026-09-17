using System.IO;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>Real functional tests for CreateEncryptedArchive/ExtractArchiveWithPassword.</summary>
    public class ArchiveUtilsEncryptedTests : TempDirectoryTestBase
    {
        // --- CreateEncryptedArchive ---

        [Fact]
        public void CreateEncryptedArchive_EmptyPassword_ReturnsFalseWithMessage()
        {
            string source = TempSubdirectory("source");
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");

            bool result = Archive.CreateEncryptedArchive(source, TempFilePath("out.zip"), "", false, false, false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void CreateEncryptedArchive_SourceDirectoryDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Archive.CreateEncryptedArchive(Path.Combine(TempDir, "nope"), TempFilePath("out.zip"), "pw", false, false, false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void CreateEncryptedArchive_HappyPath_RoundTripsThroughExtractWithPassword()
        {
            string source = TempSubdirectory("source");
            File.WriteAllText(Path.Combine(source, "secret.txt"), "top secret content");
            string nested = Path.Combine(source, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "deep.txt"), "deep secret");

            string archivePath = TempFilePath("encrypted.zip");
            bool created = Archive.CreateEncryptedArchive(source, archivePath, "correct horse battery staple", false, false, false, out string createMessage);
            Assert.True(created, createMessage);
            Assert.Null(createMessage);

            string destination = TempSubdirectory("extracted");
            bool extracted = Archive.ExtractArchiveWithPassword(archivePath, destination, "correct horse battery staple", false, 0, 0, out string extractMessage);
            Assert.True(extracted, extractMessage);
            Assert.Null(extractMessage);
            Assert.Equal("top secret content", File.ReadAllText(Path.Combine(destination, "secret.txt")));
            Assert.Equal("deep secret", File.ReadAllText(Path.Combine(destination, "nested", "deep.txt")));
        }

        [Fact]
        public void CreateEncryptedArchive_LegacyZipCrypto_AlsoRoundTrips()
        {
            string source = TempSubdirectory("source");
            File.WriteAllText(Path.Combine(source, "secret.txt"), "legacy-encrypted content");

            string archivePath = TempFilePath("encrypted-legacy.zip");
            bool created = Archive.CreateEncryptedArchive(source, archivePath, "pw", false, false, true, out string createMessage);
            Assert.True(created, createMessage);

            string destination = TempSubdirectory("extracted");
            bool extracted = Archive.ExtractArchiveWithPassword(archivePath, destination, "pw", false, 0, 0, out string extractMessage);
            Assert.True(extracted, extractMessage);
            Assert.Equal("legacy-encrypted content", File.ReadAllText(Path.Combine(destination, "secret.txt")));
        }

        // --- ExtractArchiveWithPassword ---

        [Fact]
        public void ExtractArchiveWithPassword_EmptyPassword_ReturnsFalseWithMessage()
        {
            bool result = Archive.ExtractArchiveWithPassword(TempFilePath("x.zip"), TempSubdirectory("dest"), "", false, 0, 0, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ExtractArchiveWithPassword_WrongPassword_ReturnsFalseWithMessage()
        {
            string archivePath = TempFilePath("encrypted.zip");
            ArchiveFixtures.CreateSharpZipLibEncryptedFixture(archivePath, "correct-password", useAes: true, out _);

            bool result = Archive.ExtractArchiveWithPassword(archivePath, TempSubdirectory("dest"), "wrong-password", false, 0, 0, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ExtractArchiveWithPassword_IndependentlyBuiltFixture_ExtractsRealContent()
        {
            string archivePath = TempFilePath("encrypted.zip");
            ArchiveFixtures.CreateSharpZipLibEncryptedFixture(archivePath, "fixture-password", useAes: true, out string entryName);

            string destination = TempSubdirectory("dest");
            bool result = Archive.ExtractArchiveWithPassword(archivePath, destination, "fixture-password", false, 0, 0, out string message);

            Assert.True(result, message);
            Assert.Equal("this is the real secret content", File.ReadAllText(Path.Combine(destination, entryName)));
        }
    }
}
