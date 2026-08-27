using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace KeyboardAutomation
{
    /// <summary>
    /// Identifies a keyboard key by its Windows virtual-key code (<c>VK_*</c>).
    /// </summary>
    public enum VirtualKey
    {
        /// <summary>Backspace (VK_BACK, 0x08).</summary>
        Back = 0x08,
        /// <summary>Tab (VK_TAB, 0x09).</summary>
        Tab = 0x09,
        /// <summary>Enter / Return (VK_RETURN, 0x0D).</summary>
        Enter = 0x0D,
        /// <summary>Shift, either side (VK_SHIFT, 0x10).</summary>
        Shift = 0x10,
        /// <summary>Control, either side (VK_CONTROL, 0x11).</summary>
        Control = 0x11,
        /// <summary>Alt / Menu, either side (VK_MENU, 0x12).</summary>
        Alt = 0x12,
        /// <summary>Pause/Break (VK_PAUSE, 0x13).</summary>
        Pause = 0x13,
        /// <summary>Caps Lock (VK_CAPITAL, 0x14).</summary>
        CapsLock = 0x14,
        /// <summary>Escape (VK_ESCAPE, 0x1B).</summary>
        Escape = 0x1B,
        /// <summary>Spacebar (VK_SPACE, 0x20).</summary>
        Space = 0x20,
        /// <summary>Page Up (VK_PRIOR, 0x21).</summary>
        PageUp = 0x21,
        /// <summary>Page Down (VK_NEXT, 0x22).</summary>
        PageDown = 0x22,
        /// <summary>End (VK_END, 0x23).</summary>
        End = 0x23,
        /// <summary>Home (VK_HOME, 0x24).</summary>
        Home = 0x24,
        /// <summary>Left arrow (VK_LEFT, 0x25).</summary>
        Left = 0x25,
        /// <summary>Up arrow (VK_UP, 0x26).</summary>
        Up = 0x26,
        /// <summary>Right arrow (VK_RIGHT, 0x27).</summary>
        Right = 0x27,
        /// <summary>Down arrow (VK_DOWN, 0x28).</summary>
        Down = 0x28,
        /// <summary>Print Screen (VK_SNAPSHOT, 0x2C).</summary>
        PrintScreen = 0x2C,
        /// <summary>Insert (VK_INSERT, 0x2D).</summary>
        Insert = 0x2D,
        /// <summary>Delete (VK_DELETE, 0x2E).</summary>
        Delete = 0x2E,
        /// <summary>Top-row digit 0 (0x30).</summary>
        D0 = 0x30,
        /// <summary>Top-row digit 1 (0x31).</summary>
        D1 = 0x31,
        /// <summary>Top-row digit 2 (0x32).</summary>
        D2 = 0x32,
        /// <summary>Top-row digit 3 (0x33).</summary>
        D3 = 0x33,
        /// <summary>Top-row digit 4 (0x34).</summary>
        D4 = 0x34,
        /// <summary>Top-row digit 5 (0x35).</summary>
        D5 = 0x35,
        /// <summary>Top-row digit 6 (0x36).</summary>
        D6 = 0x36,
        /// <summary>Top-row digit 7 (0x37).</summary>
        D7 = 0x37,
        /// <summary>Top-row digit 8 (0x38).</summary>
        D8 = 0x38,
        /// <summary>Top-row digit 9 (0x39).</summary>
        D9 = 0x39,
        /// <summary>Letter A (0x41).</summary>
        A = 0x41,
        /// <summary>Letter B (0x42).</summary>
        B = 0x42,
        /// <summary>Letter C (0x43).</summary>
        C = 0x43,
        /// <summary>Letter D (0x44).</summary>
        D = 0x44,
        /// <summary>Letter E (0x45).</summary>
        E = 0x45,
        /// <summary>Letter F (0x46).</summary>
        F = 0x46,
        /// <summary>Letter G (0x47).</summary>
        G = 0x47,
        /// <summary>Letter H (0x48).</summary>
        H = 0x48,
        /// <summary>Letter I (0x49).</summary>
        I = 0x49,
        /// <summary>Letter J (0x4A).</summary>
        J = 0x4A,
        /// <summary>Letter K (0x4B).</summary>
        K = 0x4B,
        /// <summary>Letter L (0x4C).</summary>
        L = 0x4C,
        /// <summary>Letter M (0x4D).</summary>
        M = 0x4D,
        /// <summary>Letter N (0x4E).</summary>
        N = 0x4E,
        /// <summary>Letter O (0x4F).</summary>
        O = 0x4F,
        /// <summary>Letter P (0x50).</summary>
        P = 0x50,
        /// <summary>Letter Q (0x51).</summary>
        Q = 0x51,
        /// <summary>Letter R (0x52).</summary>
        R = 0x52,
        /// <summary>Letter S (0x53).</summary>
        S = 0x53,
        /// <summary>Letter T (0x54).</summary>
        T = 0x54,
        /// <summary>Letter U (0x55).</summary>
        U = 0x55,
        /// <summary>Letter V (0x56).</summary>
        V = 0x56,
        /// <summary>Letter W (0x57).</summary>
        W = 0x57,
        /// <summary>Letter X (0x58).</summary>
        X = 0x58,
        /// <summary>Letter Y (0x59).</summary>
        Y = 0x59,
        /// <summary>Letter Z (0x5A).</summary>
        Z = 0x5A,
        /// <summary>Left Windows key (VK_LWIN, 0x5B).</summary>
        LWin = 0x5B,
        /// <summary>Right Windows key (VK_RWIN, 0x5C).</summary>
        RWin = 0x5C,
        /// <summary>Numpad 0 (0x60).</summary>
        Numpad0 = 0x60,
        /// <summary>Numpad 1 (0x61).</summary>
        Numpad1 = 0x61,
        /// <summary>Numpad 2 (0x62).</summary>
        Numpad2 = 0x62,
        /// <summary>Numpad 3 (0x63).</summary>
        Numpad3 = 0x63,
        /// <summary>Numpad 4 (0x64).</summary>
        Numpad4 = 0x64,
        /// <summary>Numpad 5 (0x65).</summary>
        Numpad5 = 0x65,
        /// <summary>Numpad 6 (0x66).</summary>
        Numpad6 = 0x66,
        /// <summary>Numpad 7 (0x67).</summary>
        Numpad7 = 0x67,
        /// <summary>Numpad 8 (0x68).</summary>
        Numpad8 = 0x68,
        /// <summary>Numpad 9 (0x69).</summary>
        Numpad9 = 0x69,
        /// <summary>Numpad multiply / * (VK_MULTIPLY, 0x6A).</summary>
        Multiply = 0x6A,
        /// <summary>Numpad add / + (VK_ADD, 0x6B).</summary>
        Add = 0x6B,
        /// <summary>Numpad separator (VK_SEPARATOR, 0x6C).</summary>
        Separator = 0x6C,
        /// <summary>Numpad subtract / - (VK_SUBTRACT, 0x6D).</summary>
        Subtract = 0x6D,
        /// <summary>Numpad decimal / . (VK_DECIMAL, 0x6E).</summary>
        Decimal = 0x6E,
        /// <summary>Numpad divide / (VK_DIVIDE, 0x6F).</summary>
        Divide = 0x6F,
        /// <summary>F1 (0x70).</summary>
        F1 = 0x70,
        /// <summary>F2 (0x71).</summary>
        F2 = 0x71,
        /// <summary>F3 (0x72).</summary>
        F3 = 0x72,
        /// <summary>F4 (0x73).</summary>
        F4 = 0x73,
        /// <summary>F5 (0x74).</summary>
        F5 = 0x74,
        /// <summary>F6 (0x75).</summary>
        F6 = 0x75,
        /// <summary>F7 (0x76).</summary>
        F7 = 0x76,
        /// <summary>F8 (0x77).</summary>
        F8 = 0x77,
        /// <summary>F9 (0x78).</summary>
        F9 = 0x78,
        /// <summary>F10 (0x79).</summary>
        F10 = 0x79,
        /// <summary>F11 (0x7A).</summary>
        F11 = 0x7A,
        /// <summary>F12 (0x7B).</summary>
        F12 = 0x7B,
        /// <summary>F13 (0x7C).</summary>
        F13 = 0x7C,
        /// <summary>F14 (0x7D).</summary>
        F14 = 0x7D,
        /// <summary>F15 (0x7E).</summary>
        F15 = 0x7E,
        /// <summary>F16 (0x7F).</summary>
        F16 = 0x7F,
        /// <summary>F17 (0x80).</summary>
        F17 = 0x80,
        /// <summary>F18 (0x81).</summary>
        F18 = 0x81,
        /// <summary>F19 (0x82).</summary>
        F19 = 0x82,
        /// <summary>F20 (0x83).</summary>
        F20 = 0x83,
        /// <summary>F21 (0x84).</summary>
        F21 = 0x84,
        /// <summary>F22 (0x85).</summary>
        F22 = 0x85,
        /// <summary>F23 (0x86).</summary>
        F23 = 0x86,
        /// <summary>F24 (0x87).</summary>
        F24 = 0x87,
        /// <summary>Num Lock (VK_NUMLOCK, 0x90).</summary>
        NumLock = 0x90,
        /// <summary>Scroll Lock (VK_SCROLL, 0x91).</summary>
        ScrollLock = 0x91,
        /// <summary>Left Shift specifically (VK_LSHIFT, 0xA0).</summary>
        LShift = 0xA0,
        /// <summary>Right Shift specifically (VK_RSHIFT, 0xA1).</summary>
        RShift = 0xA1,
        /// <summary>Left Control specifically (VK_LCONTROL, 0xA2).</summary>
        LControl = 0xA2,
        /// <summary>Right Control specifically (VK_RCONTROL, 0xA3).</summary>
        RControl = 0xA3,
        /// <summary>Left Alt specifically (VK_LMENU, 0xA4).</summary>
        LAlt = 0xA4,
        /// <summary>Right Alt specifically (VK_RMENU, 0xA5).</summary>
        RAlt = 0xA5
    }

    /// <summary>
    /// Modifier keys combinable in <see cref="KeyboardUtils.PressKeyWithModifiers"/> and
    /// reported by <see cref="KeyboardUtils.GetActiveModifiers"/>.
    /// </summary>
    [Flags]
    public enum ModifierKeys
    {
        /// <summary>No modifiers.</summary>
        None = 0,
        /// <summary>Control.</summary>
        Control = 1,
        /// <summary>Shift.</summary>
        Shift = 2,
        /// <summary>Alt.</summary>
        Alt = 4,
        /// <summary>Windows key.</summary>
        Win = 8
    }

    /// <summary>
    /// Pega Robot Studio-ready component that injects keyboard input (key presses,
    /// combos, text typing) using the Windows <c>SendInput</c> API, and reads
    /// keyboard/modifier state via <c>GetAsyncKeyState</c>.
    /// </summary>
    [Description("Injects keyboard input: key presses, combos, text typing, and " +
                 "clipboard-paste. Drag this component onto a Pega Robot Studio " +
                 "automation to use its methods.")]
    public class KeyboardUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public KeyboardUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public KeyboardUtils(IContainer container)
        {
            container?.Add(this);
        }

        #region Core Press/Hold/Combo

        /// <summary>Presses and holds a key. Pair with <see cref="KeyUp"/>.</summary>
        /// <exception cref="Win32Exception">Input injection failed.</exception>
        public void KeyDown(VirtualKey key)
        {
            SendInputs(new[] { MakeKeyInput((int)key, false) });
        }

        /// <summary>Releases a key previously pressed with <see cref="KeyDown"/>.</summary>
        /// <exception cref="Win32Exception">Input injection failed.</exception>
        public void KeyUp(VirtualKey key)
        {
            SendInputs(new[] { MakeKeyInput((int)key, true) });
        }

        /// <summary>Presses and releases a key (~20 ms between down and up).</summary>
        /// <exception cref="Win32Exception">Input injection failed.</exception>
        public void PressKey(VirtualKey key)
        {
            KeyDown(key);
            Thread.Sleep(20);
            KeyUp(key);
        }

        /// <summary>
        /// Presses a key while holding modifier keys (Control/Shift/Alt/Win, combinable),
        /// injected as one atomic <c>SendInput</c> batch so real user input cannot
        /// interleave mid-sequence.
        /// </summary>
        /// <exception cref="Win32Exception">Input injection failed.</exception>
        public void PressKeyWithModifiers(VirtualKey key, ModifierKeys modifiers)
        {
            var batch = new List<INPUT>();

            if ((modifiers & ModifierKeys.Control) != 0) batch.Add(MakeKeyInput((int)VirtualKey.Control, false));
            if ((modifiers & ModifierKeys.Shift) != 0) batch.Add(MakeKeyInput((int)VirtualKey.Shift, false));
            if ((modifiers & ModifierKeys.Alt) != 0) batch.Add(MakeKeyInput((int)VirtualKey.Alt, false));
            if ((modifiers & ModifierKeys.Win) != 0) batch.Add(MakeKeyInput((int)VirtualKey.LWin, false));

            batch.Add(MakeKeyInput((int)key, false));
            batch.Add(MakeKeyInput((int)key, true));

            if ((modifiers & ModifierKeys.Win) != 0) batch.Add(MakeKeyInput((int)VirtualKey.LWin, true));
            if ((modifiers & ModifierKeys.Alt) != 0) batch.Add(MakeKeyInput((int)VirtualKey.Alt, true));
            if ((modifiers & ModifierKeys.Shift) != 0) batch.Add(MakeKeyInput((int)VirtualKey.Shift, true));
            if ((modifiers & ModifierKeys.Control) != 0) batch.Add(MakeKeyInput((int)VirtualKey.Control, true));

            SendInputs(batch.ToArray());
        }

        /// <summary>
        /// Presses all given keys down in order, then releases them in reverse order, as
        /// one atomic <c>SendInput</c> batch (e.g. Ctrl+Shift+Esc, where none of the keys
        /// is a modifier in the <see cref="ModifierKeys"/> flags sense).
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="keys"/> is null or empty.</exception>
        /// <exception cref="Win32Exception">Input injection failed.</exception>
        public void PressKeyCombo(params VirtualKey[] keys)
        {
            if (keys == null || keys.Length == 0)
                throw new ArgumentException("At least one key is required.", nameof(keys));

            var batch = new List<INPUT>();
            foreach (var key in keys)
                batch.Add(MakeKeyInput((int)key, false));
            for (int i = keys.Length - 1; i >= 0; i--)
                batch.Add(MakeKeyInput((int)keys[i], true));

            SendInputs(batch.ToArray());
        }

        /// <summary>Holds a key down for the given duration, then releases it.</summary>
        /// <exception cref="Win32Exception">Input injection failed.</exception>
        public void HoldKey(VirtualKey key, int holdMilliseconds)
        {
            KeyDown(key);
            Thread.Sleep(Math.Max(0, holdMilliseconds));
            KeyUp(key);
        }

        #endregion

        #region Text Typing

        /// <summary>Types a string via <c>KEYEVENTF_UNICODE</c> (default ~10 ms/character).</summary>
        /// <exception cref="ArgumentException"><paramref name="text"/> is null.</exception>
        /// <exception cref="Win32Exception">Input injection failed.</exception>
        public void TypeText(string text)
        {
            TypeText(text, 10);
        }

        /// <summary>
        /// Types a string via <c>KEYEVENTF_UNICODE</c> with a custom per-character delay.
        /// Unicode-safe: characters outside the Basic Multilingual Plane (emoji, some CJK
        /// extension characters) are sent as their UTF-16 surrogate pair, one code unit
        /// per <c>SendInput</c> key-down/up pair.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="text"/> is null.</exception>
        /// <exception cref="Win32Exception">Input injection failed.</exception>
        public void TypeText(string text, int delayMilliseconds)
        {
            if (text == null)
                throw new ArgumentException("Text cannot be null.", nameof(text));

            Span<char> chars = stackalloc char[2];
            foreach (var rune in text.EnumerateRunes())
            {
                if (rune.Utf16SequenceLength == 1)
                {
                    SendInputs(new[]
                    {
                        MakeUnicodeKeyInput((char)rune.Value, false),
                        MakeUnicodeKeyInput((char)rune.Value, true)
                    });
                }
                else
                {
                    rune.EncodeToUtf16(chars);
                    SendInputs(new[]
                    {
                        MakeUnicodeKeyInput(chars[0], false),
                        MakeUnicodeKeyInput(chars[0], true),
                        MakeUnicodeKeyInput(chars[1], false),
                        MakeUnicodeKeyInput(chars[1], true)
                    });
                }

                if (delayMilliseconds > 0)
                    Thread.Sleep(delayMilliseconds);
            }
        }

        #endregion

        #region Clipboard-Paste Fallback

        /// <summary>
        /// Saves the current clipboard text (if any), sets the clipboard to
        /// <paramref name="text"/>, sends Ctrl+V, then restores the original clipboard
        /// contents. Use when synthetic key events are ignored or mangled by the target
        /// (e.g. some IME-backed fields). This uses raw Win32 clipboard calls directly (no
        /// COM/OLE), so unlike <c>System.Windows.Forms.Clipboard</c> it does not require an
        /// STA thread; <c>OpenClipboard</c> can transiently fail if another process (e.g. a
        /// clipboard manager) briefly holds the clipboard open.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="text"/> is null.</exception>
        /// <exception cref="Win32Exception">A clipboard or input injection call failed.</exception>
        public void PasteText(string text)
        {
            if (text == null)
                throw new ArgumentException("Text cannot be null.", nameof(text));

            string original = GetClipboardText();
            bool clipboardWasEmpty = CountClipboardFormats() == 0;
            try
            {
                SetClipboardText(text);
                PressKeyWithModifiers(VirtualKey.V, ModifierKeys.Control);
                Thread.Sleep(50);
            }
            finally
            {
                if (original != null)
                    SetClipboardText(original);
                else if (clipboardWasEmpty)
                    ClearClipboard();
                // else: the clipboard held non-text content we can't restore (e.g. an
                // image) - leave the pasted text in place rather than silently destroying
                // it via EmptyClipboard.
            }
        }

        private static string GetClipboardText()
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                return null;

            if (!OpenClipboard(IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenClipboard failed.");
            try
            {
                IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero)
                    return null;

                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                    return null;
                try
                {
                    return Marshal.PtrToStringUni(pointer);
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static void SetClipboardText(string text)
        {
            if (!OpenClipboard(IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenClipboard failed.");
            try
            {
                EmptyClipboard();

                int byteCount = (text.Length + 1) * 2;
                IntPtr handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
                if (handle == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalAlloc failed.");

                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalLock failed.");
                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                    Marshal.WriteInt16(pointer, text.Length * 2, 0);
                }
                finally
                {
                    GlobalUnlock(handle);
                }

                if (SetClipboardData(CF_UNICODETEXT, handle) == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetClipboardData failed.");
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static void ClearClipboard()
        {
            if (!OpenClipboard(IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenClipboard failed.");
            try
            {
                EmptyClipboard();
            }
            finally
            {
                CloseClipboard();
            }
        }

        #endregion

        #region Win32 Interop

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll")]
        private static extern int CountClipboardFormats();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION U;
        }

        // The union must include MOUSEINPUT even though this component never sends mouse
        // events: Windows' real INPUT struct sizes its union to fit the largest member
        // (MOUSEINPUT), and SendInput's cbSize contract requires our marshaled struct size
        // to match that real size exactly, or the call silently fails.
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        /// <summary>Builds a single INPUT structure for a virtual-key press or release.</summary>
        private static INPUT MakeKeyInput(int virtualKey, bool keyUp)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U.ki = new KEYBDINPUT
            {
                wVk = (ushort)virtualKey,
                wScan = 0,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            };
            return input;
        }

        /// <summary>Builds a single INPUT structure for one UTF-16 code unit, typed via KEYEVENTF_UNICODE.</summary>
        private static INPUT MakeUnicodeKeyInput(char ch, bool keyUp)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U.ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0u),
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            };
            return input;
        }

        /// <summary>
        /// Injects a batch of input events atomically (in order) and fails loudly when
        /// Windows refuses them (locked / secure desktop, UAC prompt, or a target app
        /// running at a higher integrity level - UIPI blocks the injection).
        /// </summary>
        private static void SendInputs(INPUT[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != (uint)inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "SendInput failed to inject the input event(s). (Desktop locked, UAC/secure desktop, or insufficient privileges?)");
        }

        #endregion
    }
}
