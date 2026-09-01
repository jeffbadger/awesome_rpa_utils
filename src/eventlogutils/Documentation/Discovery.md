# Discovery

## Check a log exists before querying it

```csharp
if (!evtLog.DoesLogExistSimple("Application", out string msg))
{
    // msg explains why - doesn't exist, or the check itself failed.
}
```

## Register a source for a bot's own audit entries, then write to it

```csharp
// Requires the calling process to be elevated (administrator rights) - this
// is a one-time, machine-wide registration, safe to call on every bot
// startup since it's idempotent for the same source+log pair.
if (evtLog.CreateEventSourceSimple("MyRpaBot", "Application", out string createMsg) || createMsg == null)
{
    evtLog.WriteEntrySimple("MyRpaBot", "Bot started successfully.", out _);
}
```

## Find out which log an existing source already writes to

```csharp
// Useful before calling CreateEventSource, since a source can only ever be
// registered to one log - reassigning it to a different log is a hard failure.
if (evtLog.TryGetLogNameForSource("MyRpaBot", out string logName, out string msg))
{
    Console.WriteLine($"MyRpaBot already writes to '{logName}'.");
}
```

## List every log on the machine

```csharp
List<string> logs = evtLog.ListLogNames();
string logsCsv = evtLog.ListLogNamesDelimited(); // for designers without a List<string> proxy
```

## Check existence without interpreting `message`

```csharp
// querySucceeded separates "the query completed" from the existence answer,
// so no null-message test is needed to tell a genuine negative apart from a failure.
if (!evtLog.DoesLogExist("Application", out bool querySucceeded, out string msg))
{
    if (!querySucceeded)
        Logger.Error($"Could not check whether the Application log exists: {msg}");
}
```
