# Write

## Write a simple audit entry from a bot

```csharp
// The source must already be registered - see Discovery.md's
// CreateEventSourceSimple example. This does not require elevation itself,
// only the one-time source registration does.
evtLog.WriteEntrySimple("MyRpaBot", "Invoice batch 4821 processed successfully.", out string message);
```

## Write with an explicit level and event id

```csharp
evtLog.WriteEntry("MyRpaBot", "Invoice batch 4821 failed validation.",
    EventLogLevel.Error, eventId: 2001, out string message);
```

## Detect a truncated message

```csharp
// WriteEntry/WriteEntrySimple still succeed for an over-length message, but
// truncate it first - errorMessage reports the truncation even on success.
bool wrote = evtLog.WriteEntrySimple("MyRpaBot", veryLongDiagnosticDump, out string errorMessage);
if (wrote && errorMessage != null)
{
    // Truncation note - the entry was written, but shortened.
}
```
