using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace ResourceLockAutomation.Tests
{
    /// <summary>The README's method table must say exactly what the code has: every public method once, its signature and its description.</summary>
    public sealed class DocumentationSyncTests
    {
        private static string ComponentDirectory()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "resourcelockutils");
                if (File.Exists(Path.Combine(candidate, "ResourceLockUtils.csproj"))) return candidate;
            }
            throw new FileNotFoundException("Could not find src/resourcelockutils above " + AppContext.BaseDirectory);
        }

        // A Windows checkout has CRLF line endings, which would defeat the line-anchored patterns below.
        private static string Readme() => File.ReadAllText(Path.Combine(ComponentDirectory(), "README.md")).Replace("\r\n", "\n");

        private static string TypeName(Type t) => t == typeof(string) ? "string" : t == typeof(bool) ? "bool" : t == typeof(int) ? "int" : t.Name;

        private static string Signature(MethodInfo m) =>
            TypeName(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p =>
                (p.IsOut ? "out " : "") + TypeName(p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType) + " " + p.Name)) + ")";

        [Fact]
        public void TheReadmeMethodTable_MatchesTheCode()
        {
            var rows = Regex.Matches(Readme(), "^\\| `(\\w+)` \\| `(bool [^`]+)` \\| (.+) \\|$", RegexOptions.Multiline).Cast<Match>()
                .Select(m => (name: m.Groups[1].Value, signature: m.Groups[2].Value, description: m.Groups[3].Value.Replace("\\|", "|"))).ToList();   // a | in a cell is written \| so it does not split the column
            MethodInfo[] methods = ConventionTests.PublicMethods();
            Assert.Equal(methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal), rows.Select(r => r.name).OrderBy(n => n, StringComparer.Ordinal));
            var problems = new List<string>();
            foreach (MethodInfo m in methods)
            {
                var row = rows.Single(r => r.name == m.Name);
                string description = m.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description;
                description = description.EndsWith(" Never throws.") ? description.Substring(0, description.Length - " Never throws.".Length) : description;
                if (Signature(m) != row.signature) problems.Add(m.Name + " signature: README " + row.signature + " / code " + Signature(m));
                if (description != row.description) problems.Add(m.Name + " description differs from [Description]");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            Assert.Contains("All " + methods.Length + " methods", Readme());
            foreach (Match row in Regex.Matches(Readme(), "^\\| `\\w+` \\| `bool [^`]+` \\| (.+) \\|$", RegexOptions.Multiline))
                Assert.DoesNotMatch("(?<!\\\\)\\|", row.Groups[1].Value);                                   // every | inside a description is escaped
        }

        [Fact]
        public void TheReadmeInputRules_MatchTheLimits()
        {
            string readme = Readme();
            Assert.Contains("at most " + LockLimits.MaxResourceLength + " characters", readme);
            Assert.Contains("1 to " + LockLimits.MaxHolderLength + " characters", readme);
            Assert.Contains("**leaseSeconds** " + LockLimits.MinLeaseSeconds + " to " + LockLimits.MaxLeaseSeconds.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), readme);
            Assert.Contains("**waitMilliseconds** 0 to " + LockLimits.MaxWaitMilliseconds.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), readme);
            Assert.Contains("**capacity** " + LockLimits.MinCapacity + " to " + LockLimits.MaxCapacity, readme);
        }

        // ------------------------------------------------------------------ repository-wide documentation

        private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(ComponentDirectory(), "..", ".."));

        private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

        private static List<(string name, string signature, string description)> Rows(string markdown) =>
            Regex.Matches(markdown, "^\\| `(\\w+)` \\| `(bool [^`]+)` \\| (.+) \\|$", RegexOptions.Multiline).Cast<Match>()
                .Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value.Replace("\\|", "|"))).ToList();

        [Fact]
        public void TheCrossReference_ListsTheSameMethodsWithTheSameText_Sorted_WithTheRightCount()
        {
            string cross = Read("CrossReference.md");
            int start = cross.IndexOf("## ResourceLockUtils", StringComparison.Ordinal);
            int end = cross.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
            var actual = Rows(cross.Substring(start, end - start));
            var expected = Rows(Readme());
            Assert.Equal(expected.OrderBy(r => r.name, StringComparer.Ordinal), actual);
            Match summary = Regex.Match(cross, "^\\| \\[ResourceLockUtils\\]\\(#resourcelockutils\\) \\| `ResourceLockAutomation` \\| (\\d+) \\| 0 \\| 0 \\|", RegexOptions.Multiline);
            Assert.True(summary.Success);
            Assert.Equal(actual.Count.ToString(), summary.Groups[1].Value);
            int components = Regex.Matches(cross, "^## \\w+Utils$", RegexOptions.Multiline).Count;
            Assert.Contains("without opening " + components + " different", cross);                   // the intro counts the component sections
        }

        [Fact]
        public void TheDocumentationIndex_ListsEveryPage_AndEveryListedPageExists()
        {
            string dir = Path.Combine(ComponentDirectory(), "Documentation");
            string index = File.ReadAllText(Path.Combine(dir, "README.md")).Replace("\r\n", "\n");
            var listed = Regex.Matches(index, "^\\| \\[[^\\]]+\\]\\((\\w+\\.md)\\)", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value);
            var pages = Directory.GetFiles(dir, "*.md").Select(Path.GetFileName).Where(n => n != "README.md");
            Assert.Equal(pages.OrderBy(n => n, StringComparer.Ordinal), listed.OrderBy(n => n, StringComparer.Ordinal));
        }

        /// <summary>The anchors GitHub gives headings: lower case, punctuation removed, spaces to hyphens, repeats numbered, fenced code ignored.</summary>
        private static HashSet<string> Anchors(string markdown)
        {
            var anchors = new HashSet<string>(StringComparer.Ordinal);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            bool fence = false;
            foreach (string line in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { fence = !fence; continue; }
                if (fence) continue;
                Match h = Regex.Match(line, "^#{1,6}\\s+(.+?)\\s*#*$");
                if (!h.Success) continue;
                string slug = Regex.Replace(h.Groups[1].Value.ToLowerInvariant(), "[^\\p{L}\\p{N} _-]", "").Replace(' ', '-');
                seen.TryGetValue(slug, out int n);
                seen[slug] = n + 1;
                anchors.Add(n == 0 ? slug : slug + "-" + n);
            }
            return anchors;
        }

        [Fact]
        public void EveryRelativeLink_ResolvesToAFile_AndToAHeadingWhenItNamesOne()
        {
            var files = Directory.GetFiles(ComponentDirectory(), "*.md", SearchOption.AllDirectories)
                .Concat(new[] { Path.Combine(RepositoryRoot(), "project-docs", "pega-usability-reviews", "ResourceLockUtils-pega-usability-review.md"),
                                Path.Combine(RepositoryRoot(), "project-docs", "plans", "2026-09-27-resourcelockutils-design.md"),
                                // the repository pages this component is registered in
                                Path.Combine(RepositoryRoot(), "README.md"),
                                Path.Combine(RepositoryRoot(), "CrossReference.md"),
                                Path.Combine(RepositoryRoot(), "TESTING.md"),
                                Path.Combine(RepositoryRoot(), "project-docs", "README.md"),
                                Path.Combine(RepositoryRoot(), "project-docs", "pega-usability-reviews", "README.md") });
            foreach (string file in files)
                foreach (Match link in Regex.Matches(File.ReadAllText(file), "\\]\\(([^)#\\s]*)(#[^)\\s]*)?\\)"))
                {
                    string target = link.Groups[1].Value, fragment = link.Groups[2].Value;
                    if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = target.Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), target));
                    // A link that leaves the repository (the root README's ../../releases) is a GitHub web route, not a file.
                    if (!path.StartsWith(RepositoryRoot(), StringComparison.Ordinal)) continue;
                    Assert.True(File.Exists(path) || Directory.Exists(path), Path.GetFileName(file) + " links to " + target + ", which does not exist");
                    if (fragment.Length > 1) Assert.True(Anchors(File.ReadAllText(path)).Contains(fragment.Substring(1)), Path.GetFileName(file) + " links to a missing heading " + target + fragment);
                }
        }

        [Fact]
        public void TheRepositoryRegistration_IsComplete()
        {
            string root = Read("README.md");
            Assert.Contains("[resourcelockutils](src/resourcelockutils/README.md)", root);
            Assert.Contains("[resourcelockutils/Documentation/](src/resourcelockutils/Documentation/README.md)", root);
            Assert.Contains("ResourceLockUtils.Tests.csproj", root);
            Assert.Contains("\"ResourceLockAutomation.dll\"", Read("scripts", "Package-Release.ps1"));
            Assert.Contains("### ResourceLockUtils", Read("TESTING.md"));
            Assert.Contains("ResourceLockUtils-pega-usability-review.md", Read("project-docs", "pega-usability-reviews", "README.md"));
            Assert.Contains("2026-09-27-resourcelockutils-design.md", Read("project-docs", "README.md"));
            Assert.Contains("\"ResourceLockUtils\", \"resourcelockutils\\ResourceLockUtils.csproj\"", Read("src", "AwesomeRpaUtils.sln"));
            Assert.Contains("<AssemblyName>ResourceLockAutomation</AssemblyName>", Read("src", "resourcelockutils", "ResourceLockUtils.csproj"));
            Assert.Contains("<None Include=\"README.md\" Pack=\"true\"", Read("src", "resourcelockutils", "ResourceLockUtils.csproj"));

            // the root README's count of release DLLs matches the release script
            string script = Read("scripts", "Package-Release.ps1");
            string list = script.Substring(script.IndexOf("$releaseAssemblies = @(", StringComparison.Ordinal));
            list = list.Substring(0, list.IndexOf("\n)", StringComparison.Ordinal));
            int count = Regex.Matches(list, "\"\\w+\\.dll\"").Count;
            var words = new Dictionary<string, int> { ["twenty-five"] = 25, ["twenty-six"] = 26, ["twenty-seven"] = 27, ["twenty-eight"] = 28 };
            var stated = Regex.Matches(root, "contains (?:the )?(?:same )?(twenty-\\w+) (?:project )?DLLs").Cast<Match>().Select(m => words[m.Groups[1].Value]).ToList();
            Assert.Equal(2, stated.Count);
            Assert.All(stated, n => Assert.Equal(count, n));
        }
    }
}
