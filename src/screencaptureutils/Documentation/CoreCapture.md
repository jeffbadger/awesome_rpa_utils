# Core Capture

Getting pixels onto disk or the clipboard — the starting point for every
audit trail, visual verification, and annotated screenshot this component
produces. All coordinates here are **absolute screen pixels**, consistent
with MouseUtils.

Every capture target follows the same shape: a base `Capture*` method that
returns an in-memory `Bitmap`, plus `*ToFile`/`*ToClipboard` wrappers built
on top of it. Most automations only ever call the `*ToFile`/`*ToClipboard`
wrappers shown below; the base methods exist for the less common case where
the automation needs the image itself. See
[Getting the image directly](#getting-the-image-directly) at the bottom of
this page for that case, including who owns disposing the `Bitmap`.

## `CaptureAllScreensToFile(string filePath)`

**Scenario:** A run-level "what did the desktop look like" snapshot at the
start of an automation, before anything has been clicked.

```csharp
screenCapture.CaptureAllScreensToFile(@"C:\evidence\run_start.png", out string message);
```

## `CaptureRegionToFile(int left, int top, int width, int height, string filePath)`

**Scenario:** Only the toolbar of an application needs to be captured for a
targeted bug report, not the whole desktop.

```csharp
screenCapture.CaptureRegionToFile(0, 0, 1920, 80, @"C:\evidence\toolbar.png", out string message);
```

A region that lies entirely off the virtual screen (e.g. a coordinate typo,
or a monitor that got disconnected since the automation was authored) fails
with a message instead of silently writing a black rectangle — check
`message` rather than assuming the file is a real capture.

## `CaptureWindowToFile(IntPtr hWnd, string filePath)`

**Scenario:** Capture a specific application window as evidence, even if
another window is on top of it — the most common capture call in an RPA
script, since `hWnd` almost always comes from a prior `WindowUtils`/
`DialogUtils` lookup rather than being typed in.

```csharp
IntPtr hWnd = windowUtils.FindWindowByTitle("Order Entry", exactMatch: true);
if (hWnd == IntPtr.Zero)
{
    Logger.Error("Order Entry window not found.");
    return;
}

screenCapture.CaptureWindowToFile(hWnd, @"C:\evidence\order_entry.png", out string message);
```

A minimized window is rejected up front with a message rather than captured
— `PrintWindow` on a minimized window is unreliable across Windows versions
and can return a stale or solid-black bitmap without failing, which would
otherwise pass as valid evidence. Neither `WindowUtils` nor this component
currently exposes a "restore from minimized" call, so design the automation
to avoid minimizing the target window before evidence capture, or check
`message` for `"minimized"` and log/skip the capture instead of treating a
`false` return as an unconditional error:

```csharp
if (!screenCapture.CaptureWindowToFile(hWnd, @"C:\evidence\order_entry.png", out string message))
{
    if (message.Contains("minimized"))
        Logger.Warn("Skipped evidence capture: window was minimized.");
    else
        Logger.Error("Evidence capture failed: " + message);
}
```

## `CaptureActiveWindowToFile(string filePath)`

**Scenario:** An exception handler wants "whatever was on top when this
failed" without first having to look up a handle.

```csharp
try
{
    // ... robot steps ...
}
catch (Exception ex)
{
    screenCapture.CaptureActiveWindowToFile(@"C:\evidence\failure_context.png", out _);
    throw;
}
```

## `CaptureAroundPointToFile(int x, int y, int width, int height, string filePath)`

**Scenario:** After a click, capture a small crop around exactly where the
robot clicked — pairs naturally with MouseUtils' cursor-position methods, and
is a cheaper alternative to a full window capture when only the clicked
control matters.

```csharp
mouse.LeftClickAt(saveButtonX, saveButtonY, out _);
mouse.GetX(out int x, out _);
mouse.GetY(out int y, out _);
screenCapture.CaptureAroundPointToFile(x, y, 200, 100, @"C:\evidence\click_target.png", out string message);
```

## `CaptureAllScreensToClipboard()`

**Scenario:** A support ticket needs a screenshot pasted directly into the
ticket body, rather than attached as a file.

```csharp
if (screenCapture.CaptureAllScreensToClipboard(out string message))
    Logger.Info("Screenshot copied to clipboard — paste it into the ticket.");
else
    Logger.Warn("Could not copy to clipboard: " + message);
```

This runs its own STA thread internally, so it works the same way whether
the calling automation's thread is STA or MTA — nothing extra to configure.
Every capture target has a `*ToClipboard` counterpart built the same way —
`CaptureRegionToClipboard`, `CaptureWindowToClipboard`,
`CaptureActiveWindowToClipboard`, `CaptureAroundPointToClipboard` — for
copying just a region, a specific window, etc. instead of the whole screen.

## `CaptureScreenToFile(int screenIndex, string filePath)`

**Scenario:** A multi-monitor workstation runs one application per screen,
and evidence for "what the second monitor showed" needs to be its own file
rather than a slice cropped out of a combined all-monitors screenshot.

```csharp
screenCapture.GetScreenCount(out int screenCount, out _);
for (int i = 0; i < screenCount; i++)
{
    screenCapture.CaptureScreenToFile(i, $@"C:\evidence\monitor_{i}.png", out string message);
}
```

`screenIndex` is a zero-based index into however many screens Windows
reports — it does **not** necessarily correspond to physical left-to-right
position, so don't assume index `0` is "the left monitor." Call
`GetScreenCount` first and treat an out-of-range index as a real failure
(`message` explains it) rather than assuming every session has the same
number of monitors the automation was authored on:

```csharp
int screenIndex = 1; // e.g. "the second monitor", supplied by the automation
screenCapture.GetScreenCount(out int screenCount, out _);
if (screenIndex >= screenCount)
{
    Logger.Warn($"Expected monitor {screenIndex}, but this session only has {screenCount}.");
    return;
}
screenCapture.CaptureScreenToFile(screenIndex, @"C:\evidence\target_monitor.png", out string message);
```

`CaptureAllScreensToFile`/`CaptureAllScreens`/`CaptureAllScreensToClipboard`
are unchanged by this — they still capture the entire virtual screen (every
monitor combined) exactly as before. `CaptureScreen(int, ...)` and
`CaptureScreenToClipboard(int, ...)` follow the same base/`ToFile`/
`ToClipboard` shape as every other capture target in this component.

## `CaptureStepEvidence(string stepName, string folderPath)`

**Scenario:** The most common RPA evidence pattern — one screenshot per
logical step, auto-numbered and auto-timestamped so a compliance reviewer
can replay the run in order just by sorting the folder.

```csharp
screenCapture.CaptureStepEvidence("LoggedIn", @"C:\evidence\run_20260917", out string path1, out _);
// ... do work ...
screenCapture.CaptureStepEvidence("OrderSubmitted", @"C:\evidence\run_20260917", out string path2, out _);
// Produces, e.g.: 001_LoggedIn_20260917_090512.png, 002_OrderSubmitted_20260917_090533.png
```

Create one `ScreenCaptureUtils` instance per run (the usual case when it's
dropped onto a Pega Robot Studio automation) so the counter starts at `001`
each time; reusing an instance across runs continues the same sequence.

## Getting the image directly

**Scenario:** An automation wants to compute its own statistic over a
screenshot (e.g. an average-brightness check, or a custom color-matching
rule beyond what `GetRegionHash`/`CompareRegionToBaseline` offer) without
writing a temporary file just to read it back.

Every `*ToFile`/`*ToClipboard` method is a thin wrapper over a base
`Capture*` method that returns the `Bitmap` itself — call the base method
directly for this case. **Unlike every other method in this component, the
caller now owns the returned image and must dispose it:**

```csharp
if (!screenCapture.CaptureRegion(0, 0, 200, 50, out Bitmap region, out string message))
{
    Logger.Error("Capture failed: " + message);
    return;
}

using (region)
{
    // Inspect the pixels directly, e.g.:
    System.Drawing.Color topLeft = region.GetPixel(0, 0);
    Logger.Info($"Top-left pixel: 0x{topLeft.ToArgb():X8}");
}
```

The base methods available this way are `CaptureAllScreens`, `CaptureScreen`
(single monitor by index), `CaptureRegion`, `CaptureWindow`,
`CaptureActiveWindow`, and `CaptureAroundPoint` — one per capture target,
same as the `*ToFile`/`*ToClipboard` wrappers above.
