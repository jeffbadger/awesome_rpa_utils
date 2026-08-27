# Background Clicks (PostMessage)

Posting click messages directly to a window handle without moving the real
cursor or stealing focus — the window can even be covered by other windows.

## `ClickWindow(IntPtr hWnd, MouseButton button)`

**Scenario:** A monitoring bot needs to click a "Refresh" button in a
minimized/background diagnostics window every five minutes, without ever
disturbing whatever the operator is actively working on in the foreground.

```csharp
IntPtr diagnosticsWindow = FindWindowByTitle("Diagnostics Console");
mouse.ClickWindow(diagnosticsWindow, MouseButton.Left); // clicks its center point
```

## `ClickWindowAtPoint(IntPtr hWnd, int screenX, int screenY, MouseButton button)`

**Scenario:** The automation already knows a button's location in screen
coordinates (from a prior `GetWindowRect`-based selector), but the target
window is currently obscured behind other windows, so a real cursor click
isn't reliable.

```csharp
mouse.ClickWindowAtPoint(hWnd, screenX: 1200, screenY: 640, MouseButton.Left);
```

## `ClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button)`

**Scenario:** A Spy++-style selector tool has already reported the target
button's client-area (window-relative) coordinates, which is the most stable
way to target a control that a background service manages, since it works
regardless of where the window currently sits on screen.

```csharp
mouse.ClickWindowAtClientPoint(hWnd, clientX: 84, clientY: 212, MouseButton.Left);
```

## `DoubleClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY)`

**Scenario:** Opening a file entry inside a background file-manager window
(one the automation is not allowed to bring to the foreground because doing so
would interrupt the operator's current task) by posting a full double-click
message sequence to it directly.

```csharp
mouse.DoubleClickWindowAtClientPoint(fileManagerHandle, clientX: 120, clientY: 340);
```

> These methods are blocked by UIPI against windows running at a higher
> integrity level, and are often ignored by browsers, DirectX games, and
> frameworks that read raw input directly rather than the classic Win32 message
> queue. For best results, target the raw child control handle rather than the
> top-level window.
