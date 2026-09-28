using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TerminalAutomation;

namespace Exerciser.Menus
{
    /// <summary>
    /// TerminalUtils' menu. "Launch independent console target" is the intended
    /// process-ID source for every method below except StartConsoleProcess
    /// itself - see both menu entries' hints for why. Simple-suffixed wrapper
    /// overloads are omitted; their non-Simple siblings cover the same ground
    /// with more output.
    /// </summary>
    internal static class TerminalMenu
    {
        // Tracks PIDs from "Launch independent console target" so they can be
        // cleaned up explicitly or on exit - CreateProcess closing its handles
        // only releases this process's reference to them, it does not terminate
        // the launched target, so without this every interactive session would
        // leave orphaned console windows behind.
        private static readonly List<int> _launchedProcessIds = new List<int>();
        private static bool _cleanupRegistered;

        internal static MenuItem[] Build(TerminalUtils terminal)
        {
            if (!_cleanupRegistered)
            {
                AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupLaunchedProcesses(quiet: true);
                _cleanupRegistered = true;
            }

            return new[]
            {
                new MenuItem("IsConsoleAttachable", "Checks whether a process ID hosts an attachable console.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    bool ok = terminal.IsConsoleAttachable(processId, out bool attachable, out string message);
                    Report.Result(ok, message, ("attachable", attachable));
                }),
                new MenuItem("StartConsoleProcess", "Launches a target console process, e.g. cmd.exe - exercises this method's own contract (arguments, return values, error handling) only. Since the exerciser is itself a console app, the launched process ATTACHES TO AND SHARES this console's screen AND input buffer (standard Windows behavior for a console-hosted parent) - the child shell and this menu's own prompts both read from the same stdin, so which one gets a given keystroke is a race. Use 'Launch independent console target' below for a processId to test the other operations against, not this one's.", () =>
                {
                    string fileName = Prompt.String("File name", "cmd.exe");
                    string arguments = Prompt.String("Arguments", null);
                    string workingDirectory = Prompt.String("Working directory", null);
                    bool ok = terminal.StartConsoleProcess(fileName, arguments, workingDirectory, out int processId, out string message);
                    Report.Result(ok, message, ("processId", processId));
                }),
                new MenuItem("Launch independent console target", "Starts cmd.exe (or another console app) with CREATE_NEW_CONSOLE, so it gets its own separate console window and input buffer instead of sharing this one - safe to use as the processId for every method below, matching what a non-console caller like Robot Studio gets from StartConsoleProcess itself. Tracked for cleanup - see 'Close all independent console targets' below.", () =>
                {
                    string fileName = Prompt.String("File name", "cmd.exe");
                    string arguments = Prompt.String("Arguments", null);
                    if (TryLaunchIndependentConsoleTarget(fileName, arguments ?? string.Empty, out int processId, out string error))
                    {
                        _launchedProcessIds.Add(processId);
                        Report.Result(true, null, ("processId", processId));
                    }
                    else
                    {
                        Report.Result(false, error);
                    }
                }),
                new MenuItem("Close all independent console targets", "Terminates every process launched by 'Launch independent console target' above that's still running. Also runs automatically when the exerciser exits, so this is only needed to clean up mid-session.", () =>
                {
                    CleanupLaunchedProcesses(quiet: false);
                }),
                new MenuItem("GetCursorPosition", "Needs a process ID from 'Launch independent console target' above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    bool ok = terminal.GetCursorPosition(processId, out int row, out int column, out string message);
                    Report.Result(ok, message, ("row", row), ("column", column));
                }),
                new MenuItem("ReadScreenRowsJson", "Needs a process ID from 'Launch independent console target' above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    bool ok = terminal.ReadScreenRowsJson(processId, out string json, out string message);
                    Report.Result(ok, message, ("json", json));
                }),
                new MenuItem("CaptureScreenText", "Needs a process ID from 'Launch independent console target' above.", () =>
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
                new MenuItem("WriteText", "Needs a process ID from 'Launch independent console target' above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    string text = Prompt.String("Text", "echo hello");
                    bool ok = terminal.WriteText(processId, text, out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("WriteLine", "Needs a process ID from 'Launch independent console target' above.", () =>
                {
                    int processId = Prompt.Int("Process ID", 0);
                    string text = Prompt.String("Text", "echo hello");
                    bool ok = terminal.WriteLine(processId, text, out string message);
                    Report.Result(ok, message);
                })
            };
        }

        private static void CleanupLaunchedProcesses(bool quiet)
        {
            if (_launchedProcessIds.Count == 0)
            {
                if (!quiet)
                {
                    Console.WriteLine("  No tracked console targets to close.");
                }
                return;
            }

            foreach (int pid in _launchedProcessIds)
            {
                try
                {
                    using Process process = Process.GetProcessById(pid);
                    if (!process.HasExited)
                    {
                        process.Kill();
                        if (!quiet)
                        {
                            Console.WriteLine($"  Closed process {pid}.");
                        }
                    }
                }
                catch (ArgumentException)
                {
                    // Already exited (e.g. the tester closed the window by hand) -
                    // GetProcessById throws for a PID that no longer exists.
                }
                catch (InvalidOperationException)
                {
                    // Exited between HasExited and Kill.
                }
            }

            _launchedProcessIds.Clear();
        }

        // Starts a process with its own genuinely separate console (CREATE_NEW_CONSOLE),
        // rather than System.Diagnostics.Process.Start's default of attaching to/sharing
        // the caller's existing one. ProcessStartInfo has no way to request this, so this
        // calls CreateProcess directly.
        private static bool TryLaunchIndependentConsoleTarget(string fileName, string arguments, out int processId, out string error)
        {
            processId = 0;
            error = null;

            var commandLine = new StringBuilder(arguments.Length > 0 ? $"\"{fileName}\" {arguments}" : $"\"{fileName}\"");
            var startupInfo = new STARTUPINFO();
            startupInfo.cb = Marshal.SizeOf<STARTUPINFO>();

            bool created = CreateProcess(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NEW_CONSOLE,
                IntPtr.Zero,
                null,
                ref startupInfo,
                out PROCESS_INFORMATION processInfo);

            if (!created)
            {
                error = $"CreateProcess failed (Win32 error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            processId = processInfo.dwProcessId;
            CloseHandle(processInfo.hProcess);
            CloseHandle(processInfo.hThread);
            return true;
        }

        private const uint CREATE_NEW_CONSOLE = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
