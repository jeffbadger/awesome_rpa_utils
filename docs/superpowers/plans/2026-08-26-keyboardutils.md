# KeyboardUtils Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `KeyboardUtils` Pega Robot Studio component (assembly `KeyboardAutomation`) that injects keyboard input (key press/hold/combo, text typing, clipboard-paste) and queries keyboard/modifier state, matching the conventions of the existing `mouseutils`/`screencaptureutils` components.

**Architecture:** A single `KeyboardUtils : Component` class in `keyboardutils/KeyboardUtils.cs`, using the same `SendInput`/`INPUT` struct P/Invoke pattern already proven in `mouseutils/MouseUtils.cs`, plus a small raw Win32 clipboard P/Invoke layer (no `System.Windows.Forms` dependency) for the paste fallback. No project references to other components — fully standalone.

**Tech Stack:** .NET 10 (`net10.0-windows`), `System.Runtime.InteropServices` (user32.dll/kernel32.dll P/Invoke), `System.ComponentModel.Component` (Robot Studio designer base class).

**Spec:** See `docs/superpowers/specs/2026-08-26-keyboard-window-ocr-dialog-utils-design.md` (KeyboardUtils section) for the approved design this plan implements.

---

### Task 1: Scaffold the project and add it to the solution

**Files:**
- Create: `keyboardutils/KeyboardUtils.csproj`
- Create: `keyboardutils/KeyboardUtils.cs`
- Modify: `AwesomeRpaUtils.sln` (via `dotnet sln add`)

- [ ] **Step 1: Create the project folder and csproj**

Create `keyboardutils/KeyboardUtils.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AssemblyName>KeyboardAutomation</AssemblyName>
    <RootNamespace>KeyboardAutomation</RootNamespace>
    <Platforms>AnyCPU;x64</Platforms>
    <!-- Emit KeyboardAutomation.xml next to the DLL so the XML doc comments are consumable -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Create a minimal compiling stub class**

Create `keyboardutils/KeyboardUtils.cs`:

```csharp
using System.ComponentModel;

namespace KeyboardAutomation
{
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
    }
}
```

- [ ] **Step 3: Add the project to the solution under its own solution folder**

Run: `dotnet sln AwesomeRpaUtils.sln add keyboardutils/KeyboardUtils.csproj --solution-folder keyboardutils`
Expected: `Project(s) added to the solution.`

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build AwesomeRpaUtils.sln`
Expected: `Build succeeded.` with no errors (Windows host required — this project targets `net10.0-windows`).

- [ ] **Step 5: Commit**

```bash
git add keyboardutils/KeyboardUtils.csproj keyboardutils/KeyboardUtils.cs AwesomeRpaUtils.sln
git commit -m "Scaffold KeyboardUtils project"
```

---

### Task 2: Enums and Win32 SendInput foundation

**Files:**
- Modify: `keyboardutils/KeyboardUtils.cs`

- [ ] **Step 1: Add the `VirtualKey` and `ModifierKeys` enums above the class**

Insert immediately above the `KeyboardUtils` class declaration (after the `using` line, before `namespace` content that defines the class):

```csharp
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

```

Then update the class opening (replace the old bare `using System.ComponentModel;` line and namespace/class declaration lines) to match — the class declaration and constructors stay the same, just now preceded by the enums above and with the fuller `using` list shown above in place of the single `using System.ComponentModel;` line.

- [ ] **Step 2: Add the Win32 interop foundation as a new region inside the class, after the constructors**

```csharp
        #region Win32 Interop

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

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
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build keyboardutils/KeyboardUtils.csproj`
Expected: `Build succeeded.` (enums and interop layer are unused by any public method yet, but must compile clean — no unused-code warnings are treated as errors in this project).

- [ ] **Step 4: Commit**

```bash
git add keyboardutils/KeyboardUtils.cs
git commit -m "Add VirtualKey/ModifierKeys enums and SendInput interop layer to KeyboardUtils"
```

---

### Task 3: Core Press/Hold/Combo methods

**Files:**
- Modify: `keyboardutils/KeyboardUtils.cs`

- [ ] **Step 1: Add the methods as a new region, inserted after the constructors and before the Win32 Interop region**

```csharp
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
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build keyboardutils/KeyboardUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual smoke test**

On a Windows machine, reference the built `KeyboardAutomation.dll` from a throwaway console app (or drop the component onto a Robot Studio design surface), focus a text editor, and call `PressKeyCombo(VirtualKey.Control, VirtualKey.A)` to select-all, then `PressKey(VirtualKey.Delete)`. Confirm the text was selected and deleted.

- [ ] **Step 4: Commit**

```bash
git add keyboardutils/KeyboardUtils.cs
git commit -m "Implement KeyboardUtils core press/hold/combo methods"
```

---

### Task 4: Text typing

**Files:**
- Modify: `keyboardutils/KeyboardUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after Core Press/Hold/Combo**

```csharp
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
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build keyboardutils/KeyboardUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual smoke test**

Focus a text field and call `TypeText("Hello, world! 👋")`. Confirm the literal text (including the emoji) appears.

- [ ] **Step 4: Commit**

```bash
git add keyboardutils/KeyboardUtils.cs
git commit -m "Implement KeyboardUtils text typing"
```

---

### Task 5: Clipboard-paste fallback

**Files:**
- Modify: `keyboardutils/KeyboardUtils.cs`

- [ ] **Step 1: Add clipboard P/Invoke declarations to the Win32 Interop region**

Insert into the existing `#region Win32 Interop` block (anywhere alongside the other `DllImport`s):

```csharp
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
```

Also add `using System;` for `IntPtr`/`UIntPtr` if not already present (it is, from Task 2's `using` list).

- [ ] **Step 2: Add the clipboard helper methods and `PasteText` as a new region, after Text Typing**

```csharp
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
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build keyboardutils/KeyboardUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Manual smoke test**

Copy some known text to the clipboard manually, then focus a text field and call `PasteText("injected text")`. Confirm "injected text" was pasted, then manually paste (Ctrl+V) again and confirm the original clipboard text is back.

- [ ] **Step 5: Commit**

```bash
git add keyboardutils/KeyboardUtils.cs
git commit -m "Implement KeyboardUtils clipboard-paste fallback"
```

---

### Task 6: State query and modifiers

**Files:**
- Modify: `keyboardutils/KeyboardUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after Clipboard-Paste Fallback**

```csharp
        #region State Query & Modifiers

        /// <summary>Returns <c>true</c> while <paramref name="key"/> is currently held down.</summary>
        public bool IsKeyDown(VirtualKey key)
        {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        /// <summary>
        /// Returns <c>true</c> if every modifier flag set in <paramref name="modifier"/> is
        /// currently held down (Win checks both LWin and RWin).
        /// </summary>
        public bool IsModifierDown(ModifierKeys modifier)
        {
            return (GetActiveModifiers() & modifier) == modifier;
        }

        /// <summary>Returns the combination of Ctrl/Shift/Alt/Win currently held, as flags.</summary>
        public ModifierKeys GetActiveModifiers()
        {
            ModifierKeys result = ModifierKeys.None;
            if (IsKeyDown(VirtualKey.Control)) result |= ModifierKeys.Control;
            if (IsKeyDown(VirtualKey.Shift)) result |= ModifierKeys.Shift;
            if (IsKeyDown(VirtualKey.Alt)) result |= ModifierKeys.Alt;
            if (IsKeyDown(VirtualKey.LWin) || IsKeyDown(VirtualKey.RWin)) result |= ModifierKeys.Win;
            return result;
        }

        #endregion
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build keyboardutils/KeyboardUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual smoke test**

Call `IsKeyDown(VirtualKey.Shift)` in a loop while physically holding and releasing Shift; confirm it tracks correctly.

- [ ] **Step 4: Commit**

```bash
git add keyboardutils/KeyboardUtils.cs
git commit -m "Implement KeyboardUtils state query and modifier methods"
```

---

### Task 7: Component README

**Files:**
- Create: `keyboardutils/README.md`

- [ ] **Step 1: Write the README**

Create `keyboardutils/README.md`:

```markdown
# KeyboardAutomation

A Pega Robot Studio-ready component (`KeyboardUtils`) that injects keyboard
input — key presses, holds, combos, and typed text — using the Windows
`SendInput` API, plus a clipboard-paste fallback and keyboard/modifier state
queries via `GetAsyncKeyState`.

- Target framework: `net10.0-windows`
- Namespace: `KeyboardAutomation`
- Assembly: `KeyboardAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

Designed to be used alongside [MouseUtils](../mouseutils/MouseUtils.cs)
(`MouseAutomation`): this component owns keyboard/text input, MouseUtils owns
cursor/click input. Input injection requires an interactive, unlocked
desktop; it is blocked on the lock screen / secure desktop and when the
target application runs at a higher integrity level (UIPI).

## Enums

### `VirtualKey`
A keyboard key identified by its Windows virtual-key code: letters (`A`-`Z`),
digits (`D0`-`D9`), function keys (`F1`-`F24`), navigation keys (`Left`,
`Right`, `Up`, `Down`, `Home`, `End`, `PageUp`, `PageDown`), editing keys
(`Enter`, `Tab`, `Space`, `Back`, `Delete`, `Insert`, `Escape`),
modifier keys (`Control`, `Shift`, `Alt`, and side-specific `LControl`/
`RControl`/`LShift`/`RShift`/`LAlt`/`RAlt`), `LWin`/`RWin`, numpad keys
(`Numpad0`-`Numpad9`, `Multiply`, `Add`, `Subtract`, `Decimal`, `Divide`,
`Separator`), and lock keys (`CapsLock`, `NumLock`, `ScrollLock`).

### `ModifierKeys` (Flags)
Modifier keys combinable in `PressKeyWithModifiers` and reported by
`GetActiveModifiers`: `None`, `Control`, `Shift`, `Alt`, `Win`.

## Constructors

| Constructor | Description |
|---|---|
| `KeyboardUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `KeyboardUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Core Press/Hold/Combo

| Method | Description |
|---|---|
| `void KeyDown(VirtualKey key)` | Presses and holds a key. Pair with `KeyUp`. |
| `void KeyUp(VirtualKey key)` | Releases a key previously pressed with `KeyDown`. |
| `void PressKey(VirtualKey key)` | Presses and releases a key (~20 ms between down and up). |
| `void PressKeyWithModifiers(VirtualKey key, ModifierKeys modifiers)` | Presses a key while holding modifier keys, injected as one atomic batch. |
| `void PressKeyCombo(params VirtualKey[] keys)` | Presses all keys down in order, then releases in reverse order, as one atomic batch (e.g. Ctrl+Shift+Esc). |
| `void HoldKey(VirtualKey key, int holdMilliseconds)` | Holds a key down for the given duration, then releases it. |

### Text Typing

| Method | Description |
|---|---|
| `void TypeText(string text)` | Types a string via `KEYEVENTF_UNICODE` (default ~10 ms/character). |
| `void TypeText(string text, int delayMilliseconds)` | Types a string with a custom per-character delay; handles surrogate pairs for non-BMP characters (e.g. emoji). |

### Clipboard-Paste Fallback

| Method | Description |
|---|---|
| `void PasteText(string text)` | Saves the current clipboard text, sets the clipboard to `text`, sends Ctrl+V, then restores the original clipboard contents. |

### State Query & Modifiers

| Method | Description |
|---|---|
| `bool IsKeyDown(VirtualKey key)` | Returns `true` while the given key is currently held down. |
| `bool IsModifierDown(ModifierKeys modifier)` | Returns `true` if every modifier flag set in the argument is currently held down. |
| `ModifierKeys GetActiveModifiers()` | Returns the combination of Ctrl/Shift/Alt/Win currently held, as flags. |

## Notes & Caveats

- **`PressKeyWithModifiers`/`PressKeyCombo`** inject their entire sequence as a single
  `SendInput` batch, so real user input cannot interleave mid-sequence.
- **`TypeText`** sends one `SendInput` call per UTF-16 code unit; characters outside the
  Basic Multilingual Plane (many emoji, some CJK extension characters) are sent as two
  code units (a surrogate pair), each with its own key-down/up pair.
- **PasteText** uses raw Win32 clipboard calls directly (not System.Windows.Forms.Clipboard),
  so it has no STA-thread requirement; OpenClipboard can transiently fail if another process
  briefly holds the clipboard open. It also only restores the original clipboard when that
  original was plain text or genuinely empty — if the clipboard held other content (an image,
  files), that content can't be restored and the pasted text is left in place instead of being
  silently cleared.
- **`IsKeyDown`/`IsModifierDown`/`GetActiveModifiers`** are point-in-time polls of real
  physical key state via `GetAsyncKeyState` — they do not distinguish real user input from
  this component's own injected input.
```

- [ ] **Step 2: Commit**

```bash
git add keyboardutils/README.md
git commit -m "Add KeyboardUtils README"
```

---

### Task 8: Per-category usage documentation

**Files:**
- Create: `keyboardutils/Documentation/README.md`
- Create: `keyboardutils/Documentation/PressHoldCombo.md`
- Create: `keyboardutils/Documentation/TextTyping.md`
- Create: `keyboardutils/Documentation/ClipboardPaste.md`
- Create: `keyboardutils/Documentation/StateQuery.md`

- [ ] **Step 1: Write the documentation index**

Create `keyboardutils/Documentation/README.md`:

```markdown
# KeyboardUtils Documentation

Real-world usage examples for every method on the `KeyboardUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Press/Hold/Combo](PressHoldCombo.md) — single keys, modifier combos, and holds
- [Text Typing](TextTyping.md) — typing whole strings
- [Clipboard-Paste](ClipboardPaste.md) — the Ctrl+V paste fallback
- [State Query](StateQuery.md) — polling key/modifier state

All examples assume a `KeyboardUtils` instance named `keyboard`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var keyboard = new KeyboardUtils();`).
```

- [ ] **Step 2: Write `PressHoldCombo.md`**

Create `keyboardutils/Documentation/PressHoldCombo.md`:

```markdown
# Press / Hold / Combo

## Press a single key

```csharp
keyboard.PressKey(VirtualKey.Enter);
```

## Select-all and delete

```csharp
keyboard.PressKeyWithModifiers(VirtualKey.A, ModifierKeys.Control);
keyboard.PressKey(VirtualKey.Delete);
```

## A three-key combo (Ctrl+Shift+Esc — open Task Manager)

```csharp
keyboard.PressKeyCombo(VirtualKey.Control, VirtualKey.Shift, VirtualKey.Escape);
```

## Hold a key down for a duration

```csharp
// Holds Tab down for 2 seconds, e.g. to trigger an app's "cycle windows" overlay.
keyboard.HoldKey(VirtualKey.Tab, 2000);
```

## Manual down/up pairing (for a press that spans other actions)

```csharp
keyboard.KeyDown(VirtualKey.Shift);
try
{
    // ... click-drag a selection while Shift is held, via MouseUtils ...
}
finally
{
    keyboard.KeyUp(VirtualKey.Shift);
}
```
```

- [ ] **Step 3: Write `TextTyping.md`**

Create `keyboardutils/Documentation/TextTyping.md`:

```markdown
# Text Typing

## Type a string at the default speed

```csharp
keyboard.TypeText("user@example.com");
```

## Type slowly, for a field that drops fast keystrokes

```csharp
keyboard.TypeText("slow-field-value", 40);
```

## Type text containing an emoji (non-BMP character)

```csharp
keyboard.TypeText("Approved 👍");
```
```

- [ ] **Step 4: Write `ClipboardPaste.md`**

Create `keyboardutils/Documentation/ClipboardPaste.md`:

```markdown
# Clipboard-Paste Fallback

## Paste text into a field that mangles synthetic keystrokes

```csharp
// Useful for IME-backed fields or apps that read raw input directly instead
// of standard keyboard messages.
keyboard.PasteText("some-value-that-typed-badly");
```

The original clipboard contents are restored automatically afterward when they were plain text (or the clipboard was empty). If the clipboard held something else, such as an image, that content can't be restored — the pasted text is left in place instead. See the main [README](../README.md)'s Notes & Caveats section for details.
```

- [ ] **Step 5: Write `StateQuery.md`**

Create `keyboardutils/Documentation/StateQuery.md`:

```markdown
# State Query & Modifiers

## Guard an action on a modifier being held

```csharp
if (keyboard.IsModifierDown(ModifierKeys.Control))
{
    // e.g. skip a confirmation dialog when the operator is holding Ctrl
}
```

## Read the full set of active modifiers

```csharp
ModifierKeys active = keyboard.GetActiveModifiers();
if ((active & ModifierKeys.Shift) != 0)
{
    // ...
}
```

## Poll for a specific key being released before continuing

```csharp
while (keyboard.IsKeyDown(VirtualKey.Escape))
{
    System.Threading.Thread.Sleep(50);
}
```
```

- [ ] **Step 6: Commit**

```bash
git add keyboardutils/Documentation
git commit -m "Add KeyboardUtils per-category usage documentation"
```

---

### Task 9: Update the root README

**Files:**
- Modify: `README.md:9-12`

- [ ] **Step 1: Add a row to the components table**

In the root `README.md`, change:

```markdown
| Component | Assembly | Description |
|---|---|---|
| [mouseutils](mouseutils/README.md) | `MouseAutomation` | Moves, clicks, drags, and scrolls the mouse via `SendInput`/`SetCursorPos`; controls cursor appearance, visibility, and confinement. |
| [screencaptureutils](screencaptureutils/README.md) | `ScreenCaptureAutomation` | Captures the screen, a region, or a window to a file/clipboard; compares captures against a baseline; annotates or redacts saved screenshots. |
```

to:

```markdown
| Component | Assembly | Description |
|---|---|---|
| [mouseutils](mouseutils/README.md) | `MouseAutomation` | Moves, clicks, drags, and scrolls the mouse via `SendInput`/`SetCursorPos`; controls cursor appearance, visibility, and confinement. |
| [screencaptureutils](screencaptureutils/README.md) | `ScreenCaptureAutomation` | Captures the screen, a region, or a window to a file/clipboard; compares captures against a baseline; annotates or redacts saved screenshots. |
| [keyboardutils](keyboardutils/README.md) | `KeyboardAutomation` | Injects keyboard input via `SendInput`: key presses, combos, typed text, and a clipboard-paste fallback; queries key/modifier state. |
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "List KeyboardUtils in the root README"
```

---

### Task 10: Final solution build

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build AwesomeRpaUtils.sln`
Expected: `Build succeeded.` with all four projects (`MouseAutomation`, `ScreenCaptureAutomation`, `KeyboardAutomation`, and any others already added) compiling cleanly.

- [ ] **Step 2: Confirm nothing was left uncommitted**

Run: `git status`
Expected: `nothing to commit, working tree clean`
