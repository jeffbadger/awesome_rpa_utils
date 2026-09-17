# Watch

## Block until a new file is created in a directory

```csharp
bool result = fw.WatchForChangeSimple(
    directoryPath: @"C:\Inbox",
    filter: "*.csv",
    changeKindsFilter: "Created",
    includeSubdirectories: false,
    timeoutMs: 60000,
    out string changedPath,
    out FileChangeKind detectedKind,
    out string message);
```

## Distinguish which kind of change occurred

```csharp
bool result = fw.WatchForChange(
    @"C:\Watched", null, null, false, 30000,
    out string changedPath, out FileChangeKind detectedKind, out bool timedOut, out string message);

if (result)
{
    switch (detectedKind)
    {
        case FileChangeKind.Created: /* new file appeared */ break;
        case FileChangeKind.Deleted: /* file removed */ break;
        case FileChangeKind.Changed: /* content/attribute/timestamp changed */ break;
        case FileChangeKind.Renamed: /* file renamed */ break;
    }
}
```

## Watch only for deletions, ignoring everything else

```csharp
// changeKindsFilter is a comma-separated list of FileChangeKind names -
// a Created or Changed event during the wait is ignored; only a matching
// Deleted event (or the timeout) ends the wait.
fw.WatchForChangeSimple(@"C:\Processing", null, "Deleted", false, 60000, out _, out _, out string message);
```

## Watch a whole directory tree

```csharp
fw.WatchForChangeSimple(@"C:\Archive", "*.pdf", null, includeSubdirectories: true, timeoutMs: 300000, out string changedPath, out FileChangeKind kind, out string message);
```

## Watch in the background instead of blocking

`WatchForChange` ties up the calling thread for the whole wait.
`StartWatching` returns immediately and reports changes via events instead
- subscribe to just the one(s) the automation cares about; an event with
no subscriber simply never fires, so there's no `changeKindsFilter`
parameter here:

```csharp
fw.Created += (sender, e) => Logger.Info($"New file: {e.FullPath}");

bool started = fw.StartWatching(@"C:\Inbox", "*.csv", includeSubdirectories: false, out string message);
if (!started)
    Logger.Error("Could not start watching: " + message);

// ... the automation goes on to do other work immediately - StartWatching
// did not block waiting for anything ...

fw.StopWatching(out _);
```

## Subscribe to multiple change kinds at once

Wire as many of `Created`/`Changed`/`Deleted`/`Renamed` as the automation
needs; each fires independently:

```csharp
fw.Created += (sender, e) => Logger.Info($"Created: {e.FullPath}");
fw.Deleted += (sender, e) => Logger.Info($"Deleted: {e.FullPath}");
fw.Renamed += (sender, e) => Logger.Info($"Renamed: {e.OldFullPath} -> {e.FullPath}");
// Changed is left unwired - the automation doesn't care about content edits here.

fw.StartWatching(@"C:\Processing", null, includeSubdirectories: true, out _);
```

## React if the watch itself fails

`WatchError` fires only when the underlying watcher fails outright (e.g.
an internal notification-buffer overflow) - the watch has already stopped
by the time it fires, so a handler that cares about staying watched needs
to restart it:

```csharp
fw.WatchError += (sender, e) =>
{
    Logger.Error("Watch failed: " + e.Message);
    fw.StartWatching(@"C:\Inbox", "*.csv", includeSubdirectories: false, out _); // restart
};
```

**Remember: these events fire on a background thread, not the automation's
own thread**, and a handler that throws is caught and logged rather than
allowed to crash the process (but every other subscriber on the same
event still runs) - see the main [README](../README.md#notes--caveats)
for both points in full.
