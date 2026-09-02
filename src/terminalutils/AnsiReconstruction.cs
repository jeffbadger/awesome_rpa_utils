using System;
using System.Text;

namespace TerminalAutomation
{
    /// <summary>
    /// A single character cell as read from the console screen buffer: the character plus
    /// its stored foreground/background color attribute. Internal - the input shape
    /// <see cref="AnsiReconstruction"/> operates on, not a public method return type.
    /// </summary>
    internal readonly struct TerminalCell
    {
        internal TerminalCell(char character, ConsoleColor foreground, ConsoleColor background)
        {
            Character = character;
            Foreground = foreground;
            Background = background;
        }

        internal char Character { get; }
        internal ConsoleColor Foreground { get; }
        internal ConsoleColor Background { get; }
    }

    /// <summary>
    /// Flattens a captured row of console cells into text, optionally re-synthesizing ANSI
    /// SGR color codes from each cell's stored color attribute.
    /// <para>
    /// This is reconstruction, not passthrough capture. The Win32 console screen buffer
    /// (<c>ReadConsoleOutputW</c>) returns <c>CHAR_INFO</c> structs holding an already-
    /// interpreted 4-bit foreground/4-bit background color attribute - by the time this API
    /// sees the buffer, conhost/Windows Terminal has already fully consumed and applied any
    /// ANSI escape sequences the target application originally emitted; the original escape
    /// bytes no longer exist anywhere to be captured. "Preserving ANSI" therefore means
    /// emitting a NEW, equivalent SGR sequence whenever the color changes across a run of
    /// cells in a row, not reproducing the target's original byte-for-byte output. This is
    /// good enough to give a downstream consumer (e.g. a log file opened in a real terminal)
    /// equivalent coloring, but is not a literal capture.
    /// </para>
    /// Pure string/bit-manipulation logic, no P/Invoke - fully unit-testable anywhere.
    /// </summary>
    internal static class AnsiReconstruction
    {
        // Index = (int)ConsoleColor (0-15). Value = the base SGR foreground code (30-37 for
        // the 8 standard colors, 90-97 for the 8 bright/intense variants). The matching
        // background code is always the foreground code + 10 (40-47 / 100-107).
        private static readonly int[] ForegroundSgrCodeByConsoleColor =
        {
            30, // Black
            34, // DarkBlue
            32, // DarkGreen
            36, // DarkCyan
            31, // DarkRed
            35, // DarkMagenta
            33, // DarkYellow
            37, // Gray
            90, // DarkGray
            94, // Blue
            92, // Green
            96, // Cyan
            91, // Red
            95, // Magenta
            93, // Yellow
            97  // White
        };

        /// <summary>
        /// Renders a single row of cells as text. When <paramref name="preserveAnsi"/> is
        /// <c>false</c>, returns the plain characters with no color information at all. When
        /// <c>true</c>, emits an SGR color-code sequence at the start of the row and again
        /// every time a cell's foreground/background differs from the previous cell, ending
        /// with a reset sequence (<c>\x1b[0m</c>) so a downstream terminal isn't left in a
        /// colored state after the row. A null or empty <paramref name="cells"/> renders as
        /// an empty string in either mode.
        /// </summary>
        internal static string Render(TerminalCell[] cells, bool preserveAnsi)
        {
            if (cells == null || cells.Length == 0)
                return string.Empty;

            if (!preserveAnsi)
            {
                var plain = new StringBuilder(cells.Length);
                foreach (TerminalCell cell in cells)
                    plain.Append(cell.Character);
                return plain.ToString();
            }

            var builder = new StringBuilder();
            ConsoleColor? lastForeground = null;
            ConsoleColor? lastBackground = null;

            foreach (TerminalCell cell in cells)
            {
                if (cell.Foreground != lastForeground || cell.Background != lastBackground)
                {
                    int fgCode = ForegroundSgrCodeByConsoleColor[(int)cell.Foreground];
                    int bgCode = ForegroundSgrCodeByConsoleColor[(int)cell.Background] + 10;
                    builder.Append("\x1b[").Append(fgCode).Append(';').Append(bgCode).Append('m');
                    lastForeground = cell.Foreground;
                    lastBackground = cell.Background;
                }
                builder.Append(cell.Character);
            }

            builder.Append("\x1b[0m");
            return builder.ToString();
        }
    }
}
