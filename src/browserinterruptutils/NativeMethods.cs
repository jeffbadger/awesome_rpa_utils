using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// The Win32 declarations needed for the native-dialog safety-net sweep: enumerating
    /// top-level windows, identifying their class and owning process, and checking whether a
    /// previously seen window still exists. This is a small subset of what
    /// <c>InterruptUtils</c>' own <c>NativeMethods</c> declares (that component also clicks
    /// buttons and reads window text directly over Win32, which this component leaves to UI
    /// Automation instead).
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>Flag for <see cref="GetAncestor"/>: the root window (the top-level owner) of the given window.</summary>
        internal const uint GA_ROOT = 2;

        /// <summary><c>GetWindowLong</c> index of the window style bits.</summary>
        internal const int GWL_STYLE = -16;

        /// <summary>Style bit: the window has a maximize box (a normal resizable application window; JS/system dialogs generally lack it).</summary>
        internal const uint WS_MAXIMIZEBOX = 0x00010000;

        /// <summary>Style bit: the window has a minimize box.</summary>
        internal const uint WS_MINIMIZEBOX = 0x00020000;

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>Process access right: query limited information (enough for the image name and times; granted even for most elevated/protected processes).</summary>
        internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>Win32 error: the buffer given to a query was too small.</summary>
        internal const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        // FILETIME is two 32-bit halves in native memory, exactly a 64-bit integer, so it is bound as long.
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(IntPtr hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        // GetWindowLongPtr only exists as an export in 64-bit user32 (32-bit exposes it as a macro
        // over GetWindowLong), so each is bound separately and GetWindowStyle picks by pointer size.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        /// <summary>The window's style bits (<c>GWL_STYLE</c>), or 0 if they could not be read.</summary>
        internal static uint GetWindowStyle(IntPtr hwnd) =>
            IntPtr.Size == 8
                ? unchecked((uint)GetWindowLongPtr64(hwnd, GWL_STYLE).ToInt64())
                : unchecked((uint)GetWindowLong32(hwnd, GWL_STYLE));

        /// <summary>
        /// Whether a window with these style bits looks like a normal top-level application window
        /// (a minimize or maximize box), as a browser's main window does and a JS/system dialog
        /// generally does not. A style of 0 means the read failed (a live window always has at least
        /// <c>WS_VISIBLE</c>/<c>WS_CLIPSIBLINGS</c>-type bits), and is treated as main-window-like:
        /// when unsure, the answer that refuses to close the window is the safe one.
        /// </summary>
        internal static bool IsMainWindowStyle(uint style) =>
            style == 0 || (style & (WS_MINIMIZEBOX | WS_MAXIMIZEBOX)) != 0;
    }
}
