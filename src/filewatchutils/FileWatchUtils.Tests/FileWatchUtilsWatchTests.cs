using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>Real functional tests for WatchForChange against a real FileSystemWatcher/temp directory.</summary>
    public class FileWatchUtilsWatchTests : TempDirectoryTestBase
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WatchForChange_NullOrEmptyDirectory_ReturnsFalseWithMessage(string directoryPath)
        {
            bool result = Fw.WatchForChange(directoryPath, null, null, false, 100, out _, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WatchForChange_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool result = Fw.WatchForChange(TempDir, null, null, false, -1, out _, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WatchForChange_DirectoryDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Fw.WatchForChange(Path.Combine(TempDir, "nope"), null, null, false, 100, out _, out _, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WatchForChange_UnknownChangeKind_ReturnsFalseWithMessage()
        {
            bool result = Fw.WatchForChange(TempDir, null, "NotAKind", false, 100, out _, out _, out _, out string message);

            Assert.False(result);
            Assert.Contains("Unknown file change kind", message);
        }

        [Fact]
        public void WatchForChange_NoEvent_TimesOut()
        {
            bool result = Fw.WatchForChange(TempDir, null, null, false, 200, out string changedPath, out _, out bool timedOut, out string message);

            Assert.False(result);
            Assert.True(timedOut);
            Assert.Null(changedPath);
            Assert.Null(message);
        }

        [Fact]
        public void WatchForChange_FileCreated_DetectsCreatedKind()
        {
            string filePath = TempFilePath("new.txt");
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                File.WriteAllText(filePath, "hello");
            });

            bool result = Fw.WatchForChange(TempDir, null, "Created", false, 5000, out string changedPath, out FileChangeKind kind, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Equal(FileChangeKind.Created, kind);
            Assert.Equal(filePath, changedPath);
        }

        [Fact]
        public void WatchForChange_FileDeleted_DetectsDeletedKind()
        {
            string filePath = TempFilePath("doomed.txt");
            File.WriteAllText(filePath, "will vanish");
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                File.Delete(filePath);
            });

            bool result = Fw.WatchForChange(TempDir, null, "Deleted", false, 5000, out string changedPath, out FileChangeKind kind, out bool timedOut, out _);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Equal(FileChangeKind.Deleted, kind);
            Assert.Equal(filePath, changedPath);
        }

        [Fact]
        public void WatchForChange_FileContentChanged_DetectsChangedKind()
        {
            string filePath = TempFilePath("data.txt");
            File.WriteAllText(filePath, "initial");
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                File.AppendAllText(filePath, " more");
            });

            bool result = Fw.WatchForChange(TempDir, null, "Changed", false, 5000, out string changedPath, out FileChangeKind kind, out bool timedOut, out _);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Equal(FileChangeKind.Changed, kind);
        }

        [Fact]
        public void WatchForChange_ChangeKindsFilter_IgnoresNonMatchingKind()
        {
            // Filtered to Deleted only: a Created event during the window must not satisfy
            // the wait - only the later Deleted event should.
            string filePath = TempFilePath("filtered.txt");
            _ = Task.Run(async () =>
            {
                await Task.Delay(150);
                File.WriteAllText(filePath, "created, should be ignored");
                await Task.Delay(150);
                File.Delete(filePath);
            });

            bool result = Fw.WatchForChange(TempDir, null, "Deleted", false, 5000, out string changedPath, out FileChangeKind kind, out bool timedOut, out _);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Equal(FileChangeKind.Deleted, kind);
        }

        [Fact]
        public void WatchForChange_FilterRestrictsToMatchingFiles()
        {
            string ignoredPath = TempFilePath("ignore.log");
            string matchedPath = TempFilePath("match.csv");
            _ = Task.Run(async () =>
            {
                await Task.Delay(150);
                File.WriteAllText(ignoredPath, "should not match filter");
                await Task.Delay(150);
                File.WriteAllText(matchedPath, "should match filter");
            });

            bool result = Fw.WatchForChange(TempDir, "*.csv", "Created", false, 5000, out string changedPath, out FileChangeKind kind, out bool timedOut, out _);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Equal(matchedPath, changedPath);
        }

        [Fact]
        public void WatchForChangeSimple_DelegatesToFullOverload()
        {
            bool result = Fw.WatchForChangeSimple(TempDir, null, null, false, 200, out string changedPath, out FileChangeKind kind, out string message);

            Assert.False(result); // no event within 200ms
            Assert.Null(message);
        }
    }
}
