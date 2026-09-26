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

        // ------------------------------------------------------------------ which calls discard results

        private static ReconciliationUtils WithResults()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.ReconcileJson("[{\"id\":\"1\"}]", "[{\"id\":\"1\"}]", out _, out m), m);
            return c;
        }

        private static bool HasResults(ReconciliationUtils c) => c.GetSummary(out _, out _, out _, out _, out _);

        /// <summary>Applies one named setup or reading call with valid arguments, returning whether it succeeded.</summary>
        private static bool Apply(ReconciliationUtils c, string name)
        {
            string m;
            const string valid = "{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"/id\",\"rightPointer\":\"/id\"}]}";
            switch (name)
            {
                case "ClearDefinition": return c.ClearDefinition(out m);
                case "AddKeyMappingSimple": return c.AddKeyMappingSimple("K2", "/a", "/a", out m);
                case "AddKeyMapping": return c.AddKeyMapping("K2", "/a", "/a", true, true, out m);
                case "AddTextComparisonSimple": return c.AddTextComparisonSimple("T", "/a", "/a", out m);
                case "AddTextComparison": return c.AddTextComparison("T", "/a", "/a", true, true, ComparisonNullPolicy.RequireValue, out m);
                case "AddDecimalComparisonSimple": return c.AddDecimalComparisonSimple("T", "/a", "/a", out m);
                case "AddDecimalComparison": return c.AddDecimalComparison("T", "/a", "/a", "0.1", ComparisonNullPolicy.RequireValue, out m);
                case "AddBooleanComparisonSimple": return c.AddBooleanComparisonSimple("T", "/a", "/a", out m);
                case "AddBooleanComparison": return c.AddBooleanComparison("T", "/a", "/a", ComparisonNullPolicy.RequireValue, out m);
                case "AddMoneyComparisonSimple": return c.AddMoneyComparisonSimple("T", "/a", "/a", "/c", "/c", out m);
                case "AddMoneyComparison": return c.AddMoneyComparison("T", "/a", "/a", "/c", "/c", "0.1", ComparisonNullPolicy.RequireValue, out m);
                case "AddCalendarDateComparisonSimple": return c.AddCalendarDateComparisonSimple("T", "/a", "/a", "yyyy-MM-dd", "yyyy-MM-dd", out m);
                case "AddCalendarDateComparison": return c.AddCalendarDateComparison("T", "/a", "/a", "yyyy-MM-dd", "yyyy-MM-dd", 1, ComparisonNullPolicy.RequireValue, out m);
                case "AddInstantComparisonSimple": return c.AddInstantComparisonSimple("T", "/a", "/a", out m);
                case "AddInstantComparison": return c.AddInstantComparison("T", "/a", "/a", 1, ComparisonNullPolicy.RequireValue, out m);
                case "LoadDefinitionJson": return c.LoadDefinitionJson(valid, out m);
                case "ConfigureLimits": return c.ConfigureLimits(100, 100000, 100, 100, out m);
                case "ConfigureTableLimits": return c.ConfigureTableLimits(10, 100, 10, out m);
                case "ConfigureOutputLimit": return c.ConfigureOutputLimit(1000000, out m);
                case "ValidateDefinitionJson": return c.ValidateDefinitionJson(valid, out _, out _, out m);
                case "GetDefinitionJson": return c.GetDefinitionJson(out _, out m);
                case "ExportResultsJson": return c.ExportResultsJson("x", out _, out m);
                case "GetSummary": return c.GetSummary(out _, out _, out _, out _, out m);
                case "GetSummaryJson": return c.GetSummaryJson(out _, out m);
                case "ResetResultCursor": return c.ResetResultCursor(out m);
                case "TryReadNextException": return c.TryReadNextException(out _, out _, out _, out _, out _, out _, out _, out _, out m);
                case "TryReadNextDifference": return c.TryReadNextDifference(out _, out _, out _, out _, out _, out _, out m);
                case "GetResultJson": return c.GetResultJson("r000001", out _, out m);
                default: throw new ArgumentException("no call is defined for " + name);
            }
        }

        private static List<string> Listed(string page, string marker)
        {
            string line = page.Split('\n').Single(l => l.TrimStart().StartsWith("- **" + marker + "**"));
            return Regex.Matches(line, "`(\\w+)`").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        }

        [Fact]
        public void TheConfigurationPage_SaysWhichCallsDiscardResults_AndTheCodeDoes_ExactlyThat()
        {
            string page = Read("src", "reconciliationutils", "Documentation", "Configuration.md");
            List<string> discards = Listed(page, "Discards results:"), keeps = Listed(page, "Keeps results:");

            // every public method that is neither a run nor ClearResults appears in exactly one list
            string[] neither = { "ReconcileJson", "ReconcileDataTables", "ClearResults" };
            Assert.Equal(ConventionTests.PublicMethods().Select(m => m.Name).Except(neither).OrderBy(n => n, StringComparer.Ordinal), discards.Concat(keeps).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Empty(discards.Intersect(keeps));

            foreach (string name in discards)
            {
                using ReconciliationUtils c = WithResults();
                Assert.True(HasResults(c));
                Assert.True(Apply(c, name), name + " should succeed with the arguments used here");
                Assert.False(HasResults(c), name + " is documented as discarding results but they are still there");
            }
            foreach (string name in keeps)
            {
                using ReconciliationUtils c = WithResults();
                Assert.True(Apply(c, name), name + " should succeed");
                Assert.True(HasResults(c), name + " is documented as keeping results but they are gone");
            }
        }

        [Fact]
        public void ARefusedSetupCall_DiscardsNothing()
        {
            using var c = WithResults();
            Assert.False(c.AddTextComparisonSimple("", "/a", "/a", out _));
            Assert.False(c.ConfigureLimits(0, 0, 0, 0, out _));
            Assert.False(c.ConfigureTableLimits(0, 0, 0, out _));
            Assert.False(c.ConfigureOutputLimit(0, out _));
            Assert.False(c.LoadDefinitionJson("{", out _));
            Assert.True(HasResults(c));
        }

        [Fact]
        public void TheReleaseAssemblyCount_StatedInTheRootReadme_MatchesTheReleaseScript()
        {
            string script = Read("scripts", "Package-Release.ps1");
            string list = script.Substring(script.IndexOf("$releaseAssemblies = @(", StringComparison.Ordinal));
            list = list.Substring(0, list.IndexOf("\n)", StringComparison.Ordinal));
            int count = Regex.Matches(list, "\"\\w+\\.dll\"").Count;
            var words = new Dictionary<string, int> { ["twenty"] = 20, ["twenty-one"] = 21, ["twenty-two"] = 22, ["twenty-three"] = 23, ["twenty-four"] = 24, ["twenty-five"] = 25, ["twenty-six"] = 26, ["twenty-seven"] = 27, ["twenty-eight"] = 28, ["twenty-nine"] = 29, ["thirty"] = 30 };
            string readme = Read("README.md");
            var stated = Regex.Matches(readme, "contains (?:the )?(?:same )?(twenty(?:-\\w+)?|thirty) (?:project )?DLLs").Cast<Match>().Select(m => words[m.Groups[1].Value]).ToList();
            Assert.Equal(2, stated.Count);
            Assert.All(stated, n => Assert.Equal(count, n));
            Assert.Contains("ReconciliationAutomation.dll", list);
        }

        [Fact]
        public void EveryComponentProject_IsInTheReleaseScript_UnderItsAssemblyName_AndInTheSolution()
        {
            string root = RepositoryRoot();
            string script = Read("scripts", "Package-Release.ps1");
            string solution = Read("src", "AwesomeRpaUtils.sln");
            string csproj = Read("src", "reconciliationutils", "ReconciliationUtils.csproj");
            string assembly = Regex.Match(csproj, "<AssemblyName>([^<]+)</AssemblyName>").Groups[1].Value;
            Assert.Equal("ReconciliationAutomation", assembly);
            Assert.Contains("\"" + assembly + ".dll\"", script);
            Assert.Contains("\"ReconciliationUtils\", \"reconciliationutils\\ReconciliationUtils.csproj\"", solution);
            Assert.Contains("\"ReconciliationUtils.Tests\", \"reconciliationutils\\ReconciliationUtils.Tests\\ReconciliationUtils.Tests.csproj\"", solution);
            Assert.Contains("<None Include=\"README.md\" Pack=\"true\"", csproj);                       // the NuGet package carries the component README
            Assert.True(File.Exists(Path.Combine(root, "src", "reconciliationutils", "README.md")));
            Assert.Contains("ReconciliationUtils", Read("CONTRIBUTING.md") + Read("README.md"));
        }
    }
}
