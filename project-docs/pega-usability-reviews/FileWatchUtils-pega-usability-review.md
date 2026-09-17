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
| `StartWatching` | Direct | Non-blocking counterpart to `WatchForChange` - see "Event-based design" below for why it has no `changeKindsFilter`. |
| `StopWatching` | Direct | Plain scalar in/out; fails if no watch is running. |
| `IsWatching` | Direct, no-message | Plain `bool`, no `out` parameters at all - matches `WindowUtils.IsWindowVisible`'s shape for a query with no failure mode. |
| `Created`/`Changed`/`Deleted`/`Renamed`/`WatchError` (events) | Direct | `EventHandler<T>` with repository-owned, scalar-property args (`FileWatchChangeEventArgs`, `FileWatchRenamedEventArgs`, `FileWatchErrorEventArgs`) instead of the BCL's `FileSystemEventArgs`/`RenamedEventArgs` - same reasoning as `FileChangeKind` avoiding `WatcherChangeTypes`. |
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
- `StartWatching`, `StopWatching`, `IsWatching` - distinct names and arities from every other method in this component (and from each other); no collision, no `Simple` sibling needed.
- `AtomicMoveFile`, `ReplaceFile`, `ClaimFile` - distinct names and/or arities from every other method in this component; no collision, no `Simple` sibling needed.
- `ComputeFileHashSha256` vs. `ComputeFileHash` - distinct names and different arity (`(string)` vs. `(string, string)`); no collision.
- No `As<Type>`-suffixed overloads exist in this component - there is no case here (unlike `ServiceUtils`'s `ServiceControllerStatus`/`ServiceStatus` pair) where two overloads return the same logical value via a different type.

## Event-based design (added after the initial pass)

`StartWatching` plus the `Created`/`Changed`/`Deleted`/`Renamed`/
`WatchError` events are this suite's **first use of a real C# event**.
Every other filesystem/UI-event mechanism in the suite either blocks
(`WatchForChange` here, `WinEventUtils`' `WaitForX`) or polls a queue
(`WinEventUtils`' `Subscribe`/`GetNextEvent`). This component deliberately
does neither:

- **Confirmed genuinely supported, not speculative**:
  `component-browser/ComponentBrowser.Core/AssemblyInspector.cs` already
  reflects `componentType.GetEvents(...)` and reads `[Category]`/
  `[Description]` off each `EventInfo`, exactly like it does for methods -
  Robot Studio's "Events" (the E in PME) is real, already-built
  infrastructure.
- **No `changeKindsFilter` parameter on `StartWatching`.** Kind selection
  is "did you wire that event's handler," not an input - a developer who
  doesn't want `Deleted` notifications just doesn't subscribe to
  `Deleted`. This is simpler than `WatchForChange`'s CSV filter, but only
  works because there's no queue to pre-filter before delivery.
- **No `WinEventUtils`-style multi-subscription/`subscriptionId` model.**
  Confirmed with the component owner: one instance runs at most one
  watch. `FileSystemWatcher` is cheap enough per-instance that a second
  folder just means a second `FileWatchUtils` component on the canvas,
  unlike `WinEventUtils`' single shared global hook, which justified its
  multi-subscription machinery.
- **The one real new risk this design introduces**: `FileSystemWatcher`'s
  native events fire on ThreadPool threads, so a Pega-wired (or any)
  subscriber's handler that throws would otherwise escape unhandled on a
  background thread and terminate the host process - and since a
  multicast event delegate invokes its subscribers in one call, a
  throwing handler would also stop every subscriber registered after it
  from ever running, not just crash the process. Every native-event
  handler invokes its public event's subscribers individually (not via a
  single `handler?.Invoke(...)`), catching and debug-logging each one's
  exception separately, so one bad handler can't take down the process
  or block its neighbors. This is the component's first "guard external
  boundary" concern that involves invoking caller-supplied code from a
  thread this component doesn't control.
- **`FileSystemWatcher.Error`** (an internal notification-buffer overflow
  - a different, rarer failure than a subscriber's own handler exception)
  stops the watch and raises `WatchError`, so a developer who wired it
  can react (e.g. restart the watch) instead of it silently going quiet.
- **This is also the component's first held resource**, and therefore its
  first `Dispose(bool)` override, to stop and dispose the background
  watcher if the automation forgets to call `StopWatching`.

### Hardening pass (post-review)

A round of review caught four additional races/gaps in the initial
implementation, all fixed before merge:

- **Stale `Error` callback could stop a replacement watch.** A delayed
  native `Error` event from a watcher already superseded by a
  `StopWatching`/`StartWatching` cycle would stop the *new* watcher and
  raise a misleading `WatchError` for it, since the handler didn't check
  whether its `sender` was still the active instance. Fixed by comparing
  `sender` against the current `_watcher` and clearing it atomically
  under `_watchLock`, so a concurrent `StartWatching` can't race the
  check-and-clear either. Covered by
  `StaleNativeError_FromReplacedWatcher_DoesNotAffectCurrentWatch`, using
  an `internal` test seam since a real buffer overflow isn't reliably
  triggerable on demand.
- **A disposed instance could still start a new watch.** `Dispose(bool)`
  stopped the current watcher but never recorded that the instance itself
  was disposed, so a `StartWatching` call arriving after (or racing)
  disposal only checked `_watcher == null` and could happily create a
  watcher a disposed instance would never stop again. Fixed with a
  `_disposed` flag set under `_watchLock` before teardown begins.
- **Watcher teardown itself wasn't exception-safe.** `Dispose(bool)` and
  `OnNativeError` both called the stop/cleanup path directly with no
  guard; if `watcher.Dispose()` (or an unsubscribe) threw, that would
  either violate `Dispose`'s own never-throw expectation or, worse,
  escape unhandled on `OnNativeError`'s ThreadPool thread and terminate
  the host process - the same class of risk `RaiseSafely` already
  protects subscriber code from, just missed for this component's own
  cleanup code. Fixed by making the shared stop/cleanup helper itself
  catch and debug-log any teardown exception, so every caller gets that
  safety for free.
- **`EnableRaisingEvents = false` happened after `_watcher` was already
  cleared and the lock released**, leaving a brief window where a
  concurrent `StartWatching` could start a new watcher while the old one
  might still raise one more event. Fixed by disabling raising events
  while still holding `_watchLock`, before `_watcher` is nulled and
  exposed to a concurrent caller.

## Operational concerns

- **TOCTOU is pervasive in this component, more than in any other in this
  suite.** `IsFileLocked`→act, `WaitForFileToExist`→open, and
  `WaitForFileMatchingPattern`→read the match all have a gap where another
  process can intervene. `ClaimFile` is the one method actually designed to
  close that gap, via a source-scoped exclusive lock plus an
  exclusively-created destination handle (both `FileMode.CreateNew` - see
  "`ClaimFile`'s concurrency bug and fix" below); every other method here
  observes state and then acts on it.
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
- **`ClaimFile` reports distinct messages for distinct failure shapes**,
  not one unified message - losing the race to claim a given source gets
  "already claimed," an unrelated file already occupying the destination
  name gets its own message, and the source vanishing after this call
  already secured its locks gets a third. An earlier version of this
  method folded the latter two into "already claimed"; that was corrected
  once it became clear a caller might reasonably want to distinguish "someone
  else is processing this exact work item" from "this destination name is
  unrelated-but-occupied."
- **A hard process crash mid-claim leaves a stale `.claiming` sidecar
  file** (`sourcePath + ".claiming"`) that permanently blocks a legitimate
  future claim of that exact source until an operator or a separate
  maintenance process removes it. This is a known, documented trade-off
  (see below), not a gap this method's own review considers a bug -
  closing it fully would need filesystem transactions this component does
  not have access to.

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
the OS's own atomic exclusive-create call. A first fix claimed the
destination that way, then streamed the source's content into it and
deleted the source - trading a true rename's near-instant, whole-file
atomicity for a copy, the same same-volume-only trade-off
`AtomicMoveFile` already documents for its own cross-volume fallback.

That first fix still had a real gap, caught by code review before merge:
exclusivity was scoped to the caller-chosen *destination* path, not the
*source*. Two callers racing to claim the same source into two
*different* `inProgressDirectoryPath` values compute two different
destination paths, so a destination-only check cannot detect that race at
all - both could exclusively create their own destination, both copy the
source, and both report success, duplicating the work item exactly as
this method exists to prevent. A further repro confirmed the natural
alternative fix - moving away from a shared source to unique destinations
via `File.Move` - is *also* unsafe on this platform, and worse: both
racing calls reported success with no exception, yet inspection showed
only one destination actually received the file's content; the other
silently never existed (real data loss, not just a race). `FileMode.CreateNew`
showed neither flaw across 500 trials.

The fix now locks the *source* first, via an exclusively-created
`sourcePath + ".claiming"` sidecar file, before ever touching a
destination - closing the cross-directory gap regardless of where each
caller intends to put the result. The destination-scoped `FileMode.CreateNew`
check remains as a secondary, independent collision case (an unrelated
file occupying that exact destination name), now reported with its own
message rather than folded into "already claimed." One platform quirk
surfaced during this second fix: losing a `FileMode.CreateNew` race is
documented to throw `IOException`, but was observed on this platform to
occasionally throw `UnauthorizedAccessException` instead in the same race
window (an NTFS timing quirk) - both are treated identically as "lost the
race" in the message-classification logic, or that classification would
occasionally misreport an ordinary lost race as a generic failure.
`ClaimFile_ConcurrentClaimAttempts_ExactlyOneSucceeds` runs 20 fresh
iterations of the same-directory race, and a new
`ClaimFile_ConcurrentClaimAttemptsToDifferentDirectories_ExactlyOneSucceeds`
(both in `FileWatchUtils.Tests`) runs 20 iterations of the cross-directory
race that the first fix could not catch.

## Recommended changes

None outstanding. The initial design pass (Wait/Watch/Actions/Hash/
Metadata) had none. Corrections were made after the fact: `ClaimFile`'s
concurrency mechanism went through two rounds of fixes (see "`ClaimFile`'s
concurrency bug and fix" above) once its `File.Move`-based guarantee, and
then its destination-only exclusivity, were each found not to hold, and
the later event-based `StartWatching` addition's own review pass (see
"Hardening pass" above) found and fixed four races/gaps before merge
rather than after. `StartWatching` itself was reviewed for the same
signature-uniqueness and never-throws concerns as the initial pass before
being added. A stale `.claiming` sidecar left behind by a hard process
crash currently requires manual/operator cleanup; an automated
reclaim-after-timeout mechanism was considered and deliberately left as a
separate, out-of-scope follow-up rather than expanding this fix's surface
area further.
