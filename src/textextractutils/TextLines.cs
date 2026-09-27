using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TextExtractAutomation
{
    /// <summary>
    /// One line of the text, normalized for matching, with a map from every normalized character back to the original text so a value can be
    /// reported exactly as it appeared (<c>raw</c>). Columns are normalized character positions: every white-space character counts as one column
    /// and runs of spaces are kept, so aligned forms and screens keep their columns.
    /// </summary>
    internal sealed class TextLine
    {
        internal TextLine(int number, string text, int[] originalStart, int[] originalEnd)
        {
            Number = number;
            Text = text;
            Folded = Fold(text);
            OriginalStart = originalStart;
            OriginalEnd = originalEnd;
        }

        /// <summary>1-based line number in the original text (CRLF counts once).</summary>
        internal int Number { get; }

        /// <summary>The normalized line.</summary>
        internal string Text { get; }

        /// <summary>The normalized line, upper-cased with the invariant culture, for case-insensitive matching position by position.</summary>
        internal string Folded { get; }

        internal int[] OriginalStart { get; }
        internal int[] OriginalEnd { get; }

        internal bool IsBlank
        {
            get
            {
                foreach (char c in Text) if (c != ' ') return false;
                return true;
            }
        }

        /// <summary>The original text behind normalized characters [start, end).</summary>
        internal string Raw(string original, int start, int end) =>
            end <= start ? string.Empty : original.Substring(OriginalStart[start], OriginalEnd[end - 1] - OriginalStart[start]);

        /// <summary>Upper-cases one character at a time (invariant), so positions never shift. Culture-independent: Turkish settings change nothing.</summary>
        internal static string Fold(string text)
        {
            var chars = new char[text.Length];
            for (int i = 0; i < text.Length; i++) chars[i] = char.ToUpperInvariant(text[i]);
            return new string(chars);
        }
    }

    /// <summary>
    /// Splits text into normalized lines. The normalization only makes matching tolerant; it never reaches a reported <c>raw</c> value:
    /// <list type="bullet">
    /// <item>line breaks: CRLF, CR, LF, NEL, LS, PS, VT and FF all end a line;</item>
    /// <item>every other white-space or control character becomes one space (tabs, non-breaking and ideographic spaces included);</item>
    /// <item>zero-width characters and soft hyphens are dropped;</item>
    /// <item>typographic quotes become ' or " and dashes and minus signs become -;</item>
    /// <item>everything else is Unicode NFKC-normalized one character (or surrogate pair) at a time: ligatures such as ﬁ become fi and
    /// full-width letters and digits become ASCII.</item>
    /// </list>
    /// </summary>
    internal static class TextLines
    {
        internal static List<TextLine> Split(string text)
        {
            var lines = new List<TextLine>();
            var builder = new StringBuilder();
            var starts = new List<int>();
            var ends = new List<int>();
            int number = 1;

            void Emit(string s, int start, int end)
            {
                foreach (char c in s)
                {
                    builder.Append(c);
                    starts.Add(start);
                    ends.Add(end);
                }
            }

            void EndLine()
            {
                lines.Add(new TextLine(number++, builder.ToString(), starts.ToArray(), ends.ToArray()));
                builder.Clear();
                starts.Clear();
                ends.Clear();
            }

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int width = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                int end = i + width;
                if (c == '\r')
                {
                    EndLine();
                    i = i + 1 < text.Length && text[i + 1] == '\n' ? i + 2 : i + 1;
                    continue;
                }
                if (c == '\n' || c == '\u0085' || c == '\u2028' || c == '\u2029' || c == '\u000B' || c == '\u000C')
                {
                    EndLine();
                    i++;
                    continue;
                }
                if (IsDropped(c)) { i = end; continue; }
                if (width == 1 && (char.IsWhiteSpace(c) || char.IsControl(c))) { Emit(" ", i, end); i = end; continue; }
                string mapped = width == 1 ? MapPunctuation(c) : null;
                if (mapped != null) { Emit(mapped, i, end); i = end; continue; }
                string element = text.Substring(i, width);
                string normalized = TextCheck.HasUnpairedSurrogate(element) ? element : element.Normalize(NormalizationForm.FormKC);
                Emit(normalized, i, end);
                i = end;
            }
            EndLine();
            return lines;
        }

        private static bool IsDropped(char c) =>
            c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\u2060' || c == '\uFEFF' || c == '\u00AD';

        /// <summary>Typographic quotes and dashes, as plain ASCII; null for any other character.</summary>
        private static string MapPunctuation(char c)
        {
            switch (c)
            {
                case '\u2018': case '\u2019': case '\u201A': case '\u201B': case '\u2032': case '\u00B4': return "'";
                case '\u201C': case '\u201D': case '\u201E': case '\u201F': case '\u2033': case '\u00AB': case '\u00BB': return "\"";
                case '\u2010': case '\u2011': case '\u2012': case '\u2013': case '\u2014': case '\u2015': case '\u2212': case '\uFE58': case '\uFE63': case '\uFF0D': return "-";
                default: return null;
            }
        }
    }
}
