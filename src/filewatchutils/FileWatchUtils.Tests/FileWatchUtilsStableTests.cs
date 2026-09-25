using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>
    /// Real functional tests for WaitForFileStable, the flagship method of this component -
    /// deserves the most scrutiny, not a trivial pass.
    /// </summary>
    public class FileWatchUtilsStableTests : TempDirectoryTestBase
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForFileStable_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.WaitForFileStable(path, 100, 1000, 20, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileStable_NegativeStableDuration_ReturnsFalseWithMessage()
        {
            bool result = Fw.WaitForFileStable(TempFilePath(), -1, 1000, 20, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileStable_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool result = Fw.WaitForFileStable(TempFilePath(), 100, -1, 20, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileStable_NonexistentFile_ReturnsFalseWithSpecificMessage_NotTimeout()
        {
            bool result = Fw.WaitForFileStable(TempFilePath(), 100, 1000, 20, out bool timedOut, out string message);

            Assert.False(result);
            Assert.False(timedOut);
            Assert.Contains("does not exist", message);
            Assert.Contains("WaitForFileToExist", message);
        }

        [Fact]
        public void WaitForFileStable_AlreadyStableAtCallTime_ReturnsTrueQuickly()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "never touched again");

            bool result = Fw.WaitForFileStable(path, 100, 5000, 20, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Null(message);
        }

        [Fact]
        public void WaitForFileStable_ActivelyBeingWritten_DoesNotReportStableUntilWritesStop()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "start");

            // Append every 50ms for ~300ms, then stop. stableDurationMs (200) is longer than
            // the append interval, so the method must not report stable while writes continue.
            //
            // The wait must start only once the writer is actually running: on a busy or cold machine
            // (a CI runner) the pool thread can start the writer more than 200ms late, and a file
            // nothing has touched for 200ms IS stable, so calling the wait immediately would
            // legitimately report stable before the first append. Sync on the first append instead of guessing.
            using var firstAppendDone = new ManualResetEventSlim(false);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long lastAppendMs = 0;
            // A dedicated thread, not the shared pool: a pool thread can be starved for hundreds of milliseconds.
            var writerThread = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 6; i++)
                {
                    System.Threading.Thread.Sleep(50);
                    File.AppendAllText(path, "x");
                    if (i == 0) firstAppendDone.Set();
                }
                lastAppendMs = clock.ElapsedMilliseconds;
            }) { IsBackground = true };
            writerThread.Start();
            Assert.True(firstAppendDone.Wait(System.TimeSpan.FromSeconds(10)), "the writer never started");

            bool result = Fw.WaitForFileStable(path, 200, 5000, 20, out bool timedOut, out string message);
            long reportedMs = clock.ElapsedMilliseconds;

            writerThread.Join();
            Assert.True(result);
            Assert.False(timedOut);
            Assert.Null(message);
            // Stable may only be reported after the writer's LAST append (the appends are 50ms apart,
            // well inside the 200ms window, so no earlier moment can have been quiet for 200ms).
            Assert.True(reportedMs >= lastAppendMs, $"Reported stable at {reportedMs}ms, before the last append at {lastAppendMs}ms.");
        }

        [Fact]
        public void WaitForFileStable_DeletedWhileWaiting_ReturnsFalseWithMessage()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "will vanish");
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                File.Delete(path);
            });

            bool result = Fw.WaitForFileStable(path, 500, 3000, 20, out bool timedOut, out string message);

            Assert.False(result);
            Assert.False(timedOut);
            Assert.Contains("deleted", message);
        }

        [Fact]
        public void WaitForFileStable_StableDurationLongerThanTimeout_TimesOutRatherThanHanging()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "content");

            bool result = Fw.WaitForFileStable(path, 5000, 150, 20, out bool timedOut, out string message);

            Assert.False(result);
            Assert.True(timedOut);
            Assert.Null(message);
        }

        [Fact]
        public void WaitForFileStableSimple_DelegatesToFullOverload()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "stable content");

            bool result = Fw.WaitForFileStableSimple(path, 50, 5000, 20, out string message);

            Assert.True(result);
            Assert.Null(message);
        }
    }
}
