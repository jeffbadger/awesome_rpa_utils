# Verification & Synchronization

Confirming an action actually had an effect, and waiting for the application
to finish processing — the pieces most RPA scripts are missing when they fall
back to guessing with fixed `Thread.Sleep` calls.

None of these methods throw. `GetPixelColor` and `GetCurrentCursorType` return
`bool` (success) with an `out` value and an `out string message`. The other four keep their original
`bool` meaning (matched/idle vs. not) and add an `out string message` that is
only set if `timeoutMs` was invalid or a Win32 failure aborted the poll early
— check whether `message` is non-null to tell "genuinely timed out" apart
from "aborted by an error". `timeoutMs` must be zero or positive, and is
capped at 30 minutes - a genuine external wait can legitimately take that
long, but bad wiring (e.g. a units mistake) still shouldn't block the
automation thread indefinitely.

## `GetPixelColor(int x, int y)`

**Scenario:** After clicking a "Save" button, the automation wants a cheap way
to confirm the button's icon changed from gray (disabled) to blue (enabled)
before moving on, without a full OCR/image-recognition pipeline.

```csharp
mouse.GetPixelColor(x: 720, y: 60, out int color, out _);
Logger.Info($"Save button pixel color: 0x{color:X6}");
```

## `WaitForPixelColor(int x, int y, int expectedColorRef, int timeoutMs, int pollIntervalMs)`

**Scenario:** A status indicator dot turns from red to green once a
background import finishes. Instead of guessing how long the import takes,
the automation polls the indicator's pixel until it turns green.

```csharp
const int Green = 0x00FF00; // 0x00BBGGRR
bool finished = mouse.WaitForPixelColor(x: 950, y: 40, expectedColorRef: Green, timeoutMs: 30000, pollIntervalMs: 250, out string message);
if (!finished)
{
    if (message != null)
        throw new InvalidOperationException($"Pixel poll aborted: {message}");
    throw new TimeoutException("Import did not complete within 30 seconds.");
}
```

## `WaitForPixelChange(int x, int y, int timeoutMs, int pollIntervalMs)`

**Scenario:** Clicking a "Refresh" button should update a chart, but the
automation doesn't know or care what the chart's new color will look like —
it only needs to know *something* changed at a sample point inside the chart
before it proceeds to read the new data.

```csharp
mouse.LeftClickAt(refreshButtonX, refreshButtonY, out _);
bool changed = mouse.WaitForPixelChange(x: 500, y: 300, timeoutMs: 10000, pollIntervalMs: 200, out _);
if (!changed)
    Logger.Warn("Chart did not appear to redraw after refresh.");
```

## `IsBusyCursorActive()`

**Scenario:** A diagnostic step logs whether the target application currently
appears to be processing (showing the hourglass), to help a support engineer
understand why a later step is timing out.

```csharp
if (mouse.IsBusyCursorActive(out _))
    Logger.Info("Application currently shows a busy cursor.");
```

## `GetCurrentCursorType()`

**Scenario:** A legacy order-entry screen turns its labels into links only once
the record has loaded. Before clicking, the automation moves the pointer over
the "Customer" label and confirms the cursor became a hand - a cheap check that
the label really is clickable now, without OCR or a pixel guess.

```csharp
mouse.MoveTo(labelX, labelY, out _);
Thread.Sleep(150);   // let the cursor settle after the move

if (mouse.GetCurrentCursorType(out CurrentCursorType cursor, out string message))
{
    if (cursor == CurrentCursorType.Hand)
        mouse.LeftClick(out _);
    else
        Logger.Warn($"Label is not a link yet (cursor is {cursor}).");
}
else
{
    Logger.Error($"Could not read the cursor: {message}");
}
```

The same check works to confirm a text field is editable (`IBeam`), that a
drop target refuses a drop (`No`), or that a splitter can be dragged
(`SizeWestEast`/`SizeNorthSouth`). `IsBusyCursorActive` is the special case for
`Wait`/`AppStarting`.

Two results mean "nothing to match against" rather than "the wrong cursor":
`Hidden` (no cursor is showing over that window) and `Unknown` (an application
drew its own cursor image; many browsers and games do, even for shapes that look
standard). Treat a match as reliable and a mismatch as "not sure", and keep a
fallback. A slot with a replaced image is still reported by its own name, so a
crosshair installed into the arrow slot with `SetCursor` reads as `Arrow`.

## `WaitForIdleCursor(int timeoutMs, int pollIntervalMs)`

**Scenario:** After clicking "Generate Report" in a legacy Win32 application
that shows the hourglass cursor while it works and reverts to the normal
arrow when done, the automation waits for that cursor to clear instead of
sleeping for a guessed duration — reacting to fast reports quickly and still
tolerating slow ones up to the timeout.

```csharp
mouse.LeftClickAt(generateReportX, generateReportY, out _);
bool finishedInTime = mouse.WaitForIdleCursor(timeoutMs: 60000, pollIntervalMs: 500, out _);
if (!finishedInTime)
    throw new TimeoutException("Report generation did not finish within 60 seconds.");

mouse.ClickAt(exportButtonX, exportButtonY, MouseButton.Left, out _);
```
