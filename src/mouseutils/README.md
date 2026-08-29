# MouseAutomation

A Pega Robot Studio-ready component (`MouseUtils`) that moves, clicks, drags, and
scrolls the mouse using the Windows `SendInput` / `SetCursorPos` APIs, and that can
change the cursor's appearance, visibility, and confinement rectangle.

- Target framework: `net10.0-windows`
- Namespace: `MouseAutomation`
- Assembly: `MouseAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

All coordinates are absolute screen pixels and work across multi-monitor setups
(including negative coordinates on monitors left of the primary display). Input
injection requires an interactive, unlocked desktop; it is blocked on the lock
screen / secure desktop and when the target application runs at a higher
integrity level (UIPI).

## Enums

### `MouseButton`
Which mouse button an action applies to: `Left`, `Right`, `Middle`, `XButton1`, `XButton2`.

### `SystemCursorType`
A standard Windows system cursor slot (used with `SetCursor` / `ReplaceSystemCursor`):
`Arrow`, `IBeam`, `Wait`, `Crosshair`, `UpArrow`, `SizeNorthWestSouthEast`,
`SizeNorthEastSouthWest`, `SizeWestEast`, `SizeNorthSouth`, `SizeAll`, `No`, `Hand`,
`AppStarting`.

### `ModifierKeys` (Flags)
Modifier keys combinable in `ClickWithModifiers`: `None`, `Control`, `Shift`, `Alt`.

## Constructors

| Constructor | Description |
|---|---|
| `MouseUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `MouseUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Position

| Method | Signature | Description |
|---|---|---|
| `GetX` | `bool GetX(out int x, out string message)` | Gets the current X coordinate of the cursor. |
| `GetY` | `bool GetY(out int y, out string message)` | Gets the current Y coordinate of the cursor. |
| `GetPosition` | `bool GetPosition(out Point position, out string message)` | Gets the current cursor position as a `System.Drawing.Point`. |
| `MoveTo` | `bool MoveTo(int x, int y, out string message)` | Instantly moves the cursor to the given screen coordinates. |
| `MoveBy` | `bool MoveBy(int deltaX, int deltaY, out string message)` | Moves the cursor by the given offsets relative to its current position. |
| `SmoothMoveTo` | `bool SmoothMoveTo(int x, int y, out string message)` | Smoothly moves the cursor to the target position (25 steps, 5 ms/step). |
| `SmoothMoveTo` | `bool SmoothMoveTo(int x, int y, int steps, int delayMilliseconds, out string message)` | Smoothly moves the cursor using a custom step count and per-step delay. |
| `JiggleMouse` | `bool JiggleMouse(out string message, int pixels = 1)` | Nudges the cursor by a tiny amount and back, to reset idle/screensaver timers without disturbing its position. |

### Clicks

| Method | Signature | Description |
|---|---|---|
| `Click` | `bool Click(MouseButton button, out string message)` | Clicks the given button at the current cursor position (~20 ms press/release). |
| `ClickAt` | `bool ClickAt(int x, int y, MouseButton button, out string message)` | Moves the cursor to the coordinates and clicks the given button. |
| `DoubleClick` | `bool DoubleClick(MouseButton button, out string message)` | Double-clicks the given button at the current cursor position. |
| `DoubleClickAt` | `bool DoubleClickAt(int x, int y, MouseButton button, out string message)` | Moves the cursor to the coordinates and double-clicks the given button. |
| `LeftClick` | `bool LeftClick(out string message)` | Left-clicks at the current cursor position. |
| `RightClick` | `bool RightClick(out string message)` | Right-clicks at the current cursor position. |
| `MiddleClick` | `bool MiddleClick(out string message)` | Middle-clicks at the current cursor position. |
| `LeftClickAt` | `bool LeftClickAt(int x, int y, out string message)` | Left-clicks at the given screen coordinates. |
| `RightClickAt` | `bool RightClickAt(int x, int y, out string message)` | Right-clicks at the given screen coordinates. |
| `LeftDoubleClick` | `bool LeftDoubleClick(out string message)` | Double left-clicks at the current cursor position. |
| `RightDoubleClick` | `bool RightDoubleClick(out string message)` | Double right-clicks at the current cursor position. |
| `LeftDoubleClickAt` | `bool LeftDoubleClickAt(int x, int y, out string message)` | Double left-clicks at the given screen coordinates. |
| `MouseDown` | `bool MouseDown(MouseButton button, out string message)` | Presses and holds the given mouse button. Pair with `MouseUp`. |
| `MouseUp` | `bool MouseUp(MouseButton button, out string message)` | Releases the given mouse button. |
| `ClickAndHold` | `bool ClickAndHold(MouseButton button, int holdMilliseconds, out string message)` | Holds the given button down for the specified time, then releases it. |
| `ClickWithModifiers` | `bool ClickWithModifiers(MouseButton button, ModifierKeys modifiers, out string message)` | Clicks a button while holding modifier keys (Control/Shift/Alt, combinable), injected as one atomic batch. |
| `ClickAndRestore` | `bool ClickAndRestore(int x, int y, MouseButton button, out string message)` | Clicks at the given coordinates, then immediately returns the cursor to its original position. |
| `ClickWithRetry` | `bool ClickWithRetry(int x, int y, MouseButton button, int maxAttempts, int retryDelayMilliseconds, out string message)` | Clicks at the given coordinates, retrying on failure up to `maxAttempts` times. |
| `TripleClick` | `bool TripleClick(MouseButton button, out string message)` | Triple-clicks the given button at the current cursor position (select-line/paragraph gesture). |

### Drag & Drop

| Method | Signature | Description |
|---|---|---|
| `DragAndDrop` | `bool DragAndDrop(int startX, int startY, int endX, int endY, out string message)` | Performs a left-button drag from start to end (30 steps, 10 ms/step). |
| `DragAndDrop` | `bool DragAndDrop(int startX, int startY, int endX, int endY, int steps, int stepDelayMilliseconds, out string message)` | Performs a left-button drag with a custom step count and step delay. |
| `RubberBandSelect` | `bool RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers, out string message)` | Performs a left-button rubber-band drag while holding modifier keys (e.g. Ctrl-drag to add to a selection). |
| `RubberBandSelect` | `bool RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers, int steps, int stepDelayMilliseconds, out string message)` | Same, with a custom step count and step delay. |
| `DragAndHold` | `bool DragAndHold(int startX, int startY, int endX, int endY, int holdMilliseconds, out string message)` | Drags from start to end, then holds the button down at the destination before releasing (for hover-to-expand drop targets). |

### Wheel / Scrolling

| Method | Signature | Description |
|---|---|---|
| `Scroll` | `bool Scroll(int wheelDelta, out string message)` | Scrolls vertically. Positive scrolls up, negative scrolls down. 120 = one notch. |
| `ScrollUp` | `bool ScrollUp(out string message)` | Scrolls up one wheel notch. |
| `ScrollUp` | `bool ScrollUp(int notches, out string message)` | Scrolls up the given number of wheel notches. |
| `ScrollDown` | `bool ScrollDown(out string message)` | Scrolls down one wheel notch. |
| `ScrollDown` | `bool ScrollDown(int notches, out string message)` | Scrolls down the given number of wheel notches. |
| `ScrollHorizontal` | `bool ScrollHorizontal(int wheelDelta, out string message)` | Scrolls horizontally. Positive scrolls right, negative scrolls left. |
| `ScrollRight` | `bool ScrollRight(out string message)` | Scrolls right one wheel notch. |
| `ScrollRight` | `bool ScrollRight(int notches, out string message)` | Scrolls right the given number of wheel notches. |
| `ScrollLeft` | `bool ScrollLeft(out string message)` | Scrolls left one wheel notch. |
| `ScrollLeft` | `bool ScrollLeft(int notches, out string message)` | Scrolls left the given number of wheel notches. |
| `ScrollHorizontalAt` | `bool ScrollHorizontalAt(int x, int y, int wheelDelta, out string message)` | Moves the cursor to the coordinates and scrolls horizontally there. |

### Cursor Appearance / Visibility / Confinement

| Method | Signature | Description |
|---|---|---|
| `SetCursor` | `bool SetCursor(SystemCursorType cursor, out string message)` | Changes the normal arrow cursor to the given system cursor. Call `ResetSystemCursors` afterwards. |
| `ReplaceSystemCursor` | `bool ReplaceSystemCursor(SystemCursorType slotToReplace, SystemCursorType newCursor, out string message)` | Replaces a specific system cursor slot with another standard system cursor. |
| `SetCursorFromFile` | `bool SetCursorFromFile(SystemCursorType slotToReplace, string filePath, out string message)` | Loads a cursor from a `.cur`/`.ani` file into the given system cursor slot. |
| `ResetSystemCursors` | `bool ResetSystemCursors(out string message)` | Restores all system cursors to the Windows defaults. |
| `HideCursor` | `void HideCursor()` | Hides the cursor. Counterbalanced by `ShowCursor`. |
| `ShowCursor` | `void ShowCursor()` | Shows the cursor again after `HideCursor`. |
| `IsCursorVisible` | `bool IsCursorVisible()` | Returns `false` if the cursor was hidden via this component's `HideCursor` method. |
| `ClipCursor` | `bool ClipCursor(int left, int top, int right, int bottom, out string message)` | Confines the cursor to the given screen rectangle until `ReleaseCursorClip` is called. |
| `ReleaseCursorClip` | `bool ReleaseCursorClip(out string message)` | Removes cursor confinement set by `ClipCursor`. |
| `GetCursorClip` | `bool GetCursorClip(out Rectangle clip, out string message)` | Gets the rectangle the cursor is currently confined to (full virtual screen when unclipped). |

### Button State / Screen Info

| Method | Signature | Description |
|---|---|---|
| `IsLeftButtonDown` | `bool IsLeftButtonDown()` | Returns `true` while the left mouse button is held down. |
| `IsRightButtonDown` | `bool IsRightButtonDown()` | Returns `true` while the right mouse button is held down. |
| `IsMiddleButtonDown` | `bool IsMiddleButtonDown()` | Returns `true` while the middle mouse button is held down. |
| `GetDoubleClickTimeMs` | `int GetDoubleClickTimeMs()` | Gets the system double-click time in milliseconds. |
| `SetDoubleClickTimeMs` | `bool SetDoubleClickTimeMs(int milliseconds, out string message)` | Sets the system double-click time in milliseconds (0 restores the 500 ms default; max 5000). |
| `GetScreenWidth` | `int GetScreenWidth()` | Gets the width of the primary screen in pixels. |
| `GetScreenHeight` | `int GetScreenHeight()` | Gets the height of the primary screen in pixels. |
| `GetVirtualScreenBounds` | `void GetVirtualScreenBounds(out int left, out int top, out int width, out int height)` | Gets the bounding rectangle of the whole virtual screen (all monitors). |
| `IsPointOnScreen` | `bool IsPointOnScreen(int x, int y)` | Returns `true` if the coordinates lie inside the virtual screen bounds. |
| `ClampToScreenX` | `int ClampToScreenX(int x)` | Clamps an X coordinate into the virtual screen's horizontal range. |
| `ClampToScreenY` | `int ClampToScreenY(int y)` | Clamps a Y coordinate into the virtual screen's vertical range. |

### Input Blocking (Attended Sessions)

| Method | Signature | Description |
|---|---|---|
| `BlockUserInput` | `bool BlockUserInput(out string message)` | Blocks all real keyboard/mouse input system-wide until `UnblockUserInput` (injected input still works). Must be paired with `UnblockUserInput` in a Finally block. |
| `UnblockUserInput` | `void UnblockUserInput()` | Re-enables real keyboard/mouse input after `BlockUserInput`. Safe to call even when nothing is blocked. |

### Background Clicks (PostMessage)

| Method | Signature | Description |
|---|---|---|
| `ClickWindow` | `bool ClickWindow(IntPtr hWnd, MouseButton button, out string message)` | Posts a click to the center of a window handle without moving the cursor or stealing focus. |
| `ClickWindowAtPoint` | `bool ClickWindowAtPoint(IntPtr hWnd, int screenX, int screenY, MouseButton button, out string message)` | Posts a click to a window at the given screen coordinates (converted to client coords). |
| `ClickWindowAtClientPoint` | `bool ClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button, out string message)` | Posts a click to a window at client-area coordinates (Spy++ style). Supports all buttons. |
| `DoubleClickWindowAtClientPoint` | `bool DoubleClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY, out string message)` | Posts a double-click sequence (DOWN/UP/DBLCLK/UP) to a window at client coordinates. |

### Window-Relative Targeting

| Method | Signature | Description |
|---|---|---|
| `GetWindowBounds` | `bool GetWindowBounds(IntPtr hWnd, out Rectangle bounds, out string message)` | Gets the screen-space bounding rectangle of a window. |
| `ClientPointToScreen` | `bool ClientPointToScreen(IntPtr hWnd, int clientX, int clientY, out int screenX, out int screenY, out string message)` | Converts a point in a window's client area to screen coordinates. |
| `ScreenPointToClient` | `bool ScreenPointToClient(IntPtr hWnd, int screenX, int screenY, out int clientX, out int clientY, out string message)` | Converts a screen coordinate to a point relative to a window's client area. |
| `ClickAtClientPoint` | `bool ClickAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button, out string message)` | Moves the real cursor to a window-relative client point and clicks there (works where PostMessage-based clicks are ignored, e.g. WPF/Electron/Chromium). |
| `ClickAtRelativePosition` | `bool ClickAtRelativePosition(IntPtr hWnd, double xFraction, double yFraction, MouseButton button, out string message)` | Clicks at a fractional position within a window's client area (e.g. 0.5, 0.9), resilient to minor resizes across machines. |
| `GetWindowAtPoint` | `IntPtr GetWindowAtPoint(int x, int y)` | Gets the handle of the window at the given screen point (`WindowFromPoint`). |
| `SafeClickAt` | `bool SafeClickAt(int x, int y, MouseButton button, IntPtr expectedWindowHandle, out string message)` | Clicks only if the window under the point matches the expected window (or a descendant) — guards against misclicks from a shifted layout. |

### DPI / Physical Coordinates

| Method | Signature | Description |
|---|---|---|
| `GetPhysicalCursorX` | `bool GetPhysicalCursorX(out int x, out string message)` | Gets the cursor X in physical pixels (unaffected by DPI scaling). |
| `GetPhysicalCursorY` | `bool GetPhysicalCursorY(out int y, out string message)` | Gets the cursor Y in physical pixels (unaffected by DPI scaling). |
| `IsProcessDpiAware` | `bool IsProcessDpiAware()` | Returns `true` if the process is DPI-aware (any level); `false` if DPI-unaware. |

### Cursor Highlight

| Method | Signature | Description |
|---|---|---|
| `FlashCursorHighlight` | `bool FlashCursorHighlight(out string message, int radius = 30, int flashes = 3, int flashMs = 200, int ringWidth = 3, int colorRef = 0x0000FF)` | Flashes an inverting ring around the cursor for demos/recordings. Erases itself exactly via XOR drawing. |

### Human-like Movement

| Method | Signature | Description |
|---|---|---|
| `MoveMouseBezier` | `bool MoveMouseBezier(int x, int y, out string message, int durationMs = 500)` | Moves the cursor to the target along a randomized Bezier curve with ease-in-out timing (human-like). |
| `BezierClickAt` | `bool BezierClickAt(int x, int y, MouseButton button, out string message, int durationMs = 500)` | Moves along a randomized Bezier curve to the target, then clicks — the human-like counterpart to `ClickAt`. |
| `BezierDoubleClickAt` | `bool BezierDoubleClickAt(int x, int y, MouseButton button, out string message, int durationMs = 500)` | Moves along a randomized Bezier curve to the target, then double-clicks. |
| `BezierDragAndDrop` | `bool BezierDragAndDrop(int startX, int startY, int endX, int endY, out string message, int durationMs = 500)` | Performs a left-button drag along a randomized Bezier curve instead of a straight line — the human-like counterpart to `DragAndDrop`. |

### Verification & Synchronization

| Method | Signature | Description |
|---|---|---|
| `GetPixelColor` | `bool GetPixelColor(int x, int y, out int color, out string message)` | Reads the color of the screen pixel at the given coordinates, as a 0x00BBGGRR COLORREF value (same format as `FlashCursorHighlight`'s `colorRef`). |
| `WaitForPixelColor` | `bool WaitForPixelColor(int x, int y, int expectedColorRef, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen pixel until it matches the expected COLORREF or the timeout elapses. `message` is only set if a Win32 failure aborted the poll early. |
| `WaitForPixelChange` | `bool WaitForPixelChange(int x, int y, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen pixel until its color changes from its value at call time, or the timeout elapses. `message` is only set if a Win32 failure aborted the poll early. |
| `IsBusyCursorActive` | `bool IsBusyCursorActive(out string message)` | Returns `true` if the current system cursor is the Wait or AppStarting busy indicator. `message` is only set if the underlying query failed. |
| `WaitForIdleCursor` | `bool WaitForIdleCursor(int timeoutMs, int pollIntervalMs, out string message)` | Waits until the busy cursor (Wait/AppStarting) clears, or the timeout elapses. `message` is only set if a Win32 failure aborted the poll early. |

## Notes & Caveats

- **Every method that could previously throw now returns `bool` with an `out string message`**
  instead — bad arguments, an undefined `MouseButton`, and Win32/input-injection failures
  (locked desktop, UAC/secure desktop, integrity level) are all reported this way, with
  `message` set to a human-readable reason whenever the method returns `false`. The only
  methods that never accepted a failure mode in the first place (`HideCursor`, `ShowCursor`,
  `IsCursorVisible`, `IsLeftButtonDown`/`IsRightButtonDown`/`IsMiddleButtonDown`,
  `GetDoubleClickTimeMs`, `GetScreenWidth`/`GetScreenHeight`, `GetVirtualScreenBounds`,
  `IsPointOnScreen`, `ClampToScreenX`/`ClampToScreenY`, `UnblockUserInput`, `GetWindowAtPoint`,
  `IsProcessDpiAware`) are unchanged — plain returns, no `message` parameter.
- **`WaitForPixelColor`/`WaitForPixelChange`/`IsBusyCursorActive`/`WaitForIdleCursor`** keep
  their original `bool` meaning (matched/idle vs. not), so a `false` return alone doesn't
  distinguish "genuinely timed out" from "aborted early due to a Win32 failure" — check
  whether `message` is non-null to tell those two cases apart.
- **Cursor changes are system-wide** (all applications) for the current session and persist
  until `ResetSystemCursors` is called — always restore them (ideally in a `Finally` block).
- **`ClickWithModifiers`** injects modifier presses, the click, and modifier releases as a
  single `SendInput` batch, so real user input cannot interleave mid-sequence. Alt+Click can
  activate the menu bar in some classic Win32 applications.
- **`BlockUserInput`** blocks real input but not input injected by this component. Only the
  blocking thread can unblock; Ctrl+Alt+Del always breaks the block as a safety hatch.
- **Background clicks (`ClickWindow*`)** are blocked by UIPI against higher-integrity
  (elevated) targets, and are often ignored by browsers, DirectX games, and frameworks that
  read raw input directly. Target the raw child control handle, not the top-level window.
- **`FlashCursorHighlight`** does not draw over exclusive fullscreen (DirectX) applications,
  and a window repaint while the ring is visible can leave artifacts the XOR erase can't clean up.
- **`MoveMouseBezier`** varies its curve, timing, and jitter on every call (useful for making
  automation less detectable/robotic), but always lands exactly on the requested endpoint.
- **`SafeClickAt`** compares the window under the point (and its root ancestor) against
  `expectedWindowHandle`; pass the top-level/root window handle you expect to own that screen
  region, not necessarily the exact child control.
- **`IsBusyCursorActive`/`WaitForIdleCursor`** are heuristics based on which system cursor is
  currently showing — an app can be busy without changing the cursor, so treat a "not busy"
  result as a hint, not a guarantee the app finished processing.
- **`GetPixelColor`/`WaitForPixelColor`/`WaitForPixelChange`** require exact color matches;
  they don't do fuzzy/tolerance-based comparison, so anti-aliased or gradient pixels may need
  a slightly different sample point.
