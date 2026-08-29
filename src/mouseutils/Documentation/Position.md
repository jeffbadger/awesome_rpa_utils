# Position

Reading and moving the cursor around the screen.

Every method here returns `bool` (success) with an `out string message` explaining
why on failure — none of them throw. The examples below discard `message` via
`out _` where the failure reason isn't needed.

## `GetX()` / `GetY()`

**Scenario:** An automation needs to log where the cursor was right before an
unexpected dialog appeared, to help diagnose a selector failure later.

```csharp
mouse.GetX(out int x, out _);
mouse.GetY(out int y, out _);
Logger.Warn($"Selector timed out; cursor was at ({x}, {y}) when it failed.");
```

## `GetPosition()`

**Scenario:** Before clicking a draggable splitter in a legacy Win32 app, the
automation captures the current position so it can restore it if the drag fails.

```csharp
mouse.GetPosition(out System.Drawing.Point before, out _);
if (!mouse.DragAndDrop(before.X, before.Y, before.X + 200, before.Y, out string message))
{
    mouse.MoveTo(before.X, before.Y, out _);
    Logger.Warn($"Drag failed, restored cursor: {message}");
}
```

## `MoveTo(int x, int y)`

**Scenario:** A screen-scrape step needs the cursor parked over a specific grid
cell so a tooltip appears, which the automation then reads via UI Automation.

```csharp
mouse.MoveTo(842, 317, out _); // Column "Balance", row 4 of the account grid
Thread.Sleep(300);             // give the tooltip time to render
```

## `MoveBy(int deltaX, int deltaY)`

**Scenario:** After clicking a button, the automation nudges the cursor away
from the button so a hover-triggered tooltip doesn't obscure the next screenshot
used for QA evidence capture.

```csharp
mouse.LeftClickAt(500, 400, out _);
mouse.MoveBy(0, 80, out _); // move down and off the button
```

## `SmoothMoveTo(int x, int y)`

**Scenario:** A legacy Citrix-published app only registers clicks on controls
that have first received a genuine `WM_MOUSEMOVE` sequence (a jump-move is
ignored). Smoothly gliding the cursor over first makes the subsequent click land.

```csharp
mouse.SmoothMoveTo(960, 540, out _); // 25 steps / 5 ms default, ~125 ms glide
mouse.LeftClick(out _);
```

## `SmoothMoveTo(int x, int y, int steps, int delayMilliseconds)`

**Scenario:** The same Citrix app is being recorded for a training video, so the
automation slows the glide down to make the mouse path clearly visible to viewers.

```csharp
mouse.SmoothMoveTo(960, 540, steps: 60, delayMilliseconds: 15, out _); // ~900 ms glide
mouse.LeftClick(out _);
```

## `JiggleMouse(int pixels = 1)`

**Scenario:** An unattended overnight batch job runs for six hours against a
VM whose group policy locks the screen after 10 minutes of no input. A
Robot Studio loop calls `JiggleMouse` every few minutes between processing
steps to keep the session alive without ever moving the cursor anywhere
visible or interrupting the app under automation.

```csharp
// Inside the automation's periodic "keep-alive" step:
mouse.JiggleMouse(out _); // 1px nudge and back
```

## Checking why a move failed

```csharp
if (!mouse.MoveTo(842, 317, out string message))
{
    Logger.Warn($"Cursor move failed: {message}");
}
```

