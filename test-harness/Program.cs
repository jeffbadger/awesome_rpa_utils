using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TestHarness
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                var options = ParseArgs(args);
                switch (args[0].ToLowerInvariant())
                {
                    case "--clipboard-owner":
                        ClipboardOwnerMode.Run(options);
                        return;
                    case "--focus-textbox":
                        ClipboardOwnerMode.RunFocusTextBox(options);
                        return;
                }
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        internal static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                int eq = arg.IndexOf('=');
                if (eq >= 0)
                {
                    result[arg.Substring(2, eq - 2)] = arg.Substring(eq + 1);
                }
                else
                {
                    result[arg.Substring(2)] = "true";
                }
            }

            return result;
        }
    }
}
