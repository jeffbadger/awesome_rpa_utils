using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>
    /// Real functional tests for IsFileLocked/WaitForFileUnlocked - a genuine
    /// FileShare.None handle is held by the test process itself, not simulated.
    /// </summary>
    public class FileWatchUtilsLockTests : TempDirectoryTestBase
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsFileLockedSimple_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.IsFileLockedSimple(path, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void IsFileLocked_NonexistentFile_ReturnsFalseWithQueryFailed()
        {
            bool result = Fw.IsFileLocked(TempFilePath(), out bool querySucceeded, out string message);

            Assert.False(result);
            Assert.False(querySucceeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void IsFileLockedSimple_UnlockedFile_ReturnsFalseWithMessage()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "not locked");

            bool result = Fw.IsFileLockedSimple(path, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void IsFileLocked_HeldExclusively_ReturnsTrueThenFalseAfterRelease()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "will be locked");

            using (var handle = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool lockedResult = Fw.IsFileLocked(path, out bool querySucceeded, out string message);
                Assert.True(lockedResult);
                Assert.True(querySucceeded);
                Assert.Null(message);
            }

            bool unlockedResult = Fw.IsFileLocked(path, out bool querySucceededAfter, out string messageAfter);
            Assert.False(unlockedResult);
            Assert.True(querySucceededAfter);
            Assert.False(string.IsNullOrEmpty(messageAfter));
        }

        // --- WaitForFileUnlocked ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForFileUnlocked_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.WaitForFileUnlocked(path, 100, 10, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileUnlocked_AlreadyUnlocked_ReturnsTrueImmediately()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "free");

            bool result = Fw.WaitForFileUnlocked(path, 1000, 20, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Null(message);
        }

        [Fact]
        public void WaitForFileUnlocked_ReleasedBeforeTimeout_ReturnsTrue()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "locked for a bit");
            var handle = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            _ = Task.Run(async () =>
            {
                await Task.Delay(150);
                handle.Dispose();
            });

            bool result = Fw.WaitForFileUnlocked(path, 5000, 20, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
        }

        [Fact]
        public void WaitForFileUnlocked_NeverReleased_TimesOut()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "locked forever (for the test)");
            using var handle = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            bool result = Fw.WaitForFileUnlocked(path, 150, 20, out bool timedOut, out string message);

            Assert.False(result);
            Assert.True(timedOut);
        }

        [Fact]
        public void WaitForFileUnlockedSimple_DelegatesToFullOverload()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "free");

            bool result = Fw.WaitForFileUnlockedSimple(path, 1000, 20, out string message);

            Assert.True(result);
            Assert.Null(message);
        }
    }
}
