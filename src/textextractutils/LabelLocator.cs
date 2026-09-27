using System;
using System.Collections.Generic;
using System.Linq;

namespace TextExtractAutomation
{
    /// <summary>
    /// Matches one label against a line at a position. Deterministic and explainable:
    /// <list type="bullet">
    /// <item>case-insensitive, one character at a time with the invariant culture;</item>
    /// <item>a space in the label matches one or more spaces in the text;</item>
    /// <item>a label that starts (ends) with a letter or digit must start (end) at a word boundary, so Total does not match Subtotal or Totals;</item>
    /// <item>OCR slips: only the listed confusions (I/L/1, O/0, S/5 and RN/M), and only in labels of four or more letters and digits, at most
    /// one per six of them (rounded up). There is no general fuzzy matching.</item>
    /// </list>
    /// </summary>
    internal sealed class LabelPattern
    {
        internal LabelPattern(string label)
        {
            Text = label;
            string folded = TextLine.Fold(label);
            // runs of white space in the label collapse to one space, which then matches any run of spaces in the text
            var chars = new List<char>();
            foreach (char c in folded)
            {
                if (char.IsWhiteSpace(c)) { if (chars.Count > 0 && chars[chars.Count - 1] != ' ') chars.Add(' '); }
                else chars.Add(c);
            }
            Folded = new string(chars.ToArray()).TrimEnd();
            int significant = Folded.Count(char.IsLetterOrDigit);
            SlipBudget = significant <= 3 ? 0 : (significant + 5) / 6;
            BoundaryBefore = Folded.Length > 0 && char.IsLetterOrDigit(Folded[0]);
            BoundaryAfter = Folded.Length > 0 && char.IsLetterOrDigit(Folded[Folded.Length - 1]);
        }

        internal string Text { get; }
        internal string Folded { get; }
        internal int SlipBudget { get; }
        internal bool BoundaryBefore { get; }
        internal bool BoundaryAfter { get; }

        /// <summary>The end (exclusive) of a match of this label starting at <paramref name="start"/> in the folded line, or -1.</summary>
        internal int MatchAt(string line, int start)
        {
            if (BoundaryBefore && start > 0 && char.IsLetterOrDigit(line[start - 1])) return -1;
            int i = 0, j = start, slips = 0;
            while (i < Folded.Length)
            {
                if (j >= line.Length) return -1;
                char l = Folded[i];
                if (l == ' ')
                {
                    if (line[j] != ' ') return -1;
                    while (j < line.Length && line[j] == ' ') j++;
                    i++;
                    continue;
                }
                char t = line[j];
                if (l == t) { i++; j++; continue; }
                if (slips < SlipBudget)
                {
                    if (Confusable(l, t)) { slips++; i++; j++; continue; }
                    if (l == 'R' && i + 1 < Folded.Length && Folded[i + 1] == 'N' && t == 'M') { slips++; i += 2; j++; continue; }
                    if (l == 'M' && t == 'R' && j + 1 < line.Length && line[j + 1] == 'N') { slips++; i++; j += 2; continue; }
                }
                return -1;
            }
            if (BoundaryAfter && j < line.Length && char.IsLetterOrDigit(line[j])) return -1;
            return j;
        }

        internal static bool Confusable(char a, char b) => Group(a) != 0 && Group(a) == Group(b);

        private static int Group(char c)
        {
            switch (c)
            {
                case 'I': case 'L': case '1': case '|': return 1;       // | only ever occurs in the text: labels cannot contain it
                case 'O': case '0': return 2;
                case 'S': case '5': return 3;
                default: return 0;
            }
        }
    }

    /// <summary>One occurrence of a field's label in the text, with the span where its value is.</summary>
    internal sealed class LabelCandidate
    {
        internal int FieldIndex;
        internal string Label;            // the alternative that matched, as written in the template
        internal int LabelLine;           // 1-based
        internal int ValueLine;           // 1-based; 0 when there is no value
        internal string Value;            // normalized text of the value span, trimmed; empty when there is none
        internal string Raw;              // the same span in the original text
        internal bool Slipped;            // the label matched with at least one OCR confusion
        internal TextLine SpanLine;       // the line the value span is on (null when there is none), so a captured part can be mapped back to the original
        internal int SpanStart;           // the value span's first column on that line
    }

    /// <summary>Finds every label occurrence of every label field and the text its position points at. Types and occurrence policies come later.</summary>
    internal static class LabelLocator
    {
        private sealed class Hit
        {
            internal int FieldIndex;
            internal LabelPattern Pattern;
            internal int Line;             // index into the lines
            internal int Start;            // label start column
            internal int End;              // label end column
            internal int ValueStart;       // column after the separators
            internal bool Slipped;
        }

        internal static List<LabelCandidate> Locate(Template template, string original, List<TextLine> lines)
        {
            // 1. every label of every label field, longest alternative first so a longer label wins at the same position
            var patterns = new List<(int field, LabelPattern pattern)>();
            for (int f = 0; f < template.Fields.Count; f++)
            {
                FieldDef field = template.Fields[f];
                if (field.Kind != FieldKind.Label) continue;
                foreach (string label in field.Labels) patterns.Add((f, new LabelPattern(label)));
            }

            // Index the labels by the characters a match can begin with, so each position of the text is tried only against the labels that could
            // start there instead of against every label.
            var byFirst = new Dictionary<char, List<(int field, LabelPattern pattern)>>();
            foreach (var entry in patterns)
            {
                if (entry.pattern.Folded.Length == 0) continue;
                foreach (char c in StartCharacters(entry.pattern))
                {
                    if (!byFirst.TryGetValue(c, out var list)) byFirst[c] = list = new List<(int, LabelPattern)>();
                    list.Add(entry);
                }
            }

            // 2. all matches on every line; where matches overlap, the longer one wins (then the exact one, then the earlier field), so "Due Date" is not also "Date"
            var hitsByLine = new List<Hit>[lines.Count];
            for (int li = 0; li < lines.Count; li++)
            {
                string folded = lines[li].Folded;
                var found = new List<Hit>();
                for (int start = 0; start < folded.Length; start++)
                {
                    if (!byFirst.TryGetValue(folded[start], out var starting)) continue;
                    foreach (var (field, pattern) in starting)
                    {
                        int end = pattern.MatchAt(folded, start);
                        if (end < 0) continue;
                        found.Add(new Hit { FieldIndex = field, Pattern = pattern, Line = li, Start = start, End = end, Slipped = !Exact(pattern, folded, start, end) });
                    }
                }
                var kept = new List<Hit>();
                foreach (Hit h in found.OrderByDescending(h => h.End - h.Start).ThenBy(h => h.Slipped).ThenBy(h => h.FieldIndex).ThenBy(h => h.Start))
                    if (!kept.Any(k => h.Start < k.End && k.Start < h.End)) kept.Add(h);
                kept.Sort((a, b) => a.Start.CompareTo(b.Start));
                foreach (Hit h in kept) h.ValueStart = SkipSeparators(lines[li].Text, h.End);
                hitsByLine[li] = kept;
            }

            // 3. the value span of each hit
            var candidates = new List<LabelCandidate>();
            for (int li = 0; li < lines.Count; li++)
                foreach (Hit h in hitsByLine[li])
                {
                    var candidate = new LabelCandidate { FieldIndex = h.FieldIndex, Label = h.Pattern.Text, LabelLine = lines[li].Number, Slipped = h.Slipped, Value = string.Empty, Raw = string.Empty };
                    FieldDef field = template.Fields[h.FieldIndex];
                    switch (field.Position)
                    {
                        case ValuePosition.SameLine:
                            SetSpan(candidate, original, lines[li], h.ValueStart, NextLabelStart(hitsByLine[li], h.ValueStart, lines[li].Text.Length));
                            break;
                        case ValuePosition.NextLine:
                        {
                            int next = NextNonBlank(lines, li);
                            if (next >= 0) SetSpan(candidate, original, lines[next], 0, NextLabelStart(hitsByLine[next], 0, lines[next].Text.Length));
                            break;
                        }
                        case ValuePosition.Below:
                        {
                            // Blank lines are skipped like NextLine: OCR often double-spaces a form. The value must still sit under the label's columns.
                            int next = NextNonBlank(lines, li);
                            if (next >= 0 && CellUnder(lines[next].Text, h.Start, h.End, out int cellStart, out int cellEnd))
                                SetSpan(candidate, original, lines[next], cellStart, cellEnd);
                            break;
                        }
                    }
                    candidates.Add(candidate);
                }
            return candidates;
        }

        /// <summary>The folded characters a match of this label can begin with: its first character, and its OCR look-alikes when it may slip.</summary>
        private static IEnumerable<char> StartCharacters(LabelPattern pattern)
        {
            char first = pattern.Folded[0];
            yield return first;
            if (pattern.SlipBudget == 0) yield break;
            foreach (char c in "IL1|O0S5")
                if (c != first && LabelPattern.Confusable(first, c)) yield return c;
            if (first == 'R') yield return 'M';
            if (first == 'M') yield return 'R';
        }

        private static bool Exact(LabelPattern pattern, string folded, int start, int end)
        {
            // an exact match reads the label's letters in order, with runs of spaces where the label has one
            int i = 0, j = start;
            while (i < pattern.Folded.Length && j < end)
            {
                if (pattern.Folded[i] == ' ') { while (j < end && folded[j] == ' ') j++; i++; continue; }
                if (pattern.Folded[i] != folded[j]) return false;
                i++; j++;
            }
            return i == pattern.Folded.Length && j == end;
        }

        /// <summary>
        /// Skips what separates a label from its value: spaces and : # = everywhere, and . or - unless a digit follows (so dot leaders and
        /// "No.:" go, but the sign of -12.00 and the point of .50 stay).
        /// </summary>
        internal static int SkipSeparators(string line, int position)
        {
            int p = position;
            while (p < line.Length)
            {
                char c = line[p];
                if (c == ' ' || c == ':' || c == '#' || c == '=') { p++; continue; }
                if ((c == '.' || c == '-') && !(p + 1 < line.Length && char.IsDigit(line[p + 1]))) { p++; continue; }
                break;
            }
            return p;
        }

        private static int NextLabelStart(List<Hit> hits, int from, int lineEnd)
        {
            foreach (Hit h in hits) if (h.Start >= from) return h.Start;
            return lineEnd;
        }

        private static int NextNonBlank(List<TextLine> lines, int from)
        {
            for (int i = from + 1; i < lines.Count; i++) if (!lines[i].IsBlank) return i;
            return -1;
        }

        /// <summary>
        /// The cell of a line that sits under the columns [labelStart, labelEnd). Cells are separated by two or more spaces, so a single space inside
        /// a value ("12 Main St") keeps it together. The first cell that overlaps the label's columns is the value.
        /// </summary>
        internal static bool CellUnder(string line, int labelStart, int labelEnd, out int cellStart, out int cellEnd)
        {
            cellStart = cellEnd = 0;
            int p = 0;
            while (p < line.Length)
            {
                while (p < line.Length && line[p] == ' ') p++;
                if (p >= line.Length) break;
                int s = p;
                while (p < line.Length && !(line[p] == ' ' && (p + 1 >= line.Length || line[p + 1] == ' '))) p++;
                int e = p;
                if (s < labelEnd && labelStart < e) { cellStart = s; cellEnd = e; return true; }
            }
            return false;
        }

        private static void SetSpan(LabelCandidate candidate, string original, TextLine line, int start, int end)
        {
            while (start < end && line.Text[start] == ' ') start++;
            while (end > start && line.Text[end - 1] == ' ') end--;
            if (end <= start) return;
            candidate.ValueLine = line.Number;
            candidate.SpanLine = line;
            candidate.SpanStart = start;
            candidate.Value = line.Text.Substring(start, end - start);
            candidate.Raw = line.Raw(original, start, end);
        }
    }
}
