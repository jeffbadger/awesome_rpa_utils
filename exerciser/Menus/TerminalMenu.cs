using TerminalAutomation;

namespace Exerciser.Menus
{
    /// <summary>
    /// TerminalUtils' menu. StartConsoleProcess launches its own target console
    /// process - no separate Setup needed (see the Setup/Cleanup menu). Every
    /// other method needs the processId it returns. Simple-suffixed wrapper
    /// overloads are omitted; their non-Simple siblings cover the same ground
    /// with more output.
    /// </summary>
    internal static class TerminalMenu
    {
        internal static MenuItem[] Build(TerminalUtils terminal)
        {
            return new[]
            {
                new MenuItem("IsConsoleAttachable", "Checks whether a process ID hosts an attachable console.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    bool ok = terminal.IsConsoleAttachable(processId, out bool attachable, out string message);
                    Report.Result(ok, message, ("attachable", attachable));
                }),
                new MenuItem("StartConsoleProcess", "Launches its own target console process, e.g. cmd.exe - use the returned processId for every other method below.", () =>
                {
                    string fileName = Prompt.String("File name", "cmd.exe");
                    string arguments = Prompt.String("Arguments", null);
                    string workingDirectory = Prompt.String("Working directory", null);
                    bool ok = terminal.StartConsoleProcess(fileName, arguments, workingDirectory, out int processId, out string message);
                    Report.Result(ok, message, ("processId", processId));
                }),
                new MenuItem("GetCursorPosition", "Needs a process ID from StartConsoleProcess above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    bool ok = terminal.GetCursorPosition(processId, out int row, out int column, out string message);
                    Report.Result(ok, message, ("row", row), ("column", column));
                }),
                new MenuItem("ReadScreenRowsJson", "Needs a process ID from StartConsoleProcess above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    bool ok = terminal.ReadScreenRowsJson(processId, out string json, out string message);
                    Report.Result(ok, message, ("json", json));
                }),
                new MenuItem("CaptureScreenText", "Needs a process ID from StartConsoleProcess above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    bool preserveAnsi = Prompt.Bool("Preserve ANSI codes", false);
                    bool ok = terminal.CaptureScreenText(processId, preserveAnsi, out string text, out string message);
                    Report.Result(ok, message, ("text", text));
                }),
                new MenuItem("WaitForScreenText", "Found-in-time case: WriteText/WriteLine matching text into the console partway through the wait.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    string pattern = Prompt.String("Pattern", "");
                    bool useRegex = Prompt.Bool("Use regex", false);
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    int pollIntervalMs = Prompt.Int("Poll interval ms", 500);
                    bool ok = terminal.WaitForScreenText(processId, pattern, useRegex, timeoutMs, pollIntervalMs, out bool timedOut, out string message);
                    Report.Result(ok, message, ("timedOut", timedOut));
                }),
                new MenuItem("WaitForScreenChange", "Found-in-time case: WriteText/WriteLine into the console partway through the wait.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    int pollIntervalMs = Prompt.Int("Poll interval ms", 500);
                    bool ok = terminal.WaitForScreenChange(processId, timeoutMs, pollIntervalMs, out bool changed, out bool timedOut, out string message);
                    Report.Result(ok, message, ("changed", changed), ("timedOut", timedOut));
                }),
                new MenuItem("WriteText", "Needs a process ID from StartConsoleProcess above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    string text = Prompt.String("Text", "echo hello");
                    bool ok = terminal.WriteText(processId, text, out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("WriteLine", "Needs a process ID from StartConsoleProcess above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    string text = Prompt.String("Text", "echo hello");
                    bool ok = terminal.WriteLine(processId, text, out string message);
                    Report.Result(ok, message);
                })
            };
        }
    }
}
