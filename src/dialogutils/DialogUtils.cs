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
    /// The state of a check box or radio button, as reported by
    /// <see cref="DialogUtils.TryGetControlCheckState"/>. The numeric values are the
    /// Windows <c>BST_*</c> constants.
    /// </summary>
    public enum ControlCheckState
    {
        /// <summary>Not checked (BST_UNCHECKED, 0).</summary>
        Unchecked = 0,
        /// <summary>Checked, or the selected radio button (BST_CHECKED, 1).</summary>
        Checked = 1,
        /// <summary>The third state of a three-state check box (BST_INDETERMINATE, 2).</summary>
        Indeterminate = 2
    }

    /// <summary>
    /// Pega Robot Studio-ready component that finds native dialogs (message boxes, common
    /// dialogs), dismisses them by button text or control ID via <c>BM_CLICK</c> - no cursor
    /// movement required, and it works even if the dialog is behind other windows. It also
    /// fills dialogs in (text boxes, check boxes, radio buttons, drop-down lists, and the
    /// Open/Save file dialogs); those fill methods send window messages too, but have only
    /// been verified with the dialog in front.
    /// </summary>
    [Description("Finds native dialogs, dismisses them by button text/control ID, and fills them " +
                 "in (text, check boxes, radio buttons, drop-down lists, Open/Save file dialogs). " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
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
        /// Finds a top-level dialog window by its title, and reports via
        /// <paramref name="canDismiss"/> whether it has at least one native <c>Button</c>
        /// control that <see cref="ClickButton"/>/<see cref="ClickDialogButtonById"/>/
        /// <see cref="ClickDialogButtonByText"/> can target — see <see cref="CanDismissDialog"/>.
        /// Never throws - a null or empty <paramref name="titlePattern"/>, or no match,
        /// are all reported by returning <c>false</c> (not found is a normal, checkable
        /// outcome, not an error).
        /// <para>
        /// Only <b>visible</b> top-level windows are considered — hidden windows (e.g. a
        /// form an app has pre-created with a title that already matches) are skipped, so
        /// the method cannot match a dialog before it actually appears on screen. When
        /// <paramref name="processId"/> is non-zero, only windows owned by that process
        /// are considered, so a robot can scope matching to its target application
        /// instead of every window on the desktop.
        /// </para>
        /// </summary>
        /// <param name="titlePattern">The title to match.</param>
        /// <param name="hDialog">The matching dialog's handle, or <see cref="IntPtr.Zero"/> if none matches.</param>
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
        /// <param name="exactMatch">
        /// If <c>true</c>, requires an exact (case-insensitive) title match.
        /// If <c>false</c>, matches any window whose title contains
        /// <paramref name="titlePattern"/> (also case-insensitive).
        /// </param>
        /// <param name="processId">
        /// When non-zero, only windows owned by this process ID (as reported by
        /// <c>GetWindowThreadProcessId</c>) are considered — use this to scope matching
        /// to the robot's target application so a title that coincidentally appears in
        /// another app's window can never be matched. 0 (the default) matches any process.
        /// </param>
        /// <returns><c>true</c> if a matching dialog was found.</returns>
        /// <remarks>
        /// Returns the <b>first</b> matching window in enumeration order. If more than
        /// one window could match (e.g. two apps both showing a "Confirm" dialog), use
        /// <see cref="FindAllDialogs"/> to get every match and pick the right one, or
        /// scope with <paramref name="processId"/>.
        /// </remarks>
        [Category("Dialog - Find & Click")]
        [Description("Finds a visible top-level dialog window by its title (optionally scoped to a process ID), and reports whether DialogUtils can dismiss it via a native Button control. Returns True if found; never throws.")]
        public bool FindDialog(string titlePattern, out IntPtr hDialog, out bool canDismiss, bool exactMatch, int processId = 0)
        {
            foreach (var hWnd in GetMatchingTopLevelWindows(titlePattern, exactMatch, processId))
            {
                hDialog = hWnd;
                canDismiss = CanDismissDialog(hWnd);
                return true;
            }

            hDialog = IntPtr.Zero;
            canDismiss = false;
            return false;
        }

        /// <summary>
        /// Finds every visible top-level window whose title matches
        /// <paramref name="titlePattern"/> — the same matching rules as
        /// <see cref="FindDialog"/>, but returning all matches instead of just the
        /// first. Use this when more than one window could match (e.g. two apps both
        /// showing a "Confirm" dialog) and you need to pick the right one, or to
        /// confirm how many windows match before acting. Never throws.
        /// </summary>
        /// <param name="titlePattern">The title to match. Null or empty never matches.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact (case-insensitive) title match.
        /// If <c>false</c>, matches any window whose title contains
        /// <paramref name="titlePattern"/> (also case-insensitive).
        /// </param>
        /// <param name="processId">
        /// When non-zero, only windows owned by this process ID are considered — see
        /// <see cref="FindDialog"/>. 0 (the default) matches any process.
        /// </param>
        /// <returns>The handles of every matching window, in enumeration order; an empty list if none match.</returns>
        [Category("Dialog - Find & Click")]
        [Description("Finds every visible top-level window whose title matches (exact or substring, optionally process-scoped). Returns all matches; never throws.")]
        public List<IntPtr> FindAllDialogs(string titlePattern, bool exactMatch = true, int processId = 0)
        {
            return GetMatchingTopLevelWindows(titlePattern, exactMatch, processId);
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
        /// Finds a button on a dialog by its visible text (case-insensitive). Never
        /// throws - a null or empty <paramref name="buttonText"/>, or no match, are all
        /// reported by returning <c>false</c> (an empty pattern is treated as a mistake,
        /// not as "match the first button").
        /// </summary>
        /// <param name="hDialog">The dialog to search.</param>
        /// <param name="hButton">The matching button's handle, or <see cref="IntPtr.Zero"/> if none matches.</param>
        /// <param name="buttonText">The button text to match.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact (case-insensitive) text match.
        /// If <c>false</c>, matches any button whose text contains
        /// <paramref name="buttonText"/> (also case-insensitive) — useful for buttons
        /// whose text includes a variable part, e.g. a trailing ellipsis
        /// (<c>"Details..."</c>) or a count (<c>"Retry (3 left)"</c>).
        /// </param>
        /// <returns><c>true</c> if a matching button was found.</returns>
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
        [Description("Finds a button on a dialog by its visible text (exact or substring match, ignoring access-key & mnemonics). Returns True if found; never throws.")]
        public bool FindButtonByText(IntPtr hDialog, out IntPtr hButton, string buttonText, bool exactMatch = true)
        {
            if (!string.IsNullOrEmpty(buttonText))
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
                    {
                        hButton = child;
                        return true;
                    }
                }
            }

            hButton = IntPtr.Zero;
            return false;
        }

        /// <summary>
        /// Finds a control on a dialog by its control ID (<c>GetDlgItem</c>). Never throws.
        /// </summary>
        /// <param name="hDialog">The dialog to search.</param>
        /// <param name="hButton">The matching control's handle, or <see cref="IntPtr.Zero"/> if none matches.</param>
        /// <param name="controlId">The control ID to match.</param>
        /// <returns><c>true</c> if a matching control was found.</returns>
        [Category("Dialog - Find & Click")]
        [Description("Finds a control on a dialog by its control ID. Returns True if found; never throws.")]
        public bool FindButtonById(IntPtr hDialog, out IntPtr hButton, int controlId)
        {
            hButton = GetDlgItem(hDialog, controlId);
            return hButton != IntPtr.Zero;
        }

        /// <summary>
        /// Invokes a button by sending it <c>BM_CLICK</c> — no cursor movement is involved,
        /// and it works even if the dialog is behind other windows. The click is delivered
        /// via <c>SendMessageTimeout</c> with <c>SMTO_ABORTIFHUNG</c>, so a hung target
        /// application aborts the call instead of blocking this thread forever.
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
        /// <returns>
        /// <c>true</c> if the button was enabled when the click was sent; <c>false</c> if it
        /// was still disabled after <paramref name="waitForEnabledMs"/> elapsed (or the
        /// handle stopped being a valid window while waiting, in which case the click is a
        /// harmless no-op). The click is sent either way - this only reports whether it had
        /// a fighting chance of landing, not whether the target application actually
        /// reacted to it, which <c>SendMessageTimeout</c>'s return value for <c>BM_CLICK</c>
        /// cannot tell you (a disabled control silently ignores <c>BM_CLICK</c>, so this is
        /// the one honest signal available without inspecting the target application
        /// itself).
        /// </returns>
        [Category("Dialog - Find & Click")]
        [Description("Invokes a button by sending it BM_CLICK, without moving the cursor. Waits briefly for the button to become enabled first. Returns True if it was enabled when clicked.")]
        public bool ClickButton(IntPtr hButton, int waitForEnabledMs = 500, int pollIntervalMs = 25)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            bool enabled = IsWindowEnabled(hButton);
            // Bail out early if the button's window dies mid-wait — IsWindowEnabled keeps
            // returning false for a dead handle, and clicking it is a guaranteed no-op.
            while (!enabled && IsWindowNative(hButton) && unchecked(Environment.TickCount - start) < waitForEnabledMs)
            {
                Thread.Sleep(pollIntervalMs);
                enabled = IsWindowEnabled(hButton);
            }

            SendMessageTimeout(hButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, BM_CLICK_TIMEOUT_MS, out _);
            return enabled;
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
        /// <param name="wasEnabled">
        /// <c>true</c> if the button was enabled when the click was sent; <c>false</c> if it
        /// was still disabled after <paramref name="waitForEnabledMs"/> elapsed, or if no
        /// control with <paramref name="controlId"/> was found. See <see cref="ClickButton"/>.
        /// </param>
        /// <param name="waitForEnabledMs">See <see cref="ClickButton"/>.</param>
        /// <param name="pollIntervalMs">See <see cref="ClickButton"/>.</param>
        /// <returns><c>true</c> if a control with <paramref name="controlId"/> was found (and clicked). Never throws.</returns>
        /// <remarks>
        /// "Found" is not the same as "closed" — this returns <c>true</c> once the click
        /// was sent, not once the dialog went away. When the click is expected to close
        /// <paramref name="hDialog"/>, verify it with <see cref="WaitForDialogToClose"/>
        /// (matching by text instead? <see cref="ClickDialogButtonByText"/> builds that
        /// verify-and-retry in).
        /// </remarks>
        [Category("Dialog - Find & Click")]
        [Description("Invokes a button by its control ID (GetDlgItem). Returns True if found; never throws.")]
        public bool ClickDialogButtonById(IntPtr hDialog, int controlId, out bool wasEnabled, int waitForEnabledMs = 500, int pollIntervalMs = 25)
        {
            IntPtr hButton = GetDlgItem(hDialog, controlId);
            if (hButton == IntPtr.Zero)
            {
                wasEnabled = false;
                return false;
            }

            wasEnabled = ClickButton(hButton, waitForEnabledMs, pollIntervalMs);
            return true;
        }

        /// <summary>
        /// Finds a button by its visible text and invokes it once.
        /// </summary>
        /// <param name="hDialog">The dialog to search and click on.</param>
        /// <param name="buttonText">The button text to match.</param>
        /// <param name="wasEnabled"><c>true</c> if the button was enabled when the click was sent; otherwise <c>false</c>.</param>
        /// <param name="message"><c>null</c> when the button was found; otherwise a human-readable reason it was not found.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact (case-insensitive) text match.
        /// If <c>false</c>, matches any button whose text contains
        /// <paramref name="buttonText"/> (also case-insensitive) — see
        /// <see cref="FindButtonByText"/>.
        /// </param>
        /// <param name="waitForEnabledMs">See <see cref="ClickButton"/>.</param>
        /// <param name="pollIntervalMs">See <see cref="ClickButton"/>.</param>
        /// <returns><c>true</c> if a matching button was found and the click was sent; <c>false</c> if no matching button was found. Never throws.</returns>
        /// <remarks>
        /// Windows does not provide a reliable signal that the target application acted
        /// on <c>BM_CLICK</c>. Use <see cref="WaitForDialogToClose"/> separately when the
        /// automation specifically needs to observe the original dialog handle closing.
        /// </remarks>
        [Category("Dialog - Find & Click")]
        [Description("Finds a button by its visible text (exact or substring match) and invokes it once. Returns True if found and reports whether it was enabled; never throws.")]
        public bool ClickDialogButtonByText(IntPtr hDialog, string buttonText, out bool wasEnabled, out string message, bool exactMatch = true, int waitForEnabledMs = 500, int pollIntervalMs = 25)
        {
            wasEnabled = false;
            if (!FindButtonByText(hDialog, out IntPtr hButton, buttonText, exactMatch))
            {
                message = $"Dialog has no button labeled '{buttonText}'.";
                return false;
            }

            wasEnabled = ClickButton(hButton, waitForEnabledMs, pollIntervalMs);
            message = null;
            return true;
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

        /// <summary>
        /// Gets any control's text (buttons, static labels, edit fields, drop-down lists,
        /// and the dialog's own title bar), including controls in another process.
        /// </summary>
        /// <remarks>
        /// Reads with <c>WM_GETTEXT</c> rather than <c>GetWindowText</c>: for a control in
        /// another process, <c>GetWindowText</c> deliberately returns an empty string for an
        /// edit box or drop-down list (their text lives in the control, not in the window),
        /// so an edit field always read back as empty. The message is sent with a timeout
        /// that gives up immediately on a hung application. At most 1,048,576 characters
        /// are read. Returns an empty string for an invalid handle; for a control that does not answer the text request it falls back to the non-blocking window caption, which may be empty or stale.
        /// </remarks>
        [Category("Dialog - Read Text")]
        [Description("Gets any control's text (buttons, labels, edit fields, drop-down lists, or a dialog's title bar), including in another process.")]
        public string GetControlText(IntPtr hControl)
        {
            return ReadControlText(hControl);
        }

        /// <summary>
        /// The text of a control via <c>WM_GETTEXT</c>, falling back to
        /// <c>GetWindowText</c> if the control does not answer in time.
        /// </summary>
        private static string ReadControlText(IntPtr hControl)
        {
            if (SendMessageTimeout(hControl, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, TEXT_MESSAGE_TIMEOUT_MS, out IntPtr lengthResult) == IntPtr.Zero)
                return GetWindowTextNonBlocking(hControl);

            int length = (int)Math.Min(Math.Max(lengthResult.ToInt64(), 0L), MaxReadTextChars);
            // A minimum buffer so text that grows between the length query and the read
            // isn't silently truncated.
            var sb = new StringBuilder(Math.Max(length, 256) + 1);
            if (SendMessageTimeoutText(hControl, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, TEXT_MESSAGE_TIMEOUT_MS, out _) == IntPtr.Zero)
                return GetWindowTextNonBlocking(hControl);
            return sb.ToString();
        }

        /// <summary>
        /// A window's caption via <c>GetWindowText</c>, which never sends a message to the
        /// owning application and so can never block on it. Right for matching top-level
        /// window titles across every process on the desktop; it cannot read an edit box in
        /// another process (see <see cref="GetControlText"/>).
        /// </summary>
        private static string GetWindowTextNonBlocking(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new StringBuilder(Math.Max(length, 256) + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        #endregion

        #region Set Values

        /// <summary>
        /// Sets a control's text with <c>WM_SETTEXT</c> - a text box, or the editable part of a
        /// drop-down list - and reads it back to confirm it took.
        /// </summary>
        /// <param name="hControl">The control to set, from <see cref="ListDialogControls"/>, <see cref="FindButtonById"/>, or another lookup.</param>
        /// <param name="text">The new text; an empty string clears the control. May not be <c>null</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason it failed. The text itself is never included, so a password is not echoed into a log.</param>
        /// <returns><c>true</c> if the control's text equals <paramref name="text"/> afterwards; <c>false</c> if <paramref name="text"/> is <c>null</c>, the handle is not a window, the control did not answer, or it did not keep the text (read-only, length-limited, or reformatting its input). Never throws.</returns>
        /// <remarks>
        /// <para>
        /// This writes the control's text directly, as if it had been typed and committed, but
        /// most applications only notice the change when the dialog is confirmed - they read the
        /// box then - and a few validate as you type and will not see it. Confirm with the
        /// dialog's OK button (<see cref="ClickDialogButtonById"/>), or use
        /// <see cref="SubmitFileDialog"/> for an Open/Save dialog.
        /// </para>
        /// <para>
        /// The handle must be the control that holds the text. For a drop-down list with an edit
        /// box, the <c>ComboBox</c> handle works; a <c>ComboBoxEx32</c> is best addressed by its
        /// inner <c>Edit</c>.
        /// </para>
        /// </remarks>
        [Category("Dialog - Set Values")]
        [Description("Sets a control's text (a text box or the editable part of a drop-down) and confirms it took. Returns True on success; never throws.")]
        public bool SetControlText(IntPtr hControl, string text, out string message)
        {
            message = default;
            try
            {
                if (text == null)
                {
                    message = "text may not be null (use an empty string to clear the control).";
                    return false;
                }
                if (!IsWindowNative(hControl))
                {
                    message = "Invalid or nonexistent control handle.";
                    return false;
                }

                // WM_SETTEXT is a programmatic write: it goes straight past ES_READONLY, which
                // only stops the keyboard. Refuse here so a read-only box is never changed.
                if (IsReadOnlyEdit(GetWindowClassName(hControl), GetWindowLong(hControl, GWL_STYLE)))
                {
                    message = "The text box is read-only, so its text was not changed.";
                    return false;
                }

                if (SendMessageTimeoutString(hControl, WM_SETTEXT, IntPtr.Zero, text, SMTO_ABORTIFHUNG, TEXT_MESSAGE_TIMEOUT_MS, out IntPtr setResult) == IntPtr.Zero
                    || setResult == IntPtr.Zero)
                {
                    message = "The control did not accept the text (it may be hung, or not a text control).";
                    return false;
                }

                if (!string.Equals(ReadControlText(hControl), text, StringComparison.Ordinal))
                {
                    message = "The control's text was not the requested text after setting it - it may be read-only, length-limited, or reformat what it is given.";
                    return false;
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetControlText", ex);
                return false;
            }
        }

        /// <summary>Reports whether a check box or radio button is checked.</summary>
        /// <param name="hControl">The check box or radio button.</param>
        /// <param name="state">Unchecked, Checked (also the selected radio button), or Indeterminate; <see cref="ControlCheckState.Unchecked"/> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the handle is not a window, is not a check box or radio button (a push button, group box, or a control of another kind), or does not answer. Never throws.</returns>
        [Category("Dialog - Set Values")]
        [Description("Reports whether a check box or radio button is checked. Returns True on success; never throws.")]
        public bool TryGetControlCheckState(IntPtr hControl, out ControlCheckState state, out string message)
        {
            state = ControlCheckState.Unchecked;
            message = default;
            try
            {
                if (!TryGetCheckableKind(hControl, out _, out message))
                    return false;
                return TryReadCheckState(hControl, out state, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                state = ControlCheckState.Unchecked;
                message = NeverThrowsGuard.Failure("TryGetControlCheckState", ex);
                return false;
            }
        }

        /// <summary>
        /// Checks or unchecks a check box, or selects a radio button, by clicking it only if it
        /// is not already in the requested state - then confirms the state it ended in.
        /// </summary>
        /// <param name="hControl">The check box or radio button.</param>
        /// <param name="isChecked"><c>true</c> to check it (or select the radio button); <c>false</c> to uncheck it.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason it failed.</param>
        /// <returns><c>true</c> if the control is in the requested state afterwards (including when it already was, in which case nothing is clicked); <c>false</c> if the handle is not a check box or radio button, a click is needed and it is disabled, a radio button was asked to be unchecked, or clicking did not leave it in the requested state. Never throws.</returns>
        /// <remarks>
        /// <para>
        /// This clicks (<c>BM_CLICK</c>) rather than setting the state directly, so the
        /// application's own click handling runs and it finds out - setting the state directly
        /// changes the box on screen without telling the application, which then acts as if
        /// nothing changed. Idempotent: it is safe to call whether or not the control is
        /// already in the requested state.
        /// </para>
        /// <para>
        /// A radio button can only be unchecked by selecting another button in its group, so
        /// <paramref name="isChecked"/> = <c>false</c> on one that is currently selected fails
        /// with a message saying so. A three-state check box is clicked up to twice to reach the
        /// checked or unchecked state.
        /// </para>
        /// </remarks>
        [Category("Dialog - Set Values")]
        [Description("Checks/unchecks a check box or selects a radio button (only clicking if needed) and confirms the result. Returns True on success; never throws.")]
        public bool SetControlChecked(IntPtr hControl, bool isChecked, out string message)
        {
            message = default;
            try
            {
                if (!TryGetCheckableKind(hControl, out CheckableKind kind, out message))
                    return false;
                if (!TryReadCheckState(hControl, out ControlCheckState current, out message))
                    return false;

                ControlCheckState wanted = isChecked ? ControlCheckState.Checked : ControlCheckState.Unchecked;
                if (current == wanted)
                {
                    message = null;
                    return true;
                }

                if (kind == CheckableKind.Radio && !isChecked)
                {
                    message = "A radio button can't be unchecked directly - select another button in its group instead.";
                    return false;
                }
                if (!IsWindowEnabled(hControl))
                {
                    message = "The control is disabled, so it cannot be clicked.";
                    return false;
                }

                // A three-state box cycles unchecked -> checked -> indeterminate, so it can
                // need two clicks; nothing else needs more than one.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    SendMessageTimeout(hControl, BM_CLICK, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, BM_CLICK_TIMEOUT_MS, out _);

                    // BM_CLICK is delivered synchronously, but an application may update the
                    // state a moment later; wait for it to change rather than click again.
                    ControlCheckState after = current;
                    int start = Environment.TickCount;
                    while (unchecked(Environment.TickCount - start) < CheckStateSettleMs)
                    {
                        if (!TryReadCheckState(hControl, out after, out message))
                            return false;
                        if (after != current)
                            break;
                        Thread.Sleep(20);
                    }

                    if (after == wanted)
                    {
                        message = null;
                        return true;
                    }
                    if (after == current)
                        break; // the click did nothing - clicking again would not help
                    current = after;
                }

                message = "Clicking the control did not leave it " + (isChecked ? "checked" : "unchecked") + " - it may ignore clicks or reset itself.";
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetControlChecked", ex);
                return false;
            }
        }

        /// <summary>
        /// Selects an item in a drop-down list or combo box by its text, and tells the
        /// application the selection changed.
        /// </summary>
        /// <param name="hCombo">The <c>ComboBox</c> (for a <c>ComboBoxEx32</c>, use its inner <c>ComboBox</c>).</param>
        /// <param name="itemText">The item's text; may not be null or empty.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason it failed. When no item matches, the message lists the items that are there.</param>
        /// <param name="exactMatch">If <c>true</c> (default), the item's text must equal <paramref name="itemText"/> (case-insensitive). If <c>false</c>, the first item whose text contains it (case-insensitive) is chosen.</param>
        /// <returns><c>true</c> if the item is selected afterwards (including when it already was); <c>false</c> if the handle is not a combo box, no item matches, or the selection did not take. Never throws.</returns>
        /// <remarks>
        /// Setting a combo box's selection does not by itself tell the dialog it changed - a
        /// person choosing an item does - so a dialog that reacts to the choice (for example,
        /// an Open dialog re-filtering its file list) would otherwise carry on as if nothing
        /// happened. After selecting, this sends the combo box's parent the two notifications a
        /// person's choice produces: <c>CBN_SELCHANGE</c>, which ordinary dialogs and WinForms
        /// react to, and <c>CBN_SELENDOK</c>, which the Open/Save dialogs act on (they ignore
        /// <c>CBN_SELCHANGE</c> alone). Items are matched by the text the list stores; a list
        /// that draws its own items without storing text cannot be matched.
        /// </remarks>
        [Category("Dialog - Set Values")]
        [Description("Selects an item in a drop-down list by its text and notifies the dialog. Returns True on success; never throws.")]
        public bool SelectComboItem(IntPtr hCombo, string itemText, out string message, bool exactMatch = true)
        {
            message = default;
            try
            {
                if (string.IsNullOrEmpty(itemText))
                {
                    message = "itemText is required.";
                    return false;
                }
                if (!IsWindowNative(hCombo))
                {
                    message = "Invalid or nonexistent control handle.";
                    return false;
                }
                string className = GetWindowClassName(hCombo);
                if (!IsComboBoxClass(className))
                {
                    message = className.Equals("ComboBoxEx32", StringComparison.OrdinalIgnoreCase)
                        ? "This is a ComboBoxEx32; pass the ComboBox inside it (see ListDialogControls)."
                        : "The control is a '" + className + "', not a combo box.";
                    return false;
                }

                if (!TrySend(hCombo, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero, out IntPtr countResult) || countResult.ToInt64() < 0)
                {
                    message = "The combo box did not report its items (it may be hung).";
                    return false;
                }

                int count = (int)countResult.ToInt64();
                var items = new List<string>(count);
                for (int i = 0; i < count; i++)
                    items.Add(ReadComboItem(hCombo, i));

                int index = FindItemIndex(items, itemText, exactMatch);
                if (index < 0)
                {
                    message = "The combo box has no item " + (exactMatch ? "matching" : "containing") + " '" + itemText + "'. Items: " + DescribeItems(items) + ".";
                    return false;
                }

                if (TrySend(hCombo, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero, out IntPtr currentResult) && currentResult.ToInt64() == index)
                {
                    message = null;
                    return true; // already selected - nothing to change or announce
                }

                if (!TrySend(hCombo, CB_SETCURSEL, (IntPtr)index, IntPtr.Zero, out IntPtr setResult) || setResult.ToInt64() != index)
                {
                    message = "The combo box did not select the item.";
                    return false;
                }

                // Tell the parent, as a person choosing the item would: SELCHANGE (the one
                // ordinary dialogs and WinForms react to) and then SELENDOK (the one the
                // Open/Save dialogs act on - SELCHANGE alone leaves their file-type filter
                // unchanged).
                IntPtr parent = GetParent(hCombo);
                if (parent != IntPtr.Zero)
                {
                    uint controlId = (uint)(GetDlgCtrlID(hCombo) & 0xFFFF);
                    bool changeSent = TrySend(parent, WM_COMMAND, (IntPtr)(((long)CBN_SELCHANGE << 16) | controlId), hCombo, out _);
                    bool endOkSent = TrySend(parent, WM_COMMAND, (IntPtr)(((long)CBN_SELENDOK << 16) | controlId), hCombo, out _);
                    if (!changeSent || !endOkSent)
                    {
                        message = "The item was selected, but the dialog did not receive the selection notification (it may be hung), so it may not have reacted to the change.";
                        return false;
                    }
                }

                if (!TrySend(hCombo, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero, out IntPtr afterResult) || afterResult.ToInt64() != index)
                {
                    message = "The selection did not stay on the requested item.";
                    return false;
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SelectComboItem", ex);
                return false;
            }
        }

        #endregion

        #region File Dialogs

        /// <summary>
        /// Types a path into an Open or Save As dialog's "File name" box (<c>WM_SETTEXT</c>,
        /// confirmed by reading it back), without confirming the dialog.
        /// </summary>
        /// <param name="hDialog">The Open/Save dialog, from <see cref="WaitForDialog"/> or <see cref="FindDialog"/>.</param>
        /// <param name="path">The file name or full path. A full path also changes the dialog's folder. Quote each name and separate them with spaces to choose several files in a dialog that allows it. May not be null or empty.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason it failed.</param>
        /// <returns><c>true</c> if the File name box was found and now holds <paramref name="path"/>; <c>false</c> if the dialog has no recognizable File name box, or setting it failed. Never throws.</returns>
        /// <remarks>
        /// Finding the box is structural, not by control ID: it is a direct child with one ID in
        /// some dialogs and buried inside a DirectUI host with no ID at all in others, and every
        /// dialog also contains an Explorer address bar and search box that are edit boxes too.
        /// The address bar and search box are skipped. If your dialog is not recognized, inspect
        /// it with <see cref="ListDialogControls"/> and use <see cref="SetControlText"/> with the
        /// right handle. To confirm, use <see cref="SubmitFileDialog"/>, or
        /// <see cref="ClickDialogButtonById"/> with <c>(int)DialogButton.Ok</c> (the Open/Save
        /// button is always control ID 1).
        /// </remarks>
        [Category("Dialog - File Dialogs")]
        [Description("Types a path into an Open/Save dialog's File name box without confirming it. Returns True on success; never throws.")]
        public bool SetFileDialogPath(IntPtr hDialog, string path, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    message = "path is required.";
                    return false;
                }
                if (!IsWindowNative(hDialog))
                {
                    message = "Invalid or nonexistent dialog handle.";
                    return false;
                }

                ControlNode edit = FindFileNameControl(SnapshotControls(hDialog));
                if (edit == null)
                {
                    message = "No File name box was found - this may not be an Open/Save dialog. Inspect it with ListDialogControls and use SetControlText with the right handle.";
                    return false;
                }

                return SetControlText(edit.Handle, path, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetFileDialogPath", ex);
                return false;
            }
        }

        /// <summary>
        /// Chooses an entry in an Open or Save As dialog's "Save as type" / "Files of type"
        /// list (for example <c>CSV (*.csv)</c>), and tells the dialog.
        /// </summary>
        /// <param name="hDialog">The Open/Save dialog.</param>
        /// <param name="fileTypeText">The entry's text, or part of it. May not be null or empty.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason it failed. When no entry matches, the message lists the entries that are there.</param>
        /// <param name="exactMatch">If <c>true</c> (default), the entry's text must equal <paramref name="fileTypeText"/> (case-insensitive). If <c>false</c>, the first entry containing it is chosen - so <c>*.csv</c> is enough.</param>
        /// <returns><c>true</c> if the entry is selected afterwards; <c>false</c> if the dialog has no recognizable file-type list, or the selection failed. Never throws.</returns>
        /// <remarks>
        /// For a Save As dialog this matters: a name typed without an extension gets the
        /// selected type's extension. Like <see cref="SetFileDialogPath"/>, the list is found
        /// structurally; if it is not recognized, use <see cref="SelectComboItem"/> with a
        /// handle from <see cref="ListDialogControls"/>.
        /// </remarks>
        [Category("Dialog - File Dialogs")]
        [Description("Chooses an entry in an Open/Save dialog's file-type list (exact or substring match). Returns True on success; never throws.")]
        public bool SelectFileDialogFileType(IntPtr hDialog, string fileTypeText, out string message, bool exactMatch = true)
        {
            message = default;
            try
            {
                if (string.IsNullOrEmpty(fileTypeText))
                {
                    message = "fileTypeText is required.";
                    return false;
                }
                if (!IsWindowNative(hDialog))
                {
                    message = "Invalid or nonexistent dialog handle.";
                    return false;
                }

                ControlNode combo = FindFileTypeCombo(SnapshotControls(hDialog));
                if (combo == null)
                {
                    message = "No file-type list was found - this may not be an Open/Save dialog, or it has none. Inspect it with ListDialogControls and use SelectComboItem with the right handle.";
                    return false;
                }

                return SelectComboItem(combo.Handle, fileTypeText, out message, exactMatch);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SelectFileDialogFileType", ex);
                return false;
            }
        }

        /// <summary>
        /// Fills in an Open or Save As dialog and confirms it in one call: types the path,
        /// clicks the dialog's Open/Save button, and waits for the dialog to close.
        /// </summary>
        /// <param name="hDialog">The Open/Save dialog.</param>
        /// <param name="path">The file name or full path; see <see cref="SetFileDialogPath"/>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason it did not complete.</param>
        /// <param name="closeTimeoutMs">How long to wait for the dialog to close after confirming, in milliseconds (default 5000). Must be zero or positive, and at most <c>300000</c> (5 minutes).</param>
        /// <returns><c>true</c> if the path was entered, the button clicked, and the dialog closed within the timeout; <c>false</c> otherwise (check <paramref name="message"/>). Never throws.</returns>
        /// <remarks>
        /// <para>
        /// A dialog that stays open is not necessarily a failure to click: Windows asks first
        /// when a Save As would overwrite an existing file, and reports an error for an Open of
        /// a file that does not exist, in both cases leaving the dialog open behind a second
        /// message box. That is reported as a <c>false</c> return with a message saying so; find
        /// the message box with <see cref="FindDialog"/> or <see cref="WaitForDialog"/> and answer
        /// it with <see cref="ClickDialogButtonByText"/> (<c>"Yes"</c> to overwrite, <c>"OK"</c> to
        /// dismiss an error). Use the button's text, not its ID: these boxes are laid out by
        /// DirectUI and every button in them has control ID 0, so
        /// <see cref="ClickDialogButtonById"/> cannot find them. The box's title and button text
        /// are in the language of the operating system.
        /// </para>
        /// <para>
        /// A closed dialog means it was confirmed, not that the file exists or was written:
        /// closing on Cancel looks the same, and this clicks only Open/Save (control ID 1).
        /// </para>
        /// </remarks>
        [Category("Dialog - File Dialogs")]
        [Description("Types a path into an Open/Save dialog, clicks Open/Save, and waits for it to close. Returns True if it closed; never throws.")]
        public bool SubmitFileDialog(IntPtr hDialog, string path, out string message, int closeTimeoutMs = 5000)
        {
            message = default;
            try
            {
                if (closeTimeoutMs < 0 || closeTimeoutMs > MaxFileDialogCloseTimeoutMs)
                {
                    message = "closeTimeoutMs must be between 0 and " + MaxFileDialogCloseTimeoutMs + " (5 minutes).";
                    return false;
                }
                if (!SetFileDialogPath(hDialog, path, out message))
                    return false;

                IntPtr hConfirm = GetDlgItem(hDialog, (int)DialogButton.Ok);
                if (hConfirm == IntPtr.Zero)
                {
                    message = "The path was entered, but the dialog has no Open/Save button (control ID 1) to click.";
                    return false;
                }
                if (!ClickButton(hConfirm))
                {
                    message = "The path was entered, but the Open/Save button was not enabled (the dialog may not accept that name), so the click may have been ignored.";
                    return false;
                }

                if (!WaitForDialogToClose(hDialog, closeTimeoutMs, 50))
                {
                    message = "The dialog was still open " + closeTimeoutMs + " ms after clicking Open/Save. It may have asked to confirm an overwrite, reported that the file or folder does not exist, or rejected the name - look for another dialog.";
                    return false;
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SubmitFileDialog", ex);
                return false;
            }
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

            /// <summary>
            /// The control's visible text (button label, static text, edit field contents, etc.).
            /// Always empty for a password edit box (see <see cref="IsPassword"/>) - its contents
            /// are deliberately not read, so listing a dialog's controls into a log cannot leak them.
            /// </summary>
            public string Text { get; set; }

            /// <summary>
            /// <c>true</c> if the control is a password edit box (<c>ES_PASSWORD</c>), whose text is
            /// therefore left out of <see cref="Text"/>. Only standard Win32/WinForms password boxes
            /// can be recognized; a custom-drawn or browser-rendered password field is not.
            /// </summary>
            public bool IsPassword { get; set; }

            /// <summary>The control's window class name (e.g. <c>Button</c>, <c>Static</c>, <c>Edit</c>).</summary>
            public string ClassName { get; set; }

            /// <summary>Whether the control is currently enabled (<c>IsWindowEnabled</c>); a disabled <c>Button</c> silently ignores <c>BM_CLICK</c>.</summary>
            public bool Enabled { get; set; }

            /// <inheritdoc />
            public override string ToString() => $"[{Id}] {ClassName}: {(IsPassword ? "(password)" : "\"" + Text + "\"")}{(Enabled ? "" : " (disabled)")}";
        }

        /// <summary>
        /// Lists every control on a dialog — including controls nested inside another
        /// control, such as a group box — with its ID, text, and window class. Use this
        /// to discover a dialog's controls when you don't already know their IDs or text,
        /// then <see cref="HighlightControl"/> a handle from the results to confirm which
        /// control on screen it corresponds to.
        /// </summary>
        /// <remarks>
        /// The text of a password edit box (<c>ES_PASSWORD</c>) is not read: its
        /// <see cref="DialogControlInfo.Text"/> is empty and <see cref="DialogControlInfo.IsPassword"/>
        /// is <c>true</c>. Windows hands a password box's real contents to another process that
        /// asks for them, and a control list is exactly the kind of thing that ends up in a log.
        /// <see cref="GetControlText"/> on a specific handle still returns the real contents, as an
        /// explicit request.
        /// </remarks>
        [Category("Dialog - Discover & Highlight")]
        [Description("Lists every control on a dialog with its control ID, text, and window class name. Password boxes are listed without their text.")]
        public List<DialogControlInfo> ListDialogControls(IntPtr hDialog)
        {
            var controls = new List<DialogControlInfo>();
            foreach (var child in GetChildWindows(hDialog))
            {
                string className = GetWindowClassName(child);
                bool isPassword = IsPasswordEdit(className, GetWindowLong(child, GWL_STYLE));
                controls.Add(new DialogControlInfo
                {
                    Handle = child,
                    Id = GetDlgCtrlID(child),
                    // Not read at all for a password box, rather than read and then blanked.
                    Text = isPassword ? string.Empty : GetControlText(child),
                    IsPassword = isPassword,
                    ClassName = className,
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
        /// <param name="color">
        /// Color of the rectangle. Because the rectangle uses XOR drawing, the visible
        /// color depends on what is under it.
        /// </param>
        /// <returns><c>true</c> if the control was highlighted; <c>false</c> if <c>GetWindowRect</c>, <c>GetDC</c>, or pen creation failed (e.g. an invalid handle), or the first draw failed. Never throws.</returns>
        /// <remarks>
        /// Caveats:
        ///  - Blocks the calling thread for roughly <c>flashes × 2 × flashMs</c>
        ///    (~1.2 s with the defaults) — don't call it from a thread that must keep
        ///    repainting.
        ///  - If the control repaints while the rectangle is visible, the highlight pixels
        ///    may leave artifacts that the second XOR pass cannot fully erase.
        ///  - Does not draw over exclusive fullscreen (DirectX) applications.
        ///  - The XOR blend means the apparent color varies by background.
        ///  - Coordinates come from <c>GetWindowRect</c> and the screen DC, so under DPI
        ///    virtualization (a process that isn't DPI-aware while the display is scaled)
        ///    the rectangle can be drawn offset from the control.
        /// </remarks>
        [Category("Dialog - Discover & Highlight")]
        [Description("Flashes an inverting rectangle around a control to visually confirm which on-screen control a handle corresponds to. Returns True on success; never throws.")]
        public bool HighlightControl(IntPtr hControl, System.Drawing.Color color, int flashes = 3, int flashMs = 200, int lineWidth = 3)
        {
            if (flashes < 1) flashes = 1;
            if (flashMs < 1) flashMs = 1;
            if (lineWidth < 1) lineWidth = 1;

            if (!GetWindowRect(hControl, out RECT rc))
                return false;

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return false;

            uint colorRef = (uint)(color.R | (color.G << 8) | (color.B << 16));
            IntPtr hPen = CreatePen(PS_SOLID, lineWidth, colorRef);
            if (hPen == IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, hdc);
                return false;
            }

            IntPtr hOldPen = IntPtr.Zero;
            IntPtr hOldBrush = IntPtr.Zero;

            try
            {
                hOldPen = SelectObject(hdc, hPen);
                hOldBrush = SelectObject(hdc, GetStockObject(NULL_BRUSH));
                if (hOldPen == IntPtr.Zero || hOldBrush == IntPtr.Zero)
                    return false;

                SetROP2(hdc, R2_NOTXORPEN);

                for (int i = 0; i < flashes; i++)
                {
                    // Draw (visible) — XOR. If the first draw fails (e.g. the control
                    // moved off-screen), nothing was drawn, so report failure.
                    if (!Rectangle(hdc, rc.Left, rc.Top, rc.Right, rc.Bottom))
                        return false;
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

            return true;
        }

        #endregion

        #region Wait-for-Dialog Polling

        /// <summary>Polls for a visible top-level dialog matching <paramref name="titlePattern"/> until it appears or the timeout elapses.</summary>
        /// <param name="titlePattern">The title to match. Null or empty never matches.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="hWnd">The matching dialog's handle, or <see cref="IntPtr.Zero"/> if not found in time.</param>
        /// <param name="processId">
        /// When non-zero, only windows owned by this process ID are considered — see
        /// <see cref="FindDialog"/>. 0 (the default) matches any process.
        /// </param>
        /// <param name="exactMatch">
        /// If <c>true</c>, requires an exact (case-insensitive) title match. If <c>false</c>
        /// matches any window whose title contains <paramref name="titlePattern"/>
        /// (also case-insensitive). This value is required so the matching behavior is
        /// explicit at every call site.
        /// </param>
        /// <returns><c>true</c> if a matching dialog was found before the timeout.</returns>
        [Category("Dialog - Wait for Dialog")]
        [Description("Polls for a visible top-level dialog matching a title pattern (exact or substring, optionally scoped to a process ID) until it appears or the timeout elapses.")]
        public bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd, bool exactMatch, int processId = 0)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (true)
            {
                if (FindDialog(titlePattern, out IntPtr found, out _, exactMatch, processId))
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

        /// <summary>
        /// Max time (ms) <c>SendMessageTimeout</c> waits for a click handler to return;
        /// combined with <c>SMTO_ABORTIFHUNG</c> the call returns immediately if the target
        /// thread is hung, so a frozen target application can never block the caller.
        /// </summary>
        private const uint BM_CLICK_TIMEOUT_MS = 2000;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        private const uint WM_SETTEXT = 0x000C;
        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;
        private const uint WM_COMMAND = 0x0111;
        private const uint BM_GETCHECK = 0x00F0;
        private const uint CB_GETCOUNT = 0x0146;
        private const uint CB_GETCURSEL = 0x0147;
        private const uint CB_GETLBTEXT = 0x0148;
        private const uint CB_GETLBTEXTLEN = 0x0149;
        private const uint CB_SETCURSEL = 0x014E;
        private const int CBN_SELCHANGE = 1;
        private const int CBN_SELENDOK = 9;
        private const int GWL_STYLE = -16;

        /// <summary>How long a control gets to answer a text or state message before the call gives up.</summary>
        private const uint TEXT_MESSAGE_TIMEOUT_MS = 1000;

        /// <summary>The most characters <see cref="GetControlText"/> reads from one control.</summary>
        private const int MaxReadTextChars = 1 << 20;

        /// <summary>How long to wait for a click to change a check box's state before deciding it did nothing.</summary>
        private const int CheckStateSettleMs = 300;

        /// <summary>The most <see cref="SubmitFileDialog"/> will wait for the dialog to close (5 minutes).</summary>
        private const int MaxFileDialogCloseTimeoutMs = 300000;

        private const int WS_VISIBLE = 0x10000000;
        private const int CBS_TYPE_MASK = 0x0003;
        private const int CBS_DROPDOWNLIST = 0x0003;
        private const int BS_TYPE_MASK = 0x000F;
        private const int BS_OWNERDRAW = 0x000B;
        private const int ES_PASSWORD = 0x0020;
        private const int ES_READONLY = 0x0800;

        // Control IDs in the common Open/Save dialogs.
        private const int FileNameEditIdOldStyle = 1152;   // edt1
        private const int FileNameEditIdOpen = 1148;       // cmb13's inner edit
        private const int FileTypeComboId = 1136;          // cmb1
        private const int FileListBoxId = 1120;            // lst1

        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeoutText(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeoutString(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

        /// <summary>
        /// Enumerates the visible top-level windows whose title matches
        /// <paramref name="titlePattern"/>, applying the same visibility, process-ID
        /// scoping, and exact/substring rules as <see cref="FindDialog"/>. A null or
        /// empty pattern matches nothing (no Win32 calls are made).
        /// </summary>
        private List<IntPtr> GetMatchingTopLevelWindows(string titlePattern, bool exactMatch, int processId)
        {
            var matches = new List<IntPtr>();
            if (string.IsNullOrEmpty(titlePattern))
                return matches;

            if (processId < 0) processId = 0;

            foreach (var hWnd in GetTopLevelWindows())
            {
                if (!IsWindowVisible(hWnd))
                    continue;

                if (processId != 0)
                {
                    GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                    if (windowProcessId != (uint)processId)
                        continue;
                }

                // Non-blocking on purpose: this visits every visible top-level window on the
                // desktop, including ones whose application may be unresponsive.
                if (TitleMatches(GetWindowTextNonBlocking(hWnd), titlePattern, exactMatch))
                    matches.Add(hWnd);
            }
            return matches;
        }

        private static bool TitleMatches(string title, string titlePattern, bool exactMatch)
        {
            return exactMatch
                ? string.Equals(title, titlePattern, StringComparison.OrdinalIgnoreCase)
                : title.IndexOf(titlePattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<IntPtr> GetChildWindows(IntPtr hWndParent)
        {
            var children = new List<IntPtr>();

            // EnumChildWindows(NULL, ...) is defined to enumerate every top-level window on
            // the desktop. A null dialog handle must mean "no dialog, so no controls", never
            // "every window of every application".
            if (hWndParent == IntPtr.Zero)
                return children;

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

        private static bool TrySend(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            return SendMessageTimeout(hWnd, msg, wParam, lParam, SMTO_ABORTIFHUNG, TEXT_MESSAGE_TIMEOUT_MS, out result) != IntPtr.Zero;
        }

        // ---- check boxes and radio buttons ----

        internal enum CheckableKind { None, CheckBox, ThreeState, Radio }

        /// <summary>
        /// What kind of check box or radio button a <c>Button</c>'s window style describes
        /// (the low four bits, <c>BS_*</c>), or <see cref="CheckableKind.None"/> for a push
        /// button, group box, or owner-drawn button.
        /// </summary>
        internal static CheckableKind ClassifyCheckable(int style)
        {
            switch (style & BS_TYPE_MASK)
            {
                case 2:  // BS_CHECKBOX
                case 3:  // BS_AUTOCHECKBOX
                    return CheckableKind.CheckBox;
                case 5:  // BS_3STATE
                case 6:  // BS_AUTO3STATE
                    return CheckableKind.ThreeState;
                case 4:  // BS_RADIOBUTTON
                case 9:  // BS_AUTORADIOBUTTON
                    return CheckableKind.Radio;
                default:
                    return CheckableKind.None;
            }
        }

        // A native Button, or a WinForms button-family control (WindowsForms10.BUTTON.app...),
        // which superclasses it and answers the same messages.
        internal static bool IsButtonClass(string className)
        {
            return !string.IsNullOrEmpty(className)
                && (className.Equals("Button", StringComparison.OrdinalIgnoreCase)
                    || className.IndexOf(".BUTTON.", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // A native Edit, or a WinForms TextBox (WindowsForms10.EDIT.app...), which superclasses it.
        internal static bool IsEditClass(string className)
        {
            return !string.IsNullOrEmpty(className)
                && (className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
                    || className.IndexOf(".EDIT.", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Whether a control is a password edit box: an edit control with <c>ES_PASSWORD</c>
        /// set. The style bit alone is not enough - <c>0x20</c> means something else for a
        /// button or a combo box - so the class is checked first.
        /// </summary>
        internal static bool IsReadOnlyEdit(string className, int style)
            => IsEditClass(className) && (style & ES_READONLY) != 0;

        internal static bool IsPasswordEdit(string className, int style)
        {
            return IsEditClass(className) && (style & ES_PASSWORD) != 0;
        }

        internal static bool IsComboBoxClass(string className)
        {
            return !string.IsNullOrEmpty(className)
                && (className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)
                    || className.IndexOf(".COMBOBOX.", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TryGetCheckableKind(IntPtr hControl, out CheckableKind kind, out string message)
        {
            kind = CheckableKind.None;
            message = null;
            if (!IsWindowNative(hControl))
            {
                message = "Invalid or nonexistent control handle.";
                return false;
            }

            string className = GetWindowClassName(hControl);
            if (!IsButtonClass(className))
            {
                message = "The control is a '" + className + "', not a check box or radio button.";
                return false;
            }

            int style = GetWindowLong(hControl, GWL_STYLE);
            kind = ClassifyCheckable(style);
            if (kind == CheckableKind.None)
            {
                message = (style & BS_TYPE_MASK) == BS_OWNERDRAW
                    // WinForms draws its own buttons, check boxes, and radio buttons, so there
                    // is no Win32 check state to read or click reliably.
                    ? "The button is owner-drawn (drawn by the application itself, as WinForms does), so it does not report a check state through Win32 - use UIAutomationUtils (IsToggled/Toggle) for it."
                    : "The button is not a check box or radio button (it is a push button or group box).";
                return false;
            }
            return true;
        }

        private static bool TryReadCheckState(IntPtr hControl, out ControlCheckState state, out string message)
        {
            state = ControlCheckState.Unchecked;
            message = null;
            if (!TrySend(hControl, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero, out IntPtr result))
            {
                message = "The control did not report its state (it may be hung).";
                return false;
            }

            switch ((int)result.ToInt64())
            {
                case 1: state = ControlCheckState.Checked; break;
                case 2: state = ControlCheckState.Indeterminate; break;
                default: state = ControlCheckState.Unchecked; break;
            }
            return true;
        }

        // ---- combo boxes ----

        private static string ReadComboItem(IntPtr hCombo, int index)
        {
            if (!TrySend(hCombo, CB_GETLBTEXTLEN, (IntPtr)index, IntPtr.Zero, out IntPtr lengthResult) || lengthResult.ToInt64() < 0)
                return string.Empty;

            var sb = new StringBuilder((int)Math.Min(lengthResult.ToInt64(), MaxReadTextChars) + 1);
            if (SendMessageTimeoutText(hCombo, CB_GETLBTEXT, (IntPtr)index, sb, SMTO_ABORTIFHUNG, TEXT_MESSAGE_TIMEOUT_MS, out _) == IntPtr.Zero)
                return string.Empty;
            return sb.ToString();
        }

        /// <summary>
        /// The index of the first item matching <paramref name="text"/> (case-insensitive:
        /// equal when <paramref name="exactMatch"/>, otherwise containing it), or -1.
        /// </summary>
        internal static int FindItemIndex(IList<string> items, string text, bool exactMatch)
        {
            if (items == null || string.IsNullOrEmpty(text))
                return -1;

            for (int i = 0; i < items.Count; i++)
            {
                string item = items[i] ?? string.Empty;
                bool matches = exactMatch
                    ? string.Equals(item, text, StringComparison.OrdinalIgnoreCase)
                    : item.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                if (matches)
                    return i;
            }
            return -1;
        }

        // A short list of what is there, for an error message: enough to spot a typo
        // without dumping a 500-entry list into a log.
        internal static string DescribeItems(IList<string> items)
        {
            const int Shown = 10;
            if (items == null || items.Count == 0)
                return "(none)";

            var parts = new List<string>();
            for (int i = 0; i < items.Count && i < Shown; i++)
                parts.Add("'" + items[i] + "'");
            string text = string.Join(", ", parts);
            return items.Count > Shown ? text + ", ... (" + items.Count + " in all)" : text;
        }

        // ---- finding the File name box and file-type list in Open/Save dialogs ----

        /// <summary>One control of a dialog, as seen in a snapshot of its control tree.</summary>
        internal sealed class ControlNode
        {
            public ControlNode(IntPtr handle, IntPtr parent, int id, string className, int style)
            {
                Handle = handle;
                Parent = parent;
                Id = id;
                ClassName = className ?? string.Empty;
                Style = style;
            }

            public IntPtr Handle { get; }
            public IntPtr Parent { get; }
            public int Id { get; }
            public string ClassName { get; }
            public int Style { get; }
            public bool IsVisible => (Style & WS_VISIBLE) != 0;
            public bool Is(string className) => ClassName.Equals(className, StringComparison.OrdinalIgnoreCase);
        }

        private static List<ControlNode> SnapshotControls(IntPtr hDialog)
        {
            var nodes = new List<ControlNode>();
            foreach (IntPtr child in GetChildWindows(hDialog))
                nodes.Add(new ControlNode(child, GetParent(child), GetDlgCtrlID(child), GetWindowClassName(child), GetWindowLong(child, GWL_STYLE)));
            return nodes;
        }

        // Every Open/Save dialog has an Explorer address bar and search box, whose edit
        // boxes sit under a WorkerW window; they are never the File name box.
        private static bool IsUnderExplorerBar(ControlNode node, IDictionary<IntPtr, ControlNode> byHandle)
        {
            for (IntPtr parent = node.Parent; parent != IntPtr.Zero && byHandle.TryGetValue(parent, out ControlNode ancestor); parent = ancestor.Parent)
            {
                if (ancestor.Is("WorkerW"))
                    return true;
            }
            return false;
        }

        private static Dictionary<IntPtr, ControlNode> IndexByHandle(IEnumerable<ControlNode> nodes)
        {
            var byHandle = new Dictionary<IntPtr, ControlNode>();
            foreach (ControlNode node in nodes)
                byHandle[node.Handle] = node;
            return byHandle;
        }

        /// <summary>
        /// Whether a control tree is an Open/Save dialog: it contains the folder view
        /// (<c>SHELLDLL_DefView</c>) every such dialog hosts, or the legacy file list box
        /// (ID 1120). Without this, the finders below would take the first edit box in a
        /// combo box in any dialog - a Font dialog's font-name box, say - for the File name box.
        /// </summary>
        internal static bool LooksLikeFileDialog(IReadOnlyList<ControlNode> nodes)
        {
            if (nodes == null)
                return false;

            foreach (ControlNode node in nodes)
            {
                if (node.Is("SHELLDLL_DefView") || (node.Id == FileListBoxId && node.Is("ListBox")))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The File name edit box of an Open/Save dialog, or <c>null</c>. Older dialogs put it
        /// at a fixed ID; newer ones put it in a combo box whose ID differs by dialog and is
        /// sometimes zero, so the fallback is the first edit box inside a combo box that is not
        /// part of the Explorer address bar or search box.
        /// </summary>
        internal static ControlNode FindFileNameControl(IReadOnlyList<ControlNode> nodes)
        {
            if (!LooksLikeFileDialog(nodes))
                return null;

            Dictionary<IntPtr, ControlNode> byHandle = IndexByHandle(nodes);
            var edits = new List<ControlNode>();
            foreach (ControlNode node in nodes)
            {
                if (node.Is("Edit") && node.IsVisible && !IsUnderExplorerBar(node, byHandle))
                    edits.Add(node);
            }

            foreach (int id in new[] { FileNameEditIdOldStyle, FileNameEditIdOpen })
            {
                foreach (ControlNode edit in edits)
                {
                    if (edit.Id == id)
                        return edit;
                }
            }

            foreach (ControlNode edit in edits)
            {
                if (byHandle.TryGetValue(edit.Parent, out ControlNode parent) && parent.Is("ComboBox"))
                    return edit;
            }
            return null;
        }

        /// <summary>
        /// The "Save as type"/"Files of type" list of an Open/Save dialog, or <c>null</c>.
        /// A fixed ID in some dialogs; otherwise the first drop-down list (as opposed to the
        /// File name box's editable drop-down) outside the Explorer address bar.
        /// </summary>
        internal static ControlNode FindFileTypeCombo(IReadOnlyList<ControlNode> nodes)
        {
            if (!LooksLikeFileDialog(nodes))
                return null;

            Dictionary<IntPtr, ControlNode> byHandle = IndexByHandle(nodes);
            var combos = new List<ControlNode>();
            foreach (ControlNode node in nodes)
            {
                if (node.Is("ComboBox") && node.IsVisible && !IsUnderExplorerBar(node, byHandle))
                    combos.Add(node);
            }

            foreach (ControlNode combo in combos)
            {
                if (combo.Id == FileTypeComboId)
                    return combo;
            }

            foreach (ControlNode combo in combos)
            {
                bool insideComboBoxEx = byHandle.TryGetValue(combo.Parent, out ControlNode parent) && parent.Is("ComboBoxEx32");
                if ((combo.Style & CBS_TYPE_MASK) == CBS_DROPDOWNLIST && !insideComboBoxEx)
                    return combo;
            }
            return null;
        }

        /// <summary>
        /// Strips the Win32 access-key mnemonic marker from button/label text: a single
        /// <c>&amp;</c> is removed (its following character is the underlined access key),
        /// while a doubled <c>&amp;&amp;</c> — the escape sequence for a literal ampersand —
        /// collapses to one <c>&amp;</c>. E.g. <c>"&amp;Yes"</c> → <c>"Yes"</c>,
        /// <c>"Save &amp;&amp; Exit"</c> → <c>"Save &amp; Exit"</c>.
        /// </summary>
        internal static string StripAccessKeyMnemonic(string text)
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
