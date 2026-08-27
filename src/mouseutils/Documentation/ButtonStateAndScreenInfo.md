# Button State & Screen Info

Polling physical button state, double-click timing, and screen geometry.

## `IsLeftButtonDown()` / `IsRightButtonDown()` / `IsMiddleButtonDown()`

**Scenario:** An attended-automation script waits for the operator to finish a
manual drag (left button held down) before it resumes its own mouse actions,
to avoid fighting with the human for control of the cursor.

```csharp
while (mouse.IsLeftButtonDown())
{
    Thread.Sleep(50); // wait for the operator to release the button
}
mouse.LeftClickAt(400, 300);
```

## `GetDoubleClickTimeMs()`

**Scenario:** Before temporarily widening the system double-click time (see
below), the automation records the operator's current setting so it can be
restored exactly afterward.

```csharp
int originalMs = mouse.GetDoubleClickTimeMs();
```

## `SetDoubleClickTimeMs(int milliseconds)`

**Scenario:** An operator has tightened their double-click speed for accessibility
reasons, which makes `DoubleClickAt`'s two synthetic clicks land too far apart to
register as a double-click in the target app. The automation widens the window
for the duration of its run, then restores the original value.

```csharp
int originalMs = mouse.GetDoubleClickTimeMs();
mouse.SetDoubleClickTimeMs(800); // widen so synthetic double-clicks register
try
{
    mouse.DoubleClickAt(500, 400);
}
finally
{
    mouse.SetDoubleClickTimeMs(originalMs);
}
```

## `GetScreenWidth()` / `GetScreenHeight()`

**Scenario:** Centering a floating status window the automation displays,
based on the primary monitor's resolution.

```csharp
int centerX = mouse.GetScreenWidth() / 2;
int centerY = mouse.GetScreenHeight() / 2;
ShowStatusWindow(centerX, centerY);
```

## `GetVirtualScreenBounds(out int left, out int top, out int width, out int height)`

**Scenario:** A three-monitor workstation has a secondary display to the left
of the primary, so it uses negative X coordinates. Before computing a click
target relative to "the leftmost edge of the whole desktop," the automation
reads the real virtual-screen bounds instead of assuming (0,0).

```csharp
mouse.GetVirtualScreenBounds(out int left, out int top, out int width, out int height);
int farLeftEdge = left; // e.g. -1920 on this workstation, not 0
mouse.MoveTo(farLeftEdge + 10, top + 10);
```

## `IsPointOnScreen(int x, int y)`

**Scenario:** A computed click target came from OCR text-recognition math that
occasionally produces a slightly out-of-bounds coordinate; the automation
validates it before clicking to fail fast with a clear error instead of
clicking the wrong (clamped) location.

```csharp
int targetX = 2450, targetY = 300;
if (!mouse.IsPointOnScreen(targetX, targetY))
    throw new InvalidOperationException($"OCR target ({targetX},{targetY}) is off-screen.");

mouse.ClickAt(targetX, targetY, MouseButton.Left);
```

## `ClampToScreenX(int x)` / `ClampToScreenY(int y)`

**Scenario:** A relative-offset click (e.g., "20px right of this icon") could
push the target slightly past the screen edge on a small monitor; clamping
keeps the automation from throwing on a harmless off-by-a-few-pixels case.

```csharp
int safeX = mouse.ClampToScreenX(iconX + 20);
int safeY = mouse.ClampToScreenY(iconY);
mouse.ClickAt(safeX, safeY, MouseButton.Left);
```
