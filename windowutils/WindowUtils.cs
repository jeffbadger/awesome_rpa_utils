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
        public IntPtr FindWindowByClass(string className)
        {
            return FindWindowNative(className, null);
        }

        /// <summary>
        /// Finds all top-level windows owned by the given process ID (a process can own
        /// more than one top-level window).
        /// </summary>
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
        public IntPtr GetForegroundWindow()
        {
            return GetForegroundWindowNative();
        }

        #endregion

        #region State & Geometry

        /// <summary>Gets the screen-space bounding rectangle of a window.</summary>
        /// <exception cref="Win32Exception">GetWindowRect failed.</exception>
        public System.Drawing.Rectangle GetWindowBounds(IntPtr hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT rect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.");
            return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        /// <summary>Moves and/or resizes a window to the given screen-space rectangle.</summary>
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is negative.</exception>
        /// <exception cref="Win32Exception">MoveWindow failed.</exception>
        public void SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height)
        {
            if (width < 0 || height < 0)
                throw new ArgumentException("width and height must be non-negative.");
            if (!MoveWindowNative(hWnd, left, top, width, height, true))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "MoveWindow failed.");
        }

        /// <summary>Moves a window to a new position without changing its size.</summary>
        /// <exception cref="Win32Exception">GetWindowRect or MoveWindow failed.</exception>
        public void MoveWindow(IntPtr hWnd, int left, int top)
        {
            var bounds = GetWindowBounds(hWnd);
            SetWindowBounds(hWnd, left, top, bounds.Width, bounds.Height);
        }

        /// <summary>Resizes a window without changing its position.</summary>
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is negative.</exception>
        /// <exception cref="Win32Exception">GetWindowRect or MoveWindow failed.</exception>
        public void ResizeWindow(IntPtr hWnd, int width, int height)
        {
            var bounds = GetWindowBounds(hWnd);
            SetWindowBounds(hWnd, bounds.Left, bounds.Top, width, height);
        }

        /// <summary>Gets a window's title text (empty string if it has none).</summary>
        public string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Gets a window's window-class name.</summary>
        public string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Gets the process ID that owns a window.</summary>
        public int GetWindowProcessId(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            return (int)processId;
        }

        /// <summary>Returns <c>true</c> if the window is visible.</summary>
        public bool IsWindowVisible(IntPtr hWnd)
        {
            return IsWindowVisibleNative(hWnd);
        }

        /// <summary>
        /// Returns <c>true</c> if the window is responding to messages (the inverse of
        /// <c>IsHungAppWindow</c>).
        /// </summary>
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
        public void SetWindowState(IntPtr hWnd, ShowWindowCommand command)
        {
            ShowWindowNative(hWnd, (int)command);
        }

        /// <summary>Asks a window to close by posting <c>WM_CLOSE</c> to it.</summary>
        /// <exception cref="Win32Exception">PostMessage failed.</exception>
        public void CloseWindow(IntPtr hWnd)
        {
            if (!PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PostMessage(WM_CLOSE) failed.");
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
