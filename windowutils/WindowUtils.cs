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

        #region Enumeration & Lookup

        /// <summary>Gets all top-level windows via <c>EnumWindows</c>.</summary>
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
        /// <param name="title">The title to match.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact, case-sensitive title match.
        /// If <c>false</c>, matches any window whose title contains <paramref name="title"/>
        /// (case-insensitive).
        /// </param>
        [Category("Window - Enumeration & Lookup")]
        [Description("Finds a top-level window by its title (exact or substring match).")]
        public IntPtr FindWindowByTitle(string title, bool exactMatch = true)
        {
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
        /// <exception cref="Win32Exception">GetWindowRect failed.</exception>
        [Category("Window - State & Geometry")]
        [Description("Gets the screen-space bounding rectangle of a window.")]
        public System.Drawing.Rectangle GetWindowBounds(IntPtr hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT rect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.");
            return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        /// <summary>Moves and/or resizes a window to the given screen-space rectangle.</summary>
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is negative.</exception>
        /// <exception cref="Win32Exception">MoveWindow failed.</exception>
        [Category("Window - State & Geometry")]
        [Description("Moves and/or resizes a window to the given screen-space rectangle.")]
        public void SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height)
        {
            if (width < 0 || height < 0)
                throw new ArgumentException("width and height must be non-negative.");
            if (!MoveWindowNative(hWnd, left, top, width, height, true))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "MoveWindow failed.");
        }

        /// <summary>Moves a window to a new position without changing its size.</summary>
        /// <exception cref="Win32Exception">GetWindowRect or MoveWindow failed.</exception>
        [Category("Window - State & Geometry")]
        [Description("Moves a window to a new position without changing its size.")]
        public void MoveWindow(IntPtr hWnd, int left, int top)
        {
            var bounds = GetWindowBounds(hWnd);
            SetWindowBounds(hWnd, left, top, bounds.Width, bounds.Height);
        }

        /// <summary>Resizes a window without changing its position.</summary>
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is negative.</exception>
        /// <exception cref="Win32Exception">GetWindowRect or MoveWindow failed.</exception>
        [Category("Window - State & Geometry")]
        [Description("Resizes a window without changing its position.")]
        public void ResizeWindow(IntPtr hWnd, int width, int height)
        {
            var bounds = GetWindowBounds(hWnd);
            SetWindowBounds(hWnd, bounds.Left, bounds.Top, width, height);
        }

        /// <summary>Gets a window's title text (empty string if it has none).</summary>
        [Category("Window - State & Geometry")]
        [Description("Gets a window's title text.")]
        public string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
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

        /// <summary>Gets the process ID that owns a window.</summary>
        [Category("Window - State & Geometry")]
        [Description("Gets the process ID that owns a window.")]
        public int GetWindowProcessId(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            return (int)processId;
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
        /// <exception cref="Win32Exception">PostMessage failed.</exception>
        [Category("Window - State & Geometry")]
        [Description("Asks a window to close by posting WM_CLOSE to it.")]
        public void CloseWindow(IntPtr hWnd)
        {
            if (!PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PostMessage(WM_CLOSE) failed.");
        }

        #endregion

        #region Activation & Z-Order

        /// <summary>Brings a window to the foreground and gives it input focus.</summary>
        /// <exception cref="Win32Exception">
        /// SetForegroundWindow failed. Windows' foreground-lock rules can block activation
        /// requested from a background process that isn't the user's currently active app.
        /// </exception>
        [Category("Window - Activation & Z-Order")]
        [Description("Brings a window to the foreground and gives it input focus.")]
        public void ActivateWindow(IntPtr hWnd)
        {
            if (!SetForegroundWindowNative(hWnd))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "SetForegroundWindow failed. (Windows' foreground-lock rules can block activation from a background process.)");
        }

        /// <summary>
        /// Makes a window always-on-top (or removes that state), system-wide and
        /// session-persistent until changed again.
        /// </summary>
        /// <exception cref="Win32Exception">SetWindowPos failed.</exception>
        [Category("Window - Activation & Z-Order")]
        [Description("Makes a window always-on-top (or removes that state).")]
        public void SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop)
        {
            IntPtr insertAfter = alwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST;
            if (!SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos failed.");
        }

        /// <summary>
        /// Polls for a top-level window matching <paramref name="title"/> (substring,
        /// case-insensitive) until it appears or the timeout elapses.
        /// </summary>
        /// <param name="title">The window title substring to match (case-insensitive).</param>
        /// <param name="timeoutMs">Maximum time to poll, in milliseconds.</param>
        /// <param name="pollIntervalMs">Time to sleep between polls, in milliseconds.</param>
        /// <param name="hWnd">The matching window's handle, or <see cref="IntPtr.Zero"/> if not found in time.</param>
        /// <returns><c>true</c> if a matching window was found before the timeout.</returns>
        [Category("Window - Activation & Z-Order")]
        [Description("Polls for a window matching the title until it appears or the timeout elapses.")]
        public bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (true)
            {
                IntPtr found = FindWindowByTitle(title, exactMatch: false);
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
        [Category("Window - Child Windows")]
        [Description("Finds a child window matching the given title and/or class name.")]
        public IntPtr FindChildWindow(IntPtr hWndParent, string title, string className)
        {
            foreach (var child in GetChildWindows(hWndParent))
            {
                bool titleMatches = title == null || GetWindowTitle(child) == title;
                bool classMatches = className == null || GetWindowClassName(child) == className;
                if (titleMatches && classMatches)
                    return child;
            }
            return IntPtr.Zero;
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
