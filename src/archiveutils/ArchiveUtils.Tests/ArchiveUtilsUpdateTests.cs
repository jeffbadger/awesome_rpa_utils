using System;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>Real functional tests for AddOrReplaceFilesInArchive/RemoveArchiveEntry/RenameArchiveEntry.</summary>
    public class ArchiveUtilsUpdateTests : TempDirectoryTestBase
    {
        // --- AddOrReplaceFilesInArchive ---

        [Fact]
        public void AddOrReplaceFilesInArchive_NullArchivePath_ReturnsFalseWithMessage()
        {
            bool result = Archive.AddOrReplaceFilesInArchive(null, TempFilePath("a.txt"), null, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void AddOrReplaceFilesInArchive_ArchiveDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Archive.AddOrReplaceFilesInArchive(TempFilePath("nope.zip"), TempFilePath("a.txt"), null, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void AddOrReplaceFilesInArchive_SourceFileMissing_ReturnsFalseWithMessageAndLeavesArchiveUntouched()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);
            byte[] before = File.ReadAllBytes(archivePath);

            bool result = Archive.AddOrReplaceFilesInArchive(archivePath, TempFilePath("does-not-exist.txt"), null, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal(before, File.ReadAllBytes(archivePath));
        }

        [Fact]
        public void AddOrReplaceFilesInArchive_NewFile_AddsIt()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);
            string newFile = TempFilePath("new.txt");
            File.WriteAllText(newFile, "brand new content");

            bool result = Archive.AddOrReplaceFilesInArchive(archivePath, newFile, null, out string message);

            Assert.True(result, message);
            Assert.Null(message);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry entry = archive.GetEntry("new.txt");
            Assert.NotNull(entry);
            using (var reader = new StreamReader(entry.Open()))
                Assert.Equal("brand new content", reader.ReadToEnd());
            Assert.NotNull(archive.GetEntry("readme.txt"));
        }

        [Fact]
        public void AddOrReplaceFilesInArchive_ExistingEntryName_ReplacesContent()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);
            string replacement = TempFilePath("readme.txt");
            File.WriteAllText(replacement, "replaced content");

            bool result = Archive.AddOrReplaceFilesInArchive(archivePath, replacement, "readme.txt", out string message);

            Assert.True(result, message);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.Single(archive.Entries, e => e.FullName == "readme.txt");
            using (var reader = new StreamReader(archive.GetEntry("readme.txt").Open()))
                Assert.Equal("replaced content", reader.ReadToEnd());
        }

        [Fact]
        public void AddOrReplaceFilesInArchive_ExplicitEntryName_PlacesFileAtGivenPath()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);
            string sourceFile = TempFilePath("source.txt");
            File.WriteAllText(sourceFile, "nested content");

            bool result = Archive.AddOrReplaceFilesInArchive(archivePath, sourceFile, "nested/folder/renamed.txt", out string message);

            Assert.True(result, message);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.NotNull(archive.GetEntry("nested/folder/renamed.txt"));
        }

        [Fact]
        public void AddOrReplaceFilesInArchive_EntryNamesCountMismatch_ReturnsFalseWithMessage()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);
            string file1 = TempFilePath("f1.txt");
            string file2 = TempFilePath("f2.txt");
            File.WriteAllText(file1, "1");
            File.WriteAllText(file2, "2");

            bool result = Archive.AddOrReplaceFilesInArchive(archivePath, $"{file1},{file2}", "onlyOneName.txt", out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void AddOrReplaceFilesInArchive_TwoNewFilesSameFallbackName_Disambiguates()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);
            string dir1 = Path.Combine(TempDir, "d1");
            string dir2 = Path.Combine(TempDir, "d2");
            Directory.CreateDirectory(dir1);
            Directory.CreateDirectory(dir2);
            string file1 = Path.Combine(dir1, "report.txt");
            string file2 = Path.Combine(dir2, "report.txt");
            File.WriteAllText(file1, "first report");
            File.WriteAllText(file2, "second report");

            bool result = Archive.AddOrReplaceFilesInArchive(archivePath, $"{file1},{file2}", null, out string message);

            Assert.True(result, message);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            using (var reader = new StreamReader(archive.GetEntry("report.txt").Open()))
                Assert.Equal("first report", reader.ReadToEnd());
            using (var reader = new StreamReader(archive.GetEntry("report_2.txt").Open()))
                Assert.Equal("second report", reader.ReadToEnd());
        }

        // --- RemoveArchiveEntry ---

        [Fact]
        public void RemoveArchiveEntry_EntryDoesNotExist_ReturnsFalseWithMessage()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);

            bool result = Archive.RemoveArchiveEntry(archivePath, "does-not-exist.txt", out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void RemoveArchiveEntry_ExistingEntry_RemovesItAndKeepsOthers()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);

            bool result = Archive.RemoveArchiveEntry(archivePath, "readme.txt", out string message);

            Assert.True(result, message);
            Assert.Null(message);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.Null(archive.GetEntry("readme.txt"));
            Assert.NotNull(archive.GetEntry("data/values.csv"));
        }

        // --- RenameArchiveEntry ---

        [Fact]
        public void RenameArchiveEntry_SourceMissing_ReturnsFalseWithMessage()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);

            bool result = Archive.RenameArchiveEntry(archivePath, "does-not-exist.txt", "new.txt", out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void RenameArchiveEntry_TargetNameAlreadyExists_ReturnsFalseWithMessageAndLeavesArchiveUntouched()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);
            byte[] before = File.ReadAllBytes(archivePath);

            bool result = Archive.RenameArchiveEntry(archivePath, "readme.txt", "data/values.csv", out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal(before, File.ReadAllBytes(archivePath));
        }

        [Fact]
        public void RenameArchiveEntry_HappyPath_PreservesContentAndTimestamp()
        {
            string archivePath = TempFilePath("archive.zip");
            ArchiveFixtures.CreateMutableFixture(archivePath);
            DateTimeOffset originalTimestamp;
            using (ZipArchive readArchive = ZipFile.OpenRead(archivePath))
                originalTimestamp = readArchive.GetEntry("readme.txt").LastWriteTime;

            bool result = Archive.RenameArchiveEntry(archivePath, "readme.txt", "renamed.txt", out string message);

            Assert.True(result, message);
            Assert.Null(message);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.Null(archive.GetEntry("readme.txt"));
            ZipArchiveEntry renamed = archive.GetEntry("renamed.txt");
            Assert.NotNull(renamed);
            using (var reader = new StreamReader(renamed.Open()))
                Assert.Equal("hello world", reader.ReadToEnd());
            Assert.Equal(originalTimestamp, renamed.LastWriteTime);
        }
    }
}
