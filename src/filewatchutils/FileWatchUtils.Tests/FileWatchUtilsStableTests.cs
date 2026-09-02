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
        public async Task WaitForFileStable_ActivelyBeingWritten_DoesNotReportStableUntilWritesStop()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "start");

            // Append every 50ms for ~300ms, then stop. stableDurationMs (200) is longer than
            // the append interval, so the method must not report stable while writes continue.
            var writer = Task.Run(async () =>
            {
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(50);
                    File.AppendAllText(path, "x");
                }
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool result = Fw.WaitForFileStable(path, 200, 5000, 20, out bool timedOut, out string message);
            sw.Stop();

            await writer;
            Assert.True(result);
            Assert.False(timedOut);
            Assert.Null(message);
            // Must not have reported stable before the writer actually finished (~300ms) plus
            // the stable window (200ms) - allow generous slack for CI scheduling jitter.
            Assert.True(sw.ElapsedMilliseconds >= 300, $"Reported stable too early, after {sw.ElapsedMilliseconds}ms.");
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
