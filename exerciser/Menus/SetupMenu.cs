using System;
using System.Diagnostics;
using System.IO;
using EventLogAutomation;

namespace Exerciser.Menus
{
    /// <summary>
    /// Disposable-resource Setup/Cleanup entries, one per component that needs
    /// one. SessionUtils and TerminalUtils need no fixture at all; their entries
    /// are print-only reminders of what TESTING.md still asks a human to trigger.
    /// </summary>
    internal static class SetupMenu
    {
        internal static MenuItem[] Build(EventLogUtils eventLog)
        {
            return new[]
            {
                new MenuItem("SessionUtils reminder", "No fixture to create - this component only reads/acts on sessions that already exist.", () =>
                {
                    Console.WriteLine("SessionUtils has no Setup/Cleanup fixture (TESTING.md: it only reads/acts on sessions that already exist).");
                    Console.WriteLine("To exercise its host-dependent paths by hand, trigger these yourself before using the SessionUtils menu:");
                    Console.WriteLine("  - Win+L to lock the workstation, for IsWorkstationLocked/WaitForWorkstationUnlocked");
                    Console.WriteLine("  - A UAC consent prompt, for IsInputDesktopAvailable's other 'unavailable' path");
                    Console.WriteLine("  - A second concurrent session (fast user switching or a second RDP connection), for GetSessionKind/GetSessionConnectState/GetSessionUser/IsSessionDisconnected against a session other than your own");
                    Console.WriteLine("  - GetCurrentSessionKind returning Rdp specifically needs an actual RDP session, not just the console");
                    Console.WriteLine("  - A real Windows service (Session 0), for IsRunningAsServiceSession/IsSessionInteractive's false path");
                }),
                new MenuItem("OcrUtils fixture", "Prints the path to the checked-in OCR fixture image.", () =>
                {
                    string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ocr-sample.png");
                    Console.WriteLine($"OCR fixture image: {fixturePath}");
                    Console.WriteLine("Known text: \"Hello Exerciser\", 400x120 white background, black text.");
                    Console.WriteLine("Exists: " + File.Exists(fixturePath));
                }),
                new MenuItem("TerminalUtils reminder", "No separate Setup needed.", () =>
                {
                    Console.WriteLine("TerminalUtils needs no separate Setup - StartConsoleProcess, in the TerminalUtils menu, launches its own target console process (e.g. cmd.exe).");
                }),
                new MenuItem("ServiceUtils: create ZZTestSvc", "Installs the disposable test service via 'sc create', pointed at NullService.exe. Never test ServiceUtils against a real system service.", () =>
                {
                    string defaultBinPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NullService", "bin", "Debug", "net10.0-windows", "NullService.exe");
                    string binPath = Prompt.String("Path to NullService.exe (build it first: dotnet build exerciser/NullService/NullService.csproj)", Path.GetFullPath(defaultBinPath));
                    if (!File.Exists(binPath))
                    {
                        Console.WriteLine($"No file found at {binPath} - build NullService.csproj first, or supply the correct path.");
                        return;
                    }
                    RunScCommand($"create ZZTestSvc binPath= \"{binPath}\" start= demand");
                }),
                new MenuItem("ServiceUtils: delete ZZTestSvc", "Stops and removes the disposable test service.", () =>
                {
                    RunScCommand("stop ZZTestSvc");
                    RunScCommand("delete ZZTestSvc");
                }),
                new MenuItem("EventLogUtils: create ZZTestEventLogUtils source", "Needs an elevated session. Never test EventLogUtils against Application/System/Security directly.", () =>
                {
                    string sourceName = Prompt.String("Source name", "ZZTestEventLogUtils");
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    bool ok = eventLog.CreateEventSourceSimple(sourceName, logName, out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("EventLogUtils: remove ZZTestEventLogUtils source", "Shells out to PowerShell's Remove-EventLog - the component itself has no programmatic delete.", () =>
                {
                    string sourceName = Prompt.String("Source name", "ZZTestEventLogUtils");
                    RunPowerShellCommand($"Remove-EventLog -Source '{sourceName}'");
                }),
                new MenuItem("ResourceLockUtils reminder", "Process scope needs no fixture. Machine scope needs a folder ACL this tool can't grant itself.", () =>
                {
                    Console.WriteLine("ResourceLockUtils' Process scope needs no setup at all - it's held in memory for this Robot Runtime.");
                    Console.WriteLine("Machine scope needs a lock folder (default C:\\ProgramData\\AwesomeRpaUtils\\Locks) where every robot");
                    Console.WriteLine("account that will contend for locks can list the folder, read its files, create files, and write to");
                    Console.WriteLine("the files it creates. Granting that ACL is an account-provisioning step outside any single process's");
                    Console.WriteLine("power, so it isn't automated here - see Documentation/ServerBots.md, then use the ResourceLockUtils");
                    Console.WriteLine("menu's ConfigureLockFolder/ValidateLockFolder entries to point at and check the folder.");
                })
            };
        }

        private static void RunPowerShellCommand(string command)
        {
            Console.WriteLine($"> powershell.exe -Command \"{command}\"");
            var startInfo = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using Process process = Process.Start(startInfo);
            process.WaitForExit();
            Console.WriteLine(process.StandardOutput.ReadToEnd());
            Console.WriteLine(process.StandardError.ReadToEnd());
            Console.WriteLine($"Exit code: {process.ExitCode}");
        }

        private static void RunScCommand(string arguments)
        {
            Console.WriteLine($"> sc.exe {arguments}");
            var startInfo = new ProcessStartInfo("sc.exe", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using Process process = Process.Start(startInfo);
            process.WaitForExit();
            Console.WriteLine(process.StandardOutput.ReadToEnd());
            Console.WriteLine(process.StandardError.ReadToEnd());
            Console.WriteLine($"Exit code: {process.ExitCode}");
        }
    }
}
