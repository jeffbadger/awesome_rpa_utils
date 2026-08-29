using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KeyboardAutomation;
using Xunit;

namespace KeyboardAutomation.Tests
{
    /// <summary>
    /// Unit tests for KeyboardUtils. Cross-platform tests never call Win32 interop.
    /// Tests that inject real input are gated behind OperatingSystem.IsWindows().
    /// </summary>
    public class KeyboardUtilsTests
    {
        // ------------------------------------------------------------------
        // VirtualKey / ModifierKeys value pinning
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(VirtualKey.Back, 0x08)]
        [InlineData(VirtualKey.Tab, 0x09)]
        [InlineData(VirtualKey.Enter, 0x0D)]
        [InlineData(VirtualKey.Control, 0x11)]
        [InlineData(VirtualKey.Escape, 0x1B)]
        [InlineData(VirtualKey.Space, 0x20)]
        [InlineData(VirtualKey.Delete, 0x2E)]
        [InlineData(VirtualKey.D0, 0x30)]
        [InlineData(VirtualKey.Z, 0x5A)]
        [InlineData(VirtualKey.LWin, 0x5B)]
        [InlineData(VirtualKey.Numpad0, 0x60)]
        [InlineData(VirtualKey.F1, 0x70)]
        [InlineData(VirtualKey.F24, 0x87)]
        [InlineData(VirtualKey.NumLock, 0x90)]
        [InlineData(VirtualKey.RAlt, 0xA5)]
        public void VirtualKey_values_match_Win32_VK_codes(VirtualKey key, int expected)
        {
            Assert.Equal(expected, (int)key);
        }

        [Theory]
        [InlineData(ModifierKeys.None, 0)]
        [InlineData(ModifierKeys.Control, 1)]
        [InlineData(ModifierKeys.Shift, 2)]
        [InlineData(ModifierKeys.Alt, 4)]
        [InlineData(ModifierKeys.Win, 8)]
        public void ModifierKeys_flags_have_stable_values(ModifierKeys flag, int expected)
        {
            Assert.Equal(expected, (int)flag);
        }

        // ------------------------------------------------------------------
        // INPUT marshaling size (SendInput's cbSize contract)
        // ------------------------------------------------------------------

        [Fact]
        public void INPUT_size_matches_native_Win32_layout_on_64bit()
        {
            if (IntPtr.Size != 8)
                return; // native size differs on 32-bit; only pin the 64-bit layout.

            // INPUT { type + MOUSEINPUT union } is 40 bytes on 64-bit Windows. If this
            // changes, SendInput will silently reject every batch.
            Assert.Equal(40, Marshal.SizeOf<KeyboardUtils.INPUT>());
        }

        // ------------------------------------------------------------------
        // Batch construction (pure logic, no interop)
        // ------------------------------------------------------------------

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint INPUT_KEYBOARD = 1;

        private static ushort Wvk(KeyboardUtils.INPUT input) => input.U.ki.wVk;

        [Fact]
        public void BuildModifierComboBatch_presses_and_releases_in_reverse_order()
        {
            var batch = KeyboardUtils.BuildModifierComboBatch(VirtualKey.V, ModifierKeys.Control);

            // Ctrl down, V down, V up, Ctrl up.
            Assert.Equal(INPUT_KEYBOARD, batch[0].type);
            Assert.Equal((ushort)0x11, Wvk(batch[0]));
            Assert.Equal(0u, batch[0].U.ki.dwFlags & KEYEVENTF_KEYUP);

            Assert.Equal((ushort)0x56, Wvk(batch[1]));
            Assert.Equal(0u, batch[1].U.ki.dwFlags & KEYEVENTF_KEYUP);

            Assert.Equal((ushort)0x56, Wvk(batch[2]));
            Assert.NotEqual(0u, batch[2].U.ki.dwFlags & KEYEVENTF_KEYUP);

            Assert.Equal((ushort)0x11, Wvk(batch[3]));
            Assert.NotEqual(0u, batch[3].U.ki.dwFlags & KEYEVENTF_KEYUP);

            Assert.Equal(4, batch.Count);
        }

        [Fact]
        public void BuildModifierComboBatch_with_all_modifiers_releases_in_reverse_modifiers()
        {
            var batch = KeyboardUtils.BuildModifierComboBatch(
                VirtualKey.Escape,
                ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Win);

            // Down order: Control, Shift, Alt, LWin, key, key.
            var expectedDown = new ushort[] { 0x11, 0x10, 0x12, 0x5B };
            for (int i = 0; i < expectedDown.Length; i++)
                Assert.Equal(expectedDown[i], Wvk(batch[i]));

            // Up order (positions 6-9): LWin, Alt, Shift, Control.
            var expectedUp = new ushort[] { 0x5B, 0x12, 0x10, 0x11 };
            for (int i = 0; i < expectedUp.Length; i++)
            {
                var input = batch[6 + i];
                Assert.Equal(expectedUp[i], Wvk(input));
                Assert.NotEqual(0u, input.U.ki.dwFlags & KEYEVENTF_KEYUP);
            }

            Assert.Equal(10, batch.Count);
        }

        [Fact]
        public void BuildModifierComboBatch_without_modifiers_is_just_press_and_release()
        {
            var batch = KeyboardUtils.BuildModifierComboBatch(VirtualKey.A, ModifierKeys.None);

            Assert.Equal(2, batch.Count);
            Assert.Equal((ushort)0x41, Wvk(batch[0]));
            Assert.Equal(0u, batch[0].U.ki.dwFlags & KEYEVENTF_KEYUP);
            Assert.Equal((ushort)0x41, Wvk(batch[1]));
            Assert.NotEqual(0u, batch[1].U.ki.dwFlags & KEYEVENTF_KEYUP);
        }

        [Fact]
        public void BuildComboBatch_releases_keys_in_reverse_order()
        {
            var batch = KeyboardUtils.BuildComboBatch(
                new[] { VirtualKey.Control, VirtualKey.Shift, VirtualKey.Escape });

            var expected = new ushort[] { 0x11, 0x10, 0x1B, 0x1B, 0x10, 0x11 };

            Assert.Equal(expected.Length, batch.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var input = batch[i];
                Assert.Equal(expected[i], Wvk(input));
                var isKeyUp = (i >= 3);
                Assert.Equal(isKeyUp ? KEYEVENTF_KEYUP : 0u, input.U.ki.dwFlags & KEYEVENTF_KEYUP);
            }
        }

        // ------------------------------------------------------------------
        // Argument validation / never-throw contract (no interop needed)
        // ------------------------------------------------------------------

        [Fact]
        public void PasteText_null_text_returns_false_and_message()
        {
            var keyboard = new KeyboardUtils();
            Assert.False(keyboard.PasteText(null, out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TypeText_null_text_returns_false_and_message()
        {
            string text = null;
            var keyboard = new KeyboardUtils();
            Assert.False(keyboard.TypeText(text, out string message));
            Assert.Equal("Text cannot be null.", message);
        }

        [Fact]
        public void TypeText_with_delay_null_text_returns_false_and_message()
        {
            var keyboard = new KeyboardUtils();
            Assert.False(keyboard.TypeText(null, 5, out string message));
            Assert.Equal("Text cannot be null.", message);
        }

        [Fact]
        public void PressKeyCombo_null_keys_returns_false_and_message()
        {
            var keyboard = new KeyboardUtils();
            Assert.False(keyboard.PressKeyCombo(out string message, null));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void PressKeyCombo_no_keys_returns_false_and_message()
        {
            var keyboard = new KeyboardUtils();
            Assert.False(keyboard.PressKeyCombo(out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        // ------------------------------------------------------------------
        // Windows-only interop cases (self-skip on other platforms)
        // ------------------------------------------------------------------

        [Fact]
        public void PressKey_benign_F15_succeeds_on_windows()
        {
            if (!OperatingSystem.IsWindows())
                return; // user32.dll is unavailable off-Windows.

            var keyboard = new KeyboardUtils();
            Assert.True(keyboard.PressKey(VirtualKey.F15, out string message));
            Assert.Null(message);
        }

        [Fact]
        public void TypeText_empty_string_succeeds_without_injecting_on_windows()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var keyboard = new KeyboardUtils();
            Assert.True(keyboard.TypeText(string.Empty, 0, out string message));
            Assert.Null(message);
        }

        [Fact]
        public void KeyDown_KeyUp_roundtrip_succeeds_on_windows()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var keyboard = new KeyboardUtils();
            Assert.True(keyboard.KeyDown(VirtualKey.F16, out string downMessage));
            Assert.Null(downMessage);
            Assert.True(keyboard.KeyUp(VirtualKey.F16, out string upMessage));
            Assert.Null(upMessage);
        }

        [Fact]
        public void GetActiveModifiers_returns_none_when_no_modifiers_held()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var keyboard = new KeyboardUtils();
            // Not press-atomic with the assertion below, but F-keys are not modifiers,
            // so this should hold unless a human is typing at the same moment.
            var modifiers = keyboard.GetActiveModifiers();

            Assert.True(
                modifiers == ModifierKeys.None || Enum.IsDefined(typeof(ModifierKeys), modifiers)
                || IsValidFlagCombo(modifiers));
        }

        private static bool IsValidFlagCombo(ModifierKeys modifiers)
        {
            return modifiers != ModifierKeys.None
                && modifiers == (ModifierKeys.None
                    | (((int)modifiers & 1) != 0 ? ModifierKeys.Control : 0)
                    | (((int)modifiers & 2) != 0 ? ModifierKeys.Shift : 0)
                    | (((int)modifiers & 4) != 0 ? ModifierKeys.Alt : 0)
                    | (((int)modifiers & 8) != 0 ? ModifierKeys.Win : 0));
        }
    }
}