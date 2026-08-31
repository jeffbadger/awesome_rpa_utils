# Activation & Z-Order

`ActivateWindow` and `SetAlwaysOnTop` return `bool` (success) with an
`out string message` — neither throws. The examples below discard `message`
via `out _` where the failure reason isn't needed.

## Bring a window to the front before interacting with it

```csharp
window.ActivateWindow(hWnd, out _);
```

## Pin a window on top of everything else

```csharp
window.SetAlwaysOnTop(hWnd, alwaysOnTop: true, out _);
// ... later ...
window.SetAlwaysOnTop(hWnd, alwaysOnTop: false, out _);
```

## Wait for an application to launch and its window to appear

```csharp
System.Diagnostics.Process.Start("notepad.exe");
if (window.WaitForWindow("Notepad", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd))
{
    window.ActivateWindow(hWnd, out _);
}
```

The original overload's `false` covers both a genuine timeout and a null/empty
`title` (polling refused), indistinguishably. The `message`-output overload
tells them apart:

```csharp
if (!window.WaitForWindow("Notepad", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd, out string message))
{
    if (message != null)
        throw new ArgumentException($"Bad WaitForWindow call: {message}");
    Logger.Warn("Notepad window never appeared within 5s.");
}
```

## Wait for a window to close before continuing

```csharp
window.CloseWindow(hWnd, out _);
window.WaitForWindowToClose(hWnd, timeoutMs: 5000, pollIntervalMs: 100);
```

## Wait for a window to actually become active after requesting activation

```csharp
window.ActivateWindow(hWnd, out _);
window.WaitForWindowActive(hWnd, timeoutMs: 2000, pollIntervalMs: 50);
```

## Checking why activation failed

```csharp
if (!window.ActivateWindow(hWnd, out string message))
{
    Logger.Warn($"Could not activate window: {message}");
}
```
