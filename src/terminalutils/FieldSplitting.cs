using System;
using System.Text.RegularExpressions;

namespace TerminalAutomation
{
    /// <summary>
    /// Heuristic column/field extraction from a captured console row's text. This is
    /// deliberately NOT true attribute/field-boundary parsing (the way a real 3270/5250
    /// terminal emulator delimits fields via protected/unprotected attribute bytes) - the
    /// Win32 console screen buffer carries no such concept, and full terminal-emulation-
    /// grade field support is explicitly out of scope for this component. This is a
    /// pragmatic "runs of 2+ whitespace characters are column separators" split, good
    /// enough for typical command-line/legacy-host table-style output. Pure string logic,
    /// no P/Invoke - fully unit-testable anywhere.
    /// </summary>
    internal static class FieldSplitting
    {
        private static readonly Regex FieldSeparator = new Regex("\\s{2,}", RegexOptions.Compiled);

        /// <summary>
        /// Splits <paramref name="rowText"/> into fields on runs of 2 or more whitespace
        /// characters (spaces, tabs, or a mix). Leading/trailing whitespace is trimmed
        /// before splitting. A null, empty, or whitespace-only row yields zero fields.
        /// Single-space-separated words are never split - only 2+ consecutive whitespace
        /// characters count as a column boundary.
        /// </summary>
        internal static string[] SplitFields(string rowText)
        {
            if (string.IsNullOrWhiteSpace(rowText))
                return Array.Empty<string>();

            string trimmed = rowText.Trim();
            if (trimmed.Length == 0)
                return Array.Empty<string>();

            return FieldSeparator.Split(trimmed);
        }
    }
}
