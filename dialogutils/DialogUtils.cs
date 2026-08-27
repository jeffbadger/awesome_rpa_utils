using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DialogAutomation
{
    /// <summary>
    /// A standard Windows MessageBox button, identified by its well-known control ID
    /// (<c>IDOK</c>, <c>IDCANCEL</c>, etc.), for use with
    /// <see cref="DialogUtils.ClickDialogButton"/>.
    /// </summary>
    public enum DialogButton
    {
        /// <summary>OK (IDOK, 1).</summary>
        Ok = 1,
        /// <summary>Cancel (IDCANCEL, 2).</summary>
        Cancel = 2,
        /// <summary>Abort (IDABORT, 3).</summary>
        Abort = 3,
        /// <summary>Retry (IDRETRY, 4).</summary>
        Retry = 4,
        /// <summary>Ignore (IDIGNORE, 5).</summary>
        Ignore = 5,
        /// <summary>Yes (IDYES, 6).</summary>
        Yes = 6,
        /// <summary>No (IDNO, 7).</summary>
        No = 7
    }

    /// <summary>
    /// Pega Robot Studio-ready component that finds and dismisses native dialogs
    /// (message boxes, common dialogs) by button text or control ID, via <c>BM_CLICK</c>
    /// — no cursor movement required, and it works even if the dialog is behind other
    /// windows.
    /// </summary>
    [Description("Finds and dismisses native dialogs by button text/control ID. Drag " +
                 "this component onto a Pega Robot Studio automation to use its methods.")]
    public class DialogUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public DialogUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public DialogUtils(IContainer container)
        {
            container?.Add(this);
        }

        #region Find & Click

        /// <summary>
        /// Finds a top-level dialog window by its title. Returns <see cref="IntPtr.Zero"/>
        /// if none matches (not found is a normal, checkable outcome, not an error).
        /// </summary>
        /// <param name="titlePattern">The title to match.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact, case-sensitive title match.
        /// If <c>false</c>, matches any window whose title contains
        /// <paramref name="titlePattern"/> (case-insensitive).
        /// </param>
        [Category("Dialog - Find & Click")]
        [Description("Finds a top-level dialog window by its title, or returns a zero handle if none matches.")]
        public IntPtr FindDialog(string titlePattern, bool exactMatch = true)
        {
            foreach (var hWnd in GetTopLevelWindows())
            {
                string title = GetControlText(hWnd);
                bool matches = exactMatch
                    ? string.Equals(title, titlePattern, StringComparison.Ordinal)
                    : title.IndexOf(titlePattern, StringComparison.OrdinalIgnoreCase) >= 0;
                if (matches)
                    return hWnd;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Finds a button on a dialog by its visible text (case-insensitive). Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        [Category("Dialog - Find & Click")]
        [Description("Finds a button on a dialog by its visible text, or returns a zero handle if none matches.")]
        public IntPtr FindButtonByText(IntPtr hDialog, string buttonText)
        {
            foreach (var child in GetChildWindows(hDialog))
            {
                if (GetWindowClassName(child) == "Button" &&
                    string.Equals(GetControlText(child), buttonText, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Finds a control on a dialog by its control ID (<c>GetDlgItem</c>). Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        [Category("Dialog - Find & Click")]
        [Description("Finds a control on a dialog by its control ID, or returns a zero handle if none matches.")]
        public IntPtr FindButtonById(IntPtr hDialog, int controlId)
        {
            return GetDlgItem(hDialog, controlId);
        }

        /// <summary>
        /// Invokes a button by sending it <c>BM_CLICK</c> — no cursor movement is involved,
        /// and it works even if the dialog is behind other windows.
        /// </summary>
        /// <remarks>
        /// <c>SendMessage</c>'s return value for <c>BM_CLICK</c> carries no useful
        /// success/failure signal, so this method never throws based on it.
        /// </remarks>
        [Category("Dialog - Find & Click")]
        [Description("Invokes a button by sending it BM_CLICK, without moving the cursor.")]
        public void ClickButton(IntPtr hButton)
        {
            SendMessage(hButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Invokes a standard dialog button by its well-known control ID.</summary>
        /// <exception cref="InvalidOperationException">The dialog has no control with that ID.</exception>
        [Category("Dialog - Find & Click")]
        [Description("Invokes a standard dialog button by its well-known control ID.")]
        public void ClickDialogButton(IntPtr hDialog, DialogButton button)
        {
            IntPtr hButton = GetDlgItem(hDialog, (int)button);
            if (hButton == IntPtr.Zero)
                throw new InvalidOperationException($"Dialog has no control with ID {(int)button} ({button}).");
            ClickButton(hButton);
        }

        /// <summary>Finds a button by its visible text and invokes it.</summary>
        /// <exception cref="InvalidOperationException">The dialog has no button with that text.</exception>
        [Category("Dialog - Find & Click")]
        [Description("Finds a button by its visible text and invokes it.")]
        public void ClickDialogButtonByText(IntPtr hDialog, string buttonText)
        {
            IntPtr hButton = FindButtonByText(hDialog, buttonText);
            if (hButton == IntPtr.Zero)
                throw new InvalidOperationException($"Dialog has no button labeled '{buttonText}'.");
            ClickButton(hButton);
        }

        #endregion

        #region Read Text

        /// <summary>
        /// Gets a dialog's message body: the text of the first child control of class
        /// <c>Static</c> (the standard control class for a MessageBox's message text)
        /// that has non-empty text, skipping empty-text <c>Static</c> children such as
        /// an icon control on a MessageBox with an icon set. Returns an empty string if
        /// no <c>Static</c> child has text.
        /// </summary>
        [Category("Dialog - Read Text")]
        [Description("Gets a dialog's message body text from its first non-empty Static child control.")]
        public string GetDialogText(IntPtr hDialog)
        {
            foreach (var child in GetChildWindows(hDialog))
            {
                if (GetWindowClassName(child) == "Static")
                {
                    string text = GetControlText(child);
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
            }
            return string.Empty;
        }

        /// <summary>Gets any control's text via <c>GetWindowText</c> (buttons, static labels, edit fields, and the dialog's own title bar).</summary>
        [Category("Dialog - Read Text")]
        [Description("Gets any control's text (buttons, labels, edit fields, or a dialog's title bar).")]
        public string GetControlText(IntPtr hControl)
        {
            int length = GetWindowTextLength(hControl);
            var sb = new StringBuilder(length + 1);
            GetWindowText(hControl, sb, sb.Capacity);
            return sb.ToString();
        }

        #endregion

        #region Wait-for-Dialog Polling

        /// <summary>Polls for a top-level dialog matching <paramref name="titlePattern"/> (substring, case-insensitive) until it appears or the timeout elapses.</summary>
        /// <param name="titlePattern">The title to match.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="hWnd">The matching dialog's handle, or <see cref="IntPtr.Zero"/> if not found in time.</param>
        /// <returns><c>true</c> if a matching dialog was found before the timeout.</returns>
        [Category("Dialog - Wait for Dialog")]
        [Description("Polls for a top-level dialog matching a title pattern until it appears or the timeout elapses.")]
        public bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (true)
            {
                IntPtr found = FindDialog(titlePattern, exactMatch: false);
                if (found != IntPtr.Zero)
                {
                    hWnd = found;
                    return true;
                }
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                {
                    hWnd = IntPtr.Zero;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
            }
        }

        /// <summary>Polls until a dialog handle is no longer valid (the dialog closed), or the timeout elapses.</summary>
        /// <param name="hWnd">The dialog handle to watch.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <returns><c>true</c> if the handle became invalid before the timeout; <c>false</c> if the timeout elapsed first.</returns>
        [Category("Dialog - Wait for Dialog")]
        [Description("Polls until a dialog handle is no longer valid, or the timeout elapses.")]
        public bool WaitForDialogToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (IsWindowNative(hWnd))
            {
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    return false;
                Thread.Sleep(pollIntervalMs);
            }
            return true;
        }

        #endregion

        #region Win32 Interop

        private const uint BM_CLICK = 0x00F5;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowNative(IntPtr hWnd);

        private static List<IntPtr> GetTopLevelWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                windows.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static List<IntPtr> GetChildWindows(IntPtr hWndParent)
        {
            var children = new List<IntPtr>();
            EnumChildWindows(hWndParent, (hWnd, lParam) =>
            {
                children.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return children;
        }

        private static string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        #endregion
    }
}
