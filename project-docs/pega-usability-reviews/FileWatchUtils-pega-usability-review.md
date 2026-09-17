# FileWatchUtils Pega API Usability Review

## Summary

Like `EventLogUtils`'s and `SessionUtils`'s review docs, this one is written
**up front**, alongside the initial implementation, rather than as a
post-hoc pass over an already-shipped surface - there is no legacy API to
retrofit here. It records the signature-uniqueness decisions made before any
code was written, per the
[Signature Uniqueness Standard](../coding-standards/signature-uniqueness-standard.md).

All method ports are scalars, strings, ints, Booleans, or JSON strings -
directly usable in Pega. The repository-owned `FileChangeKind` enum avoids
requiring Pega to reference `System.IO.WatcherChangeTypes` (a `[Flags]`
enum) just to read what kind of change was detected; the combinable input
filter for the same concept stays a plain CSV string, matching this suite's
established filter convention.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `WaitForFileToExist`/`Simple` | Direct, disambiguated | Scalar path/timeout in, Boolean/message out. |
| `WaitForFileToBeDeleted`/`Simple` | Direct, disambiguated | Same shape. |
| `WaitForFileToChange`/`Simple` | Direct, disambiguated | Same shape; deletion during the wait is treated as a change, documented explicitly. |
| `WaitForFileStable`/`Simple` | Direct, disambiguated | The flagship method - all scalar inputs/outputs despite tracking a rolling internal state (last-changed timestamp) that never crosses the Pega boundary. |
| `IsFileLockedSimple`/`IsFileLocked` | Direct, disambiguated | The `querySucceeded`-output overload separates a genuine "not locked" answer from a query failure (e.g. the file doesn't exist). |
| `WaitForFileUnlocked`/`Simple` | Direct, disambiguated | Same filter/timeout shape as the other `WaitForX` methods. |
| `WaitForFileMatchingPattern`/`Simple` | Direct, disambiguated | `matchedFilePath` is a plain scalar string output. |
| `WatchForChange`/`Simple` | Direct, disambiguated | Uses `FileSystemWatcher.WaitForChanged` internally - a BCL detail entirely hidden behind scalar parameters and the repository-owned `FileChangeKind` output. |
| `AtomicMoveFile` | Direct | Plain scalar in/out, no disambiguation needed - only method of its name. |
| `ReplaceFile` | Direct | Same; `backupPath` accepts null/empty for "no backup," a plain scalar convention already used elsewhere in this suite (e.g. delimiter defaults). |
| `ClaimFile` | Direct | `claimedPath` is a plain scalar string output - no live/disposable object crosses the boundary. |
| `ComputeFileHashSha256` | Direct | Scalar path in, scalar hex string out. |
| `ComputeFileHash` | Direct, power-user escape hatch | `algorithmName` is a plain string rather than an enum, since the underlying `HashAlgorithmName`/`HashAlgorithm` types are BCL cryptography types with no reason to leak onto the Pega boundary as a dedicated enum for five values. |
| `AreFilesIdenticalByHash` | Direct | Scalar Boolean output, the most direct possible port of "are these the same." |
| `TryGetFileMetadata` | Direct | All scalar outputs (`long`, ISO-8601 strings). |
| `GetFileMetadataJson`/`GetDirectoryListingJson` | Direct, JSON | Returns a JSON object/array string rather than a typed object, consistent with this suite's preference for JSON over complex objects on the Pega boundary. |

## Signature-uniqueness decisions made up front

- `WaitForFileToExistSimple` vs. `WaitForFileToExist` - both share the identical `(string, int, int)` non-`out` parameter list, so `Simple` is required, not optional.
- `WaitForFileToBeDeletedSimple`/`WaitForFileToBeDeleted` - same pattern.
- `WaitForFileToChangeSimple`/`WaitForFileToChange` - same pattern.
- `WaitForFileStableSimple`/`WaitForFileStable` - both share `(string, int, int, int)`, so `Simple` is required.
- `IsFileLockedSimple(string, out string)` vs. `IsFileLocked(string, out bool, out string)` - `Simple` suffix on the overload missing the disambiguating `querySucceeded` output, per the standard's convention (identical to `SessionUtils`'s `IsWorkstationLockedSimple` precedent).
- `WaitForFileUnlockedSimple`/`WaitForFileUnlocked` - both share `(string, int, int)`, so `Simple` is required.
- `WaitForFileMatchingPatternSimple`/`WaitForFileMatchingPattern` - both share `(string, string, bool, int, int)` plus a shared `out string matchedFilePath`; the collision is on the non-`out` list regardless of the `out` shapes involved, so `Simple` is required.
- `WatchForChangeSimple`/`WatchForChange` - both share `(string, string, string, bool, int)`, so `Simple` is required.
- `AtomicMoveFile`, `ReplaceFile`, `ClaimFile` - distinct names and/or arities from every other method in this component; no collision, no `Simple` sibling needed.
- `ComputeFileHashSha256` vs. `ComputeFileHash` - distinct names and different arity (`(string)` vs. `(string, string)`); no collision.
- No `As<Type>`-suffixed overloads exist in this component - there is no case here (unlike `ServiceUtils`'s `ServiceControllerStatus`/`ServiceStatus` pair) where two overloads return the same logical value via a different type.

## Operational concerns

- **TOCTOU is pervasive in this component, more than in any other in this
  suite.** `IsFileLocked`→act, `WaitForFileToExist`→open, and
  `WaitForFileMatchingPattern`→read the match all have a gap where another
  process can intervene. `ClaimFile` is the one method actually designed to
  close that gap, via an exclusively-created destination handle
  (`FileMode.CreateNew` - see "`ClaimFile`'s concurrency bug and fix"
  below); every other method here observes state and then acts on it.
- **Lock-check is not a write-complete signal.** A writer holding
  `FileShare.ReadWrite` open the entire time it writes will report
  "unlocked" throughout. `WaitForFileStable` is the correct signal for "is
  the writer done."
- **`AtomicMoveFile`'s atomicity is same-volume only** - a cross-volume
  move falls back to a non-atomic copy-then-delete.
- **`ReplaceFile` is Windows-specific by .NET design** - throws
  `PlatformNotSupportedException` (reported as a failure, never an
  unhandled throw) on non-Windows runtimes, and requires the destination to
  already exist, unlike `AtomicMoveFile`.
- **`ClaimFile`'s two race-outcome messages are unified** so a caller never
  needs to distinguish "the destination already had a file" from "the
  source vanished before it could be read" - both are the same underlying
  situation (another instance won the race) and both messages say "already
  claimed."

## `ClaimFile`'s concurrency bug and fix

The initial implementation relied on `File.Move(sourcePath, destination,
overwrite: false)`'s documented "throws if the destination already
exists" contract as the sole concurrency-safety mechanism - the entire
reason this method exists. That contract turned out not to hold under
concurrency: two threads racing `File.Move(src, dst, overwrite: false)`
against the same destination on this repository's target platform both
reported success essentially every time in an isolated, non-xunit repro
(a TOCTOU race inside .NET's own implementation of that overload - a
managed existence check followed by a separate move, not a single atomic
OS call - not a guarantee the OS itself fails to honor). The same repro
showed `new FileStream(destination, FileMode.CreateNew, ...)` reliably
rejects one of the two racers on every trial, since it maps directly to
the OS's own atomic exclusive-create call. `ClaimFile` now claims the
destination that way, then streams the source's content into it and
deletes the source - trading a true rename's near-instant, whole-file
atomicity for a copy, the same same-volume-only trade-off
`AtomicMoveFile` already documents for its own cross-volume fallback.
`ClaimFile_ConcurrentClaimAttempts_ExactlyOneSucceeds` (in
`FileWatchUtils.Tests`) now runs 20 fresh iterations of the race rather
than one, since a single iteration was too weak a regression guard to
have caught the original bug reliably on its own.

## Recommended changes

None outstanding. The initial design pass (Wait/Watch/Actions/Hash/
Metadata) had none; `ClaimFile`'s concurrency mechanism was corrected
after the fact (see above) once its `File.Move`-based guarantee was found
not to hold.
