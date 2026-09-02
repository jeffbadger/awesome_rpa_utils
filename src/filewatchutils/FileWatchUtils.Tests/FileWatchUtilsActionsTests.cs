using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>Real functional tests for AtomicMoveFile/ReplaceFile/ClaimFile.</summary>
    public class FileWatchUtilsActionsTests : TempDirectoryTestBase
    {
        // --- AtomicMoveFile ---

        [Theory]
        [InlineData(null, "dest")]
        [InlineData("source", null)]
        public void AtomicMoveFile_NullArgs_ReturnsFalseWithMessage(string sourcePath, string destinationPath)
        {
            bool result = Fw.AtomicMoveFile(sourcePath, destinationPath, false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void AtomicMoveFile_SourceDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Fw.AtomicMoveFile(TempFilePath(), TempFilePath(), false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void AtomicMoveFile_HappyPath_MovesFile()
        {
            string source = TempFilePath("source.txt");
            string destination = TempFilePath("destination.txt");
            File.WriteAllText(source, "payload");

            bool result = Fw.AtomicMoveFile(source, destination, false, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.False(File.Exists(source));
            Assert.Equal("payload", File.ReadAllText(destination));
        }

        [Fact]
        public void AtomicMoveFile_DestinationExistsWithoutOverwrite_ReturnsFalseWithMessage()
        {
            string source = TempFilePath("source.txt");
            string destination = TempFilePath("destination.txt");
            File.WriteAllText(source, "new");
            File.WriteAllText(destination, "old");

            bool result = Fw.AtomicMoveFile(source, destination, false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal("old", File.ReadAllText(destination));
        }

        [Fact]
        public void AtomicMoveFile_DestinationExistsWithOverwrite_ReplacesFile()
        {
            string source = TempFilePath("source.txt");
            string destination = TempFilePath("destination.txt");
            File.WriteAllText(source, "new");
            File.WriteAllText(destination, "old");

            bool result = Fw.AtomicMoveFile(source, destination, true, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.Equal("new", File.ReadAllText(destination));
        }

        // --- ReplaceFile ---

        [Theory]
        [InlineData(null, "dest")]
        [InlineData("source", null)]
        public void ReplaceFile_NullArgs_ReturnsFalseWithMessage(string sourcePath, string destinationPath)
        {
            bool result = Fw.ReplaceFile(sourcePath, destinationPath, null, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ReplaceFile_DestinationDoesNotExist_ReturnsFalseWithMessage()
        {
            string source = TempFilePath("source.txt");
            File.WriteAllText(source, "content");

            bool result = Fw.ReplaceFile(source, TempFilePath(), null, out string message);

            Assert.False(result);
            Assert.Contains("does not exist", message);
        }

        [Fact]
        public void ReplaceFile_HappyPath_ReplacesDestinationContent()
        {
            // File.Replace throws PlatformNotSupportedException on non-Windows by .NET design -
            // self-skip there, matching this suite's established idiom.
            if (!System.OperatingSystem.IsWindows())
                return;

            string source = TempFilePath("source.txt");
            string destination = TempFilePath("destination.txt");
            File.WriteAllText(source, "new content");
            File.WriteAllText(destination, "old content");

            bool result = Fw.ReplaceFile(source, destination, null, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.Equal("new content", File.ReadAllText(destination));
        }

        [Fact]
        public void ReplaceFile_WithBackupPath_KeepsBackup()
        {
            if (!System.OperatingSystem.IsWindows())
                return;

            string source = TempFilePath("source.txt");
            string destination = TempFilePath("destination.txt");
            string backup = TempFilePath("backup.txt");
            File.WriteAllText(source, "new content");
            File.WriteAllText(destination, "old content");

            bool result = Fw.ReplaceFile(source, destination, backup, out string message);

            Assert.True(result);
            Assert.Equal("old content", File.ReadAllText(backup));
        }

        // --- ClaimFile ---

        [Theory]
        [InlineData(null, "dir")]
        [InlineData("source", null)]
        public void ClaimFile_NullArgs_ReturnsFalseWithMessage(string sourcePath, string inProgressDir)
        {
            bool result = Fw.ClaimFile(sourcePath, inProgressDir, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ClaimFile_SourceDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Fw.ClaimFile(TempFilePath(), TempSubdirectory(), out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ClaimFile_InProgressDirectoryDoesNotExist_ReturnsFalseWithMessage()
        {
            string source = TempFilePath("work.txt");
            File.WriteAllText(source, "work item");

            bool result = Fw.ClaimFile(source, Path.Combine(TempDir, "nope"), out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ClaimFile_HappyPath_MovesFileAndReportsClaimedPath()
        {
            string source = TempFilePath("work.txt");
            File.WriteAllText(source, "work item");
            string inProgressDir = TempSubdirectory("in-progress");

            bool result = Fw.ClaimFile(source, inProgressDir, out string claimedPath, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.False(File.Exists(source));
            Assert.Equal(Path.Combine(inProgressDir, "work.txt"), claimedPath);
            Assert.True(File.Exists(claimedPath));
        }

        [Fact]
        public void ClaimFile_DestinationCollision_ReturnsFalseWithoutOverwriting()
        {
            string source = TempFilePath("work.txt");
            File.WriteAllText(source, "new attempt");
            string inProgressDir = TempSubdirectory("in-progress");
            File.WriteAllText(Path.Combine(inProgressDir, "work.txt"), "already claimed by someone else");

            bool result = Fw.ClaimFile(source, inProgressDir, out string claimedPath, out string message);

            Assert.False(result);
            Assert.Null(claimedPath);
            Assert.Contains("already claimed", message);
            // The original claim must be untouched and the new attempt's source file preserved.
            Assert.Equal("already claimed by someone else", File.ReadAllText(Path.Combine(inProgressDir, "work.txt")));
            Assert.True(File.Exists(source));
        }

        [Fact]
        public async Task ClaimFile_ConcurrentClaimAttempts_ExactlyOneSucceeds()
        {
            // The standout concurrency test: two "robot instances" race to claim the same
            // work file. Exactly one must succeed; the other must get a clear collision
            // failure, never a silent overwrite or file corruption.
            string source = TempFilePath("racedwork.txt");
            File.WriteAllText(source, "contested work item");
            string inProgressDir = TempSubdirectory("in-progress");

            var results = new bool[2];
            var messages = new string[2];
            var claimedPaths = new string[2];

            var barrier = new System.Threading.Barrier(2);
            var t1 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                results[0] = Fw.ClaimFile(source, inProgressDir, out claimedPaths[0], out messages[0]);
            });
            var t2 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                results[1] = Fw.ClaimFile(source, inProgressDir, out claimedPaths[1], out messages[1]);
            });
            await Task.WhenAll(t1, t2);

            int successCount = results.Count(r => r);
            Assert.Equal(1, successCount);
            int failureIndex = results[0] ? 1 : 0;
            Assert.False(results[failureIndex]);
            Assert.Contains("already claimed", messages[failureIndex]);

            // Exactly one copy of the file exists in the in-progress directory - no
            // duplication, no corruption.
            string[] filesInProgress = Directory.GetFiles(inProgressDir);
            Assert.Single(filesInProgress);
            Assert.Equal("contested work item", File.ReadAllText(filesInProgress[0]));
        }
    }
}
