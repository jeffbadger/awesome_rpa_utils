using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>Real functional tests (real temp-directory files) for WaitForFileToExist/ToBeDeleted/ToChange.</summary>
    public class FileWatchUtilsExistenceTests : TempDirectoryTestBase
    {
        // --- WaitForFileToExist ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void WaitForFileToExist_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.WaitForFileToExist(path, 100, 10, out bool timedOut, out string message);

            Assert.False(result);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileToExist_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool result = Fw.WaitForFileToExist(TempFilePath(), -1, 10, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileToExist_FileAppearsBeforeTimeout_ReturnsTrue()
        {
            string path = TempFilePath();
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                File.WriteAllText(path, "hello");
            });

            bool result = Fw.WaitForFileToExist(path, 5000, 20, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Null(message);
        }

        [Fact]
        public void WaitForFileToExist_NeverAppears_TimesOut()
        {
            bool result = Fw.WaitForFileToExist(TempFilePath(), 150, 20, out bool timedOut, out string message);

            Assert.False(result);
            Assert.True(timedOut);
            Assert.Null(message);
        }

        [Fact]
        public void WaitForFileToExistSimple_DelegatesToFullOverload()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "already here");

            bool result = Fw.WaitForFileToExistSimple(path, 1000, 20, out string message);

            Assert.True(result);
            Assert.Null(message);
        }

        // --- WaitForFileToBeDeleted ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForFileToBeDeleted_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.WaitForFileToBeDeleted(path, 100, 10, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileToBeDeleted_AlreadyGone_ReturnsTrueImmediately()
        {
            bool result = Fw.WaitForFileToBeDeleted(TempFilePath(), 1000, 20, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Null(message);
        }

        [Fact]
        public void WaitForFileToBeDeleted_DeletedBeforeTimeout_ReturnsTrue()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "will be deleted");
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                File.Delete(path);
            });

            bool result = Fw.WaitForFileToBeDeleted(path, 5000, 20, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
        }

        [Fact]
        public void WaitForFileToBeDeleted_NeverDeleted_TimesOut()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "stays forever");

            bool result = Fw.WaitForFileToBeDeleted(path, 150, 20, out bool timedOut, out string message);

            Assert.False(result);
            Assert.True(timedOut);
        }

        [Fact]
        public void WaitForFileToBeDeletedSimple_DelegatesToFullOverload()
        {
            bool result = Fw.WaitForFileToBeDeletedSimple(TempFilePath(), 1000, 20, out string message);

            Assert.True(result);
            Assert.Null(message);
        }

        // --- WaitForFileToChange ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForFileToChange_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.WaitForFileToChange(path, 100, 10, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileToChange_NonexistentFile_ReturnsFalseWithMessage()
        {
            bool result = Fw.WaitForFileToChange(TempFilePath(), 1000, 20, out bool timedOut, out string message);

            Assert.False(result);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForFileToChange_SizeChangesBeforeTimeout_ReturnsTrue()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "initial");
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                File.AppendAllText(path, " more");
            });

            bool result = Fw.WaitForFileToChange(path, 5000, 20, out bool timedOut, out string message);

            Assert.True(result);
            Assert.False(timedOut);
            Assert.Null(message);
        }

        [Fact]
        public void WaitForFileToChange_DeletedCountsAsChange_ReturnsTrue()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "will vanish");
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                File.Delete(path);
            });

            bool result = Fw.WaitForFileToChange(path, 5000, 20, out bool timedOut, out _);

            Assert.True(result);
            Assert.False(timedOut);
        }

        [Fact]
        public void WaitForFileToChange_NoChange_TimesOut()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "static content");

            bool result = Fw.WaitForFileToChange(path, 150, 20, out bool timedOut, out string message);

            Assert.False(result);
            Assert.True(timedOut);
        }

        [Fact]
        public void WaitForFileToChangeSimple_DelegatesToFullOverload()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "content");

            bool result = Fw.WaitForFileToChangeSimple(path, 150, 20, out string message);

            Assert.False(result); // times out, no change
        }
    }
}
