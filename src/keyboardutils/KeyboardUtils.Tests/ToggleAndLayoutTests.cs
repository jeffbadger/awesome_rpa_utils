using System;
using KeyboardAutomation;
using Xunit;

namespace KeyboardAutomation.Tests
{
    /// <summary>
    /// Tests for the lock-key toggle queries and keyboard layout lookup. The pure logic
    /// (toggle-bit test, HKL-to-KLID derivation) needs no keyboard or registry; the few
    /// tests that call user32 are gated behind OperatingSystem.IsWindows(). What a real
    /// window reports is covered by the interactive harness - see TESTING.md.
    /// </summary>
    public class ToggleAndLayoutTests
    {
        // ------------------------------------------------------------------
        // Toggle bit
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(unchecked((short)0x8001), true)]   // held down and toggled on
        [InlineData(unchecked((short)0x8000), false)]  // held down but toggled off: not "on"
        public void IsToggled_ReadsOnlyTheLowOrderBit(short keyState, bool expected)
        {
            Assert.Equal(expected, KeyboardUtils.IsToggled(keyState));
        }

        // ------------------------------------------------------------------
        // HKL -> KLID derivation
        // ------------------------------------------------------------------

        private static string NoLookup(int layoutId, int languageId) =>
            throw new InvalidOperationException("the registry lookup must not be used for this input locale");

        [Theory]
        [InlineData(0x0409, 0x0409, "00000409")]   // US
        [InlineData(0x0407, 0x0407, "00000407")]   // German
        [InlineData(0x0809, 0x0809, "00000809")]   // United Kingdom
        [InlineData(0x040C, 0x040C, "0000040C")]   // French
        public void DeriveLayoutId_OrdinaryLayout_UsesTheHighWord(int languageId, int deviceHandle, string expected)
        {
            Assert.Equal(expected, KeyboardLayoutLookup.DeriveLayoutId(languageId, deviceHandle, NoLookup));
        }

        [Fact]
        public void DeriveLayoutId_NoDeviceHandle_MeansTheLanguagesDefaultLayout()
        {
            Assert.Equal("00000409", KeyboardLayoutLookup.DeriveLayoutId(0x0409, 0, NoLookup));
        }

        [Fact]
        public void DeriveLayoutId_HighWordDifferentFromLanguage_UsesTheHighWord()
        {
            // The UK layout (0809) selected under the en-US language (0409).
            Assert.Equal("00000809", KeyboardLayoutLookup.DeriveLayoutId(0x0409, 0x0809, NoLookup));
        }

        [Fact]
        public void DeriveLayoutId_LayoutIdForm_AsksTheLookupWithTheLayoutIdAndLanguage()
        {
            int seenLayoutId = -1, seenLanguage = -1;
            string result = KeyboardLayoutLookup.DeriveLayoutId(0x0409, 0xF002, (layoutId, languageId) =>
            {
                seenLayoutId = layoutId;
                seenLanguage = languageId;
                return "00020409";
            });

            Assert.Equal("00020409", result);
            Assert.Equal(0x002, seenLayoutId);   // low 12 bits of 0xF002
            Assert.Equal(0x0409, seenLanguage);
        }

        [Fact]
        public void DeriveLayoutId_LayoutIdFormNotFound_IsNull()
        {
            Assert.Null(KeyboardLayoutLookup.DeriveLayoutId(0x0409, 0xF002, (_, __) => null));
        }

        [Fact]
        public void DeriveLayoutId_Ime_KeepsTheHighWordAsTheKlidsHighWord()
        {
            Assert.Equal("E0010411", KeyboardLayoutLookup.DeriveLayoutId(0x0411, 0xE001, NoLookup));
        }

        [Fact]
        public void DeriveLayoutId_IgnoresBitsAboveTheWords()
        {
            Assert.Equal("00000409", KeyboardLayoutLookup.DeriveLayoutId(0x10409, 0x10409, NoLookup));
        }

        // ------------------------------------------------------------------
        // Language tag
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0x0409, "en-US")]
        [InlineData(0x0407, "de-DE")]
        [InlineData(0x0809, "en-GB")]
        public void LanguageTag_KnownLanguage_IsTheCultureName(int languageId, string expected)
        {
            Assert.Equal(expected, KeyboardLayoutLookup.LanguageTag(languageId));
        }

        // Language IDs that are not assigned to any culture (0x7F is reserved for the
        // invariant culture, so 0x7F7F is not a real language either). .NET throws
        // CultureNotFoundException for these; the documented result is an empty string.
        [Theory]
        [InlineData(0x7F7F)]
        [InlineData(0x3F3F)]
        [InlineData(0xFFFE)]
        public void LanguageTag_UnknownLanguage_IsAnEmptyStringRatherThanAnException(int languageId)
        {
            Assert.Equal(string.Empty, KeyboardLayoutLookup.LanguageTag(languageId));
        }

        // ------------------------------------------------------------------
        // Windows-only smoke tests (user32)
        // ------------------------------------------------------------------

        [Fact]
        public void GetKeyboardLayout_InvalidWindowHandle_ReturnsFalseWithMessageAndNullOutputs()
        {
            if (!OperatingSystem.IsWindows())
                return;

            bool ok = new KeyboardUtils().GetKeyboardLayout(out string name, out string id, out string tag, out string message, new IntPtr(0x7FFFFFF0));

            Assert.False(ok);
            Assert.Null(name);
            Assert.Null(id);
            Assert.Null(tag);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void LockKeyQueries_DoNotThrow()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var keyboard = new KeyboardUtils();
            // The values depend on the machine's current keyboard state; only that a
            // plain bool comes back without an exception is asserted here.
            _ = keyboard.IsCapsLockOn();
            _ = keyboard.IsNumLockOn();
            _ = keyboard.IsScrollLockOn();
        }
    }
}
