using System;
using System.Collections.Generic;
using System.Linq;

namespace TextExtractAutomation
{
    internal sealed class CellResult
    {
        internal string Column;
        internal bool Found;
        internal string Value;
        internal string Raw;
        internal string Reason;       // null when found; MissingValue or InvalidValue
    }

    internal sealed class RowResult
    {
        internal int RowNumber;       // 1-based within the table
        internal int LineNumber;      // 1-based in the text
        internal List<CellResult> Cells = new List<CellResult>();

        internal CellResult Find(string column) => Cells.FirstOrDefault(c => string.Equals(c.Column, column, StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class TableResult
    {
        internal string Name;
        internal bool Found;          // a header line was found
        internal string Reason;       // null when found; MissingLabel or TooManyRows
        internal string Explanation;
        internal int HeaderLine;      // the first header line, or 0
        internal List<RowResult> Rows = new List<RowResult>();
    }

    /// <summary>
    /// Reads fixed-width tables: rows under a header line.
    /// <list type="bullet">
    /// <item>A header line is a line where every column's header is found (matched like a label: case, spacing and OCR slips; longer and exact
    /// matches claim their places first, as with labels). The same header line found again later (a page break) continues the table.</item>
    /// <item>Rows are the lines after a header line, up to a blank line, a line holding a label of any label field (a Total line after the items),
    /// another header line or the end of the text. Lines made only of - = _ + | and spaces (rules under a header) are skipped.</item>
    /// <item>A row is split into cells at runs of two or more spaces. Columns are taken left to right: a column's cell is the first unused cell that
    /// overlaps its header, else the first unused cell between the previous and the next column's headers (not touching either). So right-aligned numbers and values a little
    /// out of line still land in their column, and a column with nothing under it is MissingValue for that row.</item>
    /// <item>Each cell is read as its column's type, exactly like a field's value.</item>
    /// </list>
    /// All tables are read in one pass over the lines. Each line is searched once for every distinct header, trying at each position only the
    /// headers that could start there (as LabelLocator does for labels), so the cost does not multiply by the number of tables and columns.
    /// </summary>
    internal static class TableReader
    {
        private sealed class HeaderSpan
        {
            internal ColumnDef Column;
            internal int Start, End;
        }

        private sealed class State
        {
            internal TableDef Table;
            internal int[] Patterns;                  // per column, the index of its header's pattern
            internal TableResult Result;
            internal List<HeaderSpan> Header;         // the header of the rows being read, or null between tables
            internal bool Done;                       // TooManyRows
        }

        internal static List<TableResult> ReadAll(List<TableDef> tables, string original, List<TextLine> lines, HashSet<int> labelLines)
        {
            // one pattern per distinct header (as folded), indexed by the characters a match can begin with
            var patterns = new List<LabelPattern>();
            var byFolded = new Dictionary<string, int>(StringComparer.Ordinal);
            var states = new List<State>();
            foreach (TableDef table in tables)
            {
                var indexes = new int[table.Columns.Count];
                for (int k = 0; k < indexes.Length; k++)
                {
                    var pattern = new LabelPattern(table.Columns[k].Header);
                    if (!byFolded.TryGetValue(pattern.Folded, out int index)) { index = patterns.Count; byFolded[pattern.Folded] = index; patterns.Add(pattern); }
                    indexes[k] = index;
                }
                states.Add(new State { Table = table, Patterns = indexes, Result = new TableResult { Name = table.Name } });
            }
            var byFirst = new Dictionary<char, List<int>>();
            for (int p = 0; p < patterns.Count; p++)
            {
                if (patterns[p].Folded.Length == 0) continue;
                foreach (char c in LabelLocator.StartCharacters(patterns[p]))
                {
                    if (!byFirst.TryGetValue(c, out var list)) byFirst[c] = list = new List<int>();
                    list.Add(p);
                }
            }

            var matches = new List<(int Start, int End, bool Slipped)>[patterns.Count];
            for (int p = 0; p < matches.Length; p++) matches[p] = new List<(int, int, bool)>();
            foreach (TextLine line in lines)
            {
                if (states.All(s => s.Done)) break;
                // every match of every header on this line, by start
                foreach (var list in matches) list.Clear();
                string folded = line.Folded;
                for (int start = 0; start < folded.Length; start++)
                {
                    if (!byFirst.TryGetValue(folded[start], out var starting)) continue;
                    foreach (int p in starting)
                    {
                        int end = patterns[p].MatchAt(folded, start);
                        if (end >= 0) matches[p].Add((start, end, !LabelLocator.Exact(patterns[p], folded, start, end)));
                    }
                }
                foreach (State s in states)
                {
                    if (s.Done) continue;
                    List<HeaderSpan> header = HeaderAt(s, matches);
                    if (s.Header != null)
                    {
                        if (line.IsBlank || labelLines.Contains(line.Number) || header != null) s.Header = null;     // the rows end here
                        else if (IsRule(line.Text)) continue;
                        else if (s.Result.Rows.Count >= TemplateLimits.MaxTableRows)
                        {
                            s.Result.Rows.Clear();
                            s.Result.Reason = "TooManyRows";
                            s.Result.Explanation = "the table has more than " + TemplateLimits.MaxTableRows + " rows";
                            s.Done = true;
                            continue;
                        }
                        else
                        {
                            s.Result.Rows.Add(ReadRow(line, original, s.Header, s.Result.Rows.Count + 1));
                            continue;
                        }
                    }
                    if (header == null) continue;
                    s.Header = header;                       // a header line starts (or, after a page break, continues) the table
                    s.Result.Found = true;
                    if (s.Result.HeaderLine == 0) s.Result.HeaderLine = line.Number;
                }
            }
            foreach (State s in states)
                if (!s.Result.Found)
                {
                    s.Result.Reason = "MissingLabel";
                    s.Result.Explanation = "no line holds every column header of the table";
                }
            return states.Select(s => s.Result).ToList();
        }

        /// <summary>The columns' header positions (in template column order) on a line where every header is found, or null. Matches claim their
        /// places in the order LabelLocator uses for labels: the longer span first, then an exact match before an OCR-slipped one, then template
        /// column order, then the leftmost. So Amount does not take the Amount inside Amount Due, and a header that is only an OCR look-alike of
        /// another (Return and RetuM) does not take the other's exact place, whatever order the columns were added in. A column claims one place;
        /// a match overlapping a place already claimed is skipped.</summary>
        private static List<HeaderSpan> HeaderAt(State s, List<(int Start, int End, bool Slipped)>[] matches)
        {
            for (int k = 0; k < s.Patterns.Length; k++)
                if (matches[s.Patterns[k]].Count == 0) return null;                   // the usual case, decided without sorting
            var candidates = new List<(int Column, int Start, int End, bool Slipped)>();
            for (int k = 0; k < s.Patterns.Length; k++)
                foreach (var (start, end, slipped) in matches[s.Patterns[k]]) candidates.Add((k, start, end, slipped));
            var spans = new HeaderSpan[s.Patterns.Length];
            var claimed = new List<HeaderSpan>();
            foreach (var m in candidates.OrderByDescending(m => m.End - m.Start).ThenBy(m => m.Slipped).ThenBy(m => m.Column).ThenBy(m => m.Start))
            {
                if (spans[m.Column] != null || claimed.Any(h => m.Start < h.End && h.Start < m.End)) continue;
                spans[m.Column] = new HeaderSpan { Column = s.Table.Columns[m.Column], Start = m.Start, End = m.End };
                claimed.Add(spans[m.Column]);
            }
            return spans.Any(h => h == null) ? null : spans.ToList();
        }

        private static bool IsRule(string text)
        {
            bool any = false;
            foreach (char c in text)
            {
                if (c == ' ') continue;
                if (c != '-' && c != '=' && c != '_' && c != '+' && c != '|') return false;
                any = true;
            }
            return any;
        }

        private static RowResult ReadRow(TextLine line, string original, List<HeaderSpan> header, int rowNumber)
        {
            var cells = Cells(line.Text);
            var used = new bool[cells.Count];
            var ordered = header.OrderBy(h => h.Start).ToList();
            var byColumn = new Dictionary<ColumnDef, CellResult>();
            for (int k = 0; k < ordered.Count; k++)
            {
                HeaderSpan h = ordered[k];
                int nextStart = k + 1 < ordered.Count ? ordered[k + 1].Start : int.MaxValue;
                int nextEnd = k + 1 < ordered.Count ? ordered[k + 1].End : int.MaxValue;
                int chosen = -1;
                for (int i = 0; i < cells.Count && chosen < 0; i++)
                    if (!used[i] && cells[i].Start < h.End && h.Start < cells[i].End) chosen = i;
                for (int i = 0; i < cells.Count && chosen < 0; i++)
                    if (!used[i] && cells[i].Start >= (k > 0 ? ordered[k - 1].End : 0) && cells[i].Start < nextStart && !(cells[i].End > nextStart && cells[i].Start < nextEnd)) chosen = i;
                var cell = new CellResult { Column = h.Column.Header };
                if (chosen < 0)
                {
                    cell.Reason = "MissingValue";
                }
                else
                {
                    used[chosen] = true;
                    var (start, end) = cells[chosen];
                    Converted value = ValueConverter.Convert(h.Column.AsField(), line.Text.Substring(start, end - start));
                    if (value.Ok)
                    {
                        cell.Found = true;
                        cell.Value = value.Value;
                        cell.Raw = line.Raw(original, start + value.Start, start + value.End);
                    }
                    else
                    {
                        cell.Reason = value.Reason;
                        cell.Raw = line.Raw(original, start, end);
                    }
                }
                byColumn[h.Column] = cell;
            }
            var row = new RowResult { RowNumber = rowNumber, LineNumber = line.Number };
            foreach (HeaderSpan h in header) row.Cells.Add(byColumn[h.Column]);      // cells in template column order
            return row;
        }

        /// <summary>The cells of a line: runs of text separated by two or more spaces (one space keeps a value together).</summary>
        internal static List<(int Start, int End)> Cells(string line)
        {
            var cells = new List<(int, int)>();
            int p = 0;
            while (p < line.Length)
            {
                while (p < line.Length && line[p] == ' ') p++;
                if (p >= line.Length) break;
                int s = p;
                while (p < line.Length && !(line[p] == ' ' && (p + 1 >= line.Length || line[p + 1] == ' '))) p++;
                cells.Add((s, p));
            }
            return cells;
        }
    }
}
