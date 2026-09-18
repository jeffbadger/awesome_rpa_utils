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

## Find a window whose title or class name changes from run to run

A claims viewer titles its window `Claim 4471 - Review`, and the number is
different every time, so an exact or substring match can't find it. A regular
expression can:

```csharp
if (window.TryFindWindowByRegex(@"^Claim \d+ - Review$", null, out IntPtr hWnd, out string message))
{
    window.ActivateWindow(hWnd, out _);
}
else if (message != null)
{
    Logger.Error($"Could not search: {message}");   // no pattern, or an invalid one
}
else
{
    Logger.Info("The claim window isn't open yet.");  // searched fine, nothing matched
}
```

Class names are the other reason to reach for it. WinForms generates class
names such as `WindowsForms10.Window.8.app.0.141b42a_r14_ad1` whose suffix is
different on every run, so match the stable prefix instead. Pass both patterns
to require both to match:

```csharp
window.TryFindWindowByRegex(
    titlePattern: "Payment Entry",
    classNamePattern: @"^WindowsForms10\.Window\.8\.app\.0\.",
    out IntPtr hWnd, out _);
```

A pattern matches if it is found anywhere in the text, so add `^` and `$` for a
whole-string match. Matching is case-insensitive unless you pass
`ignoreCase: false`. Like `FindWindowByTitle`, it also considers hidden windows
and returns the first match in top-to-bottom order; if a hidden window could
match ahead of the one you want, check `IsWindowVisible` on the result or use
`EnumerateWindowsJson` below.

Each match is capped at one second, so a badly written pattern (nested
quantifiers like `(a+)+`) returns `false` with a message rather than hanging
the automation.

## See every window on screen as JSON

`EnumerateWindowsJson` is the no-proxy way to list windows: it returns one JSON
array, so there is no `List<IntPtr>` collection proxy or loop to configure. It
is most useful for diagnostics - logging what was actually on screen when a step
failed:

```csharp
if (window.EnumerateWindowsJson(out string json, out string message))
{
    Logger.Info($"Windows at time of failure: {json}");
}
```

Each entry looks like this:

```json
{"Handle":197066,"Title":"Claim 4471 - Review","ClassName":"WindowsForms10.Window.8.app.0.141b42a_r14_ad1",
 "ProcessId":4321,"IsVisible":true,"IsEnabled":true,"State":"Normal",
 "Left":120,"Top":80,"Width":1024,"Height":768}
```

Windows are listed topmost first. Only visible windows are listed by default
(`visibleOnly: false` adds the hundreds of hidden helper windows), and
`processId` narrows the list to one process, for example the one just launched:

```csharp
window.EnumerateWindowsJson(out string json, out _, visibleOnly: true, processId: process.Id);
```

`Handle` is a plain number that fits in 32 bits, so it converts back to an
`IntPtr` for the other methods. Pull individual values out with `JsonUtils`
(for example `TryGetIntValue` on a path like `$[0].Handle`). A minimized window
reports its position as about -32000, so check `State` before trusting
`Left`/`Top`.

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
window.GetWindowBoundsAsRectangle(hWnd, out System.Drawing.Rectangle bounds, out _);
window.ActivateWindow(hWnd, out _);
```
