using TerminalAutomation;
using Xunit;

namespace TerminalAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for TerminalUtils' input guards - the paths that
    /// return before ever calling <see cref="ConsoleAttachScope.TryAttach"/> or any Win32
    /// console API. These run anywhere (including non-Windows CI shells).
    /// <para>
    /// Unlike <c>FileWatchUtils</c>/<c>ArchiveUtils</c>, this component's actual
    /// screen-buffer/attach/write behavior needs a real Windows console and cannot be
    /// exercised here - see <c>TESTING.md</c>'s TerminalUtils section for the manual test
    /// plan. Every test in this file asserts the EXACT guard message text, not just a
    /// <c>false</c> return, specifically so a test can't accidentally "pass" by reaching a
    /// deeper native-call failure instead of the intended early guard.
    /// </para>
    /// </summary>
    public class TerminalUtilsGuardTests
    {
        private readonly TerminalUtils _terminal = new TerminalUtils();

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void IsConsoleAttachable_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.IsConsoleAttachable(processId, out bool attachable, out string message);

            Assert.False(result);
            Assert.False(attachable);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StartConsoleProcess_NullOrWhitespaceFileName_ReturnsFalseWithGuardMessage(string fileName)
        {
            bool result = _terminal.StartConsoleProcess(fileName, "args", null, out int processId, out string message);

            Assert.False(result);
            Assert.Equal(0, processId);
            Assert.Equal("A file name is required.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GetCursorPosition_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.GetCursorPosition(processId, out int row, out int column, out string message);

            Assert.False(result);
            Assert.Equal(0, row);
            Assert.Equal(0, column);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ReadScreenRowsJson_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.ReadScreenRowsJson(processId, out string json, out string message);

            Assert.False(result);
            Assert.Null(json);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void CaptureScreenText_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.CaptureScreenText(processId, preserveAnsi: false, out string text, out string message);

            Assert.False(result);
            Assert.Null(text);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WaitForScreenText_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.WaitForScreenText(processId, "prompt>", useRegex: false, timeoutMs: 1000, pollIntervalMs: 100, out bool timedOut, out string message);

            Assert.False(result);
            Assert.False(timedOut);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForScreenText_NullOrEmptyPattern_ReturnsFalseWithGuardMessage(string pattern)
        {
            bool result = _terminal.WaitForScreenText(1, pattern, useRegex: false, timeoutMs: 1000, pollIntervalMs: 100, out bool timedOut, out string message);

            Assert.False(result);
            Assert.False(timedOut);
            Assert.Equal("A pattern is required.", message);
        }

        [Fact]
        public void WaitForScreenText_NegativeTimeout_ReturnsFalseWithGuardMessage()
        {
            bool result = _terminal.WaitForScreenText(1, "x", useRegex: false, timeoutMs: -1, pollIntervalMs: 100, out bool timedOut, out string message);

            Assert.False(result);
            Assert.Equal("timeoutMs must be zero or positive.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WaitForScreenText_NonPositivePollInterval_ReturnsFalseWithGuardMessage(int pollIntervalMs)
        {
            bool result = _terminal.WaitForScreenText(1, "x", useRegex: false, timeoutMs: 1000, pollIntervalMs: pollIntervalMs, out bool timedOut, out string message);

            Assert.False(result);
            Assert.Equal("pollIntervalMs must be positive.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WaitForScreenTextSimple_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.WaitForScreenTextSimple(processId, "x", useRegex: false, timeoutMs: 1000, pollIntervalMs: 100, out string message);

            Assert.False(result);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WaitForScreenChange_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.WaitForScreenChange(processId, timeoutMs: 1000, pollIntervalMs: 100, out bool changed, out bool timedOut, out string message);

            Assert.False(result);
            Assert.False(changed);
            Assert.False(timedOut);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Fact]
        public void WaitForScreenChange_NegativeTimeout_ReturnsFalseWithGuardMessage()
        {
            bool result = _terminal.WaitForScreenChange(1, timeoutMs: -1, pollIntervalMs: 100, out bool changed, out bool timedOut, out string message);

            Assert.False(result);
            Assert.Equal("timeoutMs must be zero or positive.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WaitForScreenChange_NonPositivePollInterval_ReturnsFalseWithGuardMessage(int pollIntervalMs)
        {
            bool result = _terminal.WaitForScreenChange(1, timeoutMs: 1000, pollIntervalMs: pollIntervalMs, out bool changed, out bool timedOut, out string message);

            Assert.False(result);
            Assert.Equal("pollIntervalMs must be positive.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WaitForScreenChangeSimple_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.WaitForScreenChangeSimple(processId, timeoutMs: 1000, pollIntervalMs: 100, out string message);

            Assert.False(result);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WriteText_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.WriteText(processId, "hello", out string message);

            Assert.False(result);
            Assert.Equal("processId must be a positive process ID.", message);
        }

        [Fact]
        public void WriteText_NullText_ReturnsFalseWithGuardMessage()
        {
            bool result = _terminal.WriteText(1, null, out string message);

            Assert.False(result);
            Assert.Equal("text must not be null.", message);
        }

        [Fact]
        public void WriteText_EmptyText_SucceedsAsNoOp_WithoutTouchingAnyConsole()
        {
            // The empty-string short-circuit happens before ConsoleAttachScope.TryAttach is
            // ever called, so this succeeds even though process ID 1 is not a real,
            // attachable console target on this test host.
            bool result = _terminal.WriteText(1, "", out string message);

            Assert.True(result);
            Assert.Null(message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WriteLine_NonPositiveProcessId_ReturnsFalseWithGuardMessage(int processId)
        {
            bool result = _terminal.WriteLine(processId, "hello", out string message);

            Assert.False(result);
            Assert.Equal("processId must be a positive process ID.", message);
        }
    }
}
