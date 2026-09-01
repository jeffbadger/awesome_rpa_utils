# Export

## Archive a filtered slice of a log before it rolls over

```csharp
bool exported = evtLog.ExportFilteredLog(
    "Application",
    "*[System[(Level=1 or Level=2)]]",   // Critical or Error
    @"C:\Evidence\app-errors-2026-08-31.evtx",
    out string message);
```

## Query an exported file the same way you'd query a live log

```csharp
evtLog.QueryExportedLog(@"C:\Evidence\app-errors-2026-08-31.evtx", "*", 100,
    out string json, out string message);

// A missing or corrupt .evtx file returns False with a non-null message,
// distinct from a live log's "log not found" case.
```
