# Wait

## Wait for another process to log a specific event after triggering an action

```csharp
// Only entries created after this call started are eligible - a
// pre-existing matching entry from before the wait began will not satisfy it.
TriggerTheOtherProcess();

bool found = evtLog.WaitForEntry(
    logName: "Application",
    sourceFilter: "OtherApp",
    levelFilter: null,               // any level
    eventIdFilter: 4500,
    messageContains: null,
    timeoutMs: 30000,
    pollIntervalMs: 500,
    out bool timedOut, out string timeCreated, out int eventId,
    out string entryMessage, out string message);

if (found)
{
    // Event 4500 showed up in time.
}
else if (timedOut)
{
    // Didn't show up within 30 seconds.
}
else
{
    // A real failure aborted the wait early - check `message`.
}
```

## Simple pass/fail wait

```csharp
bool ok = evtLog.WaitForEntrySimple("System", "Service Control Manager", "Error",
    0, null, 60000, 1000, out string message);
```
