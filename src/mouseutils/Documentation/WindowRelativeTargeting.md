# Window-Relative Targeting

Targeting real cursor clicks and coordinates relative to a specific window,
instead of fixed absolute screen coordinates — and guarding against clicking
the wrong place when the layout has shifted.

Every method here except `GetWindowAtPoint` returns `bool` (success) with an
`out string message` explaining why on failure — none of them throw. The
examples below discard `message` via `out _` where the failure reason isn't
needed.

## `GetWindowBounds(IntPtr hWnd)`

**Scenario:** A flow needs to know how much room a resizable application
window currently occupies before computing a click target inside it, since
the operator (or a prior automation step) may have resized or moved it.

```csharp
mouse.GetWindowBounds(appWindowHandle, out System.Drawing.Rectangle bounds, out _);
Logger.Info($"Target window is at {bounds.Location}, size {bounds.Size}");
```

## `ClientPointToScreen(IntPtr hWnd, int clientX, int clientY, out int screenX, out int screenY)`

**Scenario:** A UI Automation library reports a control's bounds in
client-relative coordinates, but `MouseUtils` needs absolute screen
coordinates to move the real cursor there.

```csharp
mouse.ClientPointToScreen(formHandle, clientX: 84, clientY: 212, out int screenX, out int screenY, out _);
mouse.ClickAt(screenX, screenY, MouseButton.Left, out _);
```

## `ScreenPointToClient(IntPtr hWnd, int screenX, int screenY, out int clientX, out int clientY)`

**Scenario:** After clicking at a known screen coordinate, the automation
wants to record that click's position relative to the window for an audit
log entry that stays meaningful even if the window later moves.

```csharp
mouse.ScreenPointToClient(formHandle, screenX: 640, screenY: 480, out int clientX, out int clientY, out _);
AuditLog.Record($"Clicked at client offset ({clientX},{clientY}) within the Order form.");
```

## `ClickAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button)`

**Scenario:** The existing PostMessage-based `ClickWindowAtClientPoint` is
being ignored by a modern Electron-based application that reads raw input
instead of the classic Win32 message queue. Switching to a real cursor click
at the same client-relative coordinate makes the click register.

```csharp
mouse.ClickAtClientPoint(electronAppHandle, clientX: 300, clientY: 150, MouseButton.Left, out _);
```

## `ClickAtRelativePosition(IntPtr hWnd, double xFraction, double yFraction, MouseButton button)`

**Scenario:** The same automation needs to run unmodified on both a
1920×1080 workstation and a 1366×768 laptop. Instead of hardcoding a pixel
coordinate for the "Next" button (which sits in different absolute positions
on each), the automation clicks at a fraction of the window's client area
that stays correct regardless of resolution.

```csharp
mouse.ClickAtRelativePosition(wizardWindowHandle, xFraction: 0.9, yFraction: 0.95, MouseButton.Left, out _); // bottom-right "Next" button
```

## `GetWindowAtPoint(int x, int y)`

**Scenario:** Before clicking a coordinate computed from a cached screen
layout, the automation double-checks which window is actually there now, to
catch the case where a dialog popped up and shifted everything underneath it.

```csharp
IntPtr actual = mouse.GetWindowAtPoint(640, 480);
if (actual != expectedFormHandle)
{
    Logger.Warn("Layout has changed since the click target was computed; re-scanning the screen.");
    return;
}
mouse.ClickAt(640, 480, MouseButton.Left, out _);
```

## `SafeClickAt(int x, int y, MouseButton button, IntPtr expectedWindowHandle)`

**Scenario:** A high-stakes "Approve Payment" click must never land on the
wrong window if an unexpected dialog (like a Windows Update prompt) has
stolen focus and shifted the screen layout in the moment between computing
the click target and executing it. `SafeClickAt` refuses the click instead of
blindly clicking whatever is now under the cursor.

```csharp
if (!mouse.SafeClickAt(x: 900, y: 640, MouseButton.Left, expectedWindowHandle: approvalFormHandle, out string message))
{
    Logger.Error($"Refused to click Approve Payment: {message}");
}
```
