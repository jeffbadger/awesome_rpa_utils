# State & Geometry

`GetWindowBoundsAsRectangle`, `SetWindowBounds`, `MoveWindow`, `ResizeWindow`,
and `CloseWindow` return `bool` (success) with an `out string message` — none
of them throw. The examples below discard `message` via `out _` where the
failure reason isn't needed.

## Read a window's position and size

```csharp
window.GetWindowBoundsAsRectangle(hWnd, out System.Drawing.Rectangle bounds, out _);
```

For designers without a `Rectangle` proxy, the scalar overload returns the
same bounds as `left`/`top`/`width`/`height`:

```csharp
window.GetWindowBounds(hWnd, out int left, out int top, out int width, out int height, out _);
```

## Move and resize a window in one call

```csharp
window.SetWindowBounds(hWnd, left: 0, top: 0, width: 1024, height: 768, out _);
```

## Move only

```csharp
window.MoveWindow(hWnd, left: 100, top: 100, out _);
```

## Resize only

```csharp
window.ResizeWindow(hWnd, width: 800, height: 600, out _);
```

## Read a window's title, class, and owning process

```csharp
string title = window.GetWindowTitle(hWnd);
string className = window.GetWindowClassName(hWnd);
int pid = window.GetWindowProcessId(hWnd);
```

These collapse an invalid/stale handle to the same empty-string/`0` result as
a legitimately empty title, class name, or (theoretical) process ID `0`. When
the automation needs to tell "invalid handle" apart from "genuinely empty,"
use the `Try*` overloads instead:

```csharp
if (!window.TryGetWindowTitle(hWnd, out string title, out string message))
{
    Logger.Error($"Handle is no longer valid: {message}");
}
```

`TryGetWindowClassName` and `TryGetWindowProcessId` follow the same pattern.

## Minimize, maximize, and restore

```csharp
window.SetWindowState(hWnd, ShowWindowCommand.Minimized);
window.SetWindowState(hWnd, ShowWindowCommand.Maximized);
window.SetWindowState(hWnd, ShowWindowCommand.Restore);
```

## Check responsiveness before interacting

```csharp
if (!window.IsWindowResponding(hWnd))
{
    // the app is hung - skip it or escalate rather than clicking into it
}
```

## Close a window

```csharp
if (!window.CloseWindow(hWnd, out string message))
{
    Logger.Warn($"Could not close window: {message}");
}
```
