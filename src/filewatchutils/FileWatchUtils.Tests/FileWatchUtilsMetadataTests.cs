using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>Real functional tests for TryGetFileMetadata/GetFileMetadataJson/GetDirectoryListingJson.</summary>
    public class FileWatchUtilsMetadataTests : TempDirectoryTestBase
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryGetFileMetadata_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.TryGetFileMetadata(path, out _, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetFileMetadata_NonexistentFile_ReturnsFalseWithMessage()
        {
            bool result = Fw.TryGetFileMetadata(TempFilePath(), out _, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetFileMetadata_RealFile_ReturnsCorrectSizeAndRoundTrippableTimestamps()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "0123456789"); // 10 bytes

            bool result = Fw.TryGetFileMetadata(path, out long sizeBytes, out string lastWriteUtcIso8601, out string createdUtcIso8601, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.Equal(10, sizeBytes);
            Assert.True(DateTimeOffset.TryParse(lastWriteUtcIso8601, out _));
            Assert.True(DateTimeOffset.TryParse(createdUtcIso8601, out _));
        }

        // --- GetFileMetadataJson ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void GetFileMetadataJson_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.GetFileMetadataJson(path, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetFileMetadataJson_NonexistentPath_ReturnsFalseWithMessage()
        {
            bool result = Fw.GetFileMetadataJson(Path.Combine(TempDir, "nope"), out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetFileMetadataJson_RealFile_ProducesExpectedShape()
        {
            string path = TempFilePath("data.txt");
            File.WriteAllText(path, "hello");

            bool result = Fw.GetFileMetadataJson(path, out string json, out string message);

            Assert.True(result);
            Assert.Null(message);
            var doc = JsonDocument.Parse(json);
            Assert.Equal(path, doc.RootElement.GetProperty("FullPath").GetString());
            Assert.Equal("data.txt", doc.RootElement.GetProperty("Name").GetString());
            Assert.Equal(".txt", doc.RootElement.GetProperty("Extension").GetString());
            Assert.Equal(5, doc.RootElement.GetProperty("SizeBytes").GetInt64());
            Assert.False(doc.RootElement.GetProperty("IsDirectory").GetBoolean());
        }

        [Fact]
        public void GetFileMetadataJson_RealDirectory_ReportsIsDirectoryTrue()
        {
            string dir = TempSubdirectory("sub");

            bool result = Fw.GetFileMetadataJson(dir, out string json, out string message);

            Assert.True(result);
            var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("IsDirectory").GetBoolean());
            Assert.Equal(0, doc.RootElement.GetProperty("SizeBytes").GetInt64());
        }

        // --- GetDirectoryListingJson ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void GetDirectoryListingJson_NullOrEmptyDirectory_ReturnsFalseWithMessage(string directoryPath)
        {
            bool result = Fw.GetDirectoryListingJson(directoryPath, "*", false, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetDirectoryListingJson_DirectoryDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Fw.GetDirectoryListingJson(Path.Combine(TempDir, "nope"), "*", false, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetDirectoryListingJson_MultipleFiles_ListsAllMatching()
        {
            File.WriteAllText(TempFilePath("a.csv"), "1");
            File.WriteAllText(TempFilePath("b.csv"), "22");
            File.WriteAllText(TempFilePath("c.txt"), "not csv");

            bool result = Fw.GetDirectoryListingJson(TempDir, "*.csv", false, out string json, out string message);

            Assert.True(result);
            Assert.Null(message);
            var doc = JsonDocument.Parse(json);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
        }

        [Fact]
        public void GetDirectoryListingJson_EmptyDirectory_ReturnsEmptyArray()
        {
            bool result = Fw.GetDirectoryListingJson(TempDir, "*", false, out string json, out string message);

            Assert.True(result);
            var doc = JsonDocument.Parse(json);
            Assert.Equal(0, doc.RootElement.GetArrayLength());
        }

        [Fact]
        public void GetDirectoryListingJson_IncludeSubdirectoriesTrue_ListsNestedFiles()
        {
            string nested = TempSubdirectory("nested");
            File.WriteAllText(Path.Combine(nested, "deep.csv"), "x");
            File.WriteAllText(TempFilePath("shallow.csv"), "y");

            bool result = Fw.GetDirectoryListingJson(TempDir, "*.csv", true, out string json, out _);

            Assert.True(result);
            var doc = JsonDocument.Parse(json);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
        }
    }
}
