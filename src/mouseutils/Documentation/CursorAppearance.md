# Cursor Appearance / Visibility / Confinement

Changing the system cursor, hiding it, and confining it to a region.

Every method here that could previously fail (all except `HideCursor`,
`ShowCursor`, `IsCursorVisible`) returns `bool` (success) with an
`out string message` explaining why on failure — none of them throw. The
examples below discard `message` via `out _` where the failure reason isn't
needed.

## `SetCursor(SystemCursorType cursor)`

**Scenario:** A long-running batch automation shows a Wait cursor so anyone
glancing at the screen understands the machine is busy and shouldn't touch it.

```csharp
mouse.SetCursor(SystemCursorType.Wait, out _);
try
{
    RunLongBatchJob();
}
finally
{
    mouse.ResetSystemCursors(out _);
}
```

## `ReplaceSystemCursor(SystemCursorType slotToReplace, SystemCursorType newCursor)`

**Scenario:** During a guided data-entry demo, the automation swaps the I-beam
cursor for a crosshair to make text-field targeting more visible on the
recording, while leaving the normal arrow untouched.

```csharp
mouse.ReplaceSystemCursor(SystemCursorType.IBeam, SystemCursorType.Crosshair, out _);
try
{
    RecordDataEntryDemo();
}
finally
{
    mouse.ResetSystemCursors(out _);
}
```

If this instance can't capture the slot's current cursor before replacing
it (a `LoadCursor`/`CopyIcon` failure - rare, but possible under GDI resource
exhaustion), the replacement itself now fails rather than proceeding with
nothing recorded to restore later. Disposing the component without calling
`ResetSystemCursors` restores each slot it touched as a backstop, but that
restore only actually applies if the slot's active cursor still matches what
this instance itself last installed there - the same narrowing (not
eliminating) ownership check `ClipCursor`/`ReleaseCursorClip` use, described
further down.

## `SetCursorFromFile(SystemCursorType slotToReplace, string filePath)`

**Scenario:** Branding an attended-automation session with the company's custom
animated cursor (`.ani`) in place of the default arrow, for the duration of the
automation's on-screen guidance.

```csharp
if (!mouse.SetCursorFromFile(SystemCursorType.Arrow, @"C:\Branding\CompanyCursor.ani", out string message))
{
    Logger.Warn($"Could not apply branded cursor: {message}");
}
try
{
    RunGuidedSession();
}
finally
{
    mouse.ResetSystemCursors(out _);
}
```

## `ResetSystemCursors()`

**Scenario:** Cleanup step that always runs at the end of an automation
(success or failure) to guarantee the operator gets their normal Windows
cursors back, even if an earlier step failed.

```csharp
try
{
    mouse.SetCursor(SystemCursorType.Wait, out _);
    RunAutomation();
}
finally
{
    mouse.ResetSystemCursors(out _);
}
```

Disposing the component also restores each individual slot it touched (to
whatever was there immediately before, not necessarily the Windows default)
as a backstop, but that should not be relied on instead of calling
`ResetSystemCursors` yourself.

## `HideCursor()` / `ShowCursor()` / `IsCursorVisible()`

**Scenario:** A kiosk application hides the mouse cursor entirely while an
idle-screen video plays, then restores it as soon as the attract-mode video
ends and the touchscreen becomes interactive again.

```csharp
mouse.HideCursor();
PlayAttractLoop();
if (!mouse.IsCursorVisible())
{
    mouse.ShowCursor();
}
```

## `ClipCursor(int left, int top, int right, int bottom)`

**Scenario:** During an unattended kiosk demo, the cursor must stay on the
primary display and never wander onto a secondary monitor that shows
back-office diagnostics.

```csharp
mouse.ClipCursor(left: 0, top: 0, right: 1920, bottom: 1080, out _);
try
{
    RunKioskDemo();
}
finally
{
    mouse.ReleaseCursorClip(out _);
}
```

## `ReleaseCursorClip()`

**Scenario:** Restoring free multi-monitor cursor movement once the kiosk demo
above finishes, so the operator regains normal control.

```csharp
mouse.ReleaseCursorClip(out _);
```

This restores the exact clip (or lack of one) that was in effect immediately
before the matching `ClipCursor` call, rather than always clearing to "no
clip". It also checks, right before restoring, whether the active clip still
matches what this instance itself last applied - if another app or another
`ClipCursor` call has since taken over the clip, this leaves that newer clip
alone instead of overwriting it with a now-stale rectangle. That check is not
a hard guarantee: it only covers the instant this method runs, so a change
that happens in the brief window between the check and the underlying Win32
call can still be overwritten. Disposing the component also restores that
same saved clip, with the same check, as a backstop if `ReleaseCursorClip`
was never called.

## `GetCursorClipAsRectangle()`

**Scenario:** A diagnostic step logs the current clip rectangle before changing
it, so a support engineer can see whether a prior automation run left the
cursor confined by mistake.

```csharp
mouse.GetCursorClipAsRectangle(out System.Drawing.Rectangle clip, out _);
Logger.Info($"Cursor currently confined to {clip}");
```

A `bool GetCursorClip(out int left, out int top, out int width, out int height, out string message)`
scalar overload is also available for designers without a `Rectangle` proxy.

## Checking why a cursor change failed

```csharp
if (!mouse.SetCursor(SystemCursorType.Wait, out string message))
{
    Logger.Warn($"Could not set cursor: {message}");
}
```
