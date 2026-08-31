# Enumeration & Lookup

## Find a window by its title (substring match)

```csharp
IntPtr notepad = window.FindWindowByTitle("Notepad", exactMatch: false);
```

## Find a window by exact title

```csharp
IntPtr exact = window.FindWindowByTitle("Untitled - Notepad");
```

## Find the one window a process just opened

`FindFirstWindowByProcessId` is the scalar alternative to `FindWindowsByProcessId`
for the common case where the process owns (or is expected to own) exactly one
top-level window — no collection proxy or loop needed:

```csharp
var process = System.Diagnostics.Process.Start("notepad.exe");
IntPtr hWnd = window.FindFirstWindowByProcessId(process.Id);
```

## Find all windows belonging to a process

Use this instead of `FindFirstWindowByProcessId` only when the process may own
more than one top-level window and the automation genuinely needs all of them
— it returns `List<IntPtr>`, which needs a Pega collection proxy and a loop to
consume:

```csharp
var process = System.Diagnostics.Process.Start("notepad.exe");
var handles = window.FindWindowsByProcessId(process.Id);
```

## List every top-level window currently open

Also returns `List<IntPtr>` (a collection proxy + loop in Pega) — there is no
scalar alternative for "every window," since the whole point is enumerating an
unbounded set:

```csharp
foreach (var hWnd in window.GetTopLevelWindows())
{
    Console.WriteLine(window.GetWindowTitle(hWnd));
}
```

## Get the currently active window

```csharp
IntPtr active = window.GetForegroundWindow();
```

## Handle lifetime: find, then use immediately

Every handle produced here is only valid until the window closes, and no
method holds one open. Wire a lookup/wait method's output directly into the
very next steps that consume it (geometry, state, activation, wait,
child-window methods) - the same way a Pega flow connects one step's output
port to the next step's input port - rather than storing a handle across a
long-running automation step or between separate runs:

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
if (hWnd == IntPtr.Zero)
{
    Logger.Error("Settings window not found.");
    return;
}

// Use hWnd right away; don't cache it for a later step.
window.GetWindowBounds(hWnd, out System.Drawing.Rectangle bounds, out _);
window.ActivateWindow(hWnd, out _);
```
