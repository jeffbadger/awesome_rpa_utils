using System;
using System.Collections.Generic;
using Exerciser.Menus;
using EventLogAutomation;
using OcrAutomation;
using ResourceLockAutomation;
using ServiceAutomation;
using SessionAutomation;
using TerminalAutomation;

namespace Exerciser
{
    internal static class Program
    {
        private static void Main()
        {
            Console.WriteLine("Awesome RPA Utils - Exerciser");
            Console.WriteLine("A menu-driven console app for interactively exercising host-dependent,");
            Console.WriteLine("non-UI-shaped Utils components against TESTING.md's documented cases.");

            using SessionUtils session = new SessionUtils();
            using OcrUtils ocr = new OcrUtils();
            using TerminalUtils terminal = new TerminalUtils();
            using ServiceUtils service = new ServiceUtils();
            using EventLogUtils eventLog = new EventLogUtils();
            using ResourceLockUtils resourceLock = new ResourceLockUtils();

            var components = new List<(string Name, MenuItem[] Items)>
            {
                ("Setup / Cleanup", SetupMenu.Build(eventLog)),
                ("SessionUtils", SessionMenu.Build(session)),
                ("OcrUtils", OcrMenu.Build(ocr)),
                ("TerminalUtils", TerminalMenu.Build(terminal)),
                ("ServiceUtils", ServiceMenu.Build(service)),
                ("EventLogUtils", EventLogMenu.Build(eventLog)),
                ("ResourceLockUtils", ResourceLockMenu.Build(resourceLock))
            };

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Components:");
                for (int i = 0; i < components.Count; i++)
                {
                    Console.WriteLine($"  [{i + 1}] {components[i].Name}");
                }
                Console.WriteLine("  [0] Exit");
                Console.Write("> ");
                string choice = Console.ReadLine();
                if (choice == "0" || string.IsNullOrWhiteSpace(choice))
                {
                    return;
                }

                if (!int.TryParse(choice, out int index) || index < 1 || index > components.Count)
                {
                    Console.WriteLine("Not a valid choice.");
                    continue;
                }

                RunComponentMenu(components[index - 1].Name, components[index - 1].Items);
            }
        }

        private static void RunComponentMenu(string name, MenuItem[] items)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine($"{name}:");
                for (int i = 0; i < items.Length; i++)
                {
                    Console.WriteLine($"  [{i + 1}] {items[i].Label} - {items[i].Hint}");
                }
                Console.WriteLine("  [0] Back");
                Console.Write("> ");
                string choice = Console.ReadLine();
                if (choice == "0" || string.IsNullOrWhiteSpace(choice))
                {
                    return;
                }

                if (!int.TryParse(choice, out int index) || index < 1 || index > items.Length)
                {
                    Console.WriteLine("Not a valid choice.");
                    continue;
                }

                try
                {
                    items[index - 1].Invoke();
                }
                catch (PromptCancelledException)
                {
                    // Console input hit EOF (redirected/closed stdin) mid-prompt.
                    // Return to the component list, whose own "> " prompt already
                    // treats a null read the same way (string.IsNullOrWhiteSpace)
                    // and exits cleanly, instead of looping forever here.
                    return;
                }
                catch (Exception ex)
                {
                    // These components are documented never-throws, so an actual
                    // exception here is itself worth reporting, not just a bug in
                    // this menu's input handling.
                    Console.WriteLine($"Unexpected exception (these components are documented never-throws - this itself may be worth reporting):");
                    Console.WriteLine(ex);
                }
            }
        }
    }
}
