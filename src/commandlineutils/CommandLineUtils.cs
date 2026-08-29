using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
        /// <returns><c>true</c> if the process ran (regardless of its exit code or whether it timed out); <c>false</c> if it could not be run at all. Never throws.</returns>
        [Category("CommandLine - Run")]
        [Description("Runs an executable directly (no shell), waits for it to exit, and captures its exit code, stdout, and stderr. Returns True on success; never throws.")]
        public bool Run(string fileName, out CommandResult result, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null)
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
                    psi.Environment[pair.Key] = pair.Value;
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

        /// <summary>
        /// Runs <paramref name="command"/> through <c>cmd.exe /c</c>, waits for it to exit,
        /// and captures its exit code, stdout, and stderr. Use this for pipes, redirection,
        /// shell built-ins, or <c>.bat</c>/<c>.cmd</c> files that <see cref="Run"/> can't
        /// execute directly.
        /// </summary>
        /// <param name="command">The shell command line to run.</param>
        /// <param name="result">The result of running the process (exit code, captured output, timeout flag), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the command could not be run.</param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <returns><c>true</c> if the command ran (regardless of its exit code or whether it timed out); <c>false</c> if it could not be run at all. Never throws.</returns>
        [Category("CommandLine - Run")]
        [Description("Runs a command through cmd.exe /c, waits for it to exit, and captures its exit code, stdout, and stderr. Returns True on success; never throws.")]
        public bool RunShellCommand(string command, out CommandResult result, out string message, string workingDirectory = null, int timeoutMs = -1)
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

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + command + "\"",
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

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
        [Category("CommandLine - Elevated")]
        [Description("Runs an executable elevated (UAC prompt) and waits for it to exit. Returns True on success; never throws. Output cannot be captured for an elevated process.")]
        public bool RunElevated(string fileName, out int exitCode, out bool timedOut, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1)
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
        /// <returns><c>true</c> on success; <c>false</c> if the process could not be started. Never throws.</returns>
        [Category("CommandLine - Fire and Forget")]
        [Description("Starts a process without redirecting output or waiting for it to exit, and returns its process ID immediately. Returns True on success; never throws.")]
        public bool StartFireAndForget(string fileName, out int processId, out string message, string arguments = null, string workingDirectory = null)
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
                UseShellExecute = false
            };

            try
            {
                using (Process process = Process.Start(psi))
                {
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

        #endregion

        #region Internal Helpers

        /// <summary>
        /// Starts <paramref name="psi"/>, asynchronously drains stdout/stderr (avoiding the
        /// classic pipe-buffer deadlock from synchronous reads), waits up to
        /// <paramref name="timeoutMs"/>, and kills the whole process tree if it's exceeded.
        /// </summary>
        /// <param name="psi">The fully-configured process to start.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <returns>The result of running the process, including exit code, captured output, and whether it timed out.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeoutMs"/> is less than <c>-1</c>.</exception>
        private static CommandResult RunAndCapture(ProcessStartInfo psi, int timeoutMs)
        {
            if (timeoutMs < -1)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "timeoutMs must be -1 (infinite) or non-negative.");

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

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
                            TimedOut = false
                        };
                    }

                    return new CommandResult
                    {
                        ExitCode = 0,
                        StandardOutput = stdout.ToString(),
                        StandardError = stderr.ToString(),
                        TimedOut = true
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
                    TimedOut = false
                };
            }
        }

        #endregion
    }
}
