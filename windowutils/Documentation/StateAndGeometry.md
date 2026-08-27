# State & Geometry

## Read a window's position and size

```csharp
System.Drawing.Rectangle bounds = window.GetWindowBounds(hWnd);
```

## Move and resize a window in one call

```csharp
window.SetWindowBounds(hWnd, left: 0, top: 0, width: 1024, height: 768);
```

## Move only

```csharp
window.MoveWindow(hWnd, left: 100, top: 100);
```

## Resize only

```csharp
window.ResizeWindow(hWnd, width: 800, height: 600);
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
window.CloseWindow(hWnd);
```
