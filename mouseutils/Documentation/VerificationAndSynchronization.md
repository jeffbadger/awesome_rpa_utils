# Verification & Synchronization

Confirming an action actually had an effect, and waiting for the application
to finish processing — the pieces most RPA scripts are missing when they fall
back to guessing with fixed `Thread.Sleep` calls.

## `GetPixelColor(int x, int y)`

**Scenario:** After clicking a "Save" button, the automation wants a cheap way
to confirm the button's icon changed from gray (disabled) to blue (enabled)
before moving on, without a full OCR/image-recognition pipeline.

```csharp
int color = mouse.GetPixelColor(x: 720, y: 60);
Logger.Info($"Save button pixel color: 0x{color:X6}");
```

## `WaitForPixelColor(int x, int y, int expectedColorRef, int timeoutMs, int pollIntervalMs)`

**Scenario:** A status indicator dot turns from red to green once a
background import finishes. Instead of guessing how long the import takes,
the automation polls the indicator's pixel until it turns green.

```csharp
const int Green = 0x00FF00; // 0x00BBGGRR
bool finished = mouse.WaitForPixelColor(x: 950, y: 40, expectedColorRef: Green, timeoutMs: 30000, pollIntervalMs: 250);
if (!finished)
    throw new TimeoutException("Import did not complete within 30 seconds.");
```

## `WaitForPixelChange(int x, int y, int timeoutMs, int pollIntervalMs)`

**Scenario:** Clicking a "Refresh" button should update a chart, but the
automation doesn't know or care what the chart's new color will look like —
it only needs to know *something* changed at a sample point inside the chart
before it proceeds to read the new data.

```csharp
mouse.LeftClickAt(refreshButtonX, refreshButtonY);
bool changed = mouse.WaitForPixelChange(x: 500, y: 300, timeoutMs: 10000, pollIntervalMs: 200);
if (!changed)
    Logger.Warn("Chart did not appear to redraw after refresh.");
```

## `IsBusyCursorActive()`

**Scenario:** A diagnostic step logs whether the target application currently
appears to be processing (showing the hourglass), to help a support engineer
understand why a later step is timing out.

```csharp
if (mouse.IsBusyCursorActive())
    Logger.Info("Application currently shows a busy cursor.");
```

## `WaitForIdleCursor(int timeoutMs, int pollIntervalMs)`

**Scenario:** After clicking "Generate Report" in a legacy Win32 application
that shows the hourglass cursor while it works and reverts to the normal
arrow when done, the automation waits for that cursor to clear instead of
sleeping for a guessed duration — reacting to fast reports quickly and still
tolerating slow ones up to the timeout.

```csharp
mouse.LeftClickAt(generateReportX, generateReportY);
bool finishedInTime = mouse.WaitForIdleCursor(timeoutMs: 60000, pollIntervalMs: 500);
if (!finishedInTime)
    throw new TimeoutException("Report generation did not finish within 60 seconds.");

mouse.ClickAt(exportButtonX, exportButtonY, MouseButton.Left);
```
