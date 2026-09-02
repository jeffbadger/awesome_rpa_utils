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
