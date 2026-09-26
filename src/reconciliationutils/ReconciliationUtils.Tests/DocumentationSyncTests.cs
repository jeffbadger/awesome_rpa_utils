using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>The reference tables must say exactly what the code has: every public method, its signature and its description, everywhere it is listed.</summary>
    public sealed class DocumentationSyncTests
    {
        private static string RepositoryRoot()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "CrossReference.md")) && Directory.Exists(Path.Combine(dir, "src", "reconciliationutils"))) return dir;
            throw new FileNotFoundException("Could not find the repository root above " + AppContext.BaseDirectory);
        }

        /// <summary>The text of a repository file with line endings normalized: a Windows checkout has CRLF, which would defeat the line-anchored patterns below.</summary>
        private static string Normalize(string text) => text.Replace("\r\n", "\n");

        private static string Read(params string[] parts) => Normalize(File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray())));

        private static string TypeName(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int)) return "int";
            if (t == typeof(double)) return "double";
            return t.Name;
        }

        private static string Signature(MethodInfo m) =>
            TypeName(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p =>
                (p.IsOut ? "out " : "") + TypeName(p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType) + " " + p.Name)) + ")";

        private static string Description(MethodInfo m) =>
            m.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description;

        /// <summary>The rows of the method tables in a document: name, signature, description.</summary>
        private static List<(string name, string signature, string description)> Rows(string markdown) =>
            Regex.Matches(markdown, "^\\| `(\\w+)` \\| `(bool [^`]+)` \\| (.+) \\|$", RegexOptions.Multiline).Cast<Match>()
                .Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).ToList();

        [Fact]
        public void TheComponentReadme_ListsEveryPublicMethodOnce_WithItsExactSignatureAndDescription()
        {
            var rows = Rows(Read("src", "reconciliationutils", "README.md"));
            MethodInfo[] methods = ConventionTests.PublicMethods();
            Assert.Equal(methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal), rows.Select(r => r.name).OrderBy(n => n, StringComparer.Ordinal));
            var problems = new List<string>();
            foreach (MethodInfo m in methods)
            {
                var row = rows.Single(r => r.name == m.Name);
                if (Signature(m) != row.signature) problems.Add(m.Name + ": signature\n  README: " + row.signature + "\n  code:   " + Signature(m));
                string expected = Description(m).EndsWith(" Never throws.") ? Description(m).Substring(0, Description(m).Length - " Never throws.".Length) : Description(m);
                if (expected != row.description) problems.Add(m.Name + ": description\n  README: " + row.description + "\n  code:   " + expected);
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void TheReadmeMethodCount_AndEveryStatedCount_MatchTheCode()
        {
            string readme = Read("src", "reconciliationutils", "README.md");
            int count = ConventionTests.PublicMethods().Length;
            Assert.Contains("All " + count + " methods", readme);
            Assert.DoesNotMatch("All (?!" + count + " )\\d+ methods", readme);
        }

        [Fact]
        public void TheCrossReference_ListsTheSameMethodsWithTheSameText_AndTheRightCount()
        {
            string cross = Read("CrossReference.md");
            int start = cross.IndexOf("## ReconciliationUtils", StringComparison.Ordinal);
            int end = cross.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
            string section = cross.Substring(start, end - start);
            var actual = Rows(section);
            var expected = Rows(Read("src", "reconciliationutils", "README.md"));
            Assert.Equal(expected.OrderBy(r => r.name, StringComparer.Ordinal), actual.OrderBy(r => r.name, StringComparer.Ordinal));
            Assert.Equal(actual.Select(r => r.name).OrderBy(n => n, StringComparer.Ordinal), actual.Select(r => r.name));            // sorted like every other section

            Match summary = Regex.Match(cross, "^\\| \\[ReconciliationUtils\\]\\(#reconciliationutils\\) \\| `ReconciliationAutomation` \\| (\\d+) \\| (\\d+) \\| (\\d+) \\|", RegexOptions.Multiline);
            Assert.True(summary.Success, "the quick-reference row is missing or malformed");
            Assert.Equal(actual.Count.ToString(), summary.Groups[1].Value);
            Assert.Equal(("0", "0"), (summary.Groups[2].Value, summary.Groups[3].Value));
        }

        [Fact]
        public void TheDocumentationIndex_ListsEveryPage_AndEveryListedPageExists()
        {
            string dir = Path.Combine(RepositoryRoot(), "src", "reconciliationutils", "Documentation");
            string index = Normalize(File.ReadAllText(Path.Combine(dir, "README.md")));
            var listed = Regex.Matches(index, "^\\| \\[[^\\]]+\\]\\((\\w+\\.md)\\)", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            var pages = Directory.GetFiles(dir, "*.md").Select(Path.GetFileName).Where(n => n != "README.md").ToList();
            Assert.Equal(pages.OrderBy(n => n, StringComparer.Ordinal), listed.OrderBy(n => n, StringComparer.Ordinal));
        }

        /// <summary>The anchors GitHub gives the headings of a Markdown file: lower case, punctuation removed, spaces to hyphens, repeats numbered.</summary>
        private static HashSet<string> Anchors(string markdown)
        {
            var anchors = new HashSet<string>(StringComparer.Ordinal);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            bool inFence = false;
            foreach (string line in Normalize(markdown).Split('\n'))
            {
                if (line.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
                if (inFence) continue;
                Match heading = Regex.Match(line, "^#{1,6}\\s+(.+?)\\s*#*$");
                if (!heading.Success) continue;
                string slug = Regex.Replace(heading.Groups[1].Value.ToLowerInvariant(), "[^\\p{L}\\p{N} _-]", "").Replace(' ', '-');
                seen.TryGetValue(slug, out int count);
                seen[slug] = count + 1;
                anchors.Add(count == 0 ? slug : slug + "-" + count);
            }
            return anchors;
        }

        [Fact]
        public void EveryRelativeLink_InTheComponentDocumentation_ResolvesToAFile_AndToAHeadingWhenItNamesOne()
        {
            string root = RepositoryRoot();
            var files = Directory.GetFiles(Path.Combine(root, "src", "reconciliationutils"), "*.md", SearchOption.AllDirectories)
                .Concat(new[] { Path.Combine(root, "project-docs", "pega-usability-reviews", "ReconciliationUtils-pega-usability-review.md") });
                        foreach (string file in files)
                foreach (Match link in Regex.Matches(Normalize(File.ReadAllText(file)), "\\]\\(([^)#\\s]*)(#[^)\\s]*)?\\)"))
                {
                    string target = link.Groups[1].Value, fragment = link.Groups[2].Value;
                    if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
                    string targetPath = target.Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), target));    // "#heading" is a link within this page
                    bool isDirectory = Directory.Exists(targetPath);
                    Assert.True(File.Exists(targetPath) || isDirectory, Path.GetFileName(file) + " links to " + target + " which does not exist");
                    if (fragment.Length > 1)
                    {
                        Assert.True(!isDirectory && targetPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase), Path.GetFileName(file) + " links to a heading in " + target + ", which is not a Markdown file");
                        Assert.True(Anchors(File.ReadAllText(targetPath)).Contains(fragment.Substring(1)), Path.GetFileName(file) + " links to " + target + fragment + " but that page has no such heading");
                    }
                }
        }

        [Fact]
        public void TheAnchorRules_MatchHowGitHubNamesHeadings()
        {
            HashSet<string> anchors = Anchors("# Title\n## Two words\n### `Code` and (punctuation)!\n## Two words\n```\n# not a heading\n```\n## Under_score-hyphen\n");
            Assert.Equal(new[] { "title", "two-words", "code-and-punctuation", "two-words-1", "under_score-hyphen" }.OrderBy(x => x), anchors.OrderBy(x => x));
        }

        [Fact]
        public void TheRepositoryRegistration_IsComplete()
        {
            string root = Read("README.md");
            Assert.Contains("[reconciliationutils](src/reconciliationutils/README.md)", root);
            Assert.Contains("[reconciliationutils/Documentation/](src/reconciliationutils/Documentation/README.md)", root);
            Assert.Contains("ReconciliationUtils.Tests.csproj", root);
            Assert.Contains("\"ReconciliationAutomation.dll\"", Read("scripts", "Package-Release.ps1"));
            Assert.Contains("### ReconciliationUtils", Read("TESTING.md"));
            Assert.Contains("ReconciliationUtils-pega-usability-review.md", Read("project-docs", "pega-usability-reviews", "README.md"));
            Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "project-docs", "pega-usability-reviews", "ReconciliationUtils-pega-usability-review.md")));
            Assert.Contains("ReconciliationUtils", Read("src", "AwesomeRpaUtils.sln"));
        }
    }
}
