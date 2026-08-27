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

| Method | Description |
|---|---|
| `int GetX()` | Gets the current X coordinate of the cursor. |
| `int GetY()` | Gets the current Y coordinate of the cursor. |
| `Point GetPosition()` | Gets the current cursor position as a `System.Drawing.Point`. |
| `void MoveTo(int x, int y)` | Instantly moves the cursor to the given screen coordinates. |
| `void MoveBy(int deltaX, int deltaY)` | Moves the cursor by the given offsets relative to its current position. |
| `void SmoothMoveTo(int x, int y)` | Smoothly moves the cursor to the target position (25 steps, 5 ms/step). |
| `void SmoothMoveTo(int x, int y, int steps, int delayMilliseconds)` | Smoothly moves the cursor using a custom step count and per-step delay. |
| `void JiggleMouse(int pixels = 1)` | Nudges the cursor by a tiny amount and back, to reset idle/screensaver timers without disturbing its position. |

### Clicks

| Method | Description |
|---|---|
| `void Click(MouseButton button)` | Clicks the given button at the current cursor position (~20 ms press/release). |
| `void ClickAt(int x, int y, MouseButton button)` | Moves the cursor to the coordinates and clicks the given button. |
| `void DoubleClick(MouseButton button)` | Double-clicks the given button at the current cursor position. |
| `void DoubleClickAt(int x, int y, MouseButton button)` | Moves the cursor to the coordinates and double-clicks the given button. |
| `void LeftClick()` | Left-clicks at the current cursor position. |
| `void RightClick()` | Right-clicks at the current cursor position. |
| `void MiddleClick()` | Middle-clicks at the current cursor position. |
| `void LeftClickAt(int x, int y)` | Left-clicks at the given screen coordinates. |
| `void RightClickAt(int x, int y)` | Right-clicks at the given screen coordinates. |
| `void LeftDoubleClick()` | Double left-clicks at the current cursor position. |
| `void RightDoubleClick()` | Double right-clicks at the current cursor position. |
| `void LeftDoubleClickAt(int x, int y)` | Double left-clicks at the given screen coordinates. |
| `void MouseDown(MouseButton button)` | Presses and holds the given mouse button. Pair with `MouseUp`. |
| `void MouseUp(MouseButton button)` | Releases the given mouse button. |
| `void ClickAndHold(MouseButton button, int holdMilliseconds)` | Holds the given button down for the specified time, then releases it. |
| `void ClickWithModifiers(MouseButton button, ModifierKeys modifiers)` | Clicks a button while holding modifier keys (Control/Shift/Alt, combinable), injected as one atomic batch. |
| `void ClickAndRestore(int x, int y, MouseButton button)` | Clicks at the given coordinates, then immediately returns the cursor to its original position. |
| `void ClickWithRetry(int x, int y, MouseButton button, int maxAttempts, int retryDelayMilliseconds)` | Clicks at the given coordinates, retrying on transient `Win32Exception` failures up to `maxAttempts` times. |
| `void TripleClick(MouseButton button)` | Triple-clicks the given button at the current cursor position (select-line/paragraph gesture). |

### Drag & Drop

| Method | Description |
|---|---|
| `void DragAndDrop(int startX, int startY, int endX, int endY)` | Performs a left-button drag from start to end (30 steps, 10 ms/step). |
| `void DragAndDrop(int startX, int startY, int endX, int endY, int steps, int stepDelayMilliseconds)` | Performs a left-button drag with a custom step count and step delay. |
| `void RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers)` | Performs a left-button rubber-band drag while holding modifier keys (e.g. Ctrl-drag to add to a selection). |
| `void RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers, int steps, int stepDelayMilliseconds)` | Same, with a custom step count and step delay. |
| `void DragAndHold(int startX, int startY, int endX, int endY, int holdMilliseconds)` | Drags from start to end, then holds the button down at the destination before releasing (for hover-to-expand drop targets). |

### Wheel / Scrolling

| Method | Description |
|---|---|
| `void Scroll(int wheelDelta)` | Scrolls vertically. Positive scrolls up, negative scrolls down. 120 = one notch. |
| `void ScrollUp()` | Scrolls up one wheel notch. |
| `void ScrollUp(int notches)` | Scrolls up the given number of wheel notches. |
| `void ScrollDown()` | Scrolls down one wheel notch. |
| `void ScrollDown(int notches)` | Scrolls down the given number of wheel notches. |
| `void ScrollHorizontal(int wheelDelta)` | Scrolls horizontally. Positive scrolls right, negative scrolls left. |
| `void ScrollRight()` | Scrolls right one wheel notch. |
| `void ScrollRight(int notches)` | Scrolls right the given number of wheel notches. |
| `void ScrollLeft()` | Scrolls left one wheel notch. |
| `void ScrollLeft(int notches)` | Scrolls left the given number of wheel notches. |
| `void ScrollHorizontalAt(int x, int y, int wheelDelta)` | Moves the cursor to the coordinates and scrolls horizontally there. |

### Cursor Appearance / Visibility / Confinement

| Method | Description |
|---|---|
| `void SetCursor(SystemCursorType cursor)` | Changes the normal arrow cursor to the given system cursor. Call `ResetSystemCursors` afterwards. |
| `void ReplaceSystemCursor(SystemCursorType slotToReplace, SystemCursorType newCursor)` | Replaces a specific system cursor slot with another standard system cursor. |
| `void SetCursorFromFile(SystemCursorType slotToReplace, string filePath)` | Loads a cursor from a `.cur`/`.ani` file into the given system cursor slot. |
| `void ResetSystemCursors()` | Restores all system cursors to the Windows defaults. |
| `void HideCursor()` | Hides the cursor. Counterbalanced by `ShowCursor`. |
| `void ShowCursor()` | Shows the cursor again after `HideCursor`. |
| `bool IsCursorVisible()` | Returns `false` if the cursor was hidden via this component's `HideCursor` method. |
| `void ClipCursor(int left, int top, int right, int bottom)` | Confines the cursor to the given screen rectangle until `ReleaseCursorClip` is called. |
| `void ReleaseCursorClip()` | Removes cursor confinement set by `ClipCursor`. |
| `Rectangle GetCursorClip()` | Gets the rectangle the cursor is currently confined to (full virtual screen when unclipped). |

### Button State / Screen Info

| Method | Description |
|---|---|
| `bool IsLeftButtonDown()` | Returns `true` while the left mouse button is held down. |
| `bool IsRightButtonDown()` | Returns `true` while the right mouse button is held down. |
| `bool IsMiddleButtonDown()` | Returns `true` while the middle mouse button is held down. |
| `int GetDoubleClickTimeMs()` | Gets the system double-click time in milliseconds. |
| `void SetDoubleClickTimeMs(int milliseconds)` | Sets the system double-click time in milliseconds (0 restores the 500 ms default; max 5000). |
| `int GetScreenWidth()` | Gets the width of the primary screen in pixels. |
| `int GetScreenHeight()` | Gets the height of the primary screen in pixels. |
| `void GetVirtualScreenBounds(out int left, out int top, out int width, out int height)` | Gets the bounding rectangle of the whole virtual screen (all monitors). |
| `bool IsPointOnScreen(int x, int y)` | Returns `true` if the coordinates lie inside the virtual screen bounds. |
| `int ClampToScreenX(int x)` | Clamps an X coordinate into the virtual screen's horizontal range. |
| `int ClampToScreenY(int y)` | Clamps a Y coordinate into the virtual screen's vertical range. |

### Input Blocking (Attended Sessions)

| Method | Description |
|---|---|
| `void BlockUserInput()` | Blocks all real keyboard/mouse input system-wide until `UnblockUserInput` (injected input still works). Must be paired with `UnblockUserInput` in a Finally block. |
| `void UnblockUserInput()` | Re-enables real keyboard/mouse input after `BlockUserInput`. Safe to call even when nothing is blocked. |

### Background Clicks (PostMessage)

| Method | Description |
|---|---|
| `void ClickWindow(IntPtr hWnd, MouseButton button)` | Posts a click to the center of a window handle without moving the cursor or stealing focus. |
| `void ClickWindowAtPoint(IntPtr hWnd, int screenX, int screenY, MouseButton button)` | Posts a click to a window at the given screen coordinates (converted to client coords). |
| `void ClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button)` | Posts a click to a window at client-area coordinates (Spy++ style). Supports all buttons. |
| `void DoubleClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY)` | Posts a double-click sequence (DOWN/UP/DBLCLK/UP) to a window at client coordinates. |

### Window-Relative Targeting

| Method | Description |
|---|---|
| `Rectangle GetWindowBounds(IntPtr hWnd)` | Gets the screen-space bounding rectangle of a window. |
| `void ClientPointToScreen(IntPtr hWnd, int clientX, int clientY, out int screenX, out int screenY)` | Converts a point in a window's client area to screen coordinates. |
| `void ScreenPointToClient(IntPtr hWnd, int screenX, int screenY, out int clientX, out int clientY)` | Converts a screen coordinate to a point relative to a window's client area. |
| `void ClickAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button)` | Moves the real cursor to a window-relative client point and clicks there (works where PostMessage-based clicks are ignored, e.g. WPF/Electron/Chromium). |
| `void ClickAtRelativePosition(IntPtr hWnd, double xFraction, double yFraction, MouseButton button)` | Clicks at a fractional position within a window (e.g. 0.5, 0.9), resilient to minor resizes across machines. |
| `IntPtr GetWindowAtPoint(int x, int y)` | Gets the handle of the window at the given screen point (`WindowFromPoint`). |
| `void SafeClickAt(int x, int y, MouseButton button, IntPtr expectedWindowHandle)` | Clicks only if the window under the point matches the expected window (or a descendant) — guards against misclicks from a shifted layout. |

### DPI / Physical Coordinates

| Method | Description |
|---|---|
| `int GetPhysicalCursorX()` | Gets the cursor X in physical pixels (unaffected by DPI scaling). |
| `int GetPhysicalCursorY()` | Gets the cursor Y in physical pixels (unaffected by DPI scaling). |
| `bool IsProcessDpiAware()` | Returns `true` if the process is DPI-aware (any level); `false` if DPI-unaware. |

### Cursor Highlight

| Method | Description |
|---|---|
| `void FlashCursorHighlight(int radius = 30, int flashes = 3, int flashMs = 200, int ringWidth = 3, int colorRef = 0x0000FF)` | Flashes an inverting ring around the cursor for demos/recordings. Erases itself exactly via XOR drawing. |

### Human-like Movement

| Method | Description |
|---|---|
| `void MoveMouseBezier(int x, int y, int durationMs = 500)` | Moves the cursor to the target along a randomized Bezier curve with ease-in-out timing (human-like). |
| `void BezierClickAt(int x, int y, MouseButton button, int durationMs = 500)` | Moves along a randomized Bezier curve to the target, then clicks — the human-like counterpart to `ClickAt`. |
| `void BezierDoubleClickAt(int x, int y, MouseButton button, int durationMs = 500)` | Moves along a randomized Bezier curve to the target, then double-clicks. |
| `void BezierDragAndDrop(int startX, int startY, int endX, int endY, int durationMs = 500)` | Performs a left-button drag along a randomized Bezier curve instead of a straight line — the human-like counterpart to `DragAndDrop`. |

### Verification & Synchronization

| Method | Description |
|---|---|
| `int GetPixelColor(int x, int y)` | Reads the color of the screen pixel at the given coordinates, as a 0x00BBGGRR COLORREF value (same format as `FlashCursorHighlight`'s `colorRef`). |
| `bool WaitForPixelColor(int x, int y, int expectedColorRef, int timeoutMs, int pollIntervalMs)` | Polls a screen pixel until it matches the expected COLORREF or the timeout elapses. |
| `bool WaitForPixelChange(int x, int y, int timeoutMs, int pollIntervalMs)` | Polls a screen pixel until its color changes from its value at call time, or the timeout elapses. |
| `bool IsBusyCursorActive()` | Returns `true` if the current system cursor is the Wait or AppStarting busy indicator. |
| `bool WaitForIdleCursor(int timeoutMs, int pollIntervalMs)` | Waits until the busy cursor (Wait/AppStarting) clears, or the timeout elapses. |

## Notes & Caveats

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
