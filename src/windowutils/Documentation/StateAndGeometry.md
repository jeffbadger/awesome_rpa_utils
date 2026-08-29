# State & Geometry

`GetWindowBounds`, `SetWindowBounds`, `MoveWindow`, `ResizeWindow`, and
`CloseWindow` return `bool` (success) with an `out string message` — none of
them throw. The examples below discard `message` via `out _` where the
failure reason isn't needed.

## Read a window's position and size

```csharp
window.GetWindowBounds(hWnd, out System.Drawing.Rectangle bounds, out _);
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
