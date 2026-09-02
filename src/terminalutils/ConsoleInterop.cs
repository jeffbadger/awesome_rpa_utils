using System;
using System.Runtime.InteropServices;

namespace TerminalAutomation
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SMALL_RECT
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    // The real Win32 CHAR_INFO is a 2-byte union (UnicodeChar/AsciiChar) followed by a
    // 2-byte Attributes field - 4 bytes total. Explicit layout mirrors that exactly; this
    // component always uses the *W (wide/Unicode) console APIs, so only UnicodeChar is read.
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    internal struct CHAR_INFO
    {
        [FieldOffset(0)]
        public char UnicodeChar;
        [FieldOffset(0)]
        public byte AsciiChar;
        [FieldOffset(2)]
        public ushort Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD dwSize;
        public COORD dwCursorPosition;
        public ushort wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
    }

    // The real Win32 KEY_EVENT_RECORD: BOOL bKeyDown (4 bytes) at offset 0, two WORDs
    // (wRepeatCount, wVirtualKeyCode) then wVirtualScanCode, a 2-byte char union, then a
    // trailing DWORD dwControlKeyState - 16 bytes total, matching the real struct's size.
    [StructLayout(LayoutKind.Explicit)]
    internal struct KEY_EVENT_RECORD
    {
        [FieldOffset(0)]
        public int bKeyDown;
        [FieldOffset(4)]
        public ushort wRepeatCount;
        [FieldOffset(6)]
        public ushort wVirtualKeyCode;
        [FieldOffset(8)]
        public ushort wVirtualScanCode;
        [FieldOffset(10)]
        public char UnicodeChar;
        [FieldOffset(10)]
        public byte AsciiChar;
        [FieldOffset(12)]
        public uint dwControlKeyState;
    }

    // The real Win32 INPUT_RECORD: a WORD EventType at offset 0, then 2 bytes of padding
    // (the union that follows is DWORD-aligned because KEY_EVENT_RECORD/MOUSE_EVENT_RECORD
    // start with a BOOL/DWORD), so the event union itself starts at offset 4 - total 20
    // bytes for the KEY_EVENT_RECORD-shaped case this component only ever writes.
    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUT_RECORD
    {
        internal const ushort KEY_EVENT = 0x0001;

        [FieldOffset(0)]
        public ushort EventType;
        [FieldOffset(4)]
        public KEY_EVENT_RECORD KeyEvent;
    }

    /// <summary>
    /// P/Invoke declarations for reading/writing the currently-attached console's screen
    /// buffer and input buffer. Internal - not part of the Pega-facing surface. Attaching to
    /// a target console in the first place is <see cref="ConsoleAttachScope"/>'s
    /// responsibility; every method here operates on whatever console the calling process is
    /// CURRENTLY attached to.
    /// </summary>
    internal static class ConsoleInterop
    {
        internal const int STD_INPUT_HANDLE = -10;
        internal const int STD_OUTPUT_HANDLE = -11;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

        [DllImport("kernel32.dll", EntryPoint = "ReadConsoleOutputW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadConsoleOutputW(IntPtr hConsoleOutput, [Out] CHAR_INFO[] lpBuffer, COORD dwBufferSize, COORD dwBufferCoord, ref SMALL_RECT lpReadRegion);

        [DllImport("kernel32.dll", EntryPoint = "WriteConsoleInputW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteConsoleInputW(IntPtr hConsoleInput, [In] INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsWritten);
    }
}
