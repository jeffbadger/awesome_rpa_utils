using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CommandLineAutomation
{
    /// <summary>The result of running a command: its exit code, captured output, and whether it timed out.</summary>
    public sealed class CommandResult
    {
        /// <summary>The process's exit code. Meaningless (0) if <see cref="TimedOut"/> is true.</summary>
        public int ExitCode { get; set; }

        /// <summary>Everything the process wrote to standard output.</summary>
        public string StandardOutput { get; set; }

        /// <summary>Everything the process wrote to standard error.</summary>
        public string StandardError { get; set; }

        /// <summary>True if the process was killed after exceeding the requested timeout. When true, <see cref="StandardOutput"/>/<see cref="StandardError"/> hold whatever was captured before the kill.</summary>
        public bool TimedOut { get; set; }

        /// <summary>
        /// True if captured output had to be cut off because it exceeded the capture
        /// limit (~4M characters ≈ 8 MB per stream). The tail of the stream is replaced
        /// with a truncation notice; <see cref="StandardOutput"/>/<see cref="StandardError"/>
        /// are otherwise unmodified. A runaway process can no longer grow this result
        /// without bound.
        /// </summary>
        public bool OutputTruncated { get; set; }
    }

    /// <summary>
    /// Pega Robot Studio-ready component that runs external commands/processes and
    /// captures their exit code, standard output, and standard error.
    /// </summary>
    [Description("Runs external commands/processes and captures their exit code and output. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class CommandLineUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public CommandLineUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public CommandLineUtils(IContainer container)
        {
            container?.Add(this);
        }

        #region Run

        /// <summary>
        /// Runs <paramref name="fileName"/> directly (no shell) with the given arguments,
        /// waits for it to exit, and captures its exit code, stdout, and stderr.
        /// </summary>
        /// <param name="fileName">Path to the executable to run.</param>
        /// <param name="arguments">Command-line arguments, or <c>null</c> for none.</param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <param name="result">The result of running the process (exit code, captured output, timeout flag), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the process could not be run.</param>
        /// <param name="environmentVariables">Environment variables to add/override for the child process, or <c>null</c> for none.</param>
        /// <param name="outputEncoding">
        /// The encoding to decode the child's stdout/stderr with, or <c>null</c> for the
        /// system default. Set this when the child is known to write a different encoding
        /// (e.g. <c>Encoding.UTF8</c>), otherwise non-ASCII output can come back garbled.
        /// </param>
        /// <returns><c>true</c> if the process ran (regardless of its exit code or whether it timed out); <c>false</c> if it could not be run at all. Never throws.</returns>
        /// <remarks>
        /// <paramref name="fileName"/> is resolved by the process launcher when it is a
        /// bare or relative name — via PATH and the working directory, either of which a
        /// local attacker able to plant files there could subvert (see the README's Notes
        /// &amp; Caveats). Prefer an absolute path for anything privileged, and prefer this
        /// no-shell method over <see cref="RunShellCommand"/> whenever shell features
        /// (pipes/redirection/built-ins) are not needed, since <see cref="RunShellCommand"/>
        /// executes its input verbatim.
        /// </remarks>
        [Category("CommandLine - Run")]
        [Description("Runs an executable directly (no shell), waits for it to exit, and captures its exit code, stdout, and stderr. Returns True on success; never throws.")]
        public bool Run(string fileName, out CommandResult result, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null, Encoding outputEncoding = null)
        {
            result = default;
            message = default;
            try
            {
                result = null;

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    message = "A file name is required.";
                    return false;
                }
                if (timeoutMs < -1)
                {
                    message = "timeoutMs must be -1 (infinite) or non-negative.";
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = workingDirectory ?? string.Empty,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (environmentVariables != null)
                {
                    foreach (var pair in environmentVariables)
                    {
                        if (pair.Key == null)
                        {
                            message = "environmentVariables contains a null key.";
                            return false;
                        }
                        psi.Environment[pair.Key] = pair.Value;
                    }
                }
                if (outputEncoding != null)
                {
                    psi.StandardOutputEncoding = outputEncoding;
                    psi.StandardErrorEncoding = outputEncoding;
                }

                try
                {
                    result = RunAndCapture(psi, timeoutMs);
                    message = null;
                    return true;
                }
                catch (Win32Exception ex)
                {
                    message = ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Run", ex);
                return false;
            }
        }

        /// <summary>
        /// Runs <paramref name="command"/> through <c>cmd.exe /d /s /c "command"</c>, waits
        /// for it to exit, and captures its exit code, stdout, and stderr. Use this for
        /// pipes, redirection, shell built-ins, or <c>.bat</c>/<c>.cmd</c> files that
        /// <see cref="Run"/> can't execute directly. The <c>/d</c> flag keeps the
        /// registry's <c>AutoRun</c> scripts from running first, and <c>/s</c> makes cmd
        /// strip exactly the wrapping quotes, so the command executes verbatim regardless
        /// of embedded quotes or a trailing backslash.
        /// </summary>
        /// <param name="command">The shell command line to run.</param>
        /// <param name="result">The result of running the process (exit code, captured output, timeout flag), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the command could not be run.</param>
        /// <param name="allowedPrograms">
        /// Optional guardrail against unintended programs launching — <c>null</c> (the
        /// default) runs <paramref name="command"/> unvalidated. When a non-empty array is
        /// supplied, the first token of every top-level command segment (cmd runs the parts
        /// between <c>&amp;</c>/<c>&amp;&amp;</c>/<c>|</c>/<c>||</c>/<c>\n</c> as separate commands) must match one
        /// of these entries — case-insensitively, as either a bare name like
        /// <c>robocopy</c> or a full path like <c>C:\Windows\System32\robocopy.exe</c> — or
        /// the method returns <c>false</c> with a <paramref name="message"/> and nothing is
        /// executed. This is a guardrail against mistakes and typos, <b>not a security
        /// sandbox</b>: an allowed program's arguments and child processes are not
        /// constrained (an allowed <c>robocopy</c> run with destructive arguments is on
        /// you), and built-ins such as <c>del</c> must themselves be listed to be allowed.
        /// </param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <param name="environmentVariables">Environment variables to add/override for the child process, or <c>null</c> for none.</param>
        /// <param name="outputEncoding">
        /// The encoding to decode the child's stdout/stderr with, or <c>null</c> for the
        /// system default. Set this when the child is known to write a different encoding
        /// (e.g. <c>Encoding.UTF8</c>), otherwise non-ASCII output can come back garbled.
        /// </param>
        /// <returns><c>true</c> if the command ran (regardless of its exit code or whether it timed out); <c>false</c> if it could not be run at all (bad arguments, a disallowed program, or cmd.exe could not be started). Never throws.</returns>
        [Category("CommandLine - Run")]
        [Description("Runs a command through cmd.exe /d /s /c, waits for it to exit, and captures its exit code, stdout, and stderr. Optionally validates that every command segment launches an allowed program before running anything. Returns True on success; never throws.")]
        public bool RunShellCommand(string command, out CommandResult result, out string message, string[] allowedPrograms = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null, Encoding outputEncoding = null)
        {
            result = default;
            message = default;
            try
            {
                result = null;

                if (string.IsNullOrWhiteSpace(command))
                {
                    message = "A command is required.";
                    return false;
                }
                if (timeoutMs < -1)
                {
                    message = "timeoutMs must be -1 (infinite) or non-negative.";
                    return false;
                }
                if (allowedPrograms != null)
                {
                    // Validate before starting anything: the guard only helps if segment 2 of
                    // a compound command is checked too, not just the first thing cmd would run.
                    if (!IsCommandAllowed(command, allowedPrograms, out string segment, out string program))
                    {
                        message = program == null
                            ? $"Command segment \"{segment.Trim()}\" is empty between shell operators."
                            : $"Command segment \"{segment.Trim()}\" launches '{program}', which is not in allowedPrograms.";
                        return false;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    // Pinned to System32 so a planted cmd.exe earlier in PATH can't be resolved instead.
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    // /d skips AutoRun registry scripts; /s makes the quote-stripping rule
                    // deterministic (first and last quote only) no matter what command contains.
                    Arguments = "/d /s /c \"" + command + "\"",
                    WorkingDirectory = workingDirectory ?? string.Empty,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (environmentVariables != null)
                {
                    foreach (var pair in environmentVariables)
                    {
                        if (pair.Key == null)
                        {
                            message = "environmentVariables contains a null key.";
                            return false;
                        }
                        psi.Environment[pair.Key] = pair.Value;
                    }
                }
                if (outputEncoding != null)
                {
                    psi.StandardOutputEncoding = outputEncoding;
                    psi.StandardErrorEncoding = outputEncoding;
                }

                try
                {
                    result = RunAndCapture(psi, timeoutMs);
                    message = null;
                    return true;
                }
                catch (Win32Exception ex)
                {
                    message = ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("RunShellCommand", ex);
                return false;
            }
        }

        #endregion

        #region Elevated

        /// <summary>
        /// Runs <paramref name="fileName"/> elevated (triggers the UAC consent prompt) and
        /// waits for it to exit. Windows does not allow redirecting standard output/error
        /// for an elevated process, so no output is captured - only the exit code is
        /// available.
        /// </summary>
        /// <param name="fileName">Path to the executable to run.</param>
        /// <param name="exitCode">The process's exit code, or an unspecified value if <paramref name="timedOut"/> is <c>true</c>. Meaningless if this method returns <c>false</c>.</param>
        /// <param name="timedOut">Set to <c>true</c> if the process was killed for exceeding <paramref name="timeoutMs"/>, or if it could not be confirmed to have exited within a bounded grace period after an attempted kill; otherwise <c>false</c>. When <c>true</c>, <paramref name="exitCode"/> is meaningless - use this flag, not a sentinel exit-code value, to detect a timeout (a real process can legitimately exit with any code, including <c>-1</c>). If the calling process is not itself running elevated, it may be unable to terminate the elevated child on timeout (Windows denies a lower-integrity-level process the rights to terminate a higher-integrity-level one) - in that case the elevated process may continue running in the background after this method returns with <paramref name="timedOut"/> <c>true</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the process could not be run.</param>
        /// <param name="arguments">Command-line arguments, or <c>null</c> for none.</param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <returns><c>true</c> if the process ran (regardless of its exit code or whether it timed out); <c>false</c> if it could not be run at all (bad arguments, executable not found, <c>Process.Start</c> returned null, or the UAC prompt was cancelled). Never throws.</returns>
        /// <remarks>
        /// Only an <b>absolute</b> <paramref name="fileName"/> is accepted: a bare or
        /// relative name would be resolved through PATH and the working directory — the
        /// resolution an attacker able to plant files there could subvert, and this
        /// method's resolution happens with <i>admin</i> rights — so such a name is
        /// rejected up front rather than resolved with elevation.
        /// </remarks>
        [Category("CommandLine - Elevated")]
        [Description("Runs an executable elevated (UAC prompt) and waits for it to exit. Returns True on success; never throws. Output cannot be captured for an elevated process.")]
        public bool RunElevated(string fileName, out int exitCode, out bool timedOut, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1)
        {
            exitCode = default;
            timedOut = default;
            message = default;
            try
            {
                exitCode = 0;
                timedOut = false;

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    message = "A file name is required.";
                    return false;
                }
                if (timeoutMs < -1)
                {
                    message = "timeoutMs must be -1 (infinite) or non-negative.";
                    return false;
                }
                if (!Path.IsPathRooted(fileName))
                {
                    message = "fileName must be an absolute path (a relative name would be resolved with admin rights).";
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = workingDirectory ?? string.Empty,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process process;
                try
                {
                    process = Process.Start(psi);
                }
                catch (Win32Exception ex)
                {
                    message = ex.Message;
                    return false;
                }

                using (process)
                {
                    if (process == null)
                    {
                        message = $"Process.Start returned null for '{fileName}'.";
                        return false;
                    }

                    bool exited = process.WaitForExit(timeoutMs);
                    if (exited)
                    {
                        exitCode = process.ExitCode;
                        message = null;
                        return true;
                    }

                    // Attempt to kill it. This can fail either because it already exited
                    // (InvalidOperationException) or, for an elevated child launched from a
                    // non-elevated caller, because Windows' integrity-level rules deny
                    // PROCESS_TERMINATE access even to the process that started it
                    // (Win32Exception, "Access is denied"). Either way, we did NOT actually
                    // terminate the process ourselves in these two cases.
                    bool killedByUs = true;
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        killedByUs = false;
                    }
                    catch (Win32Exception)
                    {
                        killedByUs = false;
                    }

                    // Bounded: never let this method hang past its stated timeout contract,
                    // whether the kill succeeded, failed, or the process is just slow to die.
                    process.WaitForExit(5000);

                    if (!killedByUs && process.HasExited)
                    {
                        // We did not terminate it ourselves, and it turned out to have
                        // exited anyway (already gone before the kill, or finished on its
                        // own within the grace window despite the kill being denied) - not
                        // a timeout from the caller's perspective.
                        exitCode = process.ExitCode;
                        message = null;
                        return true;
                    }

                    // Either we successfully killed it (a real timeout), or we couldn't
                    // kill it AND it's still running (also a real timeout, from the
                    // caller's perspective, even though we couldn't confirm termination).
                    timedOut = true;
                    exitCode = process.HasExited ? process.ExitCode : -1;
                    message = null;
                    return true;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("RunElevated", ex);
                return false;
            }
        }

        #endregion

        #region Fire and Forget

        /// <summary>
        /// Starts <paramref name="fileName"/> without redirecting output or waiting for it
        /// to exit, and returns its process ID immediately. Intended for long-running
        /// background processes an automation doesn't need to block on.
        /// </summary>
        /// <param name="fileName">Path to the executable to run.</param>
        /// <param name="processId">The started process's ID, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the process could not be started.</param>
        /// <param name="arguments">Command-line arguments, or <c>null</c> for none.</param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <param name="environmentVariables">Environment variables to add/override for the child process, or <c>null</c> for none.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the process could not be started. Never throws.</returns>
        /// <remarks>
        /// The child is started with <c>CreateNoWindow</c>, so console executables appear
        /// in Task Manager but never flash a console window on the robot's desktop. As
        /// with <see cref="Run"/>/<see cref="RunElevated"/>, prefer an absolute
        /// <paramref name="fileName"/> — a bare name is resolved via PATH, outside this
        /// component's control (see the README's Notes &amp; Caveats).
        /// </remarks>
        [Category("CommandLine - Fire and Forget")]
        [Description("Starts a process without redirecting output or waiting for it to exit, and returns its process ID immediately. No console window is created for console executables. Returns True on success; never throws.")]
        public bool StartFireAndForget(string fileName, out int processId, out string message, string arguments = null, string workingDirectory = null, IDictionary<string, string> environmentVariables = null)
        {
            processId = default;
            message = default;
            try
            {
                processId = 0;

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    message = "A file name is required.";
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = workingDirectory ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (environmentVariables != null)
                {
                    foreach (var pair in environmentVariables)
                    {
                        if (pair.Key == null)
                        {
                            message = "environmentVariables contains a null key.";
                            return false;
                        }
                        psi.Environment[pair.Key] = pair.Value;
                    }
                }

                try
                {
                    using (Process process = Process.Start(psi))
                    {
                        if (process == null)
                        {
                            // Defensive: Process.Start normally throws instead of returning
                            // null, but the null case must not escape as an NRE either way.
                            message = $"Process.Start returned null for '{fileName}'.";
                            return false;
                        }

                        processId = process.Id;
                        message = null;
                        return true;
                    }
                }
                catch (Win32Exception ex)
                {
                    message = ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("StartFireAndForget", ex);
                return false;
            }
        }

        #endregion

        #region Internal Helpers

        // Per-stream capture limit (~4M chars ≈ 8 MB). internal (not const) only so the
        // unit tests can shrink it; callers are expected to leave it at the default.
        internal static int MaxCapturedOutputChars = 4_000_000;

        /// <summary>
        /// Appends one captured line to <paramref name="sb"/>, enforcing the capture limit:
        /// once the stream passes <see cref="MaxCapturedOutputChars"/> further lines are
        /// dropped and <paramref name="truncated"/> stays true. A single line larger than
        /// the cap is itself truncated, so a runaway child can grow memory only to the cap,
        /// not without bound.
        /// </summary>
        internal static void AppendCapped(StringBuilder sb, string data, ref bool truncated)
        {
            if (truncated || sb.Length >= MaxCapturedOutputChars)
            {
                truncated = true;
                return;
            }

            // A single line can be arbitrarily large (a child writing one giant
            // newline-free blob); cap the line itself so the buffer can't grow
            // past the limit by more than the truncation notice.
            if (data.Length > MaxCapturedOutputChars)
            {
                sb.AppendLine(data.Substring(0, MaxCapturedOutputChars));
                sb.AppendLine("... (line truncated)");
                truncated = true;
                return;
            }

            sb.AppendLine(data);
            if (sb.Length >= MaxCapturedOutputChars)
            {
                sb.AppendLine("... (further output truncated)");
                truncated = true;
            }
        }

        /// <summary>
        /// Starts <paramref name="psi"/>, asynchronously drains stdout/stderr (avoiding the
        /// classic pipe-buffer deadlock from synchronous reads), waits up to
        /// <paramref name="timeoutMs"/>, and kills the whole process tree if it's exceeded.
        /// Capture of each stream is capped (see <see cref="MaxCapturedOutputChars"/>).
        /// Callers validate <paramref name="timeoutMs"/> before reaching here.
        /// </summary>
        /// <param name="psi">The fully-configured process to start.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <returns>The result of running the process, including exit code, captured output, and whether it timed out.</returns>
        private static CommandResult RunAndCapture(ProcessStartInfo psi, int timeoutMs)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            bool stdoutTruncated = false;
            bool stderrTruncated = false;

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) AppendCapped(stdout, e.Data, ref stdoutTruncated); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendCapped(stderr, e.Data, ref stderrTruncated); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit(timeoutMs);
                if (!exited)
                {
                    bool killedSuccessfully = true;
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // The process (and its tree) finished on its own between
                        // WaitForExit(timeoutMs) returning false and this Kill() call.
                        killedSuccessfully = false;
                    }
                    catch (Win32Exception)
                    {
                        // Kill can fail for reasons other than "already exited" - access
                        // denied, a protected process, AV/EDR hooks, or a handle-recycling
                        // race - even for a non-elevated child (rarer than for the elevated
                        // case RunElevated guards against, but still possible).
                        killedSuccessfully = false;
                    }

                    // Bounded: a stray descendant that outlived the tree-kill snapshot
                    // must not be able to make this method hang past its stated timeout.
                    bool finishedAfterKill = process.WaitForExit(5000);

                    if (!killedSuccessfully && finishedAfterKill)
                    {
                        // It genuinely finished on its own in time after all.
                        return new CommandResult
                        {
                            ExitCode = process.ExitCode,
                            StandardOutput = stdout.ToString(),
                            StandardError = stderr.ToString(),
                            TimedOut = false,
                            OutputTruncated = stdoutTruncated || stderrTruncated
                        };
                    }

                    return new CommandResult
                    {
                        ExitCode = 0,
                        StandardOutput = stdout.ToString(),
                        StandardError = stderr.ToString(),
                        TimedOut = true,
                        OutputTruncated = stdoutTruncated || stderrTruncated
                    };
                }

                // Ensure the async OutputDataReceived/ErrorDataReceived events have
                // finished firing before reading the buffers back out.
                process.WaitForExit();

                return new CommandResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = stdout.ToString(),
                    StandardError = stderr.ToString(),
                    TimedOut = false,
                    OutputTruncated = stdoutTruncated || stderrTruncated
                };
            }
        }

        #region Command Allowlist Validation

        /// <summary>
        /// Checks every top-level segment of <paramref name="command"/> against
        /// <paramref name="allowedPrograms"/>: each segment's first token must match an
        /// allowlist entry (see <see cref="NormalizeProgramName"/>). Reports the first
        /// failing segment and the program token it starts with (null when the segment
        /// is empty). Deliberately best-effort — see the runas remarks on
        /// <see cref="RunShellCommand"/>'s <c>allowedPrograms</c> parameter.
        /// </summary>
        private static bool IsCommandAllowed(string command, string[] allowedPrograms, out string segment, out string program)
        {
            var allowed = new HashSet<string>(
                allowedPrograms
                    .Select(NormalizeProgramName)
                    .Where(name => name != null),
                StringComparer.OrdinalIgnoreCase);

            foreach (string part in SplitShellCommandSegments(command))
            {
                string firstToken = ExtractSegmentProgram(part);
                if (firstToken == null || !allowed.Contains(NormalizeProgramName(firstToken)))
                {
                    segment = part;
                    program = firstToken;
                    return false;
                }
            }

            segment = null;
            program = null;
            return true;
        }

        /// <summary>
        /// Splits a shell command line into its top-level segments — the parts cmd.exe
        /// would treat as separate commands, i.e. the text between unquoted, unescaped
        /// <c>&amp;</c>/<c>&amp;&amp;</c>/<c>|</c>/<c>||</c>/newline separators. A caret (<c>^</c>) outside
        /// quotes escapes the next character; characters inside double quotes are literal.
        /// Parenthesized blocks are not specially parsed (their first token is what it is).
        /// </summary>
        internal static List<string> SplitShellCommandSegments(string command)
        {
            var segments = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < command.Length; i++)
            {
                char c = command[i];
                if (!inQuotes && c == '^' && i + 1 < command.Length)
                {
                    current.Append(command[++i]);
                    continue;
                }
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    current.Append(c);
                    continue;
                }
                if (!inQuotes && (c == '&' || c == '|' || c == '\n'))
                {
                    if (c != '\n' && i + 1 < command.Length && command[i + 1] == c)
                        i++; // consume both characters of && / ||
                    segments.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }

            segments.Add(current.ToString());
            return segments;
        }

        /// <summary>
        /// Extracts the program token a command segment starts with: the first quoted
        /// string (a quoted path), otherwise everything up to the first space or tab.
        /// Returns null for an empty/whitespace-only segment.
        /// </summary>
        internal static string ExtractSegmentProgram(string segment)
        {
            if (segment == null)
                return null;

            segment = segment.TrimStart();
            if (segment.Length == 0)
                return null;

            if (segment[0] == '"')
            {
                int closing = segment.IndexOf('"', 1);
                string quoted = closing > 0 ? segment.Substring(1, closing - 1) : segment.Substring(1);
                return quoted.Length > 0 ? quoted : null;
            }

            for (int i = 0; i < segment.Length; i++)
            {
                if (segment[i] == ' ' || segment[i] == '\t')
                    return segment.Substring(0, i);
            }
            return segment;
        }

        /// <summary>
        /// Normalizes a program name or path for allowlist comparison: takes the file-name
        /// part of a path, strips the standard executable extension (<c>.exe</c>,
        /// <c>.bat</c>, <c>.cmd</c>, <c>.com</c>), and preserves case (matching is done
        /// case-insensitively at the comparison site). Returns null for null/whitespace.
        /// </summary>
        internal static string NormalizeProgramName(string program)
        {
            if (string.IsNullOrWhiteSpace(program))
                return null;

            program = program.Trim();
            int lastSeparator = program.LastIndexOfAny(new[] { '/', '\\' });
            if (lastSeparator >= 0)
                program = program.Substring(lastSeparator + 1);

            foreach (string extension in new[] { ".exe", ".bat", ".cmd", ".com" })
            {
                if (program.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    program = program.Substring(0, program.Length - extension.Length);
                    break;
                }
            }
            return program;
        }

        #endregion

        #endregion
    }
}
