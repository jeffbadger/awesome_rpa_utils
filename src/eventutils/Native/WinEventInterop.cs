using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EventAutomation.Native
{
    /// <summary>
    /// All P/Invoke signatures and constants used by the event engine. Kept in
    /// one place so the interop surface is auditable. No public API exposes
    /// <see cref="IntPtr"/> — handles cross the component boundary as <c>uint</c>.
    /// </summary>
    internal static class WinEventInterop
    {
        // ---- WinEvent constants -------------------------------------------------

        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_SYSTEM_MENUSTART = 0x0004;
        public const uint EVENT_SYSTEM_MENUEND = 0x0005;
        public const uint EVENT_SYSTEM_MENUPOPUPSTART = 0x0006;
        public const uint EVENT_SYSTEM_MENUPOPUPEND = 0x0007;
        public const uint EVENT_SYSTEM_MOVESIZE = 0x000A;
        public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        public const uint EVENT_SYSTEM_DIALOGSTART = 0x0010;
        public const uint EVENT_SYSTEM_DIALOGEND = 0x0011;
        public const uint EVENT_SYSTEM_SWITCHSTART = 0x0012;
        public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
        public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;

        public const uint EVENT_OBJECT_CREATE = 0x8000;
        public const uint EVENT_OBJECT_DESTROY = 0x8001;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_OBJECT_HIDE = 0x8003;
        public const uint EVENT_OBJECT_FOCUS = 0x8005;
        public const uint EVENT_OBJECT_STATECHANGE = 0x800A;
        public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
        public const uint EVENT_OBJECT_VALUECHANGE = 0x800E;

        /// <summary>
        /// The single hook range covers the SYSTEM range (0x0003..0x0017) and the
        /// OBJECT range (0x8000..0x800E). EVENT_OBJECT_LOCATIONCHANGE (0x800B) is
        /// deliberately NOT subscribed in any category mask — see README.
        /// </summary>
        public const uint EVENT_MIN = EVENT_SYSTEM_FOREGROUND;
        public const uint EVENT_MAX = EVENT_OBJECT_VALUECHANGE;

        // ---- Hook flags ---------------------------------------------------------

        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        // ---- Object ids ---------------------------------------------------------

        public const int OBJID_WINDOW = 0;
        public const int OBJID_CLIENT = -4;

        // ---- Window styles / messages ------------------------------------------

        public const int GWL_STYLE = -16;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_MINIMIZE = 0x20000000;
        public const int WS_DISABLED = 0x08000000;
        public const uint WM_NULL = 0x0000;
        public const uint WM_QUIT = 0x0012;

        // ---- Process access -----------------------------------------------------

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // ---- Delegates ----------------------------------------------------------

        public delegate void WinEventProcDelegate(IntPtr hHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idThread, uint dwmsEventTime);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // ---- user32 -------------------------------------------------------------

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProcDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        // ---- kernel32 -----------------------------------------------------------

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        // ---- Structs ------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }
    }
}
