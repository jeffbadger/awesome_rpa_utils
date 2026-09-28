using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace TestHarness
{
    /// <summary>
    /// Simulates a second process that owns the clipboard, for ClipboardUtils
    /// scenarios that need a real, separate clipboard owner (SaveClipboard/
    /// RestoreClipboard against a rich clipboard, PasteText into a focused
    /// window in another process, a hung/lazy-rendering owner). This mode never
    /// calls ClipboardUtils itself - it only creates the clipboard-owner
    /// conditions a real ClipboardUtils-driven automation reacts to.
    ///
    /// Usage: test-harness.exe --clipboard-owner [--text=T] [--html=H] [--rtf=R]
    ///   [--image=path] [--files=path1;path2] [--custom-format=name:payload]
    ///   [--hang] [--render-on-demand]
    ///        test-harness.exe --focus-textbox
    /// </summary>
    internal static class ClipboardOwnerMode
    {
        internal static void Run(Dictionary<string, string> options)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool hang = options.ContainsKey("hang");
            bool renderOnDemand = options.ContainsKey("render-on-demand");

            if (hang || renderOnDemand)
            {
                RunNativeOwner(options, hang);
                return;
            }

            RunManagedOwner(options);
        }

        internal static void RunFocusTextBox(Dictionary<string, string> options)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var form = new Form
            {
                Text = "Clipboard Target",
                Width = 320,
                Height = 140,
                StartPosition = FormStartPosition.CenterScreen
            };
            var textBox = new TextBox
            {
                Name = "txtClipboardTarget",
                Location = new Point(20, 20),
                Width = 260
            };
            form.Controls.Add(textBox);
            form.Shown += (s, e) =>
            {
                form.Activate();
                textBox.Focus();
            };
            Application.Run(form);
        }

        // The default, eager-rendering path: build a DataObject with every
        // requested format and hand it to the managed Clipboard API, which
        // flushes it immediately (copy: true) so it survives this process exiting.
        private static void RunManagedOwner(Dictionary<string, string> options)
        {
            var data = new DataObject();

            if (options.TryGetValue("text", out var text))
            {
                data.SetData(DataFormats.UnicodeText, text);
            }
            if (options.TryGetValue("html", out var html))
            {
                data.SetData(DataFormats.Html, html);
            }
            if (options.TryGetValue("rtf", out var rtf))
            {
                data.SetData(DataFormats.Rtf, rtf);
            }
            if (options.TryGetValue("image", out var imagePath) && File.Exists(imagePath))
            {
                using var image = Image.FromFile(imagePath);
                data.SetImage(image);
            }
            if (options.TryGetValue("files", out var filesArg))
            {
                var files = new StringCollection();
                files.AddRange(filesArg.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                data.SetFileDropList(files);
            }
            if (options.TryGetValue("custom-format", out var customFormat))
            {
                int sep = customFormat.IndexOf(':');
                if (sep > 0)
                {
                    string formatName = customFormat.Substring(0, sep);
                    string payload = customFormat.Substring(sep + 1);
                    data.SetData(formatName, payload);
                }
            }

            Clipboard.SetDataObject(data, copy: true);

            RunUntilEnterPressed();
        }

        // The lazy-rendering path: registers a placeholder for every requested
        // format via delayed rendering (SetClipboardData(format, IntPtr.Zero)),
        // then either renders it on demand when WM_RENDERFORMAT arrives
        // (--render-on-demand) or never answers it at all (--hang), per
        // TESTING.md's "hung owner"/"renders on demand" ClipboardUtils cases.
        private static void RunNativeOwner(Dictionary<string, string> options, bool hang)
        {
            using var owner = new NativeClipboardOwnerWindow(options, hang);
            owner.Claim();
            RunUntilEnterPressed();
        }

        // Blocks the process (pumping the WinForms message loop, so WM_RENDERFORMAT
        // and similar messages keep flowing) until the tester presses Enter in the
        // console. Keeps this process - and its clipboard ownership - alive for as
        // long as a real ClipboardUtils-driven automation needs to interact with it.
        private static void RunUntilEnterPressed()
        {
            using var context = new ApplicationContext();
            var exitThread = new Thread(() =>
            {
                Console.WriteLine("Clipboard owner running. Press Enter to exit.");
                Console.ReadLine();
                context.ExitThread();
            })
            {
                IsBackground = true
            };
            exitThread.Start();
            Application.Run(context);
        }
    }
}
