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

## Tell whether a window is minimized or maximized

`TryGetWindowState` is the read-side counterpart to `SetWindowState`. Before
reading coordinates off a window, make sure it is actually on screen - a
minimized window reports its position as about -32000:

```csharp
if (window.TryGetWindowState(hWnd, out WindowDisplayState state, out string message))
{
    if (state == WindowDisplayState.Minimized)
    {
        window.SetWindowState(hWnd, ShowWindowCommand.Restore);
    }
}
```

`WindowDisplayState` is `Normal`, `Minimized`, or `Maximized`. A window that was
minimized from a maximized state reports `Minimized` only. For a quick branch on
a Decision step, `IsWindowMinimized` and `IsWindowMaximized` return a plain
`bool` (both `false` for an invalid handle - use `TryGetWindowState` to tell that
apart). Visibility is separate: a hidden window still reports its own state.

## Detect a window blocked by a modal dialog

When an application opens a modal dialog, Windows disables the window that owns
it. Clicks and keystrokes sent to a disabled window are silently ignored, which
looks like the automation "did nothing". `IsWindowEnabled` spots this before the
next step:

```csharp
if (!window.IsWindowEnabled(mainWindow))
{
    // Something is blocking the main window - find and dismiss it first
    // (for example WaitForWindow for a known dialog title, then a dialog utility).
    Logger.Warn("Main window is not accepting input - a modal dialog is probably open.");
}
```

`IsWindowEnabled` can't say *which* dialog is open, and an application can also
disable a window for its own reasons (a busy state, a wizard step), so treat
`false` as "not accepting input" rather than proof of a dialog. A stale handle
also reads `false`; when that distinction matters, use `TryGetWindowEnabled`:

```csharp
if (!window.TryGetWindowEnabled(mainWindow, out bool enabled, out string message))
{
    Logger.Error($"Handle is no longer valid: {message}");
}
else if (!enabled)
{
    Logger.Warn("Main window is disabled.");
}
```
