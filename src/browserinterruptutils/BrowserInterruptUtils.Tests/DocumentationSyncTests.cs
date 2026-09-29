using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>
    /// The README's method and event tables must say exactly what the code has, and the repository
    /// pages this component is registered in must agree with it. Reads only files and reflection, so
    /// it runs under the local -p:UseWPF=false convenience like the rest of the fake-driven tests.
    /// </summary>
    public sealed class DocumentationSyncTests
    {
        private static string ComponentDirectory()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "browserinterruptutils");
                if (File.Exists(Path.Combine(candidate, "BrowserInterruptUtils.csproj"))) return candidate;
            }
            throw new FileNotFoundException("Could not find src/browserinterruptutils above " + AppContext.BaseDirectory);
        }

        // A Windows checkout has CRLF line endings, which would defeat the line-anchored patterns below.
        private static string Readme() => File.ReadAllText(Path.Combine(ComponentDirectory(), "README.md")).Replace("\r\n", "\n");

        private static string TypeName(Type t)
        {
            if (t.IsByRef) t = t.GetElementType();
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int)) return "int";
            return t.Name;
        }

        private static string DefaultText(object value) =>
            value == null ? "null" : value is bool b ? (b ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture);

        private static string Signature(MethodInfo m) =>
            TypeName(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p =>
                (p.IsOut ? "out " : "") + TypeName(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + DefaultText(p.DefaultValue) : ""))) + ")";

        private static MethodInfo[] PublicMethods() =>
            typeof(BrowserInterruptUtils).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName).ToArray();

        private static string DescriptionOf(MemberInfo m) => m.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description;

        private const string MethodRow = "^\\| `(\\w+)` \\| `(bool [^`]+)` \\| (.+) \\|$";

        private static List<(string name, string signature, string description)> Rows(string markdown) =>
            Regex.Matches(markdown, MethodRow, RegexOptions.Multiline).Cast<Match>()
                .Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value.Replace("\\|", "|"))).ToList();   // a | in a cell is written \| so it does not split the column

        [Fact]
        public void TheReadmeMethodTables_MatchTheCode()
        {
            var rows = Rows(Readme());
            MethodInfo[] methods = PublicMethods();
            Assert.Equal(22, methods.Length);
            Assert.Equal(methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal), rows.Select(r => r.name).OrderBy(n => n, StringComparer.Ordinal));
            var problems = new List<string>();
            foreach (MethodInfo m in methods)
            {
                var row = rows.Single(r => r.name == m.Name);
                if (Signature(m) != row.signature) problems.Add(m.Name + " signature: README " + row.signature + " / code " + Signature(m));
                if (DescriptionOf(m) != row.description) problems.Add(m.Name + " description differs from [Description]");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            Assert.Contains("All " + methods.Length + " methods", Readme());
        }

        [Fact]
        public void TheReadmeEventTable_MatchesTheCode()
        {
            EventInfo[] events = typeof(BrowserInterruptUtils).GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var rows = Regex.Matches(Readme(), "^\\| `(\\w+)` \\| `EventHandler<(\\w+)>` \\| (.+) \\|$", RegexOptions.Multiline).Cast<Match>()
                .Select(m => (name: m.Groups[1].Value, argsType: m.Groups[2].Value)).ToList();
            Assert.Equal(events.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal), rows.Select(r => r.name).OrderBy(n => n, StringComparer.Ordinal));
            foreach (EventInfo e in events)
                Assert.Equal(e.EventHandlerType.GetGenericArguments()[0].Name, rows.Single(r => r.name == e.Name).argsType);
            Assert.NotNull(DescriptionOf(events[0]));
        }

        [Fact]
        public void TheDocumentedLimits_MatchTheCode()
        {
            string rules = File.ReadAllText(Path.Combine(ComponentDirectory(), "Documentation", "Rules.md")).Replace("\r\n", "\n");
            Assert.Contains("at most " + BrowserPopupRule.MaxNameLength + " characters", rules);
            Assert.Contains("up to " + BrowserPopupRule.MaxRules + " rules", rules);

            string lifecycle = File.ReadAllText(Path.Combine(ComponentDirectory(), "Documentation", "Lifecycle.md")).Replace("\r\n", "\n");
            Assert.Contains("raised to " + BrowserPopupEngine.MinOverlaySweepIntervalMs + " ms", lifecycle);
            Assert.Contains(BrowserPopupEngine.VerifyDelayMs + " ms (`VerifyDelayMs`)", lifecycle.Replace("\n", " "));

            Assert.Contains("every " + BrowserPopupEngine.ReapIntervalMs + " ms (`ReapIntervalMs`)", lifecycle.Replace("\n", " "));
            Assert.Contains("at most " + BrowserPopupEngine.ReapMaxChecksPerPass + " checks per pass", lifecycle.Replace("\n", " "));

            string readme = Readme().Replace("\n", " ");
            Assert.Contains("every " + BrowserPopupEngine.ReapIntervalMs + " ms (`ReapIntervalMs`)", Regex.Replace(readme, " {2,}", " "));
            Assert.Contains(BrowserPopupEngine.MainWindowRefusal, Regex.Replace(readme, " {2,}", " ").Replace("(\"", "").Replace("\")", ""));
            Assert.Contains(BrowserPopupEngine.MainWindowRefusal, File.ReadAllText(Path.Combine(ComponentDirectory(), "Documentation", "Rules.md")).Replace("\r\n", "\n").Replace("\n", " ").Replace("\"", ""));
            Assert.Contains("the log keeps the last " + BrowserPopupEngine.LogCapacity, readme);
            Assert.Contains("waits " + BrowserPopupEngine.VerifyDelayMs + " ms (`VerifyDelayMs`)", Regex.Replace(readme, " {2,}", " "));
            Assert.Contains("about " + string.Join(", ", BrowserPopupEngine.ScheduleMs.Take(BrowserPopupEngine.ScheduleMs.Length - 1)) + " and "
                + BrowserPopupEngine.ScheduleMs.Last() + " ms", Regex.Replace(readme, " {2,}", " "));
        }

        // ------------------------------------------------------------------ repository-wide documentation

        private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(ComponentDirectory(), "..", ".."));

        private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

        [Fact]
        public void TheCrossReference_ListsTheSameMethodsWithTheSameText_Sorted_WithTheRightCounts()
        {
            string cross = Read("CrossReference.md");
            int start = cross.IndexOf("## BrowserInterruptUtils", StringComparison.Ordinal);
            int end = cross.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
            string section = cross.Substring(start, end - start);
            var actual = Rows(section);
            var expected = Rows(Readme());
            Assert.Equal(expected.OrderBy(r => r.name, StringComparer.Ordinal), actual);

            int events = typeof(BrowserInterruptUtils).GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length;
            Assert.Equal(events, Regex.Matches(section, "^\\| `\\w+` \\| `EventHandler<\\w+>` \\|", RegexOptions.Multiline).Count);
            Match summary = Regex.Match(cross, "^\\| \\[BrowserInterruptUtils\\]\\(#browserinterruptutils\\) \\| `BrowserInterruptAutomation` \\| (\\d+) \\| 0 \\| (\\d+) \\|", RegexOptions.Multiline);
            Assert.True(summary.Success);
            Assert.Equal(actual.Count.ToString(), summary.Groups[1].Value);
            Assert.Equal(events.ToString(), summary.Groups[2].Value);
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
                .Concat(new[] { Path.Combine(RepositoryRoot(), "project-docs", "plans", "2026-09-29-browserinterruptutils-design.md"),
                                // the repository pages this component is registered in or cross-referenced from
                                Path.Combine(RepositoryRoot(), "README.md"),
                                Path.Combine(RepositoryRoot(), "CrossReference.md"),
                                Path.Combine(RepositoryRoot(), "TESTING.md"),
                                Path.Combine(RepositoryRoot(), "CONTRIBUTING.md"),
                                Path.Combine(RepositoryRoot(), "project-docs", "README.md"),
                                Path.Combine(RepositoryRoot(), "src", "interruptutils", "README.md"),
                                Path.Combine(RepositoryRoot(), "src", "dialogutils", "README.md") });
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
            Assert.Contains("[browserinterruptutils](src/browserinterruptutils/README.md)", root);
            Assert.Contains("[browserinterruptutils/Documentation/](src/browserinterruptutils/Documentation/README.md)", root);
            Assert.Contains("BrowserInterruptUtils.Tests.csproj", root);
            Assert.Contains("\"BrowserInterruptAutomation.dll\"", Read("scripts", "Package-Release.ps1"));
            Assert.Contains("### BrowserInterruptUtils (needs a desktop and a real browser; live checks pending)", Read("TESTING.md"));
            Assert.Contains("2026-09-29-browserinterruptutils-design.md", Read("project-docs", "README.md"));
            Assert.Contains("\"BrowserInterruptUtils\", \"browserinterruptutils\\BrowserInterruptUtils.csproj\"", Read("src", "AwesomeRpaUtils.sln"));
            Assert.Contains("\"BrowserInterruptUtils.Tests\", \"browserinterruptutils\\BrowserInterruptUtils.Tests\\BrowserInterruptUtils.Tests.csproj\"", Read("src", "AwesomeRpaUtils.sln"));
            Assert.Contains("[BrowserInterruptUtils](../browserinterruptutils/README.md)", Read("src", "interruptutils", "README.md"));
            Assert.Contains("[BrowserInterruptUtils](../browserinterruptutils/README.md)", Read("src", "dialogutils", "README.md"));
            string csproj = Read("src", "browserinterruptutils", "BrowserInterruptUtils.csproj");
            Assert.Contains("<AssemblyName>BrowserInterruptAutomation</AssemblyName>", csproj);
            Assert.Contains("<None Include=\"README.md\" Pack=\"true\"", csproj);

            // the root README's count of release DLLs matches the release script
            string script = Read("scripts", "Package-Release.ps1");
            string list = script.Substring(script.IndexOf("$releaseAssemblies = @(", StringComparison.Ordinal));
            list = list.Substring(0, list.IndexOf("\n)", StringComparison.Ordinal));
            int count = Regex.Matches(list, "\"\\w+\\.dll\"").Count;
            var words = new Dictionary<string, int> { ["twenty-six"] = 26, ["twenty-seven"] = 27, ["twenty-eight"] = 28, ["twenty-nine"] = 29, ["thirty"] = 30 };
            var stated = Regex.Matches(root, "contains (?:the )?(?:same )?(twenty-\\w+|thirty) (?:project )?DLLs").Cast<Match>().Select(m => words[m.Groups[1].Value]).ToList();
            Assert.Equal(2, stated.Count);
            Assert.All(stated, n => Assert.Equal(count, n));
        }
    }
}
