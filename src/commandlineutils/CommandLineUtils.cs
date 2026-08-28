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
        /// <exception cref="Win32Exception">The executable could not be found or started.</exception>
        [Category("CommandLine - Run")]
        [Description("Runs an executable directly (no shell), waits for it to exit, and captures its exit code, stdout, and stderr.")]
        public CommandResult Run(string fileName, string arguments = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null)
        {
            throw new NotImplementedException();
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
        /// <param name="arguments">Command-line arguments, or <c>null</c> for none.</param>
        /// <param name="workingDirectory">Working directory for the process, or <c>null</c> to use the current directory.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds, or <c>-1</c> to wait indefinitely.</param>
        /// <returns>The process's exit code, or <c>-1</c> if it was killed for exceeding <paramref name="timeoutMs"/>.</returns>
        /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace.</exception>
        /// <exception cref="Win32Exception">The executable could not be started, or the UAC prompt was cancelled by the user.</exception>
        [Category("CommandLine - Elevated")]
        [Description("Runs an executable elevated (UAC prompt) and waits for it to exit. Returns only the exit code - output cannot be captured for an elevated process.")]
        public int RunElevated(string fileName, string arguments = null, string workingDirectory = null, int timeoutMs = -1)
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
    }
}
