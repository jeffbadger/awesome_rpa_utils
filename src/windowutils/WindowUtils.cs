using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WindowAutomation
{
    /// <summary>
    /// The window-state command applied by <see cref="WindowUtils.SetWindowState"/>,
    /// wrapping the Win32 <c>SW_*</c> constants used by <c>ShowWindow</c>.
    /// </summary>
    public enum ShowWindowCommand
    {
        /// <summary>Hides the window (SW_HIDE, 0).</summary>
        Hide = 0,
        /// <summary>Shows the window in its normal (restored) state (SW_SHOWNORMAL, 1).</summary>
        Normal = 1,
        /// <summary>Shows the window maximized (SW_SHOWMAXIMIZED, 3).</summary>
        Maximized = 3,
        /// <summary>Minimizes the window without activating another window (SW_MINIMIZE, 6).</summary>
        Minimized = 6,
        /// <summary>Restores a minimized/maximized window to its previous size and position (SW_RESTORE, 9).</summary>
        Restore = 9
    }

    /// <summary>
    /// Pega Robot Studio-ready component that enumerates, locates, moves/resizes,
    /// activates, and closes windows using the Win32 window APIs.
    /// </summary>
    [Description("Finds, moves, resizes, activates, and closes windows. Drag this " +
                 "component onto a Pega Robot Studio automation to use its methods.")]
    public class WindowUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public WindowUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public WindowUtils(IContainer container)
        {
            container?.Add(this);
        }

        /// <summary>
        /// Standard component cleanup override. WindowUtils holds no unmanaged resources;
        /// implementing the pattern keeps the designer-generated teardown complete.
        /// </summary>
        /// <param name="disposing">
        /// True when called from the public Dispose() method during teardown;
        /// false when called from the finalizer.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // No managed or unmanaged resources to release.
            }

            // Base Component.Dispose detaches this component from its container's site.
            base.Dispose(disposing);
        }

        #region Enumeration & Lookup

        /// <summary>Gets all top-level windows via <c>EnumWindows</c>.</summary>
        /// <remarks>
        /// <c>EnumWindows</c> returns hidden and cloaked (visually hidden UWP) windows as
        /// well as visible ones; filter with <see cref="IsWindowVisible"/> if only visible
        /// windows are wanted.
        /// </remarks>
        [Category("Window - Enumeration & Lookup")]
        [Description("Gets all top-level windows currently open.")]
        public List<IntPtr> GetTopLevelWindows()
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
        /// Finds a top-level window by its title. Returns <see cref="IntPtr.Zero"/> if no
        /// window matches (not found is a normal, checkable outcome, not an error).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Matches hidden windows too; filter with <see cref="IsWindowVisible"/> if only
        /// visible windows are wanted. A null or empty <paramref name="title"/> returns
        /// <see cref="IntPtr.Zero"/> — an empty substring would otherwise match the first
        /// window in enumeration order.
        /// </para>
        /// </remarks>
        /// <param name="title">The title to match; may not be null or empty.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact, case-sensitive title match.
        /// If <c>false</c>, matches any window whose title contains <paramref name="title"/>
        /// (case-insensitive).
        /// </param>
        [Category("Window - Enumeration & Lookup")]
        [Description("Finds a top-level window by its title (exact or substring match).")]
        public IntPtr FindWindowByTitle(string title, bool exactMatch = true)
        {
            if (string.IsNullOrEmpty(title))
                return IntPtr.Zero;

            foreach (var hWnd in GetTopLevelWindows())
            {
                string windowTitle = GetWindowTitle(hWnd);
                bool matches = exactMatch
                    ? string.Equals(windowTitle, title, StringComparison.Ordinal)
                    : windowTitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0;
                if (matches)
                    return hWnd;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Finds the first top-level window of the given window class. Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        /// <remarks>
        /// Match is case-insensitive (the Win32 <c>FindWindow</c> behavior), unlike
        /// <see cref="FindWindowByTitle"/> with <c>exactMatch: true</c>, which is
        /// case-sensitive.
        /// </remarks>
        [Category("Window - Enumeration & Lookup")]
        [Description("Finds the first top-level window of the given window class.")]
        public IntPtr FindWindowByClass(string className)
        {
            return FindWindowNative(className, null);
        }

        /// <summary>
        /// Finds all top-level windows owned by the given process ID (a process can own
        /// more than one top-level window).
        /// </summary>
        [Category("Window - Enumeration & Lookup")]
        [Description("Finds all top-level windows owned by the given process ID.")]
        public List<IntPtr> FindWindowsByProcessId(int processId)
        {
            var matches = new List<IntPtr>();
            foreach (var hWnd in GetTopLevelWindows())
            {
                if (GetWindowProcessId(hWnd) == processId)
                    matches.Add(hWnd);
            }
            return matches;
        }

        /// <summary>
        /// Finds the first top-level window owned by the given process ID, in enumeration
        /// order, for the common single-window case. Returns <see cref="IntPtr.Zero"/> if
        /// none matches. Use <see cref="FindWindowsByProcessId"/> when the process may own
        /// more than one top-level window and all of them are needed.
        /// </summary>
        [Category("Window - Enumeration & Lookup")]
        [Description("Finds the first top-level window owned by the given process ID.")]
        public IntPtr FindFirstWindowByProcessId(int processId)
        {
            foreach (var hWnd in GetTopLevelWindows())
            {
                if (GetWindowProcessId(hWnd) == processId)
                    return hWnd;
            }
            return IntPtr.Zero;
        }

        /// <summary>Gets the handle of the current foreground (active) window.</summary>
        [Category("Window - Enumeration & Lookup")]
        [Description("Gets the handle of the current foreground (active) window.")]
        public IntPtr GetForegroundWindow()
        {
            return GetForegroundWindowNative();
        }

        #endregion

        #region State & Geometry

        /// <summary>Gets the screen-space bounding rectangle of a window.</summary>
        /// <param name="hWnd">Handle of the window to measure.</param>
        /// <param name="bounds">The window's bounds, or <c>default</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if GetWindowRect failed (e.g. an invalid handle). Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Gets the screen-space bounding rectangle of a window. Returns True on success; never throws.")]
        public bool GetWindowBounds(IntPtr hWnd, out System.Drawing.Rectangle bounds, out string message)
        {
            bounds = default;
            message = default;
            try
            {
                if (!GetWindowRect(hWnd, out RECT rect))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.").Message;
                    bounds = default;
                    return false;
                }
                bounds = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetWindowBounds", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="GetWindowBounds(IntPtr, out System.Drawing.Rectangle, out string)"/>,
        /// but reports the bounds as scalar left/top/width/height outputs, for designers
        /// without a <c>Rectangle</c> proxy.
        /// </summary>
        /// <param name="hWnd">Handle of the window to measure.</param>
        /// <param name="left">Left edge of the window, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="top">Top edge of the window, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="width">Width of the window, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="height">Height of the window, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if GetWindowRect failed (e.g. an invalid handle). Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Gets the screen-space bounding rectangle of a window as scalar left/top/width/height. Returns True on success; never throws.")]
        public bool GetWindowBounds(IntPtr hWnd, out int left, out int top, out int width, out int height, out string message)
        {
            left = default;
            top = default;
            width = default;
            height = default;
            bool ok = GetWindowBounds(hWnd, out System.Drawing.Rectangle bounds, out message);
            left = bounds.Left;
            top = bounds.Top;
            width = bounds.Width;
            height = bounds.Height;
            return ok;
        }

        /// <summary>Moves and/or resizes a window to the given screen-space rectangle.</summary>
        /// <param name="hWnd">Handle of the window to move/resize.</param>
        /// <param name="left">New left edge in screen pixels.</param>
        /// <param name="top">New top edge in screen pixels.</param>
        /// <param name="width">New width in pixels; must be non-negative.</param>
        /// <param name="height">New height in pixels; must be non-negative.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the move/resize failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="width"/>/<paramref name="height"/> are negative, or MoveWindow failed. Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Moves and/or resizes a window to the given screen-space rectangle. Returns True on success; never throws.")]
        public bool SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height, out string message)
        {
            message = default;
            try
            {
                if (width < 0 || height < 0)
                {
                    message = "width and height must be non-negative.";
                    return false;
                }
                if (!MoveWindowNative(hWnd, left, top, width, height, true))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "MoveWindow failed.").Message;
                    return false;
                }
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetWindowBounds", ex);
                return false;
            }
        }

        /// <summary>Moves a window to a new position without changing its size.</summary>
        /// <param name="hWnd">Handle of the window to move.</param>
        /// <param name="left">New left edge in screen pixels.</param>
        /// <param name="top">New top edge in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the move failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if GetWindowRect or MoveWindow failed. Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Moves a window to a new position without changing its size. Returns True on success; never throws.")]
        public bool MoveWindow(IntPtr hWnd, int left, int top, out string message)
        {
            message = default;
            try
            {
                if (!GetWindowBounds(hWnd, out System.Drawing.Rectangle bounds, out message))
                    return false;
                return SetWindowBounds(hWnd, left, top, bounds.Width, bounds.Height, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("MoveWindow", ex);
                return false;
            }
        }

        /// <summary>Resizes a window without changing its position.</summary>
        /// <param name="hWnd">Handle of the window to resize.</param>
        /// <param name="width">New width in pixels; must be non-negative.</param>
        /// <param name="height">New height in pixels; must be non-negative.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the resize failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="width"/>/<paramref name="height"/> are negative, or GetWindowRect/MoveWindow failed. Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Resizes a window without changing its position. Returns True on success; never throws.")]
        public bool ResizeWindow(IntPtr hWnd, int width, int height, out string message)
        {
            message = default;
            try
            {
                if (!GetWindowBounds(hWnd, out System.Drawing.Rectangle bounds, out message))
                    return false;
                return SetWindowBounds(hWnd, bounds.Left, bounds.Top, width, height, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ResizeWindow", ex);
                return false;
            }
        }

        /// <summary>Gets a window's title text (empty string if it has none).</summary>
        /// <remarks>
        /// Reads the title once via the length-then-read pattern; a title that grows
        /// between the two Win32 calls (rare) is truncated at the length observed first.
        /// </remarks>
        [Category("Window - State & Geometry")]
        [Description("Gets a window's title text.")]
        public string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// Same as <see cref="GetWindowTitle"/>, but distinguishes an invalid/nonexistent
        /// window handle from a legitimately empty title via the return value, instead of
        /// collapsing both to an empty string.
        /// </summary>
        /// <param name="hWnd">Handle of the window to read.</param>
        /// <param name="title">The window's title text (possibly empty), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if <paramref name="hWnd"/> is a valid, currently-existing window; <c>false</c> otherwise. Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Gets a window's title text, distinguishing an invalid handle from a legitimately empty title. Returns True on success; never throws.")]
        public bool TryGetWindowTitle(IntPtr hWnd, out string title, out string message)
        {
            title = default;
            message = default;
            try
            {
                title = null;
                if (!IsWindowNative(hWnd))
                {
                    message = "Invalid or nonexistent window handle.";
                    return false;
                }
                title = GetWindowTitle(hWnd);
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("TryGetWindowTitle", ex);
                return false;
            }
        }

        /// <summary>Gets a window's window-class name.</summary>
        [Category("Window - State & Geometry")]
        [Description("Gets a window's window-class name.")]
        public string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// Same as <see cref="GetWindowClassName"/>, but distinguishes an invalid/nonexistent
        /// window handle from a legitimately empty class name via the return value.
        /// </summary>
        /// <param name="hWnd">Handle of the window to read.</param>
        /// <param name="className">The window's window-class name, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if <paramref name="hWnd"/> is a valid, currently-existing window; <c>false</c> otherwise. Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Gets a window's window-class name, distinguishing an invalid handle from a legitimately empty class name. Returns True on success; never throws.")]
        public bool TryGetWindowClassName(IntPtr hWnd, out string className, out string message)
        {
            className = default;
            message = default;
            try
            {
                className = null;
                if (!IsWindowNative(hWnd))
                {
                    message = "Invalid or nonexistent window handle.";
                    return false;
                }
                className = GetWindowClassName(hWnd);
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("TryGetWindowClassName", ex);
                return false;
            }
        }

        /// <summary>Gets the process ID that owns a window.</summary>
        [Category("Window - State & Geometry")]
        [Description("Gets the process ID that owns a window.")]
        public int GetWindowProcessId(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            return (int)processId;
        }

        /// <summary>
        /// Same as <see cref="GetWindowProcessId"/>, but distinguishes an invalid/nonexistent
        /// window handle from process ID 0 (the System Idle Process, which owns no windows
        /// in practice, but this makes the distinction explicit rather than relying on that)
        /// via the return value.
        /// </summary>
        /// <param name="hWnd">Handle of the window to read.</param>
        /// <param name="processId">The process ID that owns the window, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if <paramref name="hWnd"/> is a valid, currently-existing window; <c>false</c> otherwise. Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Gets the process ID that owns a window, distinguishing an invalid handle from process ID 0. Returns True on success; never throws.")]
        public bool TryGetWindowProcessId(IntPtr hWnd, out int processId, out string message)
        {
            processId = default;
            message = default;
            try
            {
                if (!IsWindowNative(hWnd))
                {
                    message = "Invalid or nonexistent window handle.";
                    return false;
                }
                processId = GetWindowProcessId(hWnd);
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("TryGetWindowProcessId", ex);
                return false;
            }
        }

        /// <summary>Returns <c>true</c> if the window is visible.</summary>
        [Category("Window - State & Geometry")]
        [Description("Returns True if the window is visible.")]
        public bool IsWindowVisible(IntPtr hWnd)
        {
            return IsWindowVisibleNative(hWnd);
        }

        /// <summary>
        /// Returns <c>true</c> if the window is responding to messages (the inverse of
        /// <c>IsHungAppWindow</c>).
        /// </summary>
        [Category("Window - State & Geometry")]
        [Description("Returns True if the window is responding to messages.")]
        public bool IsWindowResponding(IntPtr hWnd)
        {
            return !IsHungAppWindowNative(hWnd);
        }

        /// <summary>
        /// Applies a show/hide/minimize/maximize/restore state to a window.
        /// </summary>
        /// <remarks>
        /// <c>ShowWindow</c>'s return value reports whether the window was PREVIOUSLY
        /// visible, not whether this call succeeded — there is nothing meaningful to
        /// check or throw on, so this method never throws.
        /// </remarks>
        [Category("Window - State & Geometry")]
        [Description("Applies a show/hide/minimize/maximize/restore state to a window.")]
        public void SetWindowState(IntPtr hWnd, ShowWindowCommand command)
        {
            ShowWindowNative(hWnd, (int)command);
        }

        /// <summary>Asks a window to close by posting <c>WM_CLOSE</c> to it.</summary>
        /// <param name="hWnd">Handle of the window to close.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the request failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if PostMessage failed. Never throws.</returns>
        [Category("Window - State & Geometry")]
        [Description("Asks a window to close by posting WM_CLOSE to it. Returns True on success; never throws.")]
        public bool CloseWindow(IntPtr hWnd, out string message)
        {
            message = default;
            try
            {
                if (!PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "PostMessage(WM_CLOSE) failed.").Message;
                    return false;
                }
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CloseWindow", ex);
                return false;
            }
        }

        #endregion

        #region Activation & Z-Order

        /// <summary>Brings a window to the foreground and gives it input focus.</summary>
        /// <param name="hWnd">Handle of the window to activate.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason activation failed.</param>
        /// <returns>
        /// <c>true</c> on success; <c>false</c> if SetForegroundWindow failed. Windows'
        /// foreground-lock rules can block activation requested from a background process
        /// that isn't the user's currently active app. Never throws.
        /// </returns>
        [Category("Window - Activation & Z-Order")]
        [Description("Brings a window to the foreground and gives it input focus. Returns True on success; never throws.")]
        public bool ActivateWindow(IntPtr hWnd, out string message)
        {
            message = default;
            try
            {
                if (!SetForegroundWindowNative(hWnd))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(),
                        "SetForegroundWindow failed. (Windows' foreground-lock rules can block activation from a background process.)").Message;
                    return false;
                }
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ActivateWindow", ex);
                return false;
            }
        }

        /// <summary>
        /// Makes a window always-on-top (or removes that state), system-wide and
        /// session-persistent until changed again.
        /// </summary>
        /// <param name="hWnd">Handle of the window to change.</param>
        /// <param name="alwaysOnTop"><c>true</c> to make the window always-on-top; <c>false</c> to remove that state.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the change failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if SetWindowPos failed. Never throws.</returns>
        [Category("Window - Activation & Z-Order")]
        [Description("Makes a window always-on-top (or removes that state). Returns True on success; never throws.")]
        public bool SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop, out string message)
        {
            message = default;
            try
            {
                IntPtr insertAfter = alwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST;
                if (!SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos failed.").Message;
                    return false;
                }
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetAlwaysOnTop", ex);
                return false;
            }
        }

        /// <summary>
        /// Polls for a top-level window matching <paramref name="title"/> (substring,
        /// case-insensitive) until it appears or the timeout elapses.
        /// </summary>
        /// <param name="title">The window title substring to match (case-insensitive); may not be null or empty.</param>
        /// <param name="timeoutMs">Maximum time to poll, in milliseconds.</param>
        /// <param name="pollIntervalMs">Time to sleep between polls, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="hWnd">The matching window's handle, or <see cref="IntPtr.Zero"/> if not found in time.</param>
        /// <returns>
        /// <c>true</c> if a matching window was found before the timeout; <c>false</c> on
        /// timeout or if <paramref name="title"/> is null or empty (an empty substring
        /// would match the first window in enumeration order, so polling is refused).
        /// </returns>
        [Category("Window - Activation & Z-Order")]
        [Description("Polls for a window matching the title until it appears or the timeout elapses.")]
        public bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)
        {
            return WaitForWindow(title, timeoutMs, pollIntervalMs, out hWnd, out _);
        }

        /// <summary>
        /// Same as <see cref="WaitForWindow(string, int, int, out IntPtr)"/>, but also
        /// reports why a <c>false</c> return happened via <paramref name="message"/>, so the
        /// automation can distinguish a genuine timeout from an invalid <paramref name="title"/>
        /// without inferring it from <paramref name="hWnd"/> alone.
        /// </summary>
        /// <param name="title">The window title substring to match (case-insensitive); may not be null or empty.</param>
        /// <param name="timeoutMs">Maximum time to poll, in milliseconds.</param>
        /// <param name="pollIntervalMs">Time to sleep between polls, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="hWnd">The matching window's handle, or <see cref="IntPtr.Zero"/> if not found in time.</param>
        /// <param name="message"><c>null</c> if the poll completed (found or genuinely timed out); otherwise a human-readable reason polling was refused (a null/empty <paramref name="title"/>).</param>
        /// <returns>
        /// <c>true</c> if a matching window was found before the timeout; <c>false</c> on
        /// timeout or if <paramref name="title"/> is null or empty (check <paramref name="message"/>
        /// to tell them apart; an empty substring would match the first window in
        /// enumeration order, so polling is refused).
        /// </returns>
        [Category("Window - Activation & Z-Order")]
        [Description("Polls for a window matching the title until it appears or the timeout elapses, reporting why a False return happened.")]
        public bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd, out string message)
        {
            message = default;
            if (string.IsNullOrEmpty(title))
            {
                hWnd = IntPtr.Zero;
                message = "A title is required.";
                return false;
            }

            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (true)
            {
                IntPtr found = FindWindowByTitle(title, exactMatch: false);
                if (found != IntPtr.Zero)
                {
                    hWnd = found;
                    message = null;
                    return true;
                }
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                {
                    hWnd = IntPtr.Zero;
                    message = null;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
            }
        }

        /// <summary>Polls until a window handle is no longer valid (the window closed), or the timeout elapses.</summary>
        /// <param name="hWnd">The window handle to watch.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <returns><c>true</c> if the handle became invalid before the timeout; <c>false</c> if the timeout elapsed first.</returns>
        [Category("Window - Activation & Z-Order")]
        [Description("Polls until a window handle is no longer valid (the window closed).")]
        public bool WaitForWindowToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)
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

        /// <summary>Polls until the given window becomes the foreground window, or the timeout elapses.</summary>
        /// <param name="hWnd">The window handle to watch.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <returns><c>true</c> if the window became active before the timeout; <c>false</c> if the timeout elapsed first.</returns>
        [Category("Window - Activation & Z-Order")]
        [Description("Polls until the given window becomes the foreground window.")]
        public bool WaitForWindowActive(IntPtr hWnd, int timeoutMs, int pollIntervalMs)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (GetForegroundWindow() != hWnd)
            {
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    return false;
                Thread.Sleep(pollIntervalMs);
            }
            return true;
        }

        #endregion

        #region Child / Multi-Window Enumeration

        /// <summary>Gets all descendant windows/controls of a parent window (recursively, not just immediate children) via <c>EnumChildWindows</c>.</summary>
        /// <remarks>
        /// Passing <see cref="IntPtr.Zero"/> enumerates all top-level windows (the Win32
        /// <c>EnumChildWindows(NULL, ...)</c> special case) — usually not what a caller
        /// wants, so validate the parent handle first when in doubt.
        /// </remarks>
        [Category("Window - Child Windows")]
        [Description("Gets all descendant windows/controls of a parent window.")]
        public List<IntPtr> GetChildWindows(IntPtr hWndParent)
        {
            var children = new List<IntPtr>();
            EnumChildWindows(hWndParent, (hWnd, lParam) =>
            {
                children.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return children;
        }

        /// <summary>
        /// Finds a child window under <paramref name="hWndParent"/> matching the given title
        /// and/or class name (pass <c>null</c> for either to not filter on it). Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        /// <param name="hWndParent">Handle of the parent window whose descendants are searched.</param>
        /// <param name="title">
        /// Exact title to match, case-sensitively; null (or empty) to not filter on title.
        /// </param>
        /// <param name="className">
        /// Window class name to match, case-sensitively; null (or empty) to not filter on class.
        /// </param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), the title/class filters require exact, case-sensitive matches.
        /// If <c>false</c>, both filters match by case-insensitive substring.
        /// </param>
        [Category("Window - Child Windows")]
        [Description("Finds a child window matching the given title and/or class name (exact or substring match).")]
        public IntPtr FindChildWindow(IntPtr hWndParent, string title, string className, bool exactMatch = true)
        {
            foreach (var child in GetChildWindows(hWndParent))
            {
                bool titleMatches = string.IsNullOrEmpty(title)
                    || Matches(GetWindowTitle(child), title, exactMatch);
                bool classMatches = string.IsNullOrEmpty(className)
                    || Matches(GetWindowClassName(child), className, exactMatch);
                if (titleMatches && classMatches)
                    return child;
            }
            return IntPtr.Zero;
        }

        private static bool Matches(string value, string filter, bool exactMatch)
        {
            return exactMatch
                ? string.Equals(value, filter, StringComparison.Ordinal)
                : value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region Win32 Interop

        private const uint WM_CLOSE = 0x0010;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowNative(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static extern IntPtr GetForegroundWindowNative();

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindowNative(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "MoveWindow", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveWindowNative(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

        [DllImport("user32.dll", EntryPoint = "ShowWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowNative(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisibleNative(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowNative(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "IsHungAppWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsHungAppWindowNative(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion
    }
}
