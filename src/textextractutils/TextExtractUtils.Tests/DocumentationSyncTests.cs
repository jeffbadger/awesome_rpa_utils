using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace TextExtractAutomation.Tests
{
    /// <summary>The README's method table must say exactly what the code has: every public method once, its signature and its description.</summary>
    public sealed class DocumentationSyncTests
    {
        private static string ComponentDirectory()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "textextractutils");
                if (File.Exists(Path.Combine(candidate, "TextExtractUtils.csproj"))) return candidate;
            }
            throw new FileNotFoundException("Could not find src/textextractutils above " + AppContext.BaseDirectory);
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
                .Select(m => (name: m.Groups[1].Value, signature: m.Groups[2].Value, description: m.Groups[3].Value)).ToList();
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
            Assert.Contains("All " + methods.Length + " phase 1 methods", Readme());
        }

        [Fact]
        public void TheReadmeTemplateExample_IsAValidTemplate()
        {
            Match block = Regex.Match(Readme(), "```json\n(.*?)\n```", RegexOptions.Singleline);
            Assert.True(block.Success);
            using var c = new TextExtractUtils();
            Assert.True(c.ValidateTemplateJson(block.Groups[1].Value, out int errors, out string report, out string m), m);
            Assert.True(errors == 0, report);
        }
    }
}
