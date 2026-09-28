using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ResourceLockAutomation.Tests
{
    /// <summary>Runs the examples printed in Documentation/ and checks the documented numbers against the code, so a page cannot drift.</summary>
    public sealed class DocumentationExampleTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "rl-docs-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static string ComponentDirectory()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "resourcelockutils");
                if (File.Exists(Path.Combine(candidate, "ResourceLockUtils.csproj"))) return candidate;
            }
            throw new FileNotFoundException("Could not find src/resourcelockutils above " + AppContext.BaseDirectory);
        }

        private static string Page(string name) => File.ReadAllText(Path.Combine(ComponentDirectory(), "Documentation", name)).Replace("\r\n", "\n");

        private static List<string> Blocks(string page, string language) =>
            Regex.Matches(page, "```" + language + "\n(.*?)\n\\s*```", RegexOptions.Singleline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        /// <summary>The rows of the first Markdown table after <paramref name="heading"/>, cells trimmed and unwrapped from backticks.</summary>
        private static List<string[]> Table(string page, string heading)
        {
            string part = page.Substring(page.IndexOf(heading, StringComparison.Ordinal));
            var rows = new List<string[]>();
            bool started = false;
            foreach (string line in part.Split('\n'))
            {
                if (!line.StartsWith("|", StringComparison.Ordinal)) { if (started) break; continue; }
                started = true;
                if (Regex.IsMatch(line, "^\\|[-| ]+\\|$")) continue;
                rows.Add(line.Trim().Trim('|').Split('|').Select(c => c.Trim().Trim('`')).ToArray());
            }
            return rows.Skip(1).ToList();
        }

        private static string Resource(string page, string code) => Regex.Match(code, "LockScope\\.\\w+, \"([^\"]+)\"").Groups[1].Value;

        [Fact]
        public void QuickStart_TheDocumentedCallsGiveTheDocumentedOutcomes()
        {
            string page = Page("QuickStart.md");
            string code = Blocks(page, "csharp").Single();
            string resource = Resource(page, code) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);   // the Process scope is shared by parallel tests
            Assert.StartsWith("SAP-User-BATCH01", resource);
            using var locks = new ResourceLockUtils();
            Assert.True(locks.TryAcquireLock(LockScope.Process, resource, "Robot 1", 300, out bool acquired, out string token, out string currentHolder, out string message), message);
            Assert.Equal((true, 32, (string)null), (acquired, token.Length, currentHolder));
            Assert.Contains("// acquired True, token (32 characters), currentHolder null", code);
            Assert.True(locks.TryAcquireLock(LockScope.Process, resource, "Robot 2", 300, out acquired, out string secondToken, out currentHolder, out message), message);
            Assert.Equal((false, (string)null, "Robot 1"), (acquired, secondToken, currentHolder));
            Assert.Contains("// acquired False, secondToken null, currentHolder \"Robot 1\"", code);
            Assert.True(locks.ReleaseLock(LockScope.Process, resource, token, out bool released, out message), message);
            Assert.True(released);
            Assert.Contains("// released True", code);
            Assert.Equal(3, Regex.Matches(code, "locks\\.\\w+\\(LockScope\\.Process, \"SAP-User-BATCH01\"").Count);
        }

        [Fact]
        public void Slots_TheDocumentedPoolFillsAndReportsAsDocumented()
        {
            string page = Page("Slots.md");
            string code = Blocks(page, "csharp").Single();
            Assert.Equal("Portal-Sessions", Resource(page, code));
            using var locks = new ResourceLockUtils();
            Assert.True(locks.ConfigureLockFolder(folder, out string m), m);
            int ran = 0;
            foreach (Match call in Regex.Matches(code, "TryAcquireSlot\\(LockScope\\.Machine, \"Portal-Sessions\", (\\d+), \"([^\"]+)\", (\\d+),[^\n]*\n// acquired (True|False)[^,]*, (?:token4 null, )?holderCount (\\d+)"))
            {
                Assert.True(locks.TryAcquireSlot(LockScope.Machine, "Portal-Sessions", int.Parse(call.Groups[1].Value), call.Groups[2].Value, int.Parse(call.Groups[3].Value),
                    out bool acquired, out string token, out int holderCount, out m), m);
                Assert.Equal((call.Groups[4].Value == "True", int.Parse(call.Groups[5].Value)), (acquired, holderCount));
                Assert.Equal(acquired, token != null);
                ran++;
            }
            Assert.Equal(4, ran);                                                                           // every documented call was run
            Assert.Equal(4, Regex.Matches(code, "TryAcquireSlot\\(").Count);
            Assert.True(locks.GetLockStatus(LockScope.Machine, "Portal-Sessions", out bool held, out string holders, out _, out int count, out m), m);
            Assert.Contains("// held " + held + ", holders \"" + holders + "\", count " + count, code);
        }

        private static List<string> Names(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToList();

        [Fact]
        public void ServerBots_TheJsonExamplesHaveExactlyTheRealProperties()
        {
            string page = Page("ServerBots.md");
            List<string> json = Blocks(page, "json");
            Assert.Equal(2, json.Count);
            using var locks = new ResourceLockUtils();
            Assert.True(locks.ConfigureLockFolder(folder, out string m), m);
            Assert.True(locks.ValidateLockFolder(out _, out string report, out m), m);
            using (JsonDocument shown = JsonDocument.Parse(json[0]), real = JsonDocument.Parse(report))
                Assert.Equal(Names(real.RootElement), Names(shown.RootElement));
            Assert.True(locks.TryAcquireLock(LockScope.Machine, "SAP-User-BATCH01", "MyServer_3", 300, out _, out _, out _, out m), m);
            Assert.True(locks.GetLocksJson(LockScope.Machine, out string locksJson, out m), m);
            using (JsonDocument shown = JsonDocument.Parse(json[1]), real = JsonDocument.Parse(locksJson))
            {
                Assert.Equal(Names(real.RootElement), Names(shown.RootElement));
                JsonElement realLock = real.RootElement.GetProperty("locks")[0], shownLock = shown.RootElement.GetProperty("locks")[0];
                Assert.Equal(Names(realLock), Names(shownLock));
                Assert.Equal(Names(realLock.GetProperty("holders")[0]), Names(shownLock.GetProperty("holders")[0]));
                Assert.Equal(realLock.GetProperty("resource").GetString(), shownLock.GetProperty("resource").GetString());   // stored in lower case, as shown
            }
            Assert.Contains("icacls D:\\RobotLocks /grant \"CONTOSO\\RPA-Robots:(OI)(CI)M\"", page);
            Assert.Contains("C:\\ProgramData\\AwesomeRpaUtils\\Locks", page);
            Assert.Contains(Path.Combine("AwesomeRpaUtils", "Locks"), LockInput.DefaultFolder);
            Assert.Contains("5 minutes", page);
            Assert.Equal(5, MachineLocks.SafeDeleteMinutes);
        }

        [Fact]
        public void Limits_TheTablesAreTheCode()
        {
            string page = Page("Limits.md");
            List<string[]> limits = Table(page, "| Limit |");
            string Min(string start) => limits.Single(r => r[0].StartsWith(start, StringComparison.Ordinal))[1].Replace(",", "");
            string Max(string start) => limits.Single(r => r[0].StartsWith(start, StringComparison.Ordinal))[2].Replace(",", "");
            Assert.Equal((LockLimits.MinLeaseSeconds.ToString(), LockLimits.MaxLeaseSeconds.ToString()), (Min("Lease"), Max("Lease")));
            Assert.Equal(("0", LockLimits.MaxWaitMilliseconds.ToString()), (Min("Wait"), Max("Wait")));
            Assert.Equal((LockLimits.MinCapacity.ToString(), LockLimits.MaxCapacity.ToString()), (Min("Slots"), Max("Slots")));
            Assert.Equal(LockLimits.MaxResourceLength.ToString(), Max("Characters in a resource"));
            Assert.Equal(LockLimits.MaxHolderLength.ToString(), Max("Characters in a holder"));
            Assert.Equal(LockLimits.MaxFolderPathLength.ToString(), Max("Characters in a lock folder"));
            Assert.Equal(LockLimits.MaxHeldPerInstance.ToString(), Max("Locks and slots one component"));
            Assert.Equal(7, limits.Count);

            List<string[]> timings = Table(page, "| Timing |");
            string Value(string start) => timings.Single(r => r[0].Contains(start, StringComparison.Ordinal))[1];
            Assert.Equal(ProcessLocks.SweepIntervalMs / 1000 + " s", Value("swept"));
            Assert.Equal(MachineLocks.UnreadableGraceSeconds + " s", Value("still being written"));
            Assert.Equal(LockLimits.MaxLeaseSeconds.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " s", Value("cannot read"));
            Assert.Equal(MachineLocks.SafeDeleteMinutes + " min old", Value("old lease file"));
            Assert.Equal(MachineLocks.MaxDecisionSeconds + " s", Value("starts over"));
            Assert.Equal("50 ms, growing to 1 s", Value("waiting acquire"));
            Assert.Equal(6, timings.Count);
        }
    }
}
