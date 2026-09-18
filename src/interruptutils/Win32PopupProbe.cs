using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace InterruptAutomation
{
    /// <summary>
    /// The real window probe. It reads other applications' windows only with messages that
    /// carry a timeout and give up at once on a hung application, so a frozen popup can
    /// never stall the worker thread.
    /// </summary>
    internal sealed class Win32PopupProbe : IPopupProbe
    {
        private const int MaxTextChars = 4096;
        private const int ProcessNameCacheLimit = 256;

        private readonly object _cacheLock = new object();
        private readonly Dictionary<uint, string> _processNames = new Dictionary<uint, string>();

        public uint CurrentProcessId { get; } = (uint)Process.GetCurrentProcess().Id;

        public bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

        public PopupWindowInfo Describe(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindow(hwnd))
                return null;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return new PopupWindowInfo
            {
                ClassName = ClassNameOf(hwnd),
                Title = CaptionOf(hwnd),
                ProcessId = pid
            };
        }

        public string GetProcessName(uint processId)
        {
            if (processId == 0)
                return string.Empty;
            lock (_cacheLock)
            {
                if (_processNames.TryGetValue(processId, out string cached))
                    return cached;
            }

            string name = string.Empty;
            try
            {
                using (var process = Process.GetProcessById((int)processId))
                    name = process.ProcessName;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // The process ended, or is not one we may inspect.
            }

            lock (_cacheLock)
            {
                // Process IDs are reused, so a name is only trusted briefly: keep the cache small
                // and let it be dropped wholesale rather than tracking ages.
                if (_processNames.Count >= ProcessNameCacheLimit)
                    _processNames.Clear();
                if (name.Length > 0)
                    _processNames[processId] = name;
            }
            return name;
        }

        public string GetMessageText(IntPtr hwnd)
        {
            foreach (var child in ChildrenOf(hwnd))
            {
                if (!string.Equals(ClassNameOf(child), "Static", StringComparison.OrdinalIgnoreCase))
                    continue;
                string text = ReadText(child);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
            return string.Empty;
        }

        public IReadOnlyList<PopupButton> GetButtons(IntPtr hwnd)
        {
            var buttons = new List<PopupButton>();
            foreach (var child in ChildrenOf(hwnd))
            {
                if (!IsButtonClass(ClassNameOf(child)))
                    continue;
                buttons.Add(new PopupButton
                {
                    Handle = child,
                    Id = NativeMethods.GetDlgCtrlID(child),
                    Text = PopupRule.StripMnemonic(ReadText(child))
                });
            }
            return buttons;
        }

        public bool ClickButton(PopupButton button, int waitForEnabledMs)
        {
            int start = Environment.TickCount;
            bool enabled = NativeMethods.IsWindowEnabled(button.Handle);
            // A dead handle reads as disabled forever; stop waiting as soon as it is gone.
            while (!enabled && NativeMethods.IsWindow(button.Handle) && unchecked(Environment.TickCount - start) < waitForEnabledMs)
            {
                Thread.Sleep(25);
                enabled = NativeMethods.IsWindowEnabled(button.Handle);
            }

            NativeMethods.SendMessageTimeout(button.Handle, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG, NativeMethods.ClickTimeoutMs, out _);
            return enabled;
        }

        public void CloseWindow(IntPtr hwnd)
        {
            NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG, NativeMethods.ClickTimeoutMs, out _);
        }

        public IReadOnlyList<IntPtr> EnumerateTopLevelWindows()
        {
            var windows = new List<IntPtr>();
            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                if (NativeMethods.IsWindowVisible(hwnd))
                    windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        // A native Button, or a WinForms button-family control (WindowsForms10.BUTTON.app...),
        // which superclasses it and answers the same messages.
        internal static bool IsButtonClass(string className)
        {
            return !string.IsNullOrEmpty(className)
                && (className.Equals("Button", StringComparison.OrdinalIgnoreCase)
                    || className.IndexOf(".BUTTON.", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static List<IntPtr> ChildrenOf(IntPtr parent)
        {
            var children = new List<IntPtr>();
            // EnumChildWindows(NULL) enumerates every top-level window on the desktop; a zero
            // handle must mean "no popup, so no controls".
            if (parent == IntPtr.Zero)
                return children;
            NativeMethods.EnumChildWindows(parent, (hwnd, lParam) =>
            {
                children.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return children;
        }

        private static string ClassNameOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// A window's caption via <c>GetWindowText</c>, which never sends a message to the
        /// owning application and so never blocks on it.
        /// </summary>
        private static string CaptionOf(IntPtr hwnd)
        {
            int length = NativeMethods.GetWindowTextLength(hwnd);
            var sb = new StringBuilder(Math.Max(length, 256) + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// A child control's text via <c>WM_GETTEXT</c> with a timeout. (<c>GetWindowText</c>
        /// returns an empty string for another process's edit and static controls.) Falls back
        /// to the caption if the control does not answer.
        /// </summary>
        private static string ReadText(IntPtr hwnd)
        {
            if (NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG, NativeMethods.TextTimeoutMs, out IntPtr lengthResult) == IntPtr.Zero)
                return CaptionOf(hwnd);

            int length = (int)Math.Min(Math.Max(lengthResult.ToInt64(), 0L), MaxTextChars);
            var sb = new StringBuilder(Math.Max(length, 256) + 1);
            if (NativeMethods.SendMessageTimeoutText(hwnd, NativeMethods.WM_GETTEXT, (IntPtr)sb.Capacity, sb,
                    NativeMethods.SMTO_ABORTIFHUNG, NativeMethods.TextTimeoutMs, out _) == IntPtr.Zero)
                return CaptionOf(hwnd);
            return sb.ToString();
        }
    }
}
