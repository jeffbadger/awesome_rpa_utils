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
combinable `changeKindsFilter` CSV input those methods accept.

### `FileMetadata`
The JSON shape produced by `GetFileMetadataJson`/`GetDirectoryListingJson`:
`FullPath`, `Name`, `Extension`, `SizeBytes`, `IsDirectory`,
`CreatedUtcIso8601`, `LastWriteUtcIso8601`.

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

### Actions

| Method | Signature | Description |
|---|---|---|
| `AtomicMoveFile` | `bool AtomicMoveFile(string sourcePath, string destinationPath, bool overwrite, out string message)` | Moves a file, optionally overwriting an existing destination. Atomic only when source and destination share a volume. |
| `ReplaceFile` | `bool ReplaceFile(string sourcePath, string destinationPath, string backupPath, out string message)` | Replaces an existing destination's contents, optionally keeping a backup. Requires the destination to already exist. Windows-only behavior. |
| `ClaimFile` | `bool ClaimFile(string sourcePath, string inProgressDirectoryPath, out string claimedPath, out string message)` | Claims a work file by moving it into an in-progress directory. A destination collision means another instance already claimed it - the one method here safe under real multi-robot concurrency. |

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
- **`ClaimFile` relies on `File.Move`'s own exclusive-create-at-destination
  failure** as its concurrency-safety mechanism - it never auto-overwrites
  or auto-renames on a collision. Both ways a race between two claimants
  can surface (the destination already existing, or the source having
  vanished between an earlier check and the move itself) are reported with
  the same "already claimed" message, so a caller never needs to interpret
  two different failure shapes for one underlying situation.
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
