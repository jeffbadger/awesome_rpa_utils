using System;
using System.Runtime.InteropServices;

namespace WinEventAutomation
{
    // Net48 compatibility shims. The net8.0/net10.0 TFMs have these APIs in the
    // BCL directly; net48 does not, so each member branches on NETFRAMEWORK.
    // Call sites always go through this class so the surrounding code reads the
    // same on every TFM and the net8/net10 behavior is unchanged.
    internal static class WinCompat
    {
#if NETFRAMEWORK
        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern ulong GetTickCount64();

        // net48's Marshal has no SetLastWin32Error; go through kernel32's SetLastError.
        // SetLastError = true makes the CLR capture the value into its saved last-error
        // slot, so the following GetLastPInvokeError sees it.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void SetLastError(int errorCode);

        // Same underlying tick source as Environment.TickCount64 (kernel32 GetTickCount64).
        internal static long TickCount64 => unchecked((long)GetTickCount64());
        internal static int ProcessId => System.Diagnostics.Process.GetCurrentProcess().Id;
        internal static bool IsWindows() => Environment.OSVersion.Platform == PlatformID.Win32NT;
        internal static void SetLastPInvokeError(int value) => SetLastError(value);
        internal static int GetLastPInvokeError() => Marshal.GetLastWin32Error();
#else
        internal static long TickCount64 => Environment.TickCount64;
        internal static int ProcessId => Environment.ProcessId;
        internal static bool IsWindows() => OperatingSystem.IsWindows();
        internal static void SetLastPInvokeError(int value) => Marshal.SetLastPInvokeError(value);
        internal static int GetLastPInvokeError() => Marshal.GetLastPInvokeError();
#endif
    }
}