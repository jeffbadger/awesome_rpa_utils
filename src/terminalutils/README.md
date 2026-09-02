# TerminalAutomation

A Pega Robot Studio-ready component (`TerminalUtils`) for terminal-style
console applications that expose text poorly through normal UI automation.
Like every component in this suite, it honors the never-throws contract:
invalid input and runtime failures return `False` with a descriptive
message instead of throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `TerminalAutomation`
- Assembly: `TerminalAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

**Why this exists alongside `CommandLineUtils`.** `CommandLineUtils`
deliberately hides a launched process's console window and captures its
stdout as a redirected pipe — perfect for a run-to-completion command, but
unable to represent the live, cursor-addressed, self-overwriting screen of
an interactive console/curses-style TUI app (a piped capture would contain
every in-place redraw appended sequentially, not the final rendered
screen). `TerminalUtils` instead attaches to a target process's real Win32
console screen buffer (`AttachConsole`/`GetConsoleScreenBufferInfo`/
`ReadConsoleOutputW`) and reads it the way a human looking at the window
would — with the real advantage that this works even when the console
window is minimized, unfocused, or not currently visible.

**Not full 3270/5250 terminal emulation.** Per this component's own scope,
"rows and fields as JSON" uses a heuristic split (runs of 2+ whitespace
characters as column boundaries) rather than true attribute-based field
parsing — good enough for typical command-line/legacy-host table-style
output, not a terminal-emulation-grade field parser.

**"Preserve ANSI" is reconstruction, not passthrough.** The Win32 console
screen buffer API returns already-interpreted color attributes, never the
original escape bytes — by the time `ReadConsoleOutputW` sees the buffer,
conhost/Windows Terminal has already fully consumed and applied any ANSI
sequences the target app emitted. `CaptureScreenText(processId, preserveAnsi: true, ...)`
re-synthesizes equivalent ANSI SGR color codes from each cell's stored
color attribute; it does not, and cannot, reproduce the target's original
byte-for-byte output. See `AnsiReconstruction` and the Notes & Caveats
below.

**Unlike `FileWatchUtils`/`ArchiveUtils`, this component is Win32
console-API/P/Invoke-heavy** (`AttachConsole`, `ReadConsoleOutputW`,
`WriteConsoleInputW`), so it does not get their "real functional coverage
on any OS" treatment. Its `.Tests` project covers pure logic (ANSI
reconstruction, heuristic field splitting) and argument guards on any OS;
everything that touches a real console needs a genuine Windows session —
see `TESTING.md`'s manual test plan.

## Types

### `TerminalRowInfo`
The JSON shape produced by `ReadScreenRowsJson`: `RowIndex`, `Text`
(right-trimmed of the buffer's trailing padding spaces), `Fields` (the
heuristic whitespace-based column split).

## Constructors

| Constructor | Description |
|---|---|
| `TerminalUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `TerminalUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Attach Lifecycle

| Method | Signature | Description |
|---|---|---|
| `IsConsoleAttachable` | `bool IsConsoleAttachable(int processId, out bool attachable, out string message)` | Checks whether the calling process can attach to the given process's console, via a real attach/detach cycle. |
| `StartConsoleProcess` | `bool StartConsoleProcess(string fileName, string arguments, string workingDirectory, out int processId, out string message)` | Starts a new process with its own real console window (not hidden, not redirected), for later attachment. |

### Read — Screen Buffer

| Method | Signature | Description |
|---|---|---|
| `GetCursorPosition` | `bool GetCursorPosition(int processId, out int row, out int column, out string message)` | Gets the cursor's 0-based row/column within the console's visible viewport. |
| `ReadScreenRowsJson` | `bool ReadScreenRowsJson(int processId, out string json, out string message)` | Reads the visible viewport as a JSON array of `TerminalRowInfo` rows, each with a heuristic field split. |

### Capture — Plain Text

| Method | Signature | Description |
|---|---|---|
| `CaptureScreenText` | `bool CaptureScreenText(int processId, bool preserveAnsi, out string text, out string message)` | Captures the visible viewport as plain text, one line per row, optionally with re-synthesized ANSI color codes. |

### Wait — Prompt / Text Pattern

| Method | Signature | Description |
|---|---|---|
| `WaitForScreenText` | `bool WaitForScreenText(int processId, string pattern, bool useRegex, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls the visible screen text until it contains `pattern` (or matches it as a regular expression), or the timeout elapses. |
| `WaitForScreenTextSimple` | `bool WaitForScreenTextSimple(int processId, string pattern, bool useRegex, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |

### Detect — Screen Change

| Method | Signature | Description |
|---|---|---|
| `WaitForScreenChange` | `bool WaitForScreenChange(int processId, int timeoutMs, int pollIntervalMs, out bool changed, out bool timedOut, out string message)` | Captures a baseline, then polls until a later capture differs, or the timeout elapses. |
| `WaitForScreenChangeSimple` | `bool WaitForScreenChangeSimple(int processId, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `changed`/`timedOut` outputs. |

### Write — Console Input

| Method | Signature | Description |
|---|---|---|
| `WriteText` | `bool WriteText(int processId, string text, out string message)` | Injects text as keystrokes directly into the console's input buffer via `WriteConsoleInputW` — works even if the window is unfocused/minimized. |
| `WriteLine` | `bool WriteLine(int processId, string text, out string message)` | Same, appending a trailing carriage return so a shell/REPL submits the line. |

## Notes & Caveats

- **Never throws.** Invalid input and runtime failures return `False` with
  a descriptive message.
- **Every method is a self-contained attach → act → detach transaction.**
  `AttachConsole`/`FreeConsole` are process-wide Win32 APIs — they change
  which single console the ENTIRE calling process (the Pega Robot Runtime
  host) is attached to, not just the calling thread. This component never
  leaves a persisted "currently attached" console as state across separate
  calls; every method attaches, does its work, and detaches internally
  (`ConsoleAttachScope`), serialized by an internal lock so concurrent
  calls can't corrupt this shared process-wide state.
- **Best-effort restore, not a guarantee.** If the calling process already
  had a console attached before a method call (uncommon for a GUI/Robot
  Runtime host), `ConsoleAttachScope` restores it afterward on a
  best-effort basis by re-attaching to one of the processes that were
  sharing the original console. This re-establishes the same console
  session in the common case but is not guaranteed for every possible
  prior state.
- **Visible viewport, not full scrollback.** Every read/capture method
  operates on the console's currently-visible window rectangle
  (`srWindow`), not the full scrollback buffer — reading the whole
  scrollback is a possible future extension, not supported today.
- **"Preserve ANSI" re-synthesizes color codes; it does not capture raw
  escape bytes.** See the component summary above and `AnsiReconstruction`
  — the original escape sequences the target app emitted no longer exist
  by the time the console screen buffer is read.
- **Encrypted/unusual consoles aside, `GetConsoleWindow()`/attach behavior
  under Windows Terminal's ConPTY layer is an open, unverified risk.** On
  modern Windows, the visible window may belong to `WindowsTerminal.exe`
  hosting a pseudo-console, not to the target process or `conhost.exe`.
  Attaching to and reading the target PID's own console session should
  work at the buffer level regardless of which terminal emulator hosts the
  window, but this has not been empirically verified against Windows
  Terminal specifically.
- **`WriteText`/`WriteLine` inject key events carrying only a Unicode
  character, no virtual key code.** This is expected to work for typical
  console line-input readers (a shell waiting on `ReadConsole`), but has
  not been verified against every possible console application. Injected
  input is only consumed promptly if the target app is actually blocked on
  a console read call — a busy target won't echo instantly, which is
  normal console behavior, not a bug.
- **`WaitForScreenChange` with `timeoutMs = 0` times out immediately** —
  no time has elapsed for the screen to change from the baseline captured
  at call start.
- **A buffer resize between reading buffer info and reading its contents
  is retried once**, then fails gracefully rather than looping or
  crashing.
- **No `Simple`-suffix signature collision on `WriteText`/`WriteLine`** —
  they're different names, not an overload pair, matching this suite's
  general preference for distinct names over ambiguous overloads. The two
  genuine collisions in this component are `WaitForScreenText`/`Simple`
  and `WaitForScreenChange`/`Simple`. See
  `project-docs/pega-usability-reviews/TerminalUtils-pega-usability-review.md`.
- **Guard tests.** `TerminalUtils.Tests` (in this folder) covers argument
  guards and the pure `AnsiReconstruction`/`FieldSplitting` logic — it runs
  on Linux too. Everything that touches a real console needs a genuine
  Windows session; see the manual test plan in `TESTING.md` at the repo
  root.
