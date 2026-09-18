using System;
using System.Globalization;
using Microsoft.Win32;

namespace KeyboardAutomation
{
    /// <summary>
    /// Turns a Win32 input locale identifier (HKL) into a keyboard layout identifier
    /// (KLID, e.g. <c>00000409</c> for US) and a readable name (<c>US</c>).
    /// </summary>
    /// <remarks>
    /// An HKL packs a language ID in its low word and a "device handle" in its high word.
    /// How the high word maps to a KLID depends on its top nibble, so the derivation is a
    /// pure function (<see cref="DeriveLayoutId(int, int, Func{int, int, string})"/>) over a lookup delegate, testable without
    /// a registry; only <see cref="FindLayoutIdByRegistry"/> and <see cref="ReadLayoutText"/>
    /// touch Windows.
    /// </remarks>
    internal static class KeyboardLayoutLookup
    {
        private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";

        /// <summary>
        /// Derives the KLID for an input locale identifier, as 8 upper-case hex digits, or
        /// <c>null</c> if it can't be determined.
        /// </summary>
        /// <param name="languageId">The HKL's low word (the language ID).</param>
        /// <param name="deviceHandle">The HKL's high word.</param>
        /// <param name="findKlidByLayoutId">
        /// Resolves a "layout ID" (the low 12 bits of an <c>0xFxxx</c> high word) plus language
        /// ID to a KLID, by looking it up in the installed layouts. Called only for that case.
        /// </param>
        internal static string DeriveLayoutId(int languageId, int deviceHandle, Func<int, int, string> findKlidByLayoutId)
        {
            languageId &= 0xFFFF;
            deviceHandle &= 0xFFFF;

            // No device handle: the layout is the default one for that language.
            if (deviceHandle == 0)
                deviceHandle = languageId;

            switch (deviceHandle & 0xF000)
            {
                case 0xF000:
                    // A layout with its own "layout ID" (US-International, Dvorak, ...): the
                    // KLID is whichever installed layout carries that ID for this language.
                    return findKlidByLayoutId(deviceHandle & 0x0FFF, languageId);

                case 0xE000:
                    // An IME: the high word is the KLID's high word as-is.
                    return deviceHandle.ToString("X4", CultureInfo.InvariantCulture)
                         + languageId.ToString("X4", CultureInfo.InvariantCulture);

                default:
                    // The layout whose own language ID is the high word (the ordinary case,
                    // where high word and low word are usually the same).
                    return "0000" + deviceHandle.ToString("X4", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Splits an HKL into its low and high words and derives its KLID using the
        /// installed-layouts registry.
        /// </summary>
        internal static string DeriveLayoutId(long hkl)
        {
            return DeriveLayoutId((int)(hkl & 0xFFFF), (int)((hkl >> 16) & 0xFFFF), FindLayoutIdByRegistry);
        }

        /// <summary>The language tag for a language ID (for example <c>en-US</c>), or an empty string if .NET doesn't know it.</summary>
        internal static string LanguageTag(int languageId)
        {
            try
            {
                return CultureInfo.GetCultureInfo(languageId & 0xFFFF).Name;
            }
            catch (CultureNotFoundException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// The human-readable name Windows records for a KLID (for example <c>US</c> or
        /// <c>United Kingdom</c>), or an empty string if it has none or it can't be read.
        /// </summary>
        internal static string ReadLayoutText(string klid)
        {
            if (string.IsNullOrEmpty(klid))
                return string.Empty;
            try
            {
                using (RegistryKey layout = Registry.LocalMachine.OpenSubKey(LayoutsKey + "\\" + klid))
                    return layout?.GetValue("Layout Text") as string ?? string.Empty;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                return string.Empty;
            }
        }

        // Finds the installed layout with the given "Layout Id" value whose KLID ends in the
        // given language ID. Returns null if none matches or the registry can't be read.
        private static string FindLayoutIdByRegistry(int layoutId, int languageId)
        {
            try
            {
                using (RegistryKey layouts = Registry.LocalMachine.OpenSubKey(LayoutsKey))
                {
                    if (layouts == null)
                        return null;

                    foreach (string klid in layouts.GetSubKeyNames())
                    {
                        // The KLID's low word must be the language ID.
                        if (klid.Length != 8 || !int.TryParse(klid.Substring(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int klidLanguage) || klidLanguage != languageId)
                            continue;

                        using (RegistryKey layout = layouts.OpenSubKey(klid))
                        {
                            string idText = layout?.GetValue("Layout Id") as string;
                            if (idText != null
                                && int.TryParse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id)
                                && id == layoutId)
                            {
                                return klid.ToUpperInvariant();
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                // Best-effort: fall through to "not found".
            }
            return null;
        }
    }
}
