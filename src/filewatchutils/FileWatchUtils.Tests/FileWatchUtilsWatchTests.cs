using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
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

        // --- StartWatching guards: same shape as WatchForChange's own guards ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void StartWatching_NullOrEmptyDirectory_ReturnsFalseWithMessage(string directoryPath)
        {
            bool result = Fw.StartWatching(directoryPath, null, false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.False(Fw.IsWatching());
        }

        [Fact]
        public void StartWatching_DirectoryDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Fw.StartWatching(Path.Combine(TempDir, "nope"), null, false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.False(Fw.IsWatching());
        }

        // --- Start/Stop/IsWatching lifecycle ---

        [Fact]
        public void StartWatching_ReturnsImmediately()
        {
            var stopwatch = Stopwatch.StartNew();
            bool result = Fw.StartWatching(TempDir, null, false, out string message);
            stopwatch.Stop();

            Assert.True(result);
            Assert.Null(message);
            // The whole point of StartWatching vs. the blocking WatchForChange: it must
            // not wait around for an event. Generous bound to avoid CI flakiness while
            // still catching an accidental block (WatchForChange's own tests use
            // multi-second timeouts, so anything under ~1s clearly isn't blocking).
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"StartWatching took {stopwatch.ElapsedMilliseconds} ms - expected it not to block.");
        }

        [Fact]
        public void IsWatching_ReflectsLifecycle()
        {
            Assert.False(Fw.IsWatching());

            Assert.True(Fw.StartWatching(TempDir, null, false, out _));
            Assert.True(Fw.IsWatching());

            Assert.True(Fw.StopWatching(out _));
            Assert.False(Fw.IsWatching());
        }

        [Fact]
        public void StartWatching_AlreadyWatching_ReturnsFalseWithMessage()
        {
            Assert.True(Fw.StartWatching(TempDir, null, false, out _));

            bool result = Fw.StartWatching(TempDir, null, false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void StopWatching_NotCurrentlyWatching_ReturnsFalseWithMessage()
        {
            bool result = Fw.StopWatching(out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Real events fire on real filesystem changes ---

        [Fact]
        public void Created_FiresWithCorrectPath()
        {
            using var signal = new ManualResetEventSlim(false);
            string capturedPath = null;
            Fw.Created += (sender, e) => { capturedPath = e.FullPath; signal.Set(); };

            Assert.True(Fw.StartWatching(TempDir, null, false, out _));
            string filePath = TempFilePath("created.txt");
            File.WriteAllText(filePath, "hello");

            Assert.True(signal.Wait(5000));
            Assert.Equal(filePath, capturedPath);
        }

        [Fact]
        public void Deleted_FiresWithCorrectPath()
        {
            string filePath = TempFilePath("doomed.txt");
            File.WriteAllText(filePath, "will vanish");

            using var signal = new ManualResetEventSlim(false);
            string capturedPath = null;
            Fw.Deleted += (sender, e) => { capturedPath = e.FullPath; signal.Set(); };

            Assert.True(Fw.StartWatching(TempDir, null, false, out _));
            File.Delete(filePath);

            Assert.True(signal.Wait(5000));
            Assert.Equal(filePath, capturedPath);
        }

        [Fact]
        public void Changed_FiresWithCorrectPathAndKind()
        {
            string filePath = TempFilePath("data.txt");
            File.WriteAllText(filePath, "initial");

            using var signal = new ManualResetEventSlim(false);
            FileWatchChangeEventArgs captured = null;
            Fw.Changed += (sender, e) => { captured = e; signal.Set(); };

            Assert.True(Fw.StartWatching(TempDir, null, false, out _));
            File.AppendAllText(filePath, " more");

            Assert.True(signal.Wait(5000));
            Assert.Equal(filePath, captured.FullPath);
            Assert.Equal(FileChangeKind.Changed, captured.Kind);
        }

        [Fact]
        public void Renamed_FiresWithNewAndOldPath()
        {
            string oldPath = TempFilePath("before.txt");
            File.WriteAllText(oldPath, "content");
            string newPath = TempFilePath("after.txt");

            using var signal = new ManualResetEventSlim(false);
            FileWatchRenamedEventArgs captured = null;
            Fw.Renamed += (sender, e) => { captured = e; signal.Set(); };

            Assert.True(Fw.StartWatching(TempDir, null, false, out _));
            File.Move(oldPath, newPath);

            Assert.True(signal.Wait(5000));
            Assert.Equal(newPath, captured.FullPath);
            Assert.Equal(oldPath, captured.OldFullPath);
        }

        [Fact]
        public void StopWatching_SubsequentChangesDoNotFireEvents()
        {
            int callCount = 0;
            Fw.Created += (sender, e) => Interlocked.Increment(ref callCount);

            Assert.True(Fw.StartWatching(TempDir, null, false, out _));
            Assert.True(Fw.StopWatching(out _));

            File.WriteAllText(TempFilePath("after-stop.txt"), "should not be observed");
            Thread.Sleep(300); // give a wrongly-still-active watcher a chance to fire

            Assert.Equal(0, callCount);
        }

        [Fact]
        public void ThrowingHandler_DoesNotCrashAndDoesNotBlockLaterEvents()
        {
            using var secondSignal = new ManualResetEventSlim(false);
            int callCount = 0;
            Fw.Created += (sender, e) =>
            {
                Interlocked.Increment(ref callCount);
                throw new InvalidOperationException("deliberate test failure");
            };
            Fw.Created += (sender, e) =>
            {
                // A second, well-behaved subscriber on the same event must still run
                // even though the first one throws.
                secondSignal.Set();
            };

            Assert.True(Fw.StartWatching(TempDir, null, false, out _));
            File.WriteAllText(TempFilePath("first.txt"), "one");

            Assert.True(secondSignal.Wait(5000));
            Assert.Equal(1, callCount);

            // The process is still alive to reach this line at all - that's the real
            // assertion. Confirm the watch is still healthy for a second event too.
            secondSignal.Reset();
            File.WriteAllText(TempFilePath("second.txt"), "two");
            Assert.True(secondSignal.Wait(5000));
            Assert.Equal(2, callCount);
        }

        [Fact]
        public void StaleNativeError_FromReplacedWatcher_DoesNotAffectCurrentWatch()
        {
            // A real FileSystemWatcher buffer overflow isn't reliably triggerable on
            // demand, so this goes through the internal test seam instead - regression
            // coverage for a fix where a delayed Error callback from an
            // already-stopped-and-replaced watcher could incorrectly stop the new,
            // healthy watch and raise a misleading WatchError for it.
            Assert.True(Fw.StartWatching(TempDir, null, false, out _));

            using var staleWatcher = new FileSystemWatcher(TempDir);
            bool watchErrorFired = false;
            Fw.WatchError += (sender, e) => watchErrorFired = true;

            Fw.SimulateNativeErrorForTests(staleWatcher, new ErrorEventArgs(new IOException("stale watcher failure")));

            Assert.False(watchErrorFired);
            Assert.True(Fw.IsWatching());

            // The current watch must still be fully functional afterward.
            using var signal = new ManualResetEventSlim(false);
            Fw.Created += (sender, e) => signal.Set();
            File.WriteAllText(TempFilePath("still-alive.txt"), "content");
            Assert.True(signal.Wait(5000));
        }

        [Fact]
        public void StartWatching_AfterDispose_ReturnsFalseWithMessage()
        {
            var fw = new FileWatchUtils();
            fw.Dispose();

            bool result = fw.StartWatching(TempDir, null, false, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.False(fw.IsWatching());
        }

        [Fact]
        public void Changed_FiresForAttributeOnlyChange()
        {
            // Regression coverage for an explicit NotifyFilter: FileSystemWatcher's
            // default filter (LastWrite | FileName | DirectoryName) does not include
            // Attributes, so a pure attribute change with no content/LastWrite change
            // would otherwise never raise Changed, contradicting Changed's own
            // documented "content/attribute/timestamp change" coverage.
            string filePath = TempFilePath("attrs.txt");
            File.WriteAllText(filePath, "content");

            using var signal = new ManualResetEventSlim(false);
            Fw.Changed += (sender, e) => signal.Set();

            Assert.True(Fw.StartWatching(TempDir, null, false, out _));
            File.SetAttributes(filePath, FileAttributes.ReadOnly);

            try
            {
                Assert.True(signal.Wait(5000));
            }
            finally
            {
                // So TempDirectoryTestBase's recursive delete doesn't fail on a
                // still-read-only file.
                File.SetAttributes(filePath, FileAttributes.Normal);
            }
        }

        [Fact]
        public void Dispose_WhileWatching_CleansUpWithoutThrowing()
        {
            var fw = new FileWatchUtils();
            Assert.True(fw.StartWatching(TempDir, null, false, out _));

            fw.Dispose();

            Assert.False(fw.IsWatching());
        }
    }
}
