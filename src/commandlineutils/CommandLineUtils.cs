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
        /// <param name="environmentVariables">Environment variables to add/override for the child process, or <c>null</c> for none.</param>
        /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeoutMs"/> is less than -1.</exception>
        /// <exception cref="Win32Exception">The executable could not be found or started.</exception>
        [Category("CommandLine - Run")]
        [Description("Runs an executable directly (no shell), waits for it to exit, and captures its exit code, stdout, and stderr.")]
        public CommandResult Run(string fileName, string arguments = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("A file name is required.", nameof(fileName));

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

            return RunAndCapture(psi, timeoutMs);
        }

        /// <summary>
        /// Runs <paramref name="command"/> through <c>cmd.exe /c</c>, waits for it to exit,
        /// and captures its exit code, stdout, and stderr. Use this for pipes, redirection,
        /// shell built-ins, or <c>.bat</c>/<c>.cmd</c> files that <see cref="Run"/> can't
        /// execute directly.
        /// </summary>
        /// <param name="command">The shell command line to run.</param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <exception cref="ArgumentException"><paramref name="command"/> is null, empty, or whitespace.</exception>
        /// <exception cref="Win32Exception"><c>cmd.exe</c> could not be found or started.</exception>
        [Category("CommandLine - Run")]
        [Description("Runs a command through cmd.exe /c, waits for it to exit, and captures its exit code, stdout, and stderr.")]
        public CommandResult RunShellCommand(string command, string workingDirectory = null, int timeoutMs = -1)
        {
            throw new NotImplementedException();
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
        /// <param name="timedOut">Set to <c>true</c> if the process was killed for exceeding <paramref name="timeoutMs"/>; otherwise <c>false</c>. When <c>true</c>, the returned exit code is meaningless - use this flag, not a sentinel exit-code value, to detect a timeout (a real process can legitimately exit with any code, including <c>-1</c>).</param>
        /// <param name="arguments">Command-line arguments, or <c>null</c> for none.</param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <returns>The process's exit code, or an unspecified value if <paramref name="timedOut"/> is <c>true</c>.</returns>
        /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace.</exception>
        /// <exception cref="Win32Exception">The executable could not be started, or the UAC prompt was cancelled by the user.</exception>
        [Category("CommandLine - Elevated")]
        [Description("Runs an executable elevated (UAC prompt) and waits for it to exit. Returns only the exit code - output cannot be captured for an elevated process.")]
        public int RunElevated(string fileName, out bool timedOut, string arguments = null, string workingDirectory = null, int timeoutMs = -1)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Fire and Forget

        /// <summary>
        /// Starts <paramref name="fileName"/> without redirecting output or waiting for it
        /// to exit, and returns its process ID immediately. Intended for long-running
        /// background processes an automation doesn't need to block on.
        /// </summary>
        /// <param name="fileName">Path to the executable to run.</param>
        /// <param name="arguments">Command-line arguments, or <c>null</c> for none.</param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <returns>The started process's ID.</returns>
        /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace.</exception>
        /// <exception cref="Win32Exception">The executable could not be found or started.</exception>
        [Category("CommandLine - Fire and Forget")]
        [Description("Starts a process without redirecting output or waiting for it to exit, and returns its process ID immediately.")]
        public int StartFireAndForget(string fileName, string arguments = null, string workingDirectory = null)
        {
            throw new NotImplementedException();
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
