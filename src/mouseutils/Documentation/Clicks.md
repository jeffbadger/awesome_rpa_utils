# Clicks

Clicking, double-clicking, holding, and modifier-key clicks.

Every method here returns `bool` (success) with an `out string message` explaining
why on failure — none of them throw. The examples below discard `message` via
`out _` where the failure reason isn't needed.

## `Click(MouseButton button)`

**Scenario:** The cursor has already been positioned over an "OK" button by a
prior step; the automation just needs to press it.

```csharp
mouse.MoveTo(600, 420, out _);
mouse.Click(MouseButton.Left, out _);
```

## `ClickAt(int x, int y, MouseButton button)`

**Scenario:** A claims-processing bot needs to click the "Submit" button at a
known fixed coordinate in a maximized legacy application window.

```csharp
mouse.ClickAt(1180, 860, MouseButton.Left, out _);
```

## `DoubleClick(MouseButton button)`

**Scenario:** After tabbing focus onto a file-list entry in a Windows Explorer
window, the automation double-clicks to open it without re-targeting coordinates.

```csharp
mouse.DoubleClick(MouseButton.Left, out _);
```

## `DoubleClickAt(int x, int y, MouseButton button)`

**Scenario:** Opening a specific row's detail view in a desktop grid application
by double-clicking its known screen position.

```csharp
mouse.DoubleClickAt(340, 512, MouseButton.Left, out _); // opens the selected invoice
```

## `LeftClick()` / `RightClick()` / `MiddleClick()`

**Scenario:** A bot right-clicks a system tray icon to open its context menu,
then left-clicks "Exit" once the menu appears; a middle-click closes a browser
tab in the app under test.

```csharp
mouse.RightClick(out _);          // open the tray icon's context menu
Thread.Sleep(200);
mouse.MoveTo(exitX, exitY, out _);
mouse.LeftClick(out _);           // choose "Exit"

mouse.MiddleClick(out _);         // close the current browser tab under the cursor
```

## `LeftClickAt(int x, int y)` / `RightClickAt(int x, int y)`

**Scenario:** Opening a right-click context menu on a specific spreadsheet cell
to choose "Copy", then left-clicking a fixed "Paste" toolbar button elsewhere.

```csharp
mouse.RightClickAt(410, 260, out _); // context menu on cell B7
mouse.LeftClickAt(150, 40, out _);   // Paste button in the ribbon
```

## `LeftDoubleClick()` / `RightDoubleClick()`

**Scenario:** After moving over a column border in a grid, double-clicking
auto-fits the column width; a right-double-click on a taskbar icon restores a
minimized legacy window in some shells.

```csharp
mouse.MoveTo(columnBorderX, headerY, out _);
mouse.LeftDoubleClick(out _); // auto-fit column width
```

## `LeftDoubleClickAt(int x, int y)`

**Scenario:** Launching a desktop shortcut icon by double-clicking its known
position on a kiosk machine with a fixed, unchanging desktop layout.

```csharp
mouse.LeftDoubleClickAt(48, 96, out _); // "Order Entry" desktop icon
```

## `MouseDown(MouseButton button)` / `MouseUp(MouseButton button)`

**Scenario:** Implementing a custom drag gesture where the automation needs to
move the mouse in a non-linear path between the press and release (the built-in
`DragAndDrop` only supports a straight/eased path).

```csharp
mouse.MoveTo(startX, startY, out _);
mouse.MouseDown(MouseButton.Left, out _);
mouse.MoveTo(startX + 50, startY - 30, out _); // detour around an obstacle control
mouse.MoveTo(endX, endY, out _);
mouse.MouseUp(MouseButton.Left, out _);
```

## `ClickAndHold(MouseButton button, int holdMilliseconds)`

**Scenario:** A legacy terminal-emulator control only registers a click after
the button has been held for at least 500 ms (it debounces fast synthetic clicks).

```csharp
mouse.MoveTo(700, 300, out _);
mouse.ClickAndHold(MouseButton.Left, 500, out _);
```

## `ClickWithModifiers(MouseButton button, ModifierKeys modifiers)`

**Scenario:** Multi-selecting three non-adjacent rows in a grid by Ctrl+clicking
each one, then Shift+clicking a fourth to extend the selection to a range.

```csharp
mouse.MoveTo(rowY: 200, x: 100, out _); // pseudo-args for illustration
mouse.ClickWithModifiers(MouseButton.Left, ModifierKeys.Control, out _);

mouse.MoveTo(320, 100, out _);
mouse.ClickWithModifiers(MouseButton.Left, ModifierKeys.Control, out _);

mouse.MoveTo(560, 100, out _);
mouse.ClickWithModifiers(MouseButton.Left, ModifierKeys.Control | ModifierKeys.Shift, out _);
```

## `ClickAndRestore(int x, int y, MouseButton button)`

**Scenario:** In an attended session, a background bot needs to dismiss a
recurring "Save reminder" popup that appears at a fixed location, without
disturbing where the human operator's cursor currently is.

```csharp
// Operator is actively working elsewhere on screen; don't steal their pointer.
mouse.ClickAndRestore(dismissX: 960, dismissY: 40, MouseButton.Left, out _);
```

## `ClickWithRetry(int x, int y, MouseButton button, int maxAttempts, int retryDelayMilliseconds)`

**Scenario:** An unattended bot occasionally fails to click "Submit" because a
Windows Update notification balloon briefly grabs the secure desktop. Rather
than failing the whole case, the automation retries the click a few times with
a short delay before giving up for real.

```csharp
mouse.ClickWithRetry(x: 640, y: 480, MouseButton.Left, maxAttempts: 3, retryDelayMilliseconds: 500, out string message);
```

## `TripleClick(MouseButton button)`

**Scenario:** Replacing the entire contents of a multi-line memo field in a
legacy Win32 app before typing new text — triple-click selects the whole
line/paragraph the cursor sits on, which a single or double click won't do.

```csharp
mouse.MoveTo(notesFieldX, notesFieldY, out _);
mouse.TripleClick(MouseButton.Left, out _); // selects the whole line
SendKeys.SendWait("Reviewed and approved.");
```

## Checking why a click failed

```csharp
if (!mouse.ClickAt(1180, 860, MouseButton.Left, out string message))
{
    Logger.Warn($"Click failed: {message}");
}
```
