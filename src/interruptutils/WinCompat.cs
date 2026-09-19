using System;
using System.Runtime.InteropServices;

namespace InterruptAutomation
{
    // Net48 compatibility shim for Environment.TickCount64, which net48 lacks.
    // Same underlying tick source either way (kernel32 GetTickCount64).
    internal static class WinCompat
    {
#if NETFRAMEWORK
        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern ulong GetTickCount64();

        internal static long TickCount64 => unchecked((long)GetTickCount64());
#else
        internal static long TickCount64 => Environment.TickCount64;
#endif
    }
}