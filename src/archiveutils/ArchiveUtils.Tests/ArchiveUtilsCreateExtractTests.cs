using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>Real functional tests for CreateArchive/ExtractArchive, including the zip-bomb pre-scan.</summary>
    public class ArchiveUtilsCreateExtractTests : TempDirectoryTestBase
    {
        // --- CreateArchive ---

        [Fact]
        public void CreateArchive_NullArgs_ReturnsFalseWithMessage()
        {
            bool result = Archive.CreateArchive(null, TempFilePath(), false, false, false, null, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void CreateArchive_SourceDirectoryDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Archive.CreateArchive(Path.Combine(TempDir, "nope"), TempFilePath("out.zip"), false, false, false, null, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void CreateArchive_HappyPath_RoundTripsNestedTreeThroughExtract()
        {
            string source = TempSubdirectory("source");
            File.WriteAllText(Path.Combine(source, "top.txt"), "top-level");
            string nested = Path.Combine(source, "nested", "deep");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "deep.txt"), "deep content");
            Directory.CreateDirectory(Path.Combine(source, "empty-dir"));
            File.WriteAllText(Path.Combine(source, "zero-byte.txt"), "");

            string archivePath = TempFilePath("archive.zip");
            bool created = Archive.CreateArchive(source, archivePath, false, false, false, null, out string createMessage);
            Assert.True(created, createMessage);
            Assert.Null(createMessage);
            Assert.True(File.Exists(archivePath));

            string destination = TempSubdirectory("extracted");
            bool extracted = Archive.ExtractArchive(archivePath, destination, false, 0, 0, true, out string extractMessage);
            Assert.True(extracted);
            Assert.Null(extractMessage);

            Assert.Equal("top-level", File.ReadAllText(Path.Combine(destination, "top.txt")));
            Assert.Equal("deep content", File.ReadAllText(Path.Combine(destination, "nested", "deep", "deep.txt")));
            Assert.True(Directory.Exists(Path.Combine(destination, "empty-dir")));
            Assert.Equal("", File.ReadAllText(Path.Combine(destination, "zero-byte.txt")));
        }

        [Fact]
        public void CreateArchive_OverwriteFalseAgainstExistingArchive_FailsClosed()
        {
            string source = TempSubdirectory("source");
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            string archivePath = TempFilePath("existing.zip");
            File.WriteAllText(archivePath, "not really a zip, just needs to exist");

            bool result = Archive.CreateArchive(source, archivePath, false, false, false, null, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal("not really a zip, just needs to exist", File.ReadAllText(archivePath));
        }

        [Fact]
        public void CreateArchive_AtomicPublish_NoPartialFileLeftOnFailure()
        {
            // A source directory that will fail mid-build (an out-of-range timestamp on one
            // file) must never leave a partially-written file at the final archive path.
            string source = TempSubdirectory("source");
            string badFile = Path.Combine(source, "toooold.txt");
            File.WriteAllText(badFile, "old");
            File.SetLastWriteTimeUtc(badFile, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            string archivePath = TempFilePath("never-created.zip");
            bool result = Archive.CreateArchive(source, archivePath, false, false, false, null, out string message);

            Assert.False(result);
            Assert.False(File.Exists(archivePath));
            // No leftover .tmp files either.
            Assert.Empty(Directory.GetFiles(TempDir, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public void CreateArchive_NormalizeTimestampsTrue_SetsEveryEntryToGivenTimestamp()
        {
            string source = TempSubdirectory("source");
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            File.WriteAllText(Path.Combine(source, "b.txt"), "b");

            string archivePath = TempFilePath("normalized.zip");
            bool result = Archive.CreateArchive(source, archivePath, false, false, true, "2020-06-15T00:00:00Z", out string message);

            Assert.True(result);
            Assert.Null(message);

            // The classic ZIP/DOS date-time format has no time-zone field - only the
            // wall-clock date/time round-trips through ZipArchiveEntry.LastWriteTime, so
            // compare wall-clock components rather than a specific offset.
            using ZipArchive opened = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in opened.Entries)
                Assert.Equal(new DateTime(2020, 6, 15, 0, 0, 0), entry.LastWriteTime.DateTime);
        }

        [Fact]
        public void CreateArchive_NormalizeTimestampsFalse_PreservesSourceFileTimestamp()
        {
            string source = TempSubdirectory("source");
            string filePath = Path.Combine(source, "a.txt");
            File.WriteAllText(filePath, "a");
            var sourceTimestamp = new DateTime(2022, 3, 4, 5, 6, 6, DateTimeKind.Local); // even second, DOS-representable
            File.SetLastWriteTime(filePath, sourceTimestamp);

            string archivePath = TempFilePath("preserved.zip");
            bool result = Archive.CreateArchive(source, archivePath, false, false, false, null, out string message);

            Assert.True(result);
            using ZipArchive opened = ZipFile.OpenRead(archivePath);
            // Same wall-clock-only comparison as above - the ZIP format has no time-zone
            // field, so only the local date/time components are expected to round-trip.
            Assert.Equal(sourceTimestamp, opened.Entries.Single().LastWriteTime.DateTime);
        }

        // --- ExtractArchive limits (zip-bomb defense) ---

        [Fact]
        public void ExtractArchive_HighCompressionRatioFixture_FailsClosedAndExtractsNothing()
        {
            string archivePath = ArchiveFixtures.CreateHighRatioBombFixture(TempFilePath("bomb.zip"), uncompressedSize: 20_000_000);
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractArchive(archivePath, destination, false, 0, 100.0, true, out string message);

            Assert.False(result);
            Assert.Contains("ratio", message);
            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void ExtractArchive_TotalSizeAcrossManyEntries_FailsClosedAndExtractsNothing()
        {
            string archivePath = ArchiveFixtures.CreateManyEntriesSizeBombFixture(TempFilePath("many.zip"), entryCount: 10, perEntryBytes: 2_000_000);
            string destination = TempSubdirectory("dest");

            // No single entry (2 MB, random content) trips a 100:1 ratio limit, but the sum
            // (20 MB) exceeds this 5 MB total-size limit.
            bool result = Archive.ExtractArchive(archivePath, destination, false, 5_000_000, 0, true, out string message);

            Assert.False(result);
            Assert.Contains("total declared expanded size", message);
            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void ExtractArchive_LimitsDisabled_ExtractsHighRatioFixtureSuccessfully()
        {
            string archivePath = ArchiveFixtures.CreateHighRatioBombFixture(TempFilePath("bomb.zip"), uncompressedSize: 1_000_000);
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractArchive(archivePath, destination, false, 0, 0, true, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.NotEmpty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void ExtractArchiveSimple_UsesDefaultLimits_RejectsBombFixture()
        {
            string archivePath = ArchiveFixtures.CreateHighRatioBombFixture(TempFilePath("bomb.zip"), uncompressedSize: 20_000_000);
            string destination = TempSubdirectory("dest");

            bool result = Archive.ExtractArchiveSimple(archivePath, destination, false, out string message);

            Assert.False(result);
            Assert.Contains("ratio", message);
        }

        [Fact]
        public void ExtractArchive_PreserveTimestampsFalse_SetsExtractedFilesToNow()
        {
            string source = TempSubdirectory("source");
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            string archivePath = TempFilePath("archive.zip");
            Archive.CreateArchive(source, archivePath, false, false, true, "2001-01-01T00:00:00Z", out _);

            string destination = TempSubdirectory("dest");
            bool result = Archive.ExtractArchive(archivePath, destination, false, 0, 0, false, out string message);

            Assert.True(result);
            DateTime extractedWrite = File.GetLastWriteTimeUtc(Path.Combine(destination, "a.txt"));
            Assert.True((DateTime.UtcNow - extractedWrite) < TimeSpan.FromMinutes(5));
        }

        [Fact]
        public void ExtractArchive_ArchiveDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Archive.ExtractArchive(TempFilePath("nope.zip"), TempSubdirectory(), false, 0, 0, true, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
