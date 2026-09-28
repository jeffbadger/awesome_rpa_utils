using System;
using System.IO;

namespace Exerciser.Menus
{
    /// <summary>
    /// Disposable-resource Setup/Cleanup entries, one per component that needs
    /// one. SessionUtils and TerminalUtils need no fixture at all; their entries
    /// are print-only reminders of what TESTING.md still asks a human to trigger.
    /// </summary>
    internal static class SetupMenu
    {
        internal static MenuItem[] Build()
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
                })
            };
        }
    }
}
