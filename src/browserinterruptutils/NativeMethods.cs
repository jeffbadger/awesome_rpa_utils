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

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    }
}
