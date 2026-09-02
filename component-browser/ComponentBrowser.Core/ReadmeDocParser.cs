using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ComponentBrowser
{
    /// <summary>One row of a component README's <c>### Category</c> method table.</summary>
    public class ReadmeMethodRow
    {
        public string MethodName { get; set; }
        public string Signature { get; set; }
        public string Description { get; set; }
    }

    /// <summary>One <c>### Category</c> heading and the method rows under it.</summary>
    public class ReadmeCategory
    {
        public string Name { get; set; }
        public List<ReadmeMethodRow> Rows { get; set; } = new List<ReadmeMethodRow>();
    }

    /// <summary>The parts of a component README this app actually uses.</summary>
    public class ParsedReadme
    {
        /// <summary>The assembly name, read from the file's leading <c># AssemblyName</c> heading - every component README in this suite starts this way.</summary>
        public string AssemblyName { get; set; }

        public List<ReadmeCategory> Categories { get; set; } = new List<ReadmeCategory>();

        /// <summary>The raw text of the "## Notes &amp; Caveats" section, or null if absent.</summary>
        public string NotesAndCaveats { get; set; }
    }

    /// <summary>
    /// Parses a component <c>README.md</c> for its method tables and Notes &amp; Caveats
    /// section. Deliberately a small tolerant parser against this suite's actual, consistent
    /// README shape - not a general markdown engine, which this suite avoids adding as a
    /// dependency for a need this narrow.
    /// </summary>
    public static class ReadmeDocParser
    {
        private static readonly Regex BacktickText = new Regex("`([^`]*)`", RegexOptions.Compiled);

        public static ParsedReadme Parse(string readmePath)
        {
            string[] lines = File.ReadAllLines(readmePath);
            var result = new ParsedReadme();

            string h1 = lines.FirstOrDefault(l => l.TrimStart().StartsWith("# "));
            if (h1 != null)
                result.AssemblyName = h1.TrimStart().Substring(2).Trim();

            int methodsStart = FindHeadingIndex(lines, "## Methods");
            if (methodsStart >= 0)
            {
                int methodsEnd = FindNextHeadingIndex(lines, methodsStart + 1, "## ");
                ParseCategories(lines, methodsStart + 1, methodsEnd, result.Categories);
            }

            int notesStart = FindHeadingIndex(lines, "## Notes & Caveats");
            if (notesStart >= 0)
            {
                int notesEnd = FindNextHeadingIndex(lines, notesStart + 1, "## ");
                result.NotesAndCaveats = string.Join(
                    "\n",
                    lines.Skip(notesStart + 1).Take(Math.Max(0, notesEnd - notesStart - 1))).Trim();
            }

            return result;
        }

        /// <summary>
        /// Looks for <c>Documentation/&lt;category&gt;.md</c> next to a component's README
        /// (case-insensitive), returning its path, or null if the component has no
        /// <c>Documentation/</c> folder or no page for this category - both are normal,
        /// non-error cases (e.g. ScreenCaptureUtils ships no Documentation/ folder at all).
        /// </summary>
        public static string FindWorkedExamplePath(string componentDirectory, string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return null;

            string docsDirectory = Path.Combine(componentDirectory, "Documentation");
            if (!Directory.Exists(docsDirectory))
                return null;

            return Directory.GetFiles(docsDirectory, "*.md")
                .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), category, StringComparison.OrdinalIgnoreCase));
        }

        private static void ParseCategories(string[] lines, int start, int end, List<ReadmeCategory> categories)
        {
            ReadmeCategory current = null;

            for (int i = start; i < end; i++)
            {
                string trimmed = lines[i].TrimStart();

                if (trimmed.StartsWith("### "))
                {
                    current = new ReadmeCategory { Name = trimmed.Substring(4).Trim() };
                    categories.Add(current);
                    continue;
                }

                if (current == null || !trimmed.StartsWith("|"))
                    continue;

                List<string> cells = SplitTableRow(trimmed);
                if (cells.Count < 3)
                    continue;
                if (IsSeparatorRow(cells))
                    continue;
                if (cells[0].Trim().Equals("Method", StringComparison.OrdinalIgnoreCase))
                    continue; // header row

                string methodCell = cells[0].Trim();
                string name = ExtractBacktickText(methodCell) ?? methodCell;
                string signature = ExtractBacktickText(cells[1].Trim()) ?? cells[1].Trim();
                string description = cells[2].Trim();

                current.Rows.Add(new ReadmeMethodRow { MethodName = name, Signature = signature, Description = description });
            }
        }

        private static List<string> SplitTableRow(string line)
        {
            // Markdown table rows in this suite's docs never escape a literal "|" inside a
            // cell, so a plain split is enough - trim the leading/trailing empty cells that
            // come from the row's outer "|" delimiters.
            var cells = line.Split('|').ToList();
            if (cells.Count > 0 && cells[0].Trim().Length == 0)
                cells.RemoveAt(0);
            if (cells.Count > 0 && cells[cells.Count - 1].Trim().Length == 0)
                cells.RemoveAt(cells.Count - 1);
            return cells;
        }

        private static bool IsSeparatorRow(List<string> cells)
        {
            return cells.All(c => c.Trim().Length > 0 && c.Trim().All(ch => ch == '-' || ch == ':'));
        }

        private static string ExtractBacktickText(string text)
        {
            Match match = BacktickText.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static int FindHeadingIndex(string[] lines, string heading)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimEnd().Equals(heading, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private static int FindNextHeadingIndex(string[] lines, int start, string headingPrefix)
        {
            for (int i = start; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(headingPrefix, StringComparison.Ordinal))
                    return i;
            }
            return lines.Length;
        }
    }
}
