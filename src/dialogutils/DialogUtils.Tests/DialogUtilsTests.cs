using System;
using DialogAutomation;
using Xunit;

namespace DialogAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for DialogUtils' pure logic, plus the
    /// never-throw contract exercised against handles that cannot exist on any OS
    /// (IntPtr.Zero). Live-dialog behavior (finding/clicking real windows) is
    /// covered by the Pega Unit Test plan in the repo's TESTING.md.
    /// </summary>
    public class DialogUtilsTests
    {
        private readonly DialogUtils _dialog = new DialogUtils();

        // --- StripAccessKeyMnemonic (internal, shared by FindButtonByText/ClickDialogButtonByText) ---

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("Yes", "Yes")]
        [InlineData("&Yes", "Yes")]
        [InlineData("&Yes &No", "Yes No")]
        [InlineData("Save && Exit", "Save & Exit")]
        [InlineData("&&", "&")]
        [InlineData("&&&a", "&a")] // literal '&' followed by an access key on 'a'
        [InlineData("Text &", "Text ")] // trailing lone '&': nothing to underline, dropped
        [InlineData("Mi&&sc", "Mi&sc")]
        public void StripAccessKeyMnemonic_HandlesMnemonicsAndEscapes(string input, string expected)
        {
            Assert.Equal(expected, DialogUtils.StripAccessKeyMnemonic(input));
        }

        // --- DialogButton must keep its Win32 control-ID values (IDOK..IDNO) ---

        [Fact]
        public void DialogButton_ValuesMatchWin32ControlIds()
        {
            Assert.Equal(1, (int)DialogButton.Ok);
            Assert.Equal(2, (int)DialogButton.Cancel);
            Assert.Equal(3, (int)DialogButton.Abort);
            Assert.Equal(4, (int)DialogButton.Retry);
            Assert.Equal(5, (int)DialogButton.Ignore);
            Assert.Equal(6, (int)DialogButton.Yes);
            Assert.Equal(7, (int)DialogButton.No);
        }

        // --- Null/empty pattern handling: reported as "not found", never a wildcard match ---

        [Fact]
        public void FindDialog_NullOrEmptyPattern_ReturnsFalseAndZeroedOutParams()
        {
            Assert.False(_dialog.FindDialog(null, out IntPtr hDialog, out bool canDismiss, exactMatch: false));
            Assert.Equal(IntPtr.Zero, hDialog);
            Assert.False(canDismiss);

            Assert.False(_dialog.FindDialog("", out hDialog, out canDismiss, exactMatch: false));
            Assert.Equal(IntPtr.Zero, hDialog);
            Assert.False(canDismiss);
        }

        [Fact]
        public void FindButtonByText_NullOrEmptyPattern_ReturnsFalseAndZeroHandle()
        {
            Assert.False(_dialog.FindButtonByText(IntPtr.Zero, out IntPtr hButton, null));
            Assert.Equal(IntPtr.Zero, hButton);

            Assert.False(_dialog.FindButtonByText(IntPtr.Zero, out hButton, "", exactMatch: false));
            Assert.Equal(IntPtr.Zero, hButton);
        }

        // --- Never-throw contract against handles that cannot exist ---
        // (Windows-only: these paths call through to user32.dll, which doesn't exist
        // on other OSes. `dotnet test` still runs the pure-logic tests everywhere.)

        [Fact]
        public void Methods_AcceptingNonsenseHandles_NeverThrowAndReportFailure()
        {
            if (!OperatingSystem.IsWindows()) return;

            IntPtr zero = IntPtr.Zero;

            Assert.False(_dialog.CanDismissDialog(zero));
            Assert.Empty(_dialog.ListDialogControls(zero));
            Assert.Equal(string.Empty, _dialog.GetDialogText(zero));
            Assert.Equal(string.Empty, _dialog.GetControlText(zero));
            Assert.False(_dialog.FindButtonById(zero, out IntPtr hButton, 1));
            Assert.Equal(IntPtr.Zero, hButton);
            Assert.False(_dialog.ClickDialogButtonById(zero, 1, out bool wasEnabled));
            Assert.False(wasEnabled);
        }

        [Fact]
        public void ClickButton_OnInvalidHandle_DoesNotThrowAndReturnsFalse()
        {
            if (!OperatingSystem.IsWindows()) return;

            // waitForEnabledMs: 0 skips the polling loop so the test stays fast.
            Assert.False(_dialog.ClickButton(IntPtr.Zero, waitForEnabledMs: 0));
        }

        [Fact]
        public void WaitForDialogToClose_ZeroHandle_ReportsAlreadyClosed()
        {
            if (!OperatingSystem.IsWindows()) return;

            // A zero handle is never a valid window, so "closed" is immediate.
            Assert.True(_dialog.WaitForDialogToClose(IntPtr.Zero, timeoutMs: 10, pollIntervalMs: 10));
        }

        // --- FindAllDialogs (new): empty pattern matches nothing without touching Win32 ---

        [Fact]
        public void FindAllDialogs_NullOrEmptyPattern_ReturnsEmptyList()
        {
            Assert.Empty(_dialog.FindAllDialogs(null));
            Assert.Empty(_dialog.FindAllDialogs("", exactMatch: false));
        }

        // --- WaitForDialog (new exactMatch param): empty pattern returns false immediately ---

        [Fact]
        public void WaitForDialog_NullOrEmptyPattern_ReturnsFalseAndZeroHandle()
        {
            // timeoutMs: 0 makes the first timeout check fire immediately, so no Win32
            // call is made and the test runs on any OS.
            Assert.False(_dialog.WaitForDialog(null, timeoutMs: 0, pollIntervalMs: 10, out IntPtr hWnd, exactMatch: false));
            Assert.Equal(IntPtr.Zero, hWnd);

            Assert.False(_dialog.WaitForDialog("", timeoutMs: 0, pollIntervalMs: 10, out hWnd, exactMatch: true));
            Assert.Equal(IntPtr.Zero, hWnd);
        }

        // --- ClickDialogButtonByText (new wait params): empty button text reports via message ---

        [Fact]
        public void ClickDialogButtonByText_NullOrEmptyButtonText_ReturnsFalseWithMessage()
        {
            Assert.False(_dialog.ClickDialogButtonByText(IntPtr.Zero, null, out bool wasEnabled, out string message));
            Assert.False(wasEnabled);
            Assert.False(string.IsNullOrEmpty(message));

            Assert.False(_dialog.ClickDialogButtonByText(IntPtr.Zero, "", out wasEnabled, out message, exactMatch: false));
            Assert.False(wasEnabled);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- HighlightControl (new GDI failure handling): invalid handle reports failure ---

        [Fact]
        public void HighlightControl_InvalidHandle_ReturnsFalse()
        {
            if (!OperatingSystem.IsWindows()) return;

            Assert.False(_dialog.HighlightControl(IntPtr.Zero, System.Drawing.Color.Red));
        }
    }
}
