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

| Method | Description |
|---|---|
| `CommandResult Run(string fileName, string arguments = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null)` | Runs an executable directly (no shell), waits for it to exit, and captures its exit code, stdout, and stderr. |
| `CommandResult RunShellCommand(string command, string workingDirectory = null, int timeoutMs = -1)` | Runs a command through `cmd.exe /c`, waits for it to exit, and captures its exit code, stdout, and stderr. Use for pipes, redirection, shell built-ins, or `.bat`/`.cmd` files. |

### Elevated

| Method | Description |
|---|---|
| `int RunElevated(string fileName, out bool timedOut, string arguments = null, string workingDirectory = null, int timeoutMs = -1)` | Runs an executable elevated (UAC prompt) and waits for it to exit. Returns only the exit code — output cannot be captured for an elevated process. Check `timedOut`, not the exit code, to detect a timeout. |

### Fire and Forget

| Method | Description |
|---|---|
| `int StartFireAndForget(string fileName, string arguments = null, string workingDirectory = null)` | Starts a process without redirecting output or waiting for it to exit, and returns its process ID immediately. |

## Notes & Caveats

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
- **A missing/not-found executable throws `Win32Exception`**, not a sentinel
  return value — unlike this repo's window/dialog-finding components, "the
  command doesn't exist" is treated as an error here, not a normal outcome.
