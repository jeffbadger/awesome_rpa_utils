# DPI & Physical Coordinates

Working correctly on high-DPI monitors, where a process's logical coordinates
can be scaled relative to true hardware pixels.

## `GetPhysicalCursorX()` / `GetPhysicalCursorY()`

**Scenario:** The automation runs on a 200%-scaled 4K monitor, but the process
hosting it is DPI-unaware. `GetX()`/`GetY()` report virtualized (scaled)
coordinates, which don't match the physical pixel positions a screenshot tool
reports. The automation uses the physical variants to correlate with a
screenshot-based OCR result.

```csharp
int physicalX = mouse.GetPhysicalCursorX();
int physicalY = mouse.GetPhysicalCursorY();
CorrelateWithScreenshotCoordinates(physicalX, physicalY);
```

## `IsProcessDpiAware()`

**Scenario:** A support engineer is diagnosing "clicks land in the wrong place
on this laptop" reports. The automation logs its DPI-awareness state at
startup so the engineer can immediately tell whether the classic DPI-virtualization
bug is in play before digging further.

```csharp
if (!mouse.IsProcessDpiAware())
{
    Logger.Warn("Process is DPI-unaware — MoveTo/ClickAt use virtualized " +
                "coordinates and may be off on scaled monitors. " +
                "Consider marking the app manifest as DPI-aware.");
}
```
