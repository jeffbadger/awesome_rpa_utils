using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CommandLineAutomation;
using Xunit;

namespace CommandLineAutomation.Tests
{
    /// <summary>
    /// Unit tests for CommandLineUtils: argument validation (cross-platform), the
    /// RunShellCommand allowlist tokenizer/parser (cross-platform, pure logic), the
    /// output-capture cap, and live process behavior on Windows (cmd.exe pinning,
    /// quoting, timeouts, env vars). Live-dialog-equivalent behavior beyond these is
    /// covered by the Pega Unit Test plan in the repo's TESTING.md.
    /// </summary>
    public class CommandLineUtilsTests
    {
        private readonly CommandLineUtils _cli = new CommandLineUtils();

        private static bool IsWindows => OperatingSystem.IsWindows();

        private static string WindowsCmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

        // --- Argument validation: fail fast with false + message, never throw, no process started ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Run_NullOrWhitespaceFileName_ReturnsFalseWithMessage(string fileName)
        {
            Assert.False(_cli.Run(fileName, out CommandResult result, out string message));
            Assert.Null(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void RunShellCommand_NullOrWhitespaceCommand_ReturnsFalseWithMessage(string command)
        {
            Assert.False(_cli.RunShellCommand(command, out CommandResult result, out string message));
            Assert.Null(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void RunElevated_NullFileName_ReturnsFalseWithoutStartingAnything()
        {
            Assert.False(_cli.RunElevated(null, out int exitCode, out bool timedOut, out string message));
            Assert.Equal(0, exitCode);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StartFireAndForget_NullOrWhitespaceFileName_ReturnsFalseWithMessage(string fileName)
        {
            Assert.False(_cli.StartFireAndForget(fileName, out int processId, out string message));
            Assert.Equal(0, processId);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(-500)]
        public void Run_InvalidTimeout_ReturnsFalseWithMessage(int timeoutMs)
        {
            Assert.False(_cli.Run("everything-is-fine.exe", out _, out string message, timeoutMs: timeoutMs));
            Assert.Contains("-1", message);
        }

        [Fact]
        public void RunShellCommand_InvalidTimeout_ReturnsFalseWithMessage()
        {
            Assert.False(_cli.RunShellCommand("echo hi", out _, out string message, timeoutMs: -2));
            Assert.Contains("-1", message);
        }

        [Fact]
        public void Run_MissingExecutable_ReturnsFalseWithMessage()
        {
            string missing = IsWindows ? @"C:\definitely\missing\app-not-here.exe" : "/definitely/missing/app-not-here";
            Assert.False(_cli.Run(missing, out CommandResult result, out string message));
            Assert.Null(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void Run_EnvironmentVariablesWithNullKey_ReturnsFalseWithMessage()
        {
            // A Dictionary<string,string> can't hold a null key, but a custom
            // IDictionary built from untrusted data can — the guard must reject it
            // before Process.Start rather than throw ArgumentNullException.
            var env = new NullKeyDictionary();
            env.Add(null, "value");
            Assert.False(_cli.Run("anything.exe", out CommandResult result, out string message, environmentVariables: env));
            Assert.Null(result);
            Assert.Contains("null key", message);
        }

        [Fact]
        public void RunShellCommand_EnvironmentVariablesWithNullKey_ReturnsFalseWithMessage()
        {
            var env = new NullKeyDictionary();
            env.Add(null, "value");
            Assert.False(_cli.RunShellCommand("echo hi", out CommandResult result, out string message, environmentVariables: env));
            Assert.Null(result);
            Assert.Contains("null key", message);
        }

        [Fact]
        public void StartFireAndForget_EnvironmentVariablesWithNullKey_ReturnsFalseWithMessage()
        {
            var env = new NullKeyDictionary();
            env.Add(null, "value");
            Assert.False(_cli.StartFireAndForget("anything.exe", out int processId, out string message, environmentVariables: env));
            Assert.Equal(0, processId);
            Assert.Contains("null key", message);
        }

        [Fact]
        public void RunElevated_RelativeFileName_ReturnsFalseWithMessage()
        {
            // A relative name would be resolved through PATH/working directory with
            // admin rights — rejected up front, before any UAC prompt.
            Assert.False(_cli.RunElevated("app-not-here.exe", out int exitCode, out bool timedOut, out string message));
            Assert.Equal(0, exitCode);
            Assert.False(timedOut);
            Assert.Contains("absolute", message);
        }

        // --- Non-Windows live test: Run works cross-platform against a real child process ---

        [SkippableFactPlatform("/bin/echo")]
        public void Run_ChildOutput_IsCaptured()
        {
            Assert.True(_cli.Run("/bin/echo", out CommandResult result, out string message, arguments: "hello from unit test"));
            Assert.False(result.TimedOut);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("hello from unit test", result.StandardOutput);
            Assert.Null(message);
        }

        // --- Allowlist tokenizer/parser (internal, pure logic, cross-platform) ---

        [Theory]
        [InlineData("dir & echo hi", 2)]
        [InlineData("robocopy a b && findstr x | sort", 3)]
        [InlineData("robocopy a b || findstr x", 2)]
        [InlineData("echo \"a && b\"", 1)]           // operator inside quotes is literal
        [InlineData("echo A^&B hello", 1)]           // caret-escaped operator is literal
        [InlineData("echo hi\ndir", 2)]              // newline separates commands too
        [InlineData("robocopy /MIR src dst", 1)]
        public void SplitShellCommandSegments_SplitsOnTopLevelOperators(string command, int expectedCount)
        {
            Assert.Equal(expectedCount, CommandLineUtils.SplitShellCommandSegments(command).Count);
        }

        [Theory]
        [InlineData(" robocopy /MIR x y", "robocopy")]
        [InlineData("\"C:\\Program Files\\App\\tool.exe\" /x", "C:\\Program Files\\App\\tool.exe")]
        [InlineData("   ", null)]
        [InlineData("", null)]
        public void ExtractSegmentProgram_TakesFirstQuotedOrWhitespaceToken(string segment, string expected)
        {
            Assert.Equal(expected, CommandLineUtils.ExtractSegmentProgram(segment));
        }

        [Theory]
        [InlineData("Robocopy.EXE", "Robocopy")]
        [InlineData("C:\\Windows\\System32\\ROBOCOPY.EXE", "ROBOCOPY")]
        [InlineData(" robocopy ", "robocopy")]
        [InlineData("run.cmd", "run")]
        [InlineData("   ", null)]
        [InlineData(null, null)]
        public void NormalizeProgramName_StripsPathAndExecutableExtension(string input, string expected)
        {
            Assert.Equal(expected, CommandLineUtils.NormalizeProgramName(input));
        }

        [Fact]
        public void AppendCapped_StopsAtLimitAndFlagsTruncation()
        {
            var sb = new StringBuilder();
            bool truncated = false;
            int defaultCap = CommandLineUtils.MaxCapturedOutputChars;

            try
            {
                CommandLineUtils.MaxCapturedOutputChars = 50;
                string line = new string('x', 40); // 40 chars + newline: two lines overflow a 50-char cap
                for (int i = 0; i < 20; i++)
                    CommandLineUtils.AppendCapped(sb, line, ref truncated);

                Assert.True(truncated);
                Assert.True(sb.Length < 500);
                Assert.Contains("truncated", sb.ToString());
            }
            finally
            {
                CommandLineUtils.MaxCapturedOutputChars = defaultCap;
            }
        }

        [Fact]
        public void AppendCapped_UnderLimit_NotTruncated()
        {
            var sb = new StringBuilder();
            bool truncated = false;

            CommandLineUtils.AppendCapped(sb, "short line", ref truncated);

            Assert.False(truncated);
            Assert.Equal("short line" + Environment.NewLine, sb.ToString());
        }

        [Fact]
        public void AppendCapped_OversizedSingleLine_IsTruncated()
        {
            // A single newline-free line larger than the cap must be truncated itself,
            // not appended whole (which would grow the buffer past the cap).
            var sb = new StringBuilder();
            bool truncated = false;
            int defaultCap = CommandLineUtils.MaxCapturedOutputChars;

            try
            {
                CommandLineUtils.MaxCapturedOutputChars = 50;
                CommandLineUtils.AppendCapped(sb, new string('x', 10_000), ref truncated);

                Assert.True(truncated);
                Assert.True(sb.Length < 200); // capped line + notice, not 10k
                Assert.Contains("truncated", sb.ToString());
            }
            finally
            {
                CommandLineUtils.MaxCapturedOutputChars = defaultCap;
            }
        }

        // --- RunShellCommand Windows behavior: /d /s /c wrapping, allowlist, env vars, timeout ---

        [SkippableFactPlatform("windows")]
        public void RunShellCommand_SimpleCommand_RunsAndCaptures()
        {
            Assert.True(_cli.RunShellCommand("echo hello", out CommandResult result, out string message, workingDirectory: Environment.SystemDirectory));
            Assert.Null(message);
            Assert.Equal(0, result.ExitCode);
            Assert.False(result.TimedOut);
            Assert.Contains("hello", result.StandardOutput);
        }

        [SkippableFactPlatform("windows")]
        public void RunShellCommand_TrailingBackslashCommand_IsNotMangledByWrapping()
        {
            // 'cd C:\' ends in a backslash — the point where cmd's /c quote-stripping
            // rules became unpredictable. With /s the command must arrive verbatim.
            Assert.True(_cli.RunShellCommand("cd C:\\", out CommandResult result, out _));
            Assert.Equal(0, result.ExitCode);
            Assert.False(result.TimedOut);
        }

        [SkippableFactPlatform("windows")]
        public void RunShellCommand_EmbeddedQuotes_ExecuteVerbatim()
        {
            Assert.True(_cli.RunShellCommand("echo \"a b\"", out CommandResult result, out _));
            Assert.Contains("a b", result.StandardOutput);
            Assert.DoesNotContain("is not recognized", result.StandardOutput + result.StandardError);
        }

        [SkippableFactPlatform("windows")]
        public void RunShellCommand_AllowlistedProgram_Runs()
        {
            Assert.True(_cli.RunShellCommand("ver", out CommandResult result, out _, allowedPrograms: new[] { "ver" }));
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Windows", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }

        [SkippableFactPlatform("windows")]
        public void RunShellCommand_AllowlistIsCaseInsensitiveOnNames()
        {
            Assert.True(_cli.RunShellCommand("ver", out CommandResult result, out _, allowedPrograms: new[] { "VER" }));
            Assert.Equal(0, result.ExitCode);
        }

        [Fact]
        public void RunShellCommand_DisallowedProgram_FailsWithoutStartingAnything()
        {
            // Cross-platform: the guard rejects the command before a process is started.
            Assert.False(_cli.RunShellCommand("ver", out CommandResult result, out string message,
                allowedPrograms: new[] { "cacls" }));
            Assert.Null(result);
            Assert.Contains("not in allowedPrograms", message);
        }

        [Fact]
        public void RunShellCommand_SecondSegmentDisallowed_FailsBeforeAnythingRuns()
        {
            Assert.False(_cli.RunShellCommand("ver & format q:", out CommandResult result, out string message,
                allowedPrograms: new[] { "ver" }));
            Assert.Null(result);
            Assert.Contains("format", message);
        }

        [Fact]
        public void RunShellCommand_EmptySegmentBetweenOperators_Rejected()
        {
            Assert.False(_cli.RunShellCommand("ver &&", out _, out string message,
                allowedPrograms: new[] { "ver" }));
            Assert.Contains("empty", message);
        }

        [SkippableFactPlatform("windows")]
        public void RunShellCommand_EnvironmentVariables_ArePassedThrough()
        {
            Assert.True(_cli.RunShellCommand(
                "echo %CMDUTILS_TEST_ENV%",
                out CommandResult result, out _,
                workingDirectory: Environment.SystemDirectory,
                environmentVariables: new Dictionary<string, string> { ["CMDUTILS_TEST_ENV"] = "grpc-42" }));
            Assert.Contains("grpc-42", result.StandardOutput);
        }

        [SkippableFactPlatform("windows")]
        public void RunShellCommand_Timeout_KillsTreeAndReportsTimedOut()
        {
            Assert.True(_cli.RunShellCommand("ping -n 5 127.0.0.1 > nul", out CommandResult result, out _, timeoutMs: 300));
            Assert.True(result.TimedOut);
        }

        [SkippableFactPlatform("windows")]
        public void Run_ConsoleChildWithRedirection_OutputsToCapturedStreams()
        {
            string cmd = WindowsCmd;
            Assert.True(_cli.Run(cmd, out CommandResult result, out string message, arguments: "/d /s /c \"echo via-run && exit /b 3\"", timeoutMs: 10_000));
            Assert.Null(message);
            Assert.Contains("via-run", result.StandardOutput);
            Assert.Equal(3, result.ExitCode);
        }

        [SkippableFactPlatform("windows")]
        public void StartFireAndForget_ReturnsPid_WithoutWaiting()
        {
            Assert.True(_cli.StartFireAndForget(WindowsCmd, out int processId, out string message, arguments: "/d /c exit"));
            Assert.Null(message);
            Assert.True(processId > 0);
        }
    }

    /// <summary>
    /// Skips a test unless the platform matches: "windows", or (off-Windows) a path that
    /// must exist, so the handful of live-process cases degrade gracefully in CI.
    /// </summary>
    public class SkippableFactPlatformAttribute : FactAttribute
    {
        public SkippableFactPlatformAttribute(string requirement)
        {
            if (requirement == "windows")
            {
                if (!OperatingSystem.IsWindows())
                    Skip = "Windows-only live-process test.";
                return;
            }

            if (OperatingSystem.IsWindows() || !File.Exists(requirement))
                Skip = $"Requires '{requirement}' on disk (non-Windows only).";
        }
    }

    /// <summary>
    /// A minimal <see cref="IDictionary{TKey,TValue}"/> that permits a null key — something
    /// a <see cref="Dictionary{TKey,TValue}"/> forbids but a custom dictionary built from
    /// untrusted data could contain — so the null-key guard in
    /// Run/RunShellCommand/StartFireAndForget can be exercised.
    /// </summary>
    internal sealed class NullKeyDictionary : IDictionary<string, string>
    {
        private readonly List<KeyValuePair<string, string>> _pairs = new List<KeyValuePair<string, string>>();

        public string this[string key]
        {
            get => _pairs.Find(p => p.Key == key).Value;
            set
            {
                int i = _pairs.FindIndex(p => p.Key == key);
                if (i >= 0) _pairs[i] = new KeyValuePair<string, string>(key, value);
                else _pairs.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        public ICollection<string> Keys => _pairs.Select(p => p.Key).ToList();
        public ICollection<string> Values => _pairs.Select(p => p.Value).ToList();
        public int Count => _pairs.Count;
        public bool IsReadOnly => false;

        public void Add(string key, string value) => _pairs.Add(new KeyValuePair<string, string>(key, value));
        public void Add(KeyValuePair<string, string> item) => _pairs.Add(item);
        public void Clear() => _pairs.Clear();
        public bool Contains(KeyValuePair<string, string> item) => _pairs.Contains(item);
        public bool ContainsKey(string key) => _pairs.Exists(p => p.Key == key);
        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) => _pairs.CopyTo(array, arrayIndex);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _pairs.GetEnumerator();
        public bool Remove(string key)
        {
            int i = _pairs.FindIndex(p => p.Key == key);
            if (i < 0) return false;
            _pairs.RemoveAt(i);
            return true;
        }
        public bool Remove(KeyValuePair<string, string> item) => _pairs.Remove(item);
        public bool TryGetValue(string key, out string value)
        {
            int i = _pairs.FindIndex(p => p.Key == key);
            value = i >= 0 ? _pairs[i].Value : null;
            return i >= 0;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}