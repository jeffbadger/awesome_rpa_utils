# CommandLineAutomation

A Pega Robot Studio-ready component (`CommandLineUtils`) that runs external
commands/processes and captures their exit code, standard output, and
standard error.

- Target framework: `net10.0-windows`
- Namespace: `CommandLineAutomation`
- Assembly: `CommandLineAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

Command execution only: finding, inspecting, or killing a process this
component didn't start is out of scope, reserved for a possible future
`ProcessUtils` component.

## Types

### `CommandResult`
The result of running a command: `ExitCode` (int), `StandardOutput` (string),
`StandardError` (string), and `TimedOut` (bool) — `TimedOut` is `true` when
the process was killed for exceeding the requested timeout, in which case
`StandardOutput`/`StandardError` hold whatever was captured before the kill.

## Constructors

| Constructor | Description |
|---|---|
| `CommandLineUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `CommandLineUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Run

| Method | Signature | Description |
|---|---|---|
| `Run` | `bool Run(string fileName, out CommandResult result, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null)` | Runs an executable directly (no shell), waits for it to exit, and captures its exit code, stdout, and stderr. Returns True if the process ran (regardless of exit code/timeout); never throws. |
| `RunShellCommand` | `bool RunShellCommand(string command, out CommandResult result, out string message, string workingDirectory = null, int timeoutMs = -1)` | Runs a command through `cmd.exe /c`, waits for it to exit, and captures its exit code, stdout, and stderr. Use for pipes, redirection, shell built-ins, or `.bat`/`.cmd` files. Returns True if the command ran; never throws. |

### Elevated

| Method | Signature | Description |
|---|---|---|
| `RunElevated` | `bool RunElevated(string fileName, out int exitCode, out bool timedOut, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1)` | Runs an executable elevated (UAC prompt) and waits for it to exit. Output cannot be captured for an elevated process. Check `timedOut`, not `exitCode`, to detect a timeout. Returns True if the process ran; never throws. |

### Fire and Forget

| Method | Signature | Description |
|---|---|---|
| `StartFireAndForget` | `bool StartFireAndForget(string fileName, out int processId, out string message, string arguments = null, string workingDirectory = null)` | Starts a process without redirecting output or waiting for it to exit, and returns its process ID immediately. Returns True on success; never throws. |

## Notes & Caveats

- **Every method returns `bool` with an `out string message`** rather than throwing —
  bad arguments, a missing executable, `Process.Start` returning null, and a
  cancelled UAC prompt are all reported this way, with `message` set to a
  human-readable reason whenever the method returns `false`. A `false` return
  means the process could not be run at all; a `true` return with `TimedOut`/
  `timedOut` set means it ran but was killed for exceeding its timeout.
- **`timeoutMs = -1`** means wait indefinitely, for every method that accepts it.
- **On timeout**, `Run`/`RunShellCommand`/`RunElevated` kill the *entire process
  tree* (not just the launched process), so a timed-out batch script doesn't
  leave orphaned child processes behind.
- **`RunElevated` cannot capture output.** Windows does not allow redirecting
  standard output/error for a process launched with `UseShellExecute = true`
  and `Verb = "runas"` — only the exit code is available. If you need both
  elevation and captured output, have the elevated process write to a file
  and read that file afterward.
- **`RunShellCommand` requires `cmd.exe`**, so it only works on Windows;
  `Run` and `StartFireAndForget` launch the target executable directly and
  have no such requirement.
- **`RunShellCommand` does not escape embedded double-quotes** in `command`.
  A command containing a `"` character (e.g. one that already quotes a path
  itself) will be parsed incorrectly by `cmd.exe`. This is a known, accepted
  limitation of an intentionally-unsandboxed "run whatever shell command the
  caller gives you" method, not a bug — avoid embedded `"` in the command
  string.
- **If the caller isn't itself running elevated, `RunElevated` may be unable
  to terminate the elevated child on timeout** (Windows denies a
  lower-integrity-level process the rights to terminate a higher-integrity-level
  one). In that case `timedOut` is still reported `true`, but the elevated
  process may continue running in the background after the method returns.
- **`StartFireAndForget` gives you no way to check on the process afterward**
  (still running? exit code? kill it?) — that's out of scope for this
  component. Use `Run`/`RunShellCommand` if you need to wait for and inspect
  the result.
- **A missing/not-found executable returns `false` with a `message`**, not a
  thrown exception — unlike this repo's window/dialog-finding components,
  "the command doesn't exist" is still treated as an error condition here
  (`false` + reason), not a silent not-found sentinel.
