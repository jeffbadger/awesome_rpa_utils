# Query

## Check whether an application logged an error recently

```csharp
bool found = evtLog.TryGetMostRecentEntry(
    logName: "Application",
    sourceFilter: "MyApp",
    levelFilter: "Error,Critical",
    eventIdFilter: 0,               // any event id
    messageContains: null,
    sinceIso8601: DateTime.UtcNow.AddMinutes(-10).ToString("o"),
    out string timeCreated, out int eventId, out string level,
    out string source, out string entryMessage, out string message);

if (found)
{
    // A matching error/critical entry from MyApp in the last 10 minutes.
}
else if (message == null)
{
    // No match - a normal outcome, not a failure.
}
else
{
    // The query itself failed (bad log name, access denied) - check `message`.
}
```

## Threshold check: has this error happened 3+ times today?

```csharp
evtLog.CountMatchingEntries("Application", "MyApp", "Error", 5001, null,
    DateTime.UtcNow.Date.ToString("o"), out int count, out string message);

if (count >= 3)
{
    // Escalate.
}
```

## Bulk-read matching entries as JSON (the main day-to-day method)

```csharp
evtLog.QueryRecentEntriesJson("Application", null, "Error,Warning", 0,
    "connection timed out", null, 50, out string json, out string message);

// json is a JSON array of {TimeCreatedIso8601, RecordId, EventId, Level,
// LevelDisplayName, ProviderName, LogName, MachineName, Message,
// TaskDisplayName} objects, newest first.
```

## Dump the last N entries, unfiltered

```csharp
evtLog.DumpRecentEntriesJson("System", 100, out string json, out string message);
```

## Power-user filter the scalar helpers can't express (an EventData payload field)

```csharp
// The scalar filters above cover source/level/event id/time range/message
// substring - anything else (e.g. matching a specific EventData field by
// name) needs a raw XPath query against the log's XML schema.
evtLog.QueryByXPath("Application",
    "*[System[(EventID=1000)] and EventData[Data[@Name='UserId']='12345']]",
    25, out string json, out string message);
```
