# FileWatchAutomation

A Pega Robot Studio-ready component (`FileWatchUtils`) for coordinating with
files produced by other applications — a different problem than Pega's own
built-in file manipulation. Like every component in this suite, it honors
the never-throws contract: invalid input and runtime failures return
`False` with a descriptive message instead of throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `FileWatchAutomation`
- Assembly: `FileWatchAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

**Unlike every other component in this suite**, every operation here is
plain cross-platform BCL (`System.IO`, `System.Security.Cryptography`,
`FileSystemWatcher`) with zero P/Invoke — the `-windows` target framework is
kept only for consistency with the rest of the suite, not because any
method needs a Windows-only API. This also means its `.Tests` project gets
real, full functional coverage on Linux (real files, real locks, real
`FileSystemWatcher` events, real hashing), not just guard-clause coverage.

## Types

### `FileChangeKind`
The kind of filesystem change `WatchForChange` detected: `Created`,
`Renamed`, `Changed`, `Deleted`. A single-value output counterpart to the
combinable `changeKindsFilter` CSV input those methods accept. Also used
by `FileWatchChangeEventArgs.Kind` (see [Events](#events)).

### `FileMetadata`
The JSON shape produced by `GetFileMetadataJson`/`GetDirectoryListingJson`:
`FullPath`, `Name`, `Extension`, `SizeBytes`, `IsDirectory`,
`CreatedUtcIso8601`, `LastWriteUtcIso8601`.

### `FileWatchChangeEventArgs`, `FileWatchRenamedEventArgs`, `FileWatchErrorEventArgs`
Event-args types for [Events](#events)'s `Created`/`Changed`/`Deleted`,
`Renamed`, and `WatchError` respectively - plain scalar properties
(`FullPath`, `Kind`, `OldFullPath`, `Message`) rather than the BCL's
`System.IO.FileSystemEventArgs`/`RenamedEventArgs`.

## Constructors

| Constructor | Description |
|---|---|
| `FileWatchUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `FileWatchUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Wait — Existence & Change

| Method | Signature | Description |
|---|---|---|
| `WaitForFileToExist` | `bool WaitForFileToExist(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until a file appears, or the timeout elapses. |
| `WaitForFileToExistSimple` | `bool WaitForFileToExistSimple(string path, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |
| `WaitForFileToBeDeleted` | `bool WaitForFileToBeDeleted(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until a file is deleted, or the timeout elapses. |
| `WaitForFileToBeDeletedSimple` | `bool WaitForFileToBeDeletedSimple(string path, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |
| `WaitForFileToChange` | `bool WaitForFileToChange(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Snapshots size/last-write time at call time, polls until either differs (deletion counts as a change too). The file must already exist. |
| `WaitForFileToChangeSimple` | `bool WaitForFileToChangeSimple(string path, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |

### Wait — Stability (flagship)

| Method | Signature | Description |
|---|---|---|
| `WaitForFileStable` | `bool WaitForFileStable(string path, int stableDurationMs, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until a file's size and last-write time are unchanged for a *continuous* `stableDurationMs` window, or the timeout elapses. Prevents "the robot opened the export before the application finished writing it" failures. The file must already exist - see `WaitForFileToExist` first if it may not have been created yet. |
| `WaitForFileStableSimple` | `bool WaitForFileStableSimple(string path, int stableDurationMs, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |

### Wait — Lock

| Method | Signature | Description |
|---|---|---|
| `IsFileLockedSimple` | `bool IsFileLockedSimple(string path, out string message)` | Returns True if the file cannot currently be opened for exclusive read access. |
| `IsFileLocked` | `bool IsFileLocked(string path, out bool querySucceeded, out string message)` | Same, plus a `querySucceeded` output. |
| `WaitForFileUnlocked` | `bool WaitForFileUnlocked(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until a file is no longer locked, or the timeout elapses. |
| `WaitForFileUnlockedSimple` | `bool WaitForFileUnlockedSimple(string path, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |

### Wait — Directory Pattern

| Method | Signature | Description |
|---|---|---|
| `WaitForFileMatchingPattern` | `bool WaitForFileMatchingPattern(string directoryPath, string searchPattern, bool includeSubdirectories, int timeoutMs, int pollIntervalMs, out string matchedFilePath, out bool timedOut, out string message)` | Polls a directory until it contains a file matching a pattern, or the timeout elapses. |
| `WaitForFileMatchingPatternSimple` | `bool WaitForFileMatchingPatternSimple(string directoryPath, string searchPattern, bool includeSubdirectories, int timeoutMs, int pollIntervalMs, out string matchedFilePath, out string message)` | Same, without the `timedOut` output. |

### Watch

| Method | Signature | Description |
|---|---|---|
| `WatchForChange` | `bool WatchForChange(string directoryPath, string filter, string changeKindsFilter, bool includeSubdirectories, int timeoutMs, out string changedPath, out FileChangeKind detectedKind, out bool timedOut, out string message)` | Blocks until a matching created/renamed/changed/deleted event occurs, via the OS's native change-notification API rather than polling. The directory must already exist. |
| `WatchForChangeSimple` | `bool WatchForChangeSimple(string directoryPath, string filter, string changeKindsFilter, bool includeSubdirectories, int timeoutMs, out string changedPath, out FileChangeKind detectedKind, out string message)` | Same, without the `timedOut` output. |

### Watch - Background (non-blocking)

Starts a watch that runs in the background instead of blocking the calling
thread, and reports changes via events instead of an output parameter.
Subscribe to whichever of `Created`/`Changed`/`Deleted`/`Renamed` the
automation cares about; an event with no subscriber simply never fires, so
there's no separate "which kinds" filter parameter here (contrast
`WatchForChange`'s `changeKindsFilter`).

| Method | Signature | Description |
|---|---|---|
| `StartWatching` | `bool StartWatching(string directoryPath, string filter, bool includeSubdirectories, out string message)` | Starts watching a directory in the background, without blocking. Fails if a watch is already running (call `StopWatching` first), or the directory is invalid. |
| `StopWatching` | `bool StopWatching(out string message)` | Stops and disposes the background watcher. Fails if no watch is currently running. |
| `IsWatching` | `bool IsWatching()` | Reports whether a background watch is currently running. A plain Boolean query with no failure mode - no `out string message`, matching `WindowUtils.IsWindowVisible`'s shape. |

### Actions

| Method | Signature | Description |
|---|---|---|
| `AtomicMoveFile` | `bool AtomicMoveFile(string sourcePath, string destinationPath, bool overwrite, out string message)` | Moves a file, optionally overwriting an existing destination. Atomic only when source and destination share a volume. |
| `ReplaceFile` | `bool ReplaceFile(string sourcePath, string destinationPath, string backupPath, out string message)` | Replaces an existing destination's contents, optionally keeping a backup. Requires the destination to already exist. Windows-only behavior. |
| `ClaimFile` | `bool ClaimFile(string sourcePath, string inProgressDirectoryPath, out string claimedPath, out string message)` | Claims a work file by copying it into an in-progress directory under a source-scoped exclusive lock, then deleting the source. A collision with another claimant of the same source, or with an unrelated file already at the destination name, is reported distinctly - the one method here safe under real multi-robot concurrency. |

### Hash

| Method | Signature | Description |
|---|---|---|
| `ComputeFileHashSha256` | `bool ComputeFileHashSha256(string path, out string hashHex, out string message)` | Computes a file's SHA-256 hash as a lowercase hex string. |
| `ComputeFileHash` | `bool ComputeFileHash(string path, string algorithmName, out string hashHex, out string message)` | Same, using a named algorithm (`SHA256`, `SHA1`, `MD5`, `SHA384`, `SHA512`) - a power-user escape hatch for legacy-system interop. |
| `AreFilesIdenticalByHash` | `bool AreFilesIdenticalByHash(string pathA, string pathB, out bool identical, out string message)` | Returns True if two files have identical SHA-256 hashes - the direct "detect duplicate/unchanged inputs" convenience. |

### Metadata

| Method | Signature | Description |
|---|---|---|
| `TryGetFileMetadata` | `bool TryGetFileMetadata(string path, out long sizeBytes, out string lastWriteUtcIso8601, out string createdUtcIso8601, out string message)` | Gets a file's size and timestamps as scalar outputs. |
| `GetFileMetadataJson` | `bool GetFileMetadataJson(string path, out string json, out string message)` | Gets a file or directory's metadata as a JSON object (includes `IsDirectory`/`Extension`). |
| `GetDirectoryListingJson` | `bool GetDirectoryListingJson(string directoryPath, string searchPattern, bool includeSubdirectories, out string json, out string message)` | Lists files in a directory matching a pattern as a JSON array of metadata. Pairs naturally with `WaitForFileMatchingPattern`. |

## Events

Raised only while a background watch started by `StartWatching` is
running. Subscribe to whichever of `Created`/`Changed`/`Deleted`/`Renamed`
the automation cares about; an event with no subscriber simply never
fires, so there's no separate "which kinds" filter to configure (contrast
`WatchForChange`'s `changeKindsFilter` parameter).

| Event | Type | Description |
|---|---|---|
| `Created` | `EventHandler<FileWatchChangeEventArgs>` | Raised when a file/directory is created. |
| `Changed` | `EventHandler<FileWatchChangeEventArgs>` | Raised on a content/attribute/timestamp change. |
| `Deleted` | `EventHandler<FileWatchChangeEventArgs>` | Raised when a file/directory is deleted. |
| `Renamed` | `EventHandler<FileWatchRenamedEventArgs>` | Raised on a rename. Carries both the new and old path. |
| `WatchError` | `EventHandler<FileWatchErrorEventArgs>` | Raised if the underlying watcher itself fails (e.g. an internal notification-buffer overflow); the watch has already stopped by the time this fires. Never raised for a subscriber's own handler exception - see Notes & Caveats. |

`FileWatchChangeEventArgs` (`FullPath`, `Kind`), `FileWatchRenamedEventArgs`
(`FullPath`, `OldFullPath`), and `FileWatchErrorEventArgs` (`Message`) are
repository-owned, scalar-property types - the same reasoning that produced
the `FileChangeKind` enum instead of exposing `System.IO.WatcherChangeTypes`
directly.

## Notes & Caveats

- **Never throws.** Invalid input (null/empty path, a negative timeout) and
  runtime failures (a missing file/directory, an access-denied condition, a
  malformed argument) return `False` with a descriptive message. Timeouts
  are likewise `False` returns - a slow-arriving or slow-writing file is a
  normal, checkable outcome.
- **TOCTOU is pervasive here, more than in any other component in this
  suite.** `IsFileLocked`→act, `WaitForFileToExist`→open, and
  `WaitForFileMatchingPattern`→read the match all have a gap where another
  process can intervene between the check and whatever you do next.
  `ClaimFile` is the one method actually designed to close that gap - every
  other method here observes state and then hopes it still holds.
- **Lock-check is not the same as write-complete.** A writer that keeps
  `FileShare.ReadWrite` open the entire time it writes will report
  "unlocked" throughout, even mid-write. Use `WaitForFileStable` to detect
  "the writer is actually done" - `IsFileLocked`/`WaitForFileUnlocked`
  answer a narrower question and should not be treated as a completeness
  check on their own.
- **`WaitForFileStable` requires the file to already exist.** A nonexistent
  file is a hard, specific failure pointing you to `WaitForFileToExist`
  first, not a silent wait for it to appear - this keeps the method
  single-responsibility.
- **`AtomicMoveFile`'s atomicity is same-volume only.** A cross-volume move
  falls back to a non-atomic copy-then-delete internally.
- **`ReplaceFile` is Windows-specific by .NET design** - it throws
  `PlatformNotSupportedException` (reported as a failure, never an
  unhandled throw) on non-Windows runtimes. It also requires the
  destination to already exist and both files to be on the same volume -
  different preconditions from `AtomicMoveFile`, not interchangeable with
  it.
- **`ClaimFile` locks on the *source*, not the destination**, as its
  primary concurrency-safety mechanism - a `sourcePath + ".claiming"`
  sidecar file created exclusively (`FileMode.CreateNew`), checked before
  the destination is ever touched. This matters because two callers can
  claim the same source into two *different* `inProgressDirectoryPath`
  values, which compute two different destination paths - a
  destination-only exclusivity check (an earlier version of this method)
  cannot detect that race at all, and both callers could report success
  while duplicating the work item. Locking the source closes that gap
  regardless of where each caller intends to put the result. Neither this
  lock nor the destination claim uses `File.Move`'s `overwrite: false` -
  two threads racing that call against the same destination were measured
  reporting success essentially every time on this repository's target
  platform (a TOCTOU race inside .NET's own implementation, not a
  guarantee the OS fails to honor), and a further experiment racing it
  away from a *shared source* to two unique destinations was worse: both
  reported success with no exception, yet only one destination actually
  received the file's content. `FileMode.CreateNew` does not share either
  flaw, since it maps directly to the OS's own atomic exclusive-create
  call. The trade-off: claiming now copies the file's bytes into the new
  destination rather than just repointing a directory entry, so it loses a
  true rename's near-instant, whole-file atomicity for a large file - the
  same same-volume-only trade-off `AtomicMoveFile` already documents for
  its own cross-volume fallback. A second, narrower trade-off: a hard
  process crash between securing the source lock and this method's normal
  completion leaves the `.claiming` sidecar behind, permanently blocking a
  legitimate future claim of that exact source until it is removed by an
  operator or a separate maintenance process - full crash-safety would
  need filesystem transactions this component does not have access to.
  Distinct failure messages are reported for distinct situations rather
  than one unified "already claimed" message: losing the race for a
  source already gets that wording, but an unrelated file already sitting
  at the destination name, or the source vanishing after this call already
  secured its locks, each get their own, more specific message.
- **Hashing is stream-based**, safe for large files - never loads a whole
  file into memory.
- **`WatchForChange`'s `filter` and `WaitForFileMatchingPattern`'s
  `searchPattern` use different underlying mechanisms** (a live
  `FileSystemWatcher.Filter` vs. `Directory.GetFiles`'s enumeration
  pattern) for a similar-sounding purpose - edge-case matching behavior can
  differ subtly between the two.
- **`GetDirectoryListingJson`/`WaitForFileMatchingPattern` have no default
  for `includeSubdirectories`** - always explicit, matching this suite's
  preference for explicit boundary-crossing parameters.
- **Naming convention: `Simple` suffix marks the less-disambiguated
  overload.** Where two overloads would otherwise share an identical
  Pega-visible (non-`out`) parameter list, the overload with the extra
  disambiguating output keeps the plain name, and its sibling gets a
  `Simple` suffix. See
  `project-docs/pega-usability-reviews/FileWatchUtils-pega-usability-review.md`.
- **Guard tests.** `FileWatchUtils.Tests` (in this folder) covers not just
  guard clauses but real functional behavior - actual temp-directory files,
  actual file locks, actual `FileSystemWatcher` events, actual concurrent
  `ClaimFile` races, and actual hashing. It runs on Linux too - see
  `TESTING.md` at the repo root.
- **`Created`/`Changed`/`Deleted`/`Renamed`/`WatchError` fire on a
  background thread-pool thread, not the thread that called
  `StartWatching`.** A handler must not assume it runs synchronously with
  the rest of the automation.
- **A handler that throws is caught and logged, never allowed to crash the
  host process** - every other subscriber on the same event still runs,
  including ones registered after the one that threw. This is deliberate:
  a multicast event delegate normally invokes its subscribers in one call,
  so an unhandled exception from one would otherwise stop every subscriber
  after it from running too, in addition to being an unhandled exception
  on a background thread (which terminates the process by default in
  .NET). The exception itself is discarded after being logged via
  `System.Diagnostics.Debug.WriteLine` - there is no `message` output to
  report it through, since the method call that started the watch has
  already returned by the time any event fires.
- **`WatchError` is not a substitute for handling exceptions in your own
  `Created`/`Changed`/`Deleted`/`Renamed` handlers.** It only fires when
  the underlying watcher itself fails (e.g. an internal notification-
  buffer overflow), which is a different, rarer failure mode than a
  handler bug. A delayed `Error` callback that arrives from a watcher
  already replaced by a later `StopWatching`/`StartWatching` cycle is
  recognized as stale and ignored - it cannot stop or misreport the new,
  healthy watch.
- **`Changed` sets an explicit `NotifyFilter`** (`LastWrite`, `FileName`,
  `DirectoryName`, `Attributes`, `Size`, `CreationTime`) rather than
  relying on `FileSystemWatcher`'s narrower default (`LastWrite`/
  `FileName`/`DirectoryName` only), so a pure attribute or creation-time
  change genuinely raises it, matching the "content/attribute/timestamp
  change" description above.
- **`StartWatching`/`StopWatching`/`IsWatching` and the events they
  control are this component's first use of a real C# event** (previously
  every filesystem notification in this suite was polled or blocked on).
  They are the only members here that hold a live resource between calls
  - the background `FileSystemWatcher` - which is why this is also the
  first method in this component with a `Dispose(bool)` override to clean
  it up if the automation forgets to call `StopWatching`. Once disposed,
  the instance is final: a later `StartWatching` call fails rather than
  silently creating a watcher this disposed instance could never stop
  again - create a new `FileWatchUtils` instead.
