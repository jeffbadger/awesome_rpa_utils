# Activation & Z-Order

## Bring a window to the front before interacting with it

```csharp
window.ActivateWindow(hWnd);
```

## Pin a window on top of everything else

```csharp
window.SetAlwaysOnTop(hWnd, alwaysOnTop: true);
// ... later ...
window.SetAlwaysOnTop(hWnd, alwaysOnTop: false);
```

## Wait for an application to launch and its window to appear

```csharp
System.Diagnostics.Process.Start("notepad.exe");
if (window.WaitForWindow("Notepad", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd))
{
    window.ActivateWindow(hWnd);
}
```

## Wait for a window to close before continuing

```csharp
window.CloseWindow(hWnd);
window.WaitForWindowToClose(hWnd, timeoutMs: 5000, pollIntervalMs: 100);
```

## Wait for a window to actually become active after requesting activation

```csharp
window.ActivateWindow(hWnd);
window.WaitForWindowActive(hWnd, timeoutMs: 2000, pollIntervalMs: 50);
```
