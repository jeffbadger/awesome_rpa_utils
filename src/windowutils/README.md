# WindowAutomation

A Pega Robot Studio-ready component (`WindowUtils`) that enumerates, locates,
moves/resizes, activates, and closes windows using the Win32 window APIs
(`EnumWindows`, `GetWindowRect`, `SetWindowPos`, and friends).

- Target framework: `net10.0-windows`
- Namespace: `WindowAutomation`
- Assembly: `WindowAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

Designed to be used alongside [MouseUtils](../mouseutils/MouseUtils.cs)
(`MouseAutomation`), which already exposes a `GetWindowBounds`/`GetWindowAtPoint`
pair scoped to window-relative *clicking*: this component owns general window
management (finding, moving, activating, closing), independent of any input
component. A small amount of overlap (both components can get a window's
bounds) is intentional — each component is fully standalone with no shared
project reference.

## Enums

### `ShowWindowCommand`
The window-state command applied by `SetWindowState`, wrapping the Win32
`SW_*` constants: `Hide`, `Normal`, `Maximized`, `Minimized`, `Restore`.

### `WindowDisplayState`
A window's current display state, as reported by `TryGetWindowState` — the
read-side counterpart to `ShowWindowCommand`: `Normal`, `Minimized`, `Maximized`.
Visibility is a separate question (`IsWindowVisible`).

## Constructors

| Constructor | Description |
|---|---|
| `WindowUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `WindowUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Enumeration & Lookup

| Method | Signature | Description |
|---|---|---|
| `GetTopLevelWindows` | `List<IntPtr> GetTopLevelWindows()` | Gets all top-level windows via `EnumWindows`. Requires a Pega collection proxy and loop to consume — see Notes & Caveats. |
| `FindWindowByTitle` | `IntPtr FindWindowByTitle(string title, bool exactMatch = true)` | Finds a top-level window by title (exact or substring match). Returns `IntPtr.Zero` if none matches. |
| `FindWindowByClass` | `IntPtr FindWindowByClass(string className)` | Finds the first top-level window of the given window class. |
| `FindWindowsByProcessId` | `List<IntPtr> FindWindowsByProcessId(int processId)` | Finds all top-level windows owned by the given process ID. Requires a Pega collection proxy and loop — use `FindFirstWindowByProcessId` for the common single-window case. |
| `FindFirstWindowByProcessId` | `IntPtr FindFirstWindowByProcessId(int processId)` | Finds the first top-level window owned by the given process ID, for the common single-window case. Returns `IntPtr.Zero` if none matches. |
| `GetForegroundWindow` | `IntPtr GetForegroundWindow()` | Gets the handle of the current foreground (active) window. |
| `TryFindWindowByRegex` | `bool TryFindWindowByRegex(string titlePattern, string classNamePattern, out IntPtr hWnd, out string message, bool ignoreCase = true)` | Finds the first top-level window whose title and/or class name matches a regular expression. Returns True if found; `message` is `null` for a clean "no match" and set only when the search couldn't run (no/invalid pattern). Never throws. |
| `EnumerateWindowsJson` | `bool EnumerateWindowsJson(out string json, out string message, bool visibleOnly = true, int processId = 0)` | Lists top-level windows as a JSON array of handle/title/class/process/visibility/enabled/state/bounds objects — a scalar alternative to `GetTopLevelWindows` that needs no collection proxy. Never throws. |

### State & Geometry

| Method | Signature | Description |
|---|---|---|
| `GetWindowBoundsAsRectangle` | `bool GetWindowBoundsAsRectangle(IntPtr hWnd, out Rectangle bounds, out string message)` | Gets the screen-space bounding rectangle of a window. Returns True on success; never throws. |
| `GetWindowBounds` | `bool GetWindowBounds(IntPtr hWnd, out int left, out int top, out int width, out int height, out string message)` | Same, as scalar left/top/width/height outputs for designers without a `Rectangle` proxy. |
| `SetWindowBounds` | `bool SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height, out string message)` | Moves and/or resizes a window to the given rectangle. Returns True on success; never throws. |
| `MoveWindow` | `bool MoveWindow(IntPtr hWnd, int left, int top, out string message)` | Moves a window without changing its size. Returns True on success; never throws. |
| `ResizeWindow` | `bool ResizeWindow(IntPtr hWnd, int width, int height, out string message)` | Resizes a window without changing its position. Returns True on success; never throws. |
| `GetWindowTitle` | `string GetWindowTitle(IntPtr hWnd)` | Gets a window's title text (empty string for both an invalid handle and a legitimately titleless window). |
| `TryGetWindowTitle` | `bool TryGetWindowTitle(IntPtr hWnd, out string title, out string message)` | Same, but returns False for an invalid/nonexistent handle instead of collapsing it to an empty string. |
| `GetWindowClassName` | `string GetWindowClassName(IntPtr hWnd)` | Gets a window's window-class name (empty string for an invalid handle too). |
| `TryGetWindowClassName` | `bool TryGetWindowClassName(IntPtr hWnd, out string className, out string message)` | Same, but returns False for an invalid/nonexistent handle instead of collapsing it to an empty string. |
| `GetWindowProcessId` | `int GetWindowProcessId(IntPtr hWnd)` | Gets the process ID that owns a window (0 for an invalid handle too). |
| `TryGetWindowProcessId` | `bool TryGetWindowProcessId(IntPtr hWnd, out int processId, out string message)` | Same, but returns False for an invalid/nonexistent handle instead of collapsing it to 0. |
| `IsWindowVisible` | `bool IsWindowVisible(IntPtr hWnd)` | Returns `true` if the window is visible. |
| `IsWindowResponding` | `bool IsWindowResponding(IntPtr hWnd)` | Returns `true` if the window is responding to messages (inverse of `IsHungAppWindow`). |
| `IsWindowEnabled` | `bool IsWindowEnabled(IntPtr hWnd)` | Returns `true` if the window accepts input; `false` while a modal dialog blocks it (or for an invalid handle). |
| `TryGetWindowEnabled` | `bool TryGetWindowEnabled(IntPtr hWnd, out bool enabled, out string message)` | Same, but returns False for an invalid/nonexistent handle instead of collapsing it to "disabled". |
| `TryGetWindowState` | `bool TryGetWindowState(IntPtr hWnd, out WindowDisplayState state, out string message)` | Gets whether a window is normal, minimized, or maximized. Returns True on success; never throws. |
| `IsWindowMinimized` | `bool IsWindowMinimized(IntPtr hWnd)` | Returns `true` if the window is minimized (including one minimized from a maximized state). |
| `IsWindowMaximized` | `bool IsWindowMaximized(IntPtr hWnd)` | Returns `true` if the window is maximized and not minimized. |
| `SetWindowState` | `void SetWindowState(IntPtr hWnd, ShowWindowCommand command)` | Applies a show/hide/minimize/maximize/restore state to a window. |
| `CloseWindow` | `bool CloseWindow(IntPtr hWnd, out string message)` | Asks a window to close by posting `WM_CLOSE`. Returns True on success; never throws. |

### Activation & Z-Order

| Method | Signature | Description |
|---|---|---|
| `ActivateWindow` | `bool ActivateWindow(IntPtr hWnd, out string message)` | Brings a window to the foreground and gives it input focus. Returns True on success; never throws. |
| `SetAlwaysOnTop` | `bool SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop, out string message)` | Makes a window always-on-top (or removes that state). Returns True on success; never throws. |
| `WaitForWindowSimple` | `bool WaitForWindowSimple(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)` | Polls for a window matching the title (substring, case-insensitive) until it appears or the timeout elapses. `hWnd == IntPtr.Zero` on `false` covers both timeout and an invalid title, indistinguishably. |
| `WaitForWindow` | `bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd, out string message)` | Same, plus a `message` output (`null` on a genuine timeout; set if `title` was null/empty and polling was refused), so the two `false` cases are distinguishable. |
| `WaitForWindowToClose` | `bool WaitForWindowToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until a window handle is no longer valid, or the timeout elapses. |
| `WaitForWindowActive` | `bool WaitForWindowActive(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until the given window becomes the foreground window, or the timeout elapses. |

### Child / Multi-Window Enumeration

| Method | Signature | Description |
|---|---|---|
| `GetChildWindows` | `List<IntPtr> GetChildWindows(IntPtr hWndParent)` | Gets all descendant windows/controls of a parent window (recursively, not just immediate children). Requires a Pega collection proxy and loop — use `FindChildWindow` for the common single-match case. |
| `FindChildWindow` | `IntPtr FindChildWindow(IntPtr hWndParent, string title, string className, bool exactMatch = true)` | Finds a child window matching the given title and/or class name (exact and case-sensitive by default; `exactMatch: false` switches to case-insensitive substring). |
| `TryFindChildWindowByRegex` | `bool TryFindChildWindowByRegex(IntPtr hWndParent, string titlePattern, string classNamePattern, out IntPtr hWnd, out string message, bool ignoreCase = true)` | Finds the first descendant whose text and/or class name matches a regular expression — the way to reach a WinForms control whose class name carries a per-run suffix. Same `message` contract as `TryFindWindowByRegex`; also rejects an invalid parent handle. Never throws. |

## Notes & Caveats

- **`GetWindowBoundsAsRectangle`, `SetWindowBounds`, `MoveWindow`, `ResizeWindow`,
  `CloseWindow`, `ActivateWindow`, `SetAlwaysOnTop`, and the `WaitForWindow` overload with
  a `message` output all return `bool` with an `out string message`** rather than
  throwing — a Win32 call failing (invalid handle, `MoveWindow`/`SetWindowPos`/
  `PostMessage`/`SetForegroundWindow` refused) and bad arguments (negative width/height, a
  null/empty `WaitForWindow` title) are both reported this way, with `message` set to a
  human-readable reason whenever the method returns `false`. `TryGetWindowTitle`/
  `TryGetWindowClassName`/`TryGetWindowProcessId` also follow this pattern, reporting an
  invalid/nonexistent handle rather than collapsing it to an empty string or `0`, as do
  `TryGetWindowEnabled` and `TryGetWindowState`; `TryFindWindowByRegex`,
  `TryFindChildWindowByRegex`, and `EnumerateWindowsJson` follow it too. Every
  other method here (`GetTopLevelWindows`, `FindWindowByTitle`, `FindWindowByClass`,
  `FindWindowsByProcessId`, `FindFirstWindowByProcessId`, `GetForegroundWindow`,
  `GetWindowTitle`, `GetWindowClassName`, `GetWindowProcessId`, `IsWindowVisible`,
  `IsWindowResponding`, `IsWindowEnabled`, `IsWindowMinimized`, `IsWindowMaximized`,
  `SetWindowState`, `WaitForWindowSimple`,
  `WaitForWindowToClose`, `WaitForWindowActive`, `GetChildWindows`, `FindChildWindow`)
  was never able to fail and is unchanged.
- **Window handles (`IntPtr`) are short-lived and become invalid once a window closes.**
  No method here holds a handle open, and none should be cached across a long-running
  automation step or between separate automation runs — re-find the window immediately
  before use instead (via `FindWindowByTitle`/`FindWindowByClass`/`FindFirstWindowByProcessId`/
  `WaitForWindow`/`WaitForWindowSimple`). The intended shape is producer → immediate consumer within one
  automation: a lookup/wait method produces a handle, and the very next steps (geometry,
  state, activation, wait, child-window methods) consume it, the same way a Pega flow
  wires one step's output port directly into the next step's input port. `WaitForWindowToClose`
  exists specifically to detect when a handle you're still holding has gone stale.
- **`GetTopLevelWindows`, `FindWindowsByProcessId`, and `GetChildWindows` return
  `List<IntPtr>`**, which needs a Pega collection proxy and a loop construct to consume -
  more design-surface work than a scalar result. Prefer the scalar alternatives for the
  common case: `FindWindowByTitle`/`FindWindowByClass`/`FindFirstWindowByProcessId` for a
  single window, `FindChildWindow` for a single child. Reach for the list-returning
  methods only when the automation genuinely needs every match (e.g. closing every window
  a process owns) and is prepared to configure the proxy/loop for it.
- **`SetWindowState`** never throws: `ShowWindow`'s return value reports the window's
  *previous* visibility state, not whether the call succeeded, so there is nothing
  meaningful to check.
- **`ActivateWindow`** returns `false` when `SetForegroundWindow` reports failure, but Windows'
  foreground-lock rules can also cause the call to succeed without actually raising the window
  (a deliberate OS security behavior, not a bug) — pair with `WaitForWindowActive` to confirm
  the window actually became active rather than relying on a `true` return alone.
- **`SetAlwaysOnTop`** is system-wide and session-persistent for that window until changed
  again or the window closes.
- **`CloseWindow`** posts `WM_CLOSE` (a polite request); an application with unsaved changes
  may show a "Save changes?" prompt instead of closing immediately — pair with
  `WaitForWindowToClose` and handle that dialog if it can appear (e.g. via your own dialog-handling logic).
- **`IsWindowEnabled` is how to notice a modal dialog blocking a window.** Windows disables
  an owner window while one of its modal dialogs is open, so a main window that reports
  `false` while its dialog is up silently ignores clicks and keystrokes until the dialog is
  dismissed. It can't say *which* dialog: find it with `WaitForWindow`/`FindWindowByTitle` (or
  a dialog utility). An application can also disable a window for its own reasons (a busy
  state, a wizard step), so `false` means "not accepting input", not proof a dialog is open.
  Use `TryGetWindowEnabled` when a stale handle must not read as "disabled".
- **`TryGetWindowState`/`IsWindowMinimized`/`IsWindowMaximized`** report a window minimized
  from a maximized state as `Minimized` only (never both). Visibility is independent, so a
  hidden window still reports its normal/minimized/maximized state. A minimized window's
  bounds (`GetWindowBounds`, and `EnumerateWindowsJson`'s `Left`/`Top`) are about -32000, so
  check the state before trusting coordinates.
- **`TryFindWindowByRegex`/`TryFindChildWindowByRegex`** match with `Regex.IsMatch` (the
  pattern may match anywhere - anchor with `^`/`$` for a whole-string match), case-
  insensitively by default, and require both patterns to match when both are given (a null
  or empty pattern skips that axis; at least one is required). Class names are the main
  reason to reach for them: WinForms generates ones like
  `WindowsForms10.Window.8.app.0.141b42a_r14_ad1` whose suffix changes from run to run
  (`^WindowsForms10\.Window\.8\.app\.0\.` matches every run). Like `FindWindowByTitle`,
  they also match hidden windows and return the first match in enumeration order. Every
  match is capped at one second, so a pathological pattern (nested quantifiers such as
  `(a+)+`) fails with a message instead of stalling the automation. `message` is `null`
  for a clean "no match" and set only when the search could not run, so a `false` with a
  `null` message means "not there (yet)".
- **`EnumerateWindowsJson`** is the no-proxy way to see what is on screen: one JSON object
  per window with `Handle`, `Title`, `ClassName`, `ProcessId`, `IsVisible`, `IsEnabled`,
  `State` (`"Normal"`/`"Minimized"`/`"Maximized"`), `Left`, `Top`, `Width`, and `Height`,
  topmost first, ready for `JsonUtils` or a log line. It lists only visible windows by
  default (`visibleOnly: false` adds the hundreds of hidden ones) and can be narrowed to one
  process with `processId`. `Handle` is a plain number that fits in 32 bits, so it converts
  back to an `IntPtr` for the other methods. A window that closes mid-enumeration is left out.
