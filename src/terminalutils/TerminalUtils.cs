using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace TerminalAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component for terminal-style console applications that
    /// expose text poorly through normal UI automation - reading a target process's live
    /// console screen buffer, waiting for a prompt or a screen change, injecting keystrokes,
    /// and starting/attaching to a console process.
    /// <para>
    /// Like every component in this suite, all methods honor the never-throws contract:
    /// invalid input (a non-positive process ID, a negative timeout) and runtime failures
    /// (a process with no console, an already-exited process, insufficient rights) return
    /// <c>false</c> with a descriptive message instead of throwing.
    /// </para>
    /// <para>
    /// Every method that touches a target console is a self-contained attach -&gt; act -&gt;
    /// detach transaction (see <see cref="ConsoleAttachScope"/>) - this component never
    /// exposes a persisted "currently attached" console as state across separate method
    /// calls.
    /// </para>
    /// </summary>
    [Description("Reads and interacts with a target process's live console screen buffer: " +
                 "visible-screen capture, cursor position, waiting for a prompt or a screen " +
                 "change, keystroke injection, and starting/attaching to a console process. " +
                 "All methods return True/False with a failure message instead of throwing. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class TerminalUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public TerminalUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public TerminalUtils(IContainer container)
        {
            container?.Add(this);
        }

        #region Attach Lifecycle

        /// <summary>
        /// Checks whether the calling process can currently attach to the given process's
        /// console, via a real attach/detach cycle - there is no separate Win32 query for
        /// "does this process have a console" short of actually attempting it. Never throws.
        /// </summary>
        /// <param name="processId">The target process ID.</param>
        /// <param name="attachable"><c>true</c> if the attach/detach cycle succeeded.</param>
        /// <param name="message"><c>null</c> when the query itself succeeded (whether or not the target turned out to be attachable); otherwise a human-readable reason the query failed. When <paramref name="attachable"/> is <c>false</c>, this carries the underlying attach failure reason as diagnostic context, not an error.</param>
        /// <returns><c>true</c> if the attachability check completed; <c>false</c> only for invalid input or an unexpected failure performing the check itself. Never throws.</returns>
        [Category("Terminal - Attach")]
        [Description("Checks whether the calling process can attach to the given process's console. Never throws.")]
        public bool IsConsoleAttachable(int processId, out bool attachable, out string message)
        {
            attachable = default;
            message = default;
            try
            {
                if (processId <= 0)
                {
                    message = "processId must be a positive process ID.";
                    return false;
                }

                if (ConsoleAttachScope.TryAttach(processId, out ConsoleAttachScope scope, out string attachMessage))
                {
                    scope.Dispose();
                    attachable = true;
                    message = null;
                    return true;
                }

                attachable = false;
                message = attachMessage;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsConsoleAttachable", ex);
                return false;
            }
        }

        /// <summary>
        /// Starts a new process with its own real console window (not hidden, not
        /// redirected), so it can be attached to afterward via the other methods in this
        /// component. Never throws.
        /// </summary>
        /// <param name="fileName">The executable to launch.</param>
        /// <param name="arguments">Command-line arguments. Null/empty means none.</param>
        /// <param name="workingDirectory">The working directory. Null/empty uses the calling process's own.</param>
        /// <param name="processId">The new process's ID on success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Terminal - Attach")]
        [Description("Starts a new process with its own real console window, for later attachment. Never throws.")]
        public bool StartConsoleProcess(string fileName, string arguments, string workingDirectory, out int processId, out string message)
        {
            processId = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    message = "A file name is required.";
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                    startInfo.WorkingDirectory = workingDirectory;

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        message = $"Failed to start '{fileName}'.";
                        return false;
                    }
                    processId = process.Id;
                    message = null;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("StartConsoleProcess", ex);
                return false;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// Gets the cursor's row and column within the console's visible viewport. Both are
        /// 0-based and relative to the viewport's top-left corner - the same coordinate
        /// space as <see cref="ReadScreenRowsJson"/>'s row index - not the full scrollback
        /// buffer. Never throws.
        /// </summary>
        /// <param name="processId">The target process ID.</param>
        /// <param name="row">The cursor's 0-based row within the visible viewport.</param>
        /// <param name="column">The cursor's 0-based column within the visible viewport.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Terminal - Read")]
        [Description("Gets the cursor's row and column within the console's visible viewport. Never throws.")]
        public bool GetCursorPosition(int processId, out int row, out int column, out string message)
        {
            row = default;
            column = default;
            message = default;
            try
            {
                if (processId <= 0)
                {
                    message = "processId must be a positive process ID.";
                    return false;
                }

                if (!ConsoleAttachScope.TryAttach(processId, out ConsoleAttachScope scope, out message))
                    return false;

                using (scope)
                {
                    IntPtr stdOut = ConsoleInterop.GetStdHandle(ConsoleInterop.STD_OUTPUT_HANDLE);
                    if (stdOut == IntPtr.Zero || stdOut == new IntPtr(-1))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), "GetStdHandle(STD_OUTPUT_HANDLE) failed.").Message;
                        return false;
                    }

                    if (!ConsoleInterop.GetConsoleScreenBufferInfo(stdOut, out CONSOLE_SCREEN_BUFFER_INFO info))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), "GetConsoleScreenBufferInfo failed.").Message;
                        return false;
                    }

                    row = info.dwCursorPosition.Y - info.srWindow.Top;
                    column = info.dwCursorPosition.X - info.srWindow.Left;
                    message = null;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetCursorPosition", ex);
                return false;
            }
        }

        /// <summary>
        /// Reads the console's visible viewport as a JSON array of rows, each with its full
        /// text and a heuristic field split (runs of 2+ whitespace characters as column
        /// boundaries - see <see cref="FieldSplitting"/>, not true attribute-based field
        /// parsing). Never throws.
        /// </summary>
        /// <param name="processId">The target process ID.</param>
        /// <param name="json">A JSON array of <see cref="TerminalRowInfo"/> on success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Terminal - Read")]
        [Description("Reads the console's visible viewport as a JSON array of rows with a heuristic field split. Never throws.")]
        public bool ReadScreenRowsJson(int processId, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (processId <= 0)
                {
                    message = "processId must be a positive process ID.";
                    return false;
                }

                if (!TryCaptureScreen(processId, out TerminalCell[][] rows, out _, out _, out message))
                    return false;

                var result = new List<TerminalRowInfo>(rows.Length);
                for (int i = 0; i < rows.Length; i++)
                {
                    TerminalCell[] trimmed = TrimTrailingBlank(rows[i]);
                    string text = AnsiReconstruction.Render(trimmed, preserveAnsi: false);
                    result.Add(new TerminalRowInfo
                    {
                        RowIndex = i,
                        Text = text,
                        Fields = FieldSplitting.SplitFields(text)
                    });
                }

                json = JsonSerializer.Serialize(result, TerminalJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ReadScreenRowsJson", ex);
                return false;
            }
        }

        #endregion

        #region Capture

        /// <summary>
        /// Captures the console's visible viewport as plain text, one line per row.
        /// <para>
        /// When <paramref name="preserveAnsi"/> is <c>true</c>, this RE-SYNTHESIZES ANSI SGR
        /// color codes from each cell's stored color attribute - it is not a literal capture
        /// of the target application's original escape sequences, which no longer exist by
        /// the time the console screen buffer is read (conhost/Windows Terminal has already
        /// fully interpreted them). See <see cref="AnsiReconstruction"/> for details.
        /// </para>
        /// Never throws.
        /// </summary>
        /// <param name="processId">The target process ID.</param>
        /// <param name="preserveAnsi"><c>true</c> to re-synthesize ANSI color codes from each cell's stored color attribute; <c>false</c> for plain text with no color information.</param>
        /// <param name="text">The captured screen text on success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Terminal - Capture")]
        [Description("Captures the console's visible viewport as plain text, optionally with re-synthesized ANSI color codes. Never throws.")]
        public bool CaptureScreenText(int processId, bool preserveAnsi, out string text, out string message)
        {
            text = default;
            message = default;
            try
            {
                if (processId <= 0)
                {
                    message = "processId must be a positive process ID.";
                    return false;
                }

                if (!TryCaptureScreen(processId, out TerminalCell[][] rows, out _, out _, out message))
                    return false;

                var lines = new string[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                    lines[i] = AnsiReconstruction.Render(TrimTrailingBlank(rows[i]), preserveAnsi);

                text = string.Join(Environment.NewLine, lines);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CaptureScreenText", ex);
                return false;
            }
        }

        #endregion

        #region Wait

        /// <summary>
        /// Polls the console's visible screen text until it contains <paramref name="pattern"/>
        /// (or matches it as a regular expression), or the timeout elapses. Never throws.
        /// </summary>
        /// <param name="processId">The target process ID.</param>
        /// <param name="pattern">The substring to look for, or a regular expression when <paramref name="useRegex"/> is <c>true</c>.</param>
        /// <param name="useRegex"><c>true</c> to treat <paramref name="pattern"/> as a regular expression; <c>false</c> for a plain, case-sensitive substring search.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds. Zero means check once, immediately.</param>
        /// <param name="pollIntervalMs">Delay between polls, in milliseconds. Must be positive.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed before the pattern was found.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure or timeout reason.</param>
        [Category("Terminal - Wait")]
        [Description("Polls the console's visible screen text until it matches a substring or regular expression, or the timeout elapses. Never throws.")]
        public bool WaitForScreenText(int processId, string pattern, bool useRegex, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (processId <= 0)
                {
                    message = "processId must be a positive process ID.";
                    return false;
                }
                if (string.IsNullOrEmpty(pattern))
                {
                    message = "A pattern is required.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must be zero or positive.";
                    return false;
                }
                if (pollIntervalMs <= 0)
                {
                    message = "pollIntervalMs must be positive.";
                    return false;
                }

                Regex regex = null;
                if (useRegex)
                {
                    try
                    {
                        regex = new Regex(pattern);
                    }
                    catch (ArgumentException ex)
                    {
                        message = $"'{pattern}' is not a valid regular expression: {ex.Message}";
                        return false;
                    }
                }

                var stopwatch = Stopwatch.StartNew();
                while (true)
                {
                    if (!CaptureScreenText(processId, preserveAnsi: false, out string screenText, out message))
                        return false;

                    bool matched = useRegex ? regex.IsMatch(screenText) : screenText.Contains(pattern, StringComparison.Ordinal);
                    if (matched)
                    {
                        message = null;
                        timedOut = false;
                        return true;
                    }

                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        timedOut = true;
                        message = $"Timed out after {timeoutMs}ms waiting for the console screen to match '{pattern}'.";
                        return false;
                    }

                    Thread.Sleep(RemainingSleep(pollIntervalMs, timeoutMs, stopwatch));
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForScreenText", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForScreenText"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("Terminal - Wait")]
        [Description("Polls the console's visible screen text until it matches a substring or regular expression, or the timeout elapses. Never throws.")]
        public bool WaitForScreenTextSimple(int processId, string pattern, bool useRegex, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForScreenText(processId, pattern, useRegex, timeoutMs, pollIntervalMs, out _, out message);
        }

        #endregion

        #region Change

        /// <summary>
        /// Captures the console's visible screen text as a baseline, then polls until a
        /// later capture differs from it, or the timeout elapses. A <paramref name="timeoutMs"/>
        /// of zero times out immediately, since no time has elapsed for the screen to
        /// change. Never throws.
        /// </summary>
        /// <param name="processId">The target process ID.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between polls, in milliseconds. Must be positive.</param>
        /// <param name="changed"><c>true</c> if the screen text changed before the timeout.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed with no observed change.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure or timeout reason.</param>
        [Category("Terminal - Change")]
        [Description("Polls until the console's visible screen text changes from its state at call time, or the timeout elapses. Never throws.")]
        public bool WaitForScreenChange(int processId, int timeoutMs, int pollIntervalMs, out bool changed, out bool timedOut, out string message)
        {
            changed = default;
            timedOut = default;
            message = default;
            try
            {
                if (processId <= 0)
                {
                    message = "processId must be a positive process ID.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must be zero or positive.";
                    return false;
                }
                if (pollIntervalMs <= 0)
                {
                    message = "pollIntervalMs must be positive.";
                    return false;
                }

                if (!CaptureScreenText(processId, preserveAnsi: false, out string baseline, out message))
                    return false;

                var stopwatch = Stopwatch.StartNew();
                while (true)
                {
                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        changed = false;
                        timedOut = true;
                        message = $"Timed out after {timeoutMs}ms waiting for the console screen to change.";
                        return false;
                    }

                    Thread.Sleep(RemainingSleep(pollIntervalMs, timeoutMs, stopwatch));

                    if (!CaptureScreenText(processId, preserveAnsi: false, out string current, out message))
                        return false;

                    if (!string.Equals(current, baseline, StringComparison.Ordinal))
                    {
                        changed = true;
                        timedOut = false;
                        message = null;
                        return true;
                    }
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForScreenChange", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForScreenChange"/>, without the <c>changed</c>/<c>timedOut</c> outputs. Never throws.</summary>
        [Category("Terminal - Change")]
        [Description("Polls until the console's visible screen text changes from its state at call time, or the timeout elapses. Never throws.")]
        public bool WaitForScreenChangeSimple(int processId, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForScreenChange(processId, timeoutMs, pollIntervalMs, out _, out _, out message);
        }

        #endregion

        #region Write

        /// <summary>
        /// Injects <paramref name="text"/> as keystrokes directly into the console's input
        /// buffer via <c>WriteConsoleInputW</c> - this works even if the console window is
        /// unfocused or minimized, unlike focus-dependent keyboard simulation. Each character
        /// is sent as a key-down/key-up pair carrying only a Unicode character (no virtual
        /// key code) - this is expected to work for typical console line-input readers, but
        /// has not been verified against every possible console application. Injected input
        /// is only consumed promptly if the target application is actually blocked on a
        /// console read call; a busy target will not echo instantly, which is normal console
        /// behavior, not a bug. Never throws.
        /// </summary>
        /// <param name="processId">The target process ID.</param>
        /// <param name="text">The text to inject. An empty string is a no-op success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Terminal - Write")]
        [Description("Injects text as keystrokes into the console's input buffer. Works even if the console window is unfocused or minimized. Never throws.")]
        public bool WriteText(int processId, string text, out string message)
        {
            message = default;
            try
            {
                if (processId <= 0)
                {
                    message = "processId must be a positive process ID.";
                    return false;
                }
                if (text == null)
                {
                    message = "text must not be null.";
                    return false;
                }
                if (text.Length == 0)
                {
                    message = null;
                    return true;
                }

                if (!ConsoleAttachScope.TryAttach(processId, out ConsoleAttachScope scope, out message))
                    return false;

                using (scope)
                {
                    IntPtr stdIn = ConsoleInterop.GetStdHandle(ConsoleInterop.STD_INPUT_HANDLE);
                    if (stdIn == IntPtr.Zero || stdIn == new IntPtr(-1))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), "GetStdHandle(STD_INPUT_HANDLE) failed.").Message;
                        return false;
                    }

                    var records = new INPUT_RECORD[text.Length * 2];
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        records[i * 2] = MakeKeyEvent(c, keyDown: true);
                        records[i * 2 + 1] = MakeKeyEvent(c, keyDown: false);
                    }

                    if (!ConsoleInterop.WriteConsoleInputW(stdIn, records, (uint)records.Length, out uint written))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), "WriteConsoleInputW failed.").Message;
                        return false;
                    }
                    if (written != records.Length)
                    {
                        message = $"Only {written} of {records.Length} input events were written.";
                        return false;
                    }

                    message = null;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WriteText", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="WriteText"/>, appending a trailing carriage return (the
        /// character a Windows console line reader treats as Enter) so a shell or REPL
        /// submits the line. Never throws.
        /// </summary>
        /// <param name="processId">The target process ID.</param>
        /// <param name="text">The line of text to inject, without its terminator.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Terminal - Write")]
        [Description("Same as WriteText, appending a trailing carriage return so a shell submits the line. Never throws.")]
        public bool WriteLine(int processId, string text, out string message)
        {
            return WriteText(processId, (text ?? string.Empty) + "\r", out message);
        }

        #endregion

        #region Internal Helpers

        private static INPUT_RECORD MakeKeyEvent(char character, bool keyDown)
        {
            return new INPUT_RECORD
            {
                EventType = INPUT_RECORD.KEY_EVENT,
                KeyEvent = new KEY_EVENT_RECORD
                {
                    bKeyDown = keyDown ? 1 : 0,
                    wRepeatCount = 1,
                    wVirtualKeyCode = 0,
                    wVirtualScanCode = 0,
                    UnicodeChar = character,
                    dwControlKeyState = 0
                }
            };
        }

        /// <summary>
        /// Attaches to <paramref name="processId"/>'s console, reads its full visible
        /// viewport, and returns it as a grid of cells plus the cursor position - the shared
        /// core behind every read/capture method. Retries once on a buffer-resize race
        /// between <c>GetConsoleScreenBufferInfo</c> and <c>ReadConsoleOutputW</c>.
        /// </summary>
        private static bool TryCaptureScreen(int processId, out TerminalCell[][] rows, out int cursorRow, out int cursorColumn, out string message)
        {
            rows = null;
            cursorRow = 0;
            cursorColumn = 0;
            message = null;

            if (!ConsoleAttachScope.TryAttach(processId, out ConsoleAttachScope scope, out message))
                return false;

            using (scope)
            {
                IntPtr stdOut = ConsoleInterop.GetStdHandle(ConsoleInterop.STD_OUTPUT_HANDLE);
                if (stdOut == IntPtr.Zero || stdOut == new IntPtr(-1))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "GetStdHandle(STD_OUTPUT_HANDLE) failed.").Message;
                    return false;
                }

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!ConsoleInterop.GetConsoleScreenBufferInfo(stdOut, out CONSOLE_SCREEN_BUFFER_INFO info))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), "GetConsoleScreenBufferInfo failed.").Message;
                        return false;
                    }

                    int width = info.srWindow.Right - info.srWindow.Left + 1;
                    int height = info.srWindow.Bottom - info.srWindow.Top + 1;
                    if (width <= 0 || height <= 0)
                    {
                        message = "The console's visible viewport has zero size.";
                        return false;
                    }

                    var buffer = new CHAR_INFO[width * height];
                    var bufferSize = new COORD { X = (short)width, Y = (short)height };
                    var bufferCoord = new COORD { X = 0, Y = 0 };
                    var readRegion = new SMALL_RECT { Left = info.srWindow.Left, Top = info.srWindow.Top, Right = info.srWindow.Right, Bottom = info.srWindow.Bottom };

                    if (!ConsoleInterop.ReadConsoleOutputW(stdOut, buffer, bufferSize, bufferCoord, ref readRegion))
                    {
                        // A resize between GetConsoleScreenBufferInfo and ReadConsoleOutputW
                        // is the one documented, retryable failure case here - retry once
                        // with fresh buffer info before giving up.
                        if (attempt == 0)
                            continue;

                        message = new Win32Exception(Marshal.GetLastWin32Error(), "ReadConsoleOutputW failed.").Message;
                        return false;
                    }

                    cursorRow = info.dwCursorPosition.Y;
                    cursorColumn = info.dwCursorPosition.X;

                    rows = new TerminalCell[height][];
                    for (int y = 0; y < height; y++)
                    {
                        var row = new TerminalCell[width];
                        for (int x = 0; x < width; x++)
                        {
                            CHAR_INFO ci = buffer[y * width + x];
                            var fg = (ConsoleColor)(ci.Attributes & 0x0F);
                            var bg = (ConsoleColor)((ci.Attributes >> 4) & 0x0F);
                            row[x] = new TerminalCell(ci.UnicodeChar, fg, bg);
                        }
                        rows[y] = row;
                    }

                    message = null;
                    return true;
                }

                message = "ReadConsoleOutputW failed after a retry (the console buffer may be resizing repeatedly).";
                return false;
            }
        }

        /// <summary>
        /// Trims trailing space cells (the console buffer's fill character for unused cells
        /// in each row) so captured text doesn't carry a wall of padding out to the
        /// viewport's full width.
        /// </summary>
        private static TerminalCell[] TrimTrailingBlank(TerminalCell[] row)
        {
            int end = row.Length;
            while (end > 0 && row[end - 1].Character == ' ')
                end--;
            if (end == row.Length)
                return row;

            var trimmed = new TerminalCell[end];
            Array.Copy(row, trimmed, end);
            return trimmed;
        }

        private static int RemainingSleep(int pollIntervalMs, int timeoutMs, Stopwatch stopwatch)
        {
            long remaining = timeoutMs - stopwatch.ElapsedMilliseconds;
            return (int)Math.Max(0, Math.Min(pollIntervalMs, remaining));
        }

        #endregion
    }
}
