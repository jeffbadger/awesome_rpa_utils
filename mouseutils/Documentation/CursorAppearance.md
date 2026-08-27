# Cursor Appearance / Visibility / Confinement

Changing the system cursor, hiding it, and confining it to a region.

## `SetCursor(SystemCursorType cursor)`

**Scenario:** A long-running batch automation shows a Wait cursor so anyone
glancing at the screen understands the machine is busy and shouldn't touch it.

```csharp
mouse.SetCursor(SystemCursorType.Wait);
try
{
    RunLongBatchJob();
}
finally
{
    mouse.ResetSystemCursors();
}
```

## `ReplaceSystemCursor(SystemCursorType slotToReplace, SystemCursorType newCursor)`

**Scenario:** During a guided data-entry demo, the automation swaps the I-beam
cursor for a crosshair to make text-field targeting more visible on the
recording, while leaving the normal arrow untouched.

```csharp
mouse.ReplaceSystemCursor(SystemCursorType.IBeam, SystemCursorType.Crosshair);
try
{
    RecordDataEntryDemo();
}
finally
{
    mouse.ResetSystemCursors();
}
```

## `SetCursorFromFile(SystemCursorType slotToReplace, string filePath)`

**Scenario:** Branding an attended-automation session with the company's custom
animated cursor (`.ani`) in place of the default arrow, for the duration of the
automation's on-screen guidance.

```csharp
mouse.SetCursorFromFile(SystemCursorType.Arrow, @"C:\Branding\CompanyCursor.ani");
try
{
    RunGuidedSession();
}
finally
{
    mouse.ResetSystemCursors();
}
```

## `ResetSystemCursors()`

**Scenario:** Cleanup step that always runs at the end of an automation
(success or failure) to guarantee the operator gets their normal Windows
cursors back, even if an earlier step threw.

```csharp
try
{
    mouse.SetCursor(SystemCursorType.Wait);
    RunAutomation();
}
finally
{
    mouse.ResetSystemCursors();
}
```

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
mouse.ClipCursor(left: 0, top: 0, right: 1920, bottom: 1080);
try
{
    RunKioskDemo();
}
finally
{
    mouse.ReleaseCursorClip();
}
```

## `ReleaseCursorClip()`

**Scenario:** Restoring free multi-monitor cursor movement once the kiosk demo
above finishes, so the operator regains normal control.

```csharp
mouse.ReleaseCursorClip();
```

## `GetCursorClip()`

**Scenario:** A diagnostic step logs the current clip rectangle before changing
it, so a support engineer can see whether a prior automation run left the
cursor confined by mistake.

```csharp
System.Drawing.Rectangle clip = mouse.GetCursorClip();
Logger.Info($"Cursor currently confined to {clip}");
```
