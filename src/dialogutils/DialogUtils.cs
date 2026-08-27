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
    /// (<c>IDOK</c>, <c>IDCANCEL</c>, etc.) — cast to <c>int</c> for use with
    /// <see cref="DialogUtils.ClickDialogButtonById"/> (e.g. <c>(int)DialogButton.Yes</c>).
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
        /// Finds a top-level dialog window by its title, same as <see cref="FindDialog(string, bool)"/>,
        /// and additionally reports via <paramref name="canDismiss"/> whether the dialog has at
        /// least one native <c>Button</c> control that <see cref="ClickButton"/>/
        /// <see cref="ClickDialogButtonById"/>/<see cref="ClickDialogButtonByText"/> can target — see
        /// <see cref="CanDismissDialog"/>.
        /// </summary>
        /// <param name="titlePattern">The title to match.</param>
        /// <param name="exactMatch">
        /// If <c>true</c>, requires an exact, case-sensitive title match. If <c>false</c>,
        /// matches any window whose title contains <paramref name="titlePattern"/>
        /// (case-insensitive).
        /// </param>
        /// <param name="canDismiss">
        /// <c>true</c> if a dialog was found and it has at least one native <c>Button</c>
        /// control; <c>false</c> if no dialog was found, or the dialog was found but has no
        /// native <c>Button</c> control — e.g. a modern WinUI3/UWP "dialog" (such as the
        /// Windows 11 Notepad "Do you want to save changes?" prompt) rendered as XAML content
        /// inside its host window rather than as real <c>Button</c> controls. When
        /// <c>canDismiss</c> is <c>false</c> for a found dialog, drive it with
        /// <c>KeyboardUtils</c> instead (see the DialogUtils README's Notes &amp;
        /// Caveats).
        /// </param>
        [Category("Dialog - Find & Click")]
        [Description("Finds a top-level dialog window by its title, and reports whether DialogUtils can dismiss it via a native Button control.")]
        public IntPtr FindDialog(string titlePattern, bool exactMatch, out bool canDismiss)
        {
            IntPtr hWnd = FindDialog(titlePattern, exactMatch);
            canDismiss = hWnd != IntPtr.Zero && CanDismissDialog(hWnd);
            return hWnd;
        }

        /// <summary>
        /// Checks whether a dialog has at least one native <c>Button</c> control that
        /// <see cref="ClickButton"/>/<see cref="ClickDialogButtonById"/>/
        /// <see cref="ClickDialogButtonByText"/> can target. Returns <c>false</c> for
        /// modern WinUI3/UWP "dialogs" (e.g. the Windows 11 Notepad "Do you want to save
        /// changes?" prompt) that are rendered as XAML content inside their host window
        /// rather than as real <c>Button</c> controls — for those, use
        /// <c>KeyboardUtils</c> instead.
        /// </summary>
        [Category("Dialog - Find & Click")]
        [Description("Checks whether a dialog has at least one native Button control that DialogUtils can click.")]
        public bool CanDismissDialog(IntPtr hDialog)
        {
            foreach (var child in GetChildWindows(hDialog))
            {
                if (GetWindowClassName(child) == "Button")
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds a button on a dialog by its visible text (case-insensitive). Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        /// <param name="hDialog">The dialog to search.</param>
        /// <param name="buttonText">The button text to match.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact (case-insensitive) text match.
        /// If <c>false</c>, matches any button whose text contains
        /// <paramref name="buttonText"/> (also case-insensitive) — useful for buttons
        /// whose text includes a variable part, e.g. a trailing ellipsis
        /// (<c>"Details..."</c>) or a count (<c>"Retry (3 left)"</c>).
        /// </param>
        /// <remarks>
        /// Button window text carries a raw <c>&amp;</c> access-key mnemonic that Windows
        /// draws as an underline but keeps in <c>GetWindowText</c> — a standard Yes/No
        /// <c>MessageBox</c>'s buttons are literally <c>"&amp;Yes"</c>/<c>"&amp;No"</c>, not
        /// <c>"Yes"</c>/<c>"No"</c>. Both the button's text and <paramref name="buttonText"/>
        /// have their <c>&amp;</c> mnemonics stripped before comparing (a literal <c>&amp;</c>
        /// in a label is doubled as <c>"&amp;&amp;"</c> and preserved), so matching against
        /// the text as it visibly reads on screen — <c>"Yes"</c>, not <c>"&amp;Yes"</c> — just
        /// works, with either <paramref name="exactMatch"/> setting.
        /// </remarks>
        [Category("Dialog - Find & Click")]
        [Description("Finds a button on a dialog by its visible text (exact or substring match, ignoring access-key & mnemonics), or returns a zero handle if none matches.")]
        public IntPtr FindButtonByText(IntPtr hDialog, string buttonText, bool exactMatch = true)
        {
            string target = StripAccessKeyMnemonic(buttonText);
            foreach (var child in GetChildWindows(hDialog))
            {
                if (GetWindowClassName(child) != "Button")
                    continue;

                string text = StripAccessKeyMnemonic(GetControlText(child));
                bool matches = exactMatch
                    ? string.Equals(text, target, StringComparison.OrdinalIgnoreCase)
                    : text.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
                if (matches)
                    return child;
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
        /// <param name="hButton">The button to click.</param>
        /// <param name="waitForEnabledMs">
        /// Some dialogs briefly show a button before it's actually enabled (e.g. while the
        /// dialog is still completing its modal setup) — <c>BM_CLICK</c> on a disabled
        /// control is silently ignored by Windows, which is why a click sent the instant a
        /// button is found can have no visible effect even though the button exists. This
        /// polls <c>IsWindowEnabled</c> for up to this many milliseconds (default 500) before
        /// clicking, so the click lands once the button is actually interactive. Pass 0 to
        /// click immediately without waiting, matching the previous behavior.
        /// </param>
        /// <param name="pollIntervalMs">Delay between enabled-state checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <remarks>
        /// <c>SendMessage</c>'s return value for <c>BM_CLICK</c> carries no useful
        /// success/failure signal, so this method never throws based on it — it always sends
        /// the click after waiting, even if the button never became enabled.
        /// </remarks>
        [Category("Dialog - Find & Click")]
        [Description("Invokes a button by sending it BM_CLICK, without moving the cursor. Waits briefly for the button to become enabled first.")]
        public void ClickButton(IntPtr hButton, int waitForEnabledMs = 500, int pollIntervalMs = 25)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (!IsWindowEnabled(hButton) && unchecked(Environment.TickCount - start) < waitForEnabledMs)
            {
                Thread.Sleep(pollIntervalMs);
            }

            SendMessage(hButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Invokes a button by its control ID (<c>GetDlgItem</c>). Use a well-known
        /// <see cref="DialogButton"/> value (cast to <c>int</c>, e.g.
        /// <c>(int)DialogButton.Yes</c>) for a standard <c>MessageBox</c> button, or any
        /// custom ID discovered via <see cref="ListDialogControls"/> — the go-to for RPA,
        /// where you typically have an ID from inspecting the dialog rather than a
        /// well-known one.
        /// </summary>
        /// <param name="hDialog">The dialog whose button to click.</param>
        /// <param name="controlId">The control ID to click.</param>
        /// <param name="waitForEnabledMs">See <see cref="ClickButton"/>.</param>
        /// <param name="pollIntervalMs">See <see cref="ClickButton"/>.</param>
        /// <exception cref="InvalidOperationException">The dialog has no control with that ID.</exception>
        [Category("Dialog - Find & Click")]
        [Description("Invokes a button by its control ID (GetDlgItem).")]
        public void ClickDialogButtonById(IntPtr hDialog, int controlId, int waitForEnabledMs = 500, int pollIntervalMs = 25)
        {
            IntPtr hButton = GetDlgItem(hDialog, controlId);
            if (hButton == IntPtr.Zero)
                throw new InvalidOperationException($"Dialog has no control with ID {controlId}.");
            ClickButton(hButton, waitForEnabledMs, pollIntervalMs);
        }

        /// <summary>
        /// Finds a button by its visible text and invokes it, then verifies the dialog
        /// actually closed and re-clicks if it didn't — up to <paramref name="maxAttempts"/>
        /// times. Some apps' click handlers ignore the first click of a button for reasons
        /// that aren't visible via Win32 (e.g. app-internal validation or state that isn't
        /// reflected in <c>IsWindowEnabled</c>), so a single <c>BM_CLICK</c> can silently have
        /// no effect even though the button was found and clicked correctly; this retries
        /// past that instead of requiring you to detect and retry it yourself.
        /// </summary>
        /// <param name="hDialog">The dialog to search and click on.</param>
        /// <param name="buttonText">The button text to match.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact (case-insensitive) text match.
        /// If <c>false</c>, matches any button whose text contains
        /// <paramref name="buttonText"/> (also case-insensitive) — see
        /// <see cref="FindButtonByText"/>.
        /// </param>
        /// <param name="maxAttempts">Maximum number of click attempts (default 3).</param>
        /// <param name="retryDelayMs">
        /// How long to wait after each click for the dialog to close before deciding it
        /// didn't work and re-clicking (default 300 ms).
        /// </param>
        /// <returns><c>true</c> if the dialog closed within <paramref name="maxAttempts"/> clicks; <c>false</c> if it was still open after the last attempt.</returns>
        /// <exception cref="InvalidOperationException">The dialog has no button with that text.</exception>
        /// <remarks>
        /// Retrying is only meaningful for a click that's expected to close
        /// <paramref name="hDialog"/> — not for a button that intentionally keeps the dialog
        /// open (e.g. "Apply"), which will always report <c>false</c> after using up every
        /// attempt.
        /// </remarks>
        [Category("Dialog - Find & Click")]
        [Description("Finds a button by its visible text (exact or substring match) and invokes it, re-clicking (up to maxAttempts) if the dialog doesn't close.")]
        public bool ClickDialogButtonByText(IntPtr hDialog, string buttonText, bool exactMatch = true, int maxAttempts = 3, int retryDelayMs = 300)
        {
            if (maxAttempts < 1) maxAttempts = 1;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                IntPtr hButton = FindButtonByText(hDialog, buttonText, exactMatch);
                if (hButton == IntPtr.Zero)
                    throw new InvalidOperationException($"Dialog has no button labeled '{buttonText}'.");

                ClickButton(hButton);
                if (WaitForDialogToClose(hDialog, retryDelayMs, pollIntervalMs: 25))
                    return true;
            }
            return false;
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

        #region Enumerate Controls

        /// <summary>
        /// Describes one control on a dialog: its handle, control ID, visible text, and
        /// window class name (e.g. <c>Button</c>, <c>Static</c>, <c>Edit</c>) — everything
        /// needed to identify it for <see cref="FindButtonById"/>/<see cref="ClickButton"/>.
        /// </summary>
        public class DialogControlInfo
        {
            /// <summary>The control's window handle.</summary>
            public IntPtr Handle { get; set; }

            /// <summary>The control's ID (<c>GetDlgCtrlID</c>), as used by <see cref="FindButtonById"/>.</summary>
            public int Id { get; set; }

            /// <summary>The control's visible text (button label, static text, edit field contents, etc.).</summary>
            public string Text { get; set; }

            /// <summary>The control's window class name (e.g. <c>Button</c>, <c>Static</c>, <c>Edit</c>).</summary>
            public string ClassName { get; set; }

            /// <summary>Whether the control is currently enabled (<c>IsWindowEnabled</c>); a disabled <c>Button</c> silently ignores <c>BM_CLICK</c>.</summary>
            public bool Enabled { get; set; }

            /// <inheritdoc />
            public override string ToString() => $"[{Id}] {ClassName}: \"{Text}\"{(Enabled ? "" : " (disabled)")}";
        }

        /// <summary>
        /// Lists every control on a dialog — including controls nested inside another
        /// control, such as a group box — with its ID, text, and window class. Use this
        /// to discover a dialog's controls when you don't already know their IDs or text,
        /// then <see cref="HighlightControl"/> a handle from the results to confirm which
        /// control on screen it corresponds to.
        /// </summary>
        [Category("Dialog - Read Text")]
        [Description("Lists every control on a dialog with its control ID, text, and window class name.")]
        public List<DialogControlInfo> ListDialogControls(IntPtr hDialog)
        {
            var controls = new List<DialogControlInfo>();
            foreach (var child in GetChildWindows(hDialog))
            {
                controls.Add(new DialogControlInfo
                {
                    Handle = child,
                    Id = GetDlgCtrlID(child),
                    Text = GetControlText(child),
                    ClassName = GetWindowClassName(child),
                    Enabled = IsWindowEnabled(child)
                });
            }
            return controls;
        }

        /// <summary>
        /// Flashes an inverting rectangle around a control to visually confirm which
        /// on-screen control a handle (e.g. from <see cref="ListDialogControls"/>)
        /// corresponds to. The rectangle is drawn on the screen DC with
        /// <c>R2_NOTXORPEN</c> so drawing it twice erases it exactly (no permanent
        /// pixels left behind).
        /// </summary>
        /// <param name="hControl">Handle of the control to highlight.</param>
        /// <param name="flashes">Number of on/off flashes (default 3).</param>
        /// <param name="flashMs">Milliseconds each flash stays visible (default 200).</param>
        /// <param name="lineWidth">Pen width in pixels (default 3).</param>
        /// <param name="colorRef">
        /// RGB color for the rectangle as a 0xBBGGRR value (e.g. 0x0000FF = red).
        /// Because the rectangle uses XOR drawing, the visible color depends on what
        /// is under it. Default is 0x0000FF (red).
        /// </param>
        /// <exception cref="Win32Exception"><c>GetWindowRect</c> or <c>GetDC</c> failed (e.g. an invalid handle).</exception>
        /// <remarks>
        /// Caveats:
        ///  - If the control repaints while the rectangle is visible, the highlight pixels
        ///    may leave artifacts that the second XOR pass cannot fully erase.
        ///  - Does not draw over exclusive fullscreen (DirectX) applications.
        ///  - The XOR blend means the apparent color varies by background.
        /// </remarks>
        [Category("Dialog - Read Text")]
        [Description("Flashes an inverting rectangle around a control to visually confirm which on-screen control a handle corresponds to.")]
        public void HighlightControl(IntPtr hControl, int flashes = 3, int flashMs = 200, int lineWidth = 3, int colorRef = 0x0000FF)
        {
            if (flashes < 1) flashes = 1;
            if (flashMs < 1) flashMs = 1;
            if (lineWidth < 1) lineWidth = 1;

            if (!GetWindowRect(hControl, out RECT rc))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.");

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC(NULL) for the screen failed.");

            IntPtr hPen = CreatePen(PS_SOLID, lineWidth, (uint)colorRef);
            IntPtr hOldPen = IntPtr.Zero;
            IntPtr hOldBrush = IntPtr.Zero;

            try
            {
                hOldPen = SelectObject(hdc, hPen);
                hOldBrush = SelectObject(hdc, GetStockObject(NULL_BRUSH));
                SetROP2(hdc, R2_NOTXORPEN);

                for (int i = 0; i < flashes; i++)
                {
                    // Draw (visible) — XOR
                    Rectangle(hdc, rc.Left, rc.Top, rc.Right, rc.Bottom);
                    Thread.Sleep(flashMs);
                    // Draw again (erases) — XOR XOR = original pixels
                    Rectangle(hdc, rc.Left, rc.Top, rc.Right, rc.Bottom);

                    if (i < flashes - 1)
                        Thread.Sleep(flashMs);
                }
            }
            finally
            {
                if (hOldPen != IntPtr.Zero) SelectObject(hdc, hOldPen);
                if (hOldBrush != IntPtr.Zero) SelectObject(hdc, hOldBrush);
                if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
                if (hPen != IntPtr.Zero) DeleteObject(hPen);
            }
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowNative(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        private const int PS_SOLID = 0;
        private const int NULL_BRUSH = 5;
        private const int R2_NOTXORPEN = 10; // Draw = NOT (pen XOR dest)

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int fnObject);

        [DllImport("gdi32.dll")]
        private static extern int SetROP2(IntPtr hdc, int fnDrawMode);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Rectangle(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

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

        /// <summary>
        /// Strips the Win32 access-key mnemonic marker from button/label text: a single
        /// <c>&amp;</c> is removed (its following character is the underlined access key),
        /// while a doubled <c>&amp;&amp;</c> — the escape sequence for a literal ampersand —
        /// collapses to one <c>&amp;</c>. E.g. <c>"&amp;Yes"</c> → <c>"Yes"</c>,
        /// <c>"Save &amp;&amp; Exit"</c> → <c>"Save &amp; Exit"</c>.
        /// </summary>
        private static string StripAccessKeyMnemonic(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0)
                return text;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '&' && i + 1 < text.Length && text[i + 1] == '&')
                {
                    sb.Append('&');
                    i++;
                }
                else if (text[i] != '&')
                {
                    sb.Append(text[i]);
                }
            }
            return sb.ToString();
        }

        #endregion
    }
}
