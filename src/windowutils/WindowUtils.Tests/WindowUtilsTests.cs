using System;
using WindowAutomation;
using Xunit;

namespace WindowAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for WindowUtils' input guards, plus the
    /// never-throw contract on the paths that return before any Win32 call. These
    /// run anywhere (including non-Windows CI shells), so they deliberately avoid
    /// user32 P/Invoke paths — live window behavior (enumerate/move/close real
    /// windows) is covered by the Pega Unit Test plan in the repo's TESTING.md.
    /// </summary>
    public class WindowUtilsTests
    {
        private readonly WindowUtils _window = new WindowUtils();

        // --- FindWindowByTitle: null/empty title is "not found", never a wildcard match or exception ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void FindWindowByTitle_NullOrEmptyTitle_ReturnsZero(string title)
        {
            Assert.Equal(IntPtr.Zero, _window.FindWindowByTitle(title));
            Assert.Equal(IntPtr.Zero, _window.FindWindowByTitle(title, exactMatch: false));
        }

        // --- WaitForWindow: null/empty title refuses to poll (pointless — matches nothing) ---

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void WaitForWindow_NullOrEmptyTitle_ReturnsFalseWithZeroHandle(string title, bool _)
        {
            bool found = _window.WaitForWindow(title, timeoutMs: 5000, pollIntervalMs: 10, out IntPtr hWnd);

            Assert.False(found);
            Assert.Equal(IntPtr.Zero, hWnd);
        }

        // --- SetWindowBounds: negative dimensions rejected before any Win32 call, with a message ---

        [Theory]
        [InlineData(-1, 100)]
        [InlineData(100, -1)]
        [InlineData(-1, -1)]
        public void SetWindowBounds_NegativeDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            Assert.False(_window.SetWindowBounds(IntPtr.Zero, left: 10, top: 10, width, height, out string message));

            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- ShowWindowCommand must keep its Win32 SW_* values (ShowWindow receives the raw int) ---

        [Fact]
        public void ShowWindowCommand_ValuesMatchWin32Constants()
        {
            Assert.Equal(0, (int)ShowWindowCommand.Hide);        // SW_HIDE
            Assert.Equal(1, (int)ShowWindowCommand.Normal);      // SW_SHOWNORMAL
            Assert.Equal(3, (int)ShowWindowCommand.Maximized);   // SW_SHOWMAXIMIZED
            Assert.Equal(6, (int)ShowWindowCommand.Minimized);   // SW_MINIMIZE
            Assert.Equal(9, (int)ShowWindowCommand.Restore);     // SW_RESTORE
        }
    }
}