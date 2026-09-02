# Wait

## Wait for an export file to appear, then for it to finish writing

This is the flagship pairing this component exists for - prevents "the
robot opened the export before the application finished writing it"
failures:

```csharp
if (!fw.WaitForFileToExistSimple(exportPath, timeoutMs: 60000, pollIntervalMs: 250, out string message))
{
    // Either a real failure, or nothing showed up within a minute.
    return;
}

// The file exists, but the writing application may still be mid-write.
// Require 1.5 seconds of no size/last-write-time change before treating it
// as done.
if (!fw.WaitForFileStableSimple(exportPath, stableDurationMs: 1500, timeoutMs: 30000, pollIntervalMs: 250, out string stableMessage))
{
    // Still changing after 30 seconds, or a real failure - check stableMessage.
    return;
}

// Safe to open exportPath now.
```

## Wait for a file to be deleted (e.g. a lock/sentinel file another process clears)

```csharp
fw.WaitForFileToBeDeletedSimple(@"C:\Shared\processing.lock", 30000, 250, out string message);
```

## Wait for a file to change (e.g. a log file another process is appending to)

```csharp
// The file must already exist - WaitForFileToChange snapshots its current
// state and waits for a difference, it does not wait for creation too.
bool changed = fw.WaitForFileToChange(logPath, 10000, 200, out bool timedOut, out string message);
```

## Wait for a file to become unlocked

```csharp
// Note: this only detects an exclusive lock being released - it does not
// by itself guarantee the writer is done writing. Prefer WaitForFileStable
// when you specifically need "the writer finished," since some writers
// hold a shared (non-exclusive) handle open the entire time they write.
fw.WaitForFileUnlockedSimple(reportPath, 15000, 250, out string message);
```

## Check whether a file is locked right now, without waiting

```csharp
bool locked = fw.IsFileLockedSimple(path, out string message);
```

## Wait for a directory to receive a matching file

```csharp
bool found = fw.WaitForFileMatchingPattern(
    directoryPath: @"C:\Inbox",
    searchPattern: "invoice-*.csv",
    includeSubdirectories: false,
    timeoutMs: 60000,
    pollIntervalMs: 500,
    out string matchedFilePath,
    out bool timedOut,
    out string message);
```
