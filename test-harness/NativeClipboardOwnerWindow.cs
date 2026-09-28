using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TestHarness
{
    /// <summary>
    /// A minimal native clipboard owner window using delayed rendering (a NULL
    /// hMem placeholder per format via SetClipboardData), so WM_RENDERFORMAT
    /// handling can be controlled directly - either rendering the real data on
    /// demand, or never answering it at all, per ClipboardUtils' TESTING.md
    /// "hung owner"/"renders on demand" cases. Only text/HTML/RTF/custom-format
    /// payloads are supported here (image/file-list formats need the managed
    /// eager path - see ClipboardOwnerMode.RunManagedOwner).
    /// </summary>
    internal sealed class NativeClipboardOwnerWindow : NativeWindow, IDisposable
    {
        private const int WM_RENDERFORMAT = 0x0305;
        private const int WM_RENDERALLFORMATS = 0x0306;

        private const uint CF_TEXT = 1;
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        private readonly Dictionary<uint, Func<IntPtr>> _renderers = new Dictionary<uint, Func<IntPtr>>();
        private readonly Dictionary<string, string> _options;
        private readonly bool _hang;

        internal NativeClipboardOwnerWindow(Dictionary<string, string> options, bool hang)
        {
            _options = options;
            _hang = hang;
            CreateHandle(new CreateParams { Caption = "TestHarnessClipboardOwner" });
        }

        internal void Claim()
        {
            string text = _options.TryGetValue("text", out var t) ? t : string.Empty;
            RegisterRenderer(CF_TEXT, () => RenderAnsiText(text));
            RegisterRenderer(CF_UNICODETEXT, () => RenderUnicodeText(text));

            if (_options.TryGetValue("html", out var html))
            {
                RegisterRenderer(RegisterClipboardFormat("HTML Format"), () => RenderUnicodeText(html));
            }
            if (_options.TryGetValue("rtf", out var rtf))
            {
                RegisterRenderer(RegisterClipboardFormat("Rich Text Format"), () => RenderUnicodeText(rtf));
            }
            if (_options.TryGetValue("custom-format", out var customFormat))
            {
                int sep = customFormat.IndexOf(':');
                if (sep > 0)
                {
                    string formatName = customFormat.Substring(0, sep);
                    string payload = customFormat.Substring(sep + 1);
                    RegisterRenderer(RegisterClipboardFormat(formatName), () => RenderUnicodeText(payload));
                }
            }

            if (!OpenClipboard(Handle))
            {
                throw new InvalidOperationException(
                    "Could not open the clipboard - another application is holding it.");
            }
            try
            {
                EmptyClipboard();
                foreach (uint formatId in _renderers.Keys)
                {
                    // NULL hMem is a delayed-rendering placeholder: Windows sends
                    // WM_RENDERFORMAT to this window only when another app asks
                    // for that specific format.
                    SetClipboardData(formatId, IntPtr.Zero);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        private void RegisterRenderer(uint formatId, Func<IntPtr> render)
        {
            _renderers[formatId] = render;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_RENDERFORMAT)
            {
                uint format = (uint)m.WParam.ToInt64();
                if (!_hang && _renderers.TryGetValue(format, out var render))
                {
                    // Per the Win32 delayed-rendering contract, the clipboard is
                    // already open while handling WM_RENDERFORMAT - call
                    // SetClipboardData directly, no Open/CloseClipboard here.
                    SetClipboardData(format, render());
                }
                // --hang: deliberately do nothing - the requesting app's
                // GetClipboardData call for this format is left unanswered.
                return;
            }

            if (m.Msg == WM_RENDERALLFORMATS && !_hang)
            {
                if (OpenClipboard(Handle))
                {
                    try
                    {
                        foreach (var entry in _renderers)
                        {
                            SetClipboardData(entry.Key, entry.Value());
                        }
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                return;
            }

            base.WndProc(ref m);
        }

        private static IntPtr RenderAnsiText(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
            return AllocGlobal(bytes);
        }

        private static IntPtr RenderUnicodeText(string text)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
            return AllocGlobal(bytes);
        }

        private static IntPtr AllocGlobal(byte[] bytes)
        {
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            IntPtr target = GlobalLock(hGlobal);
            Marshal.Copy(bytes, 0, target, bytes.Length);
            GlobalUnlock(hGlobal);
            return hGlobal;
        }

        public void Dispose()
        {
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);
    }
}
