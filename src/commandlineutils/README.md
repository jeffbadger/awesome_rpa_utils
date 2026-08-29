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
`StandardError` (string), `TimedOut` (bool), and `OutputTruncated` (bool) —
`TimedOut` is `true` when the process was killed for exceeding the requested
timeout, in which case `StandardOutput`/`StandardError` hold whatever was
captured before the kill. `OutputTruncated` is `true` when captured output had
to be cut off at the capture limit (~8 MB per stream) so a runaway child can't
exhaust memory — the tail is replaced with a truncation notice.

## Constructors

| Constructor | Description |
|---|---|
| `CommandLineUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `CommandLineUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Run

| Method | Signature | Description |
|---|---|---|
| `Run` | `bool Run(string fileName, out CommandResult result, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null, Encoding outputEncoding = null)` | Runs an executable directly (no shell), waits for it to exit, and captures its exit code, stdout, and stderr (capped, see `OutputTruncated`). Returns True if the process ran (regardless of exit code/timeout); never throws. |
| `RunShellCommand` | `bool RunShellCommand(string command, out CommandResult result, out string message, string[] allowedPrograms = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null, Encoding outputEncoding = null)` | Runs a command through `cmd.exe /d /s /c`, waits for it to exit, and captures its exit code, stdout, and stderr. Use for pipes, redirection, shell built-ins, or `.bat`/`.cmd` files. When `allowedPrograms` is supplied, every top-level command segment must start with an allowed program or nothing runs. Returns True if the command ran; never throws. |

### Elevated

| Method | Signature | Description |
|---|---|---|
| `RunElevated` | `bool RunElevated(string fileName, out int exitCode, out bool timedOut, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1)` | Runs an executable elevated (UAC prompt) and waits for it to exit. Output cannot be captured for an elevated process. Check `timedOut`, not `exitCode`, to detect a timeout. Returns True if the process ran; never throws. |

### Fire and Forget

| Method | Signature | Description |
|---|---|---|
| `StartFireAndForget` | `bool StartFireAndForget(string fileName, out int processId, out string message, string arguments = null, string workingDirectory = null, IDictionary<string, string> environmentVariables = null)` | Starts a process without redirecting output or waiting for it to exit, and returns its process ID immediately. Console executables get no console window. Returns True on success; never throws. |

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
  have no such requirement. The `cmd.exe` it uses is always resolved from
  System32 (never via PATH), and it is invoked as `cmd /d /s /c "command"` —
  `/d` skips the registry's `AutoRun` scripts and `/s` makes cmd strip exactly
  the wrapping quotes, so commands execute verbatim even with embedded `"`
  characters or a trailing backslash.
- **`RunShellCommand` executes `command` verbatim — never pass untrusted
  input into it.** Any part of the command string that originates outside the
  robot's own code (file contents, user input, email bodies, web responses)
  can contain arbitrary shell operators, and cmd will happily run all of them.
  Prefer `Run` (no shell) whenever pipes/redirection/built-ins aren't needed,
  validate anything dynamic yourself, and consider the `allowedPrograms`
  guardrail: when supplied, every top-level command segment (`&`/`&&`/`|`/
  `||`/newline separates commands) must start with an allowlisted program or
  the method returns `false` and nothing executes. That guardrail is not a
  sandbox — an allowed program's arguments and child processes are not
  constrained.
- **A bare or relative `fileName` is resolved via PATH and the working
  directory**, both outside this component's control — an attacker able to
  plant files there can get a different executable run in place of the
  intended one, and for `RunElevated` the lookup happens with admin rights.
  Pass absolute paths everywhere; the methods deliberately stay permissive,
  but treating bare names as a hazard to avoid.
- **Captured output is capped** at ~4M characters (~8 MB) per stream so a runaway
  child can't exhaust memory; once the cap is hit the tail is replaced with a
  truncation notice and `CommandResult.OutputTruncated` is `true`. Redirected
  streams are decoded with the system default encoding — pass `outputEncoding:`
  (e.g. `Encoding.UTF8`) when the child writes a known different encoding, or
  non-ASCII output comes back garbled.
- **`StartFireAndForget` starts console executables with `CreateNoWindow`**, so no
  console window flashes on the robot's desktop.
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
