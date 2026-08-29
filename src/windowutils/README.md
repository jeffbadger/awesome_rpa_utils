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

## Constructors

| Constructor | Description |
|---|---|
| `WindowUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `WindowUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Enumeration & Lookup

| Method | Description |
|---|---|
| `List<IntPtr> GetTopLevelWindows()` | Gets all top-level windows via `EnumWindows`. |
| `IntPtr FindWindowByTitle(string title, bool exactMatch = true)` | Finds a top-level window by title (exact or substring match). Returns `IntPtr.Zero` if none matches. |
| `IntPtr FindWindowByClass(string className)` | Finds the first top-level window of the given window class. |
| `List<IntPtr> FindWindowsByProcessId(int processId)` | Finds all top-level windows owned by the given process ID. |
| `IntPtr GetForegroundWindow()` | Gets the handle of the current foreground (active) window. |

### State & Geometry

| Method | Description |
|---|---|
| `bool GetWindowBounds(IntPtr hWnd, out Rectangle bounds, out string message)` | Gets the screen-space bounding rectangle of a window. Returns True on success; never throws. |
| `bool SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height, out string message)` | Moves and/or resizes a window to the given rectangle. Returns True on success; never throws. |
| `bool MoveWindow(IntPtr hWnd, int left, int top, out string message)` | Moves a window without changing its size. Returns True on success; never throws. |
| `bool ResizeWindow(IntPtr hWnd, int width, int height, out string message)` | Resizes a window without changing its position. Returns True on success; never throws. |
| `string GetWindowTitle(IntPtr hWnd)` | Gets a window's title text. |
| `string GetWindowClassName(IntPtr hWnd)` | Gets a window's window-class name. |
| `int GetWindowProcessId(IntPtr hWnd)` | Gets the process ID that owns a window. |
| `bool IsWindowVisible(IntPtr hWnd)` | Returns `true` if the window is visible. |
| `bool IsWindowResponding(IntPtr hWnd)` | Returns `true` if the window is responding to messages (inverse of `IsHungAppWindow`). |
| `void SetWindowState(IntPtr hWnd, ShowWindowCommand command)` | Applies a show/hide/minimize/maximize/restore state to a window. |
| `bool CloseWindow(IntPtr hWnd, out string message)` | Asks a window to close by posting `WM_CLOSE`. Returns True on success; never throws. |

### Activation & Z-Order

| Method | Description |
|---|---|
| `bool ActivateWindow(IntPtr hWnd, out string message)` | Brings a window to the foreground and gives it input focus. Returns True on success; never throws. |
| `bool SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop, out string message)` | Makes a window always-on-top (or removes that state). Returns True on success; never throws. |
| `bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)` | Polls for a window matching the title (substring, case-insensitive) until it appears or the timeout elapses. |
| `bool WaitForWindowToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until a window handle is no longer valid, or the timeout elapses. |
| `bool WaitForWindowActive(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until the given window becomes the foreground window, or the timeout elapses. |

### Child / Multi-Window Enumeration

| Method | Description |
|---|---|
| `List<IntPtr> GetChildWindows(IntPtr hWndParent)` | Gets all descendant windows/controls of a parent window (recursively, not just immediate children). |
| `IntPtr FindChildWindow(IntPtr hWndParent, string title, string className)` | Finds a child window matching the given title and/or class name. |

## Notes & Caveats

- **`GetWindowBounds`, `SetWindowBounds`, `MoveWindow`, `ResizeWindow`, `CloseWindow`,
  `ActivateWindow`, and `SetAlwaysOnTop` return `bool` with an `out string message`** rather
  than throwing — a Win32 call failing (invalid handle, `MoveWindow`/`SetWindowPos`/
  `PostMessage`/`SetForegroundWindow` refused) and bad arguments (negative width/height) are
  both reported this way, with `message` set to a human-readable reason whenever the method
  returns `false`. Every other method here (`GetTopLevelWindows`, `FindWindowByTitle`,
  `FindWindowByClass`, `FindWindowsByProcessId`, `GetForegroundWindow`, `GetWindowTitle`,
  `GetWindowClassName`, `GetWindowProcessId`, `IsWindowVisible`, `IsWindowResponding`,
  `SetWindowState`, `WaitForWindow`, `WaitForWindowToClose`, `WaitForWindowActive`,
  `GetChildWindows`, `FindChildWindow`) was never able to fail and is unchanged.
- **Window handles (`IntPtr`) become invalid once a window closes.** No method here holds a
  handle open; always re-find a window (or use `WaitForWindow`) rather than caching a handle
  across a long-running automation step.
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
