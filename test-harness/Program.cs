using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TestHarness
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--delayed-popup", StringComparison.OrdinalIgnoreCase))
            {
                RunDelayedPopupMode(ParseArgs(args));
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>
        /// Simulates a second process showing popups on demand, for InterruptUtils
        /// to dismiss from a different process (see TESTING.md's InterruptUtils
        /// section). This mode never calls InterruptUtils itself - it only creates
        /// the popup conditions a real InterruptUtils-driven automation reacts to.
        ///
        /// Usage: TestHarness.exe --delayed-popup [--title=T] [--message=M]
        ///   [--delay-ms=N] [--button=OK|OKCancel|YesNo|YesNoCancel|AbortRetryIgnore|RetryCancel]
        ///   [--repeat=N] [--interval-ms=N] [--no-button] [--delayed-button]
        /// </summary>
        private static void RunDelayedPopupMode(Dictionary<string, string> options)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string title = GetString(options, "title", "Test Harness Popup");
            string message = GetString(options, "message", "This is a delayed popup.");
            int delayMs = GetInt(options, "delay-ms", 3000);
            int repeat = GetInt(options, "repeat", 1);
            int intervalMs = GetInt(options, "interval-ms", delayMs);
            bool noButton = options.ContainsKey("no-button");
            bool delayedButton = options.ContainsKey("delayed-button");
            MessageBoxButtons buttons = ParseButtons(GetString(options, "button", "OK"));

            if (noButton && delayedButton)
            {
                Console.Error.WriteLine("--no-button and --delayed-button describe different popup shapes and cannot be combined.");
                return;
            }

            for (int i = 0; i < repeat; i++)
            {
                if (delayedButton)
                {
                    // ShowDelayedButtonPopup already waits delayMs after showing the
                    // window before adding the button - sleeping delayMs here too
                    // (as the other modes do) would double it. Skip the outer sleep
                    // on the first iteration; still wait intervalMs between repeats.
                    if (i > 0)
                    {
                        Thread.Sleep(intervalMs);
                    }
                    ShowDelayedButtonPopup(title, message, delayMs);
                    continue;
                }

                Thread.Sleep(i == 0 ? delayMs : intervalMs);

                if (noButton)
                {
                    ShowButtonlessPopup(title, message);
                }
                else
                {
                    MessageBox.Show(message, title, buttons);
                }
            }
        }

        // A button-less popup, for InterruptUtils' "a button-less window is closed
        // by a close rule" case - no child window has class "Button" at all.
        private static void ShowButtonlessPopup(string title, string message)
        {
            using var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Width = 320,
                Height = 140,
                StartPosition = FormStartPosition.CenterScreen
            };
            form.Controls.Add(new Label { Text = message, AutoSize = true, Location = new Point(20, 20) });
            form.ShowDialog();
        }

        // A popup whose window appears immediately but whose Button is only added a
        // moment later via a Timer tick, for InterruptUtils' "a form whose button is
        // created a moment after the window appears (not a #32770, so pass
        // className: '*')" case.
        private static void ShowDelayedButtonPopup(string title, string message, int buttonDelayMs)
        {
            using var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Width = 320,
                Height = 160,
                StartPosition = FormStartPosition.CenterScreen
            };
            form.Controls.Add(new Label { Text = message, AutoSize = true, Location = new Point(20, 20) });

            using var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, buttonDelayMs) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var okButton = new Button { Text = "OK", Location = new Point(110, 80), Width = 80 };
                okButton.Click += (s2, e2) => form.Close();
                form.Controls.Add(okButton);
            };
            timer.Start();

            form.ShowDialog();
        }

        private static MessageBoxButtons ParseButtons(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "okcancel" => MessageBoxButtons.OKCancel,
                "yesno" => MessageBoxButtons.YesNo,
                "yesnocancel" => MessageBoxButtons.YesNoCancel,
                "abortretryignore" => MessageBoxButtons.AbortRetryIgnore,
                "retrycancel" => MessageBoxButtons.RetryCancel,
                _ => MessageBoxButtons.OK
            };
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
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

        private static string GetString(Dictionary<string, string> options, string key, string defaultValue)
            => options.TryGetValue(key, out var value) ? value : defaultValue;

        private static int GetInt(Dictionary<string, string> options, string key, int defaultValue)
            => options.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
