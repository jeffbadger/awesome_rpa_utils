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
    }
}
