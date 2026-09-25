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
            // The source-scoped lock sidecar is a private implementation detail and must
            // not leak once the claim resolves.
            Assert.False(File.Exists(source + ".claiming"));
        }

        [Fact]
        public void ClaimFile_DestinationCollision_ReturnsFalseWithoutOverwriting()
        {
            // An unrelated file already occupying the destination name is a distinct
            // collision from losing the source-level claim race (see
            // ClaimFile_ConcurrentClaimAttempts_ExactlyOneSucceeds) - it gets its own
            // message rather than the "already claimed" wording, since no other
            // ClaimFile call is actually contending for this source.
            string source = TempFilePath("work.txt");
            File.WriteAllText(source, "new attempt");
            string inProgressDir = TempSubdirectory("in-progress");
            File.WriteAllText(Path.Combine(inProgressDir, "work.txt"), "already claimed by someone else");

            bool result = Fw.ClaimFile(source, inProgressDir, out string claimedPath, out string message);

            Assert.False(result);
            Assert.Null(claimedPath);
            Assert.Contains("already exists", message);
            // The original claim must be untouched and the new attempt's source file preserved.
            Assert.Equal("already claimed by someone else", File.ReadAllText(Path.Combine(inProgressDir, "work.txt")));
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(source + ".claiming"));
        }

        [Fact]
        public async Task ClaimFile_ConcurrentClaimAttempts_ExactlyOneSucceeds()
        {
            // The standout concurrency test: two "robot instances" race to claim the same
            // work file. Exactly one must succeed; the other must get a clear collision
            // failure, never a silent overwrite or file corruption.
            //
            // Repeated across several fresh iterations rather than a single race: a naive
            // implementation built on File.Move(src, dst, overwrite: false) previously
            // passed this same single-race shape essentially never (measured at ~0.5-1%
            // across 200 trials in an isolated, non-xunit repro on this exact host - a
            // TOCTOU race inside .NET's own File.Move implementation, not a guarantee the
            // OS fails to honor), so a single iteration is a weak regression guard against
            // that bug reappearing. ClaimFile now claims the destination via
            // FileMode.CreateNew, which the same repro showed reliably rejects one of the
            // two racers on every single trial.
            for (int iteration = 0; iteration < 20; iteration++)
            {
                string source = TempFilePath($"racedwork-{iteration}.txt");
                File.WriteAllText(source, "contested work item");
                string inProgressDir = TempSubdirectory($"in-progress-{iteration}");

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
                Assert.True(successCount == 1, $"Iteration {iteration}: expected exactly 1 success, got {successCount}.");
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

        [Fact]
        public void ClaimFile_ACallerThatWinsTheLockAfterTheWinnerFinished_ReportsAlreadyClaimed_NotACopyFailure()
        {
            // Deterministic version of the race the concurrent tests only hit by luck: the late caller has passed its
            // first existence check, and then the other caller completes its WHOLE claim (copy, delete the source,
            // release the lock) before the late caller reaches the lock. The lock is free again, so the late caller
            // acquires it - and must notice the source is gone instead of trying to copy a vanished file.
            string source = TempFilePath("late-claim.txt");
            File.WriteAllText(source, "contested work item");
            string winnerDirectory = TempSubdirectory("winner");
            string lateDirectory = TempSubdirectory("late");

            string winnerClaimed = null;
            bool winnerResult = false;
            Fw.BeforeClaimLock = () =>
            {
                Fw.BeforeClaimLock = null;   // only the late caller's own call is interrupted
                winnerResult = Fw.ClaimFile(source, winnerDirectory, out winnerClaimed, out _);
            };

            bool lateResult = Fw.ClaimFile(source, lateDirectory, out string lateClaimed, out string lateMessage);

            Assert.True(winnerResult);
            Assert.False(lateResult);
            Assert.Null(lateClaimed);
            Assert.Equal($"Source file '{source}' does not exist, or was already claimed by another instance.", lateMessage);
            // Exactly one copy of the work item exists, in the winner's directory; nothing was left behind by the late caller.
            Assert.True(File.Exists(winnerClaimed));
            Assert.Empty(Directory.GetFiles(lateDirectory));
            Assert.False(File.Exists(source));
            Assert.False(File.Exists(source + ".claiming"));
        }

        [Fact]
        public async Task ClaimFile_ConcurrentClaimAttemptsToDifferentDirectories_ExactlyOneSucceeds()
        {
            // Regression test for a real gap in an earlier version of ClaimFile: exclusivity
            // was scoped to the caller-chosen destination path, not the shared source, so two
            // callers racing to claim the SAME source into two DIFFERENT in-progress
            // directories computed two different destinations and could both "succeed" -
            // duplicating the work item, the exact outcome this method exists to prevent.
            // ClaimFile now locks on the source itself first, which must catch this race
            // regardless of where each caller intends to put the result.
            for (int iteration = 0; iteration < 20; iteration++)
            {
                string source = TempFilePath($"racedwork-crossdir-{iteration}.txt");
                File.WriteAllText(source, "contested work item");
                string inProgressDirA = TempSubdirectory($"in-progress-a-{iteration}");
                string inProgressDirB = TempSubdirectory($"in-progress-b-{iteration}");

                var results = new bool[2];
                var messages = new string[2];
                var claimedPaths = new string[2];

                var barrier = new System.Threading.Barrier(2);
                var t1 = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    results[0] = Fw.ClaimFile(source, inProgressDirA, out claimedPaths[0], out messages[0]);
                });
                var t2 = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    results[1] = Fw.ClaimFile(source, inProgressDirB, out claimedPaths[1], out messages[1]);
                });
                await Task.WhenAll(t1, t2);

                int successCount = results.Count(r => r);
                Assert.True(successCount == 1, $"Iteration {iteration}: expected exactly 1 success, got {successCount}.");
                int failureIndex = results[0] ? 1 : 0;
                Assert.False(results[failureIndex]);
                Assert.Contains("already claimed", messages[failureIndex]);

                // The work item must exist exactly once, total, across both candidate
                // directories - not duplicated into both.
                int totalFiles = Directory.GetFiles(inProgressDirA).Length + Directory.GetFiles(inProgressDirB).Length;
                Assert.Equal(1, totalFiles);
            }
        }
    }
}
