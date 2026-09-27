# Plan: ResourceLockUtils — locks that any thread can release, shared by Server Bots

## Context

Two problems with Pega Robot Studio's `Lock` component (`RequestLock(timeout)` / `ReleaseLock()`, Properties/Methods/Events ch. 52):

1. **Thread affinity.** A lock can only be released by the thread that took it. Automations hop threads: asynchronous links, adapter and web
   events, `ParallelProcess` branches, a Catch path. A release from another thread fails, the lock stays held, and the automation stalls (the
   manual warns that an unreleased lock stalls the automation). There is also no expiry, so a lock taken on a thread that died is held forever.
2. **Server Bot Framework.** Several unattended robots run as separate RDP sessions on one Windows Server, each under its own Windows logon
   (ch. 68). `Lock` is in memory inside one Robot Runtime, so it cannot stop two Server Bots from using the same legacy login, license seat,
   shared workbook, sequence file or rate-limited portal at once. Robot Manager assigns work items, not resources. Pega solves this for its own
   startup (`MultipleRuntimeMutexTimeoutSecs`) but offers nothing to automations.

ResourceLockUtils gives automations **token-owned leases**: whoever holds the token can renew or release, from any thread, and a lease that is
not renewed expires, so a crashed or hung holder cannot block everyone forever.

## Shape

- New component `ResourceLockUtils` in `src/resourcelockutils/`, namespace/assembly `ResourceLockAutomation`, `net8.0-windows;net10.0-windows`,
  **no NuGet dependencies**. Scaffolded from `src/textextractutils/` (csproj, `Component` base, ctor pair, own `NeverThrowsGuard.cs` reporting
  the exception type only, `InternalsVisibleTo` tests, README pack item).
- Suite conventions: never throws (`bool` + trailing `out string message`), unique method names, no overloads or optional parameters,
  `[Category]`/`[Description]` on every public method, scalar/enum/JSON ports, at most one `bool` output per method, failure sentinels
  (null / 0 / false), messages never contain holder text beyond what the caller supplied.
- `LockScope` enum (drop-down): **`Process`** and **`Machine`** in phase 1; **`Shared`** in phase 2.

## Scopes

### `Process` (problem 1)
- In memory, **static across all component instances in one Robot Runtime**, so two automations in the same project share the same locks.
- Ownership by token: `TryAcquireLock` returns a token (a GUID string); `RenewLock` and `ReleaseLock` require it and work from any thread. The
  token travels as an ordinary data link or variable.
- Lease expiry is evaluated lazily on every call, so a lease that was never released frees itself.

### `Machine` (problem 2)
- Generation lease files (decision 2) in a machine-wide folder, default `%ProgramData%\AwesomeRpaUtils\Locks` (not
  `LocalApplicationData`, which is per user and would give each Server Bot its own private folder; LocalQueueUtils uses that and is therefore
  single-user).
- Acquire = create the next generation file atomically (create-if-not-exists); the file holds holder, token, expiry (UTC), process ID, process start
  time, session ID and machine name. No Windows mutex (thread affinity again) and no named semaphore (`Global\` objects need
  `SeCreateGlobalPrivilege`, which standard robot accounts usually lack, a crashed holder never gives its count back, and it cannot say who
  holds it).
- **Fast crash recovery:** all holders are on one machine, so a lease whose process no longer exists (process ID plus start time, to survive
  process ID reuse) is free at once; lease expiry covers a holder that is alive but hung.
- **Race-proof takeover** (the core of WP3): taking over an expired or dead lease must not steal a lease its holder renewed in time, and two
  robots taking over the same stale lease must not both win. The generation-file protocol (decision 2) gives both: only one robot can create
  generation n+1, and a renewal before expiry is seen by any taker. The protocol is proven by multi-process stress tests, not by argument.
- Robots need the rights to list the folder, read its files and create files in it; deleting another robot's file is never needed for correctness, only for cleanup.
  `ValidateLockFolder` reports the real permissions and fails with a clear message when files cannot be created; the documentation gives the
  recommended Server Bot setup (an admin-provisioned folder with Modify for the robot accounts, via `icacls`, set with `ConfigureLockFolder`).

## Counting slots
`TryAcquireSlot(scope, resource, capacity, …)`: at most `capacity` holders (a license pool, N portal sessions, API concurrency). Each slot is
a lease; the token identifies the slot. The capacity is recorded on first use; a call with a different capacity for the same resource fails
rather than silently allowing more holders.

## Public surface (frozen in WP1: 11 methods)

| Group | Methods |
|---|---|
| Acquire | `TryAcquireLock(scope, resource, holder, leaseSeconds, out acquired, out token, out currentHolder)`; `AcquireLock(… , waitMilliseconds, …)` (takes the lock as soon as it is free or the wait ends; the Process scope is woken by a release, the Machine scope checks the lock files with jittered backoff; run it on an asynchronous link); `TryAcquireSlot(scope, resource, capacity, holder, leaseSeconds, out acquired, out token, out holderCount)`; `AcquireSlot(… , waitMilliseconds, …)` |
| Hold | `RenewLock(scope, resource, token, leaseSeconds, out renewed, out expiresInSeconds)` — `renewed` False means the lease was lost; `ReleaseLock(scope, resource, token, out released)` — True when the call ran; `released` False means the lease had already expired or been taken over, so the work may have overlapped with another holder |
| Inspect | `GetLockStatus(scope, resource, out held, out holders, out expiresInSeconds, out holderCount)` (slot holders separated by `|`); `GetLocksJson(scope, out locksJson)` |
| Operate | `ForceReleaseLock(scope, resource, confirmForceRelease, out releasedCount)`; `ConfigureLockFolder(folderPath)`; `ValidateLockFolder(out usable, out reportJson)` |
- Disposing the component releases every lock this instance still holds (Robot Runtime shutdown), in both scopes.
- Resource names: letters, digits, `-`, `_`, `.`, at most 100 characters, ignoring case (they become file names). Holder: free text, at most
  128 characters, typically the robot name; the documentation says never to put secrets in it.

## Limits (defaults / maximums)
Lease 5 s to 86,400 s (default 300 s); wait up to 3,600,000 ms; capacity 1 to 100; resource name 100 characters; holder 128 characters; locks
held per instance 1,000. Over a limit fails the call with a message and changes nothing.

## Robot Studio usage (documented and example-tested)
```
AcquireLock(Machine, "SAP-User-BATCH01", RobotName, 300, 60000) → acquired?, token
  True  → Try → [log in, work, RenewLock between screens, log out] → ReleaseLock(token)
          Catch → ReleaseLock(token) → handle the error
  False → LocalQueueUtils.RetryItem (delay) or try another resource; log currentHolder
```
- Robot Studio has no `finally`: release on both the success and the Catch path; the lease is the safety net.
- Cross-thread: acquire in one event handler, pass `token` through a variable, release from an asynchronous link or another event.

## Work packages (one PR each; stop for merge approval)
- **WP1** Scaffold, frozen public contract (stubs), `LockScope`, validation of every input, convention and boundary tests, this plan committed.
- **WP2** `Process` scope: tokens, leases, lazy expiry, slots, waits, dispose release; tests including acquire on one thread and release on
  another, many threads contending, and expiry of an abandoned lease.
- **WP3** `Machine` scope: lease files, atomic acquire, process liveness, the takeover protocol, atomic renewal, slots; multi-process stress
  tests (child processes acquiring, renewing, crashing and taking over) asserting that no two processes ever hold the same lock.
- **WP4** `ValidateLockFolder` (the folder's real permissions) and corrupt-file handling (status, JSON and `ForceReleaseLock` moved into WP3).
- **WP5** Docs (README; Documentation: QuickStart, CrossThread, ServerBots with the folder setup, Slots, Limits), registration
  (`src/AwesomeRpaUtils.sln`, `$releaseAssemblies` in `scripts/Package-Release.ps1`, root README row/links/test line and the DLL count
  "twenty-five" → "twenty-six", `CrossReference.md`, `TESTING.md`), Pega usability review + index, documentation sync/example tests.
- **WP6** Measurements (acquire/release latency per scope, contention with N processes), packaging dry runs, release on your go-ahead.
- **Phase 2** `Shared` scope on a UNC path for robots on different machines: clock skew between machines (expiry judged against the file
  server's timestamps with a margin), SMB caching behavior, no process-liveness shortcut.

## Verification
- `dotnet test` on Linux (both scopes; file leases and child-process stress tests run on Linux) and Windows CI; Release build 0 warnings.
- Mutation checks on the expiry, takeover and token rules.
- Pending (recorded in TESTING.md, not claimed): in Robot Studio, acquire on one link and release on an asynchronous one; on a Server Bot
  host, two or more sessions under different accounts contending, killing a robot mid-lock and confirming another takes over at once, and a
  hung robot's lock expiring.

## Decisions (confirmed by Jeff)
1. **Crash detection for `Machine`**: lease file plus a check that the holder's process is still running (process ID and start time); lease
   expiry covers a holder that is alive but hung.
2. **Folder permissions**: ProgramData may not be writable, and where it is, the default ACL lets a robot
   create files but not delete or rename another robot's (the creator owns the file). So the `Machine` protocol never deletes or renames
   another robot's file: a lock is a sequence of **generation files** (`<resource>.<n>.lease`) created atomically with create-if-not-exists,
   the highest valid generation (not expired, holder process alive) holds the lock, a taker supersedes a stale generation by creating the next
   one, a holder renews by rewriting its own file and has lost the lease when a higher generation exists, and each robot deletes only its own
   old generations. This needs the rights to list the folder, read its files and create files, never to delete or rename another robot's. The default folder stays `%ProgramData%\AwesomeRpaUtils\Locks`;
   `ConfigureLockFolder` points to an admin-provisioned folder (recommended setup for Server Bots, which also allows full cleanup);
   `ValidateLockFolder` reports whether robots can create files, delete their own and delete another's, and fails with a message pointing at
   `ConfigureLockFolder` when files cannot be created. The default ProgramData ACL behaviour is to be confirmed on a real Server Bot host in WP3.
3. **Losing a lease**: `ReleaseLock` returns True with `released` False, so the automation can flag possible overlap.
4. **Name**: `ResourceLockUtils` / `ResourceLockAutomation`.
5. **`Process` scope details** (WP2): leases are timed with a monotonic clock (`Environment.TickCount64`), so a system clock change neither
   expires nor extends a lease; a release wakes waiters at once and a waiter also wakes at the earliest expiry it waits on (no polling); while a
   resource is held it is either a lock or a slot pool with one capacity, and a call that disagrees fails (the capacity can change once nothing
   holds it); a token given with the wrong resource name is `released`/`renewed` False, like a lost lease (the table cannot tell the two
   apart); disposing a component releases what it still holds, and the table checks the owner is not disposed inside its own lock, so an acquire
   admitted just before disposal cannot leave a lease behind; per-owner lease counts are kept (no scan per acquire) and every expired lease is
   swept at least once a minute by a background timer that runs only while the table holds anything (plus lazily on every visit), so resource names that are never used again do not accumulate; holders may not contain `|`, the separator of
   `GetLockStatus`'s holders; the JSON never contains tokens.
6. **`Machine` scope protocol, as built and stress-tested** (WP3). The multi-process stress test (8 processes x 300 acquisitions, repeated) found
   two flaws in the first version, both now fixed and covered:
   - A move "without overwrite" is not atomic on Linux: .NET checks for the target and then renames (2,989 of 3,000 races had several
     winners). Lease files are created exclusively instead (`FileMode.CreateNew`: `O_EXCL` / `CREATE_NEW`). A new file can be read before its
     content is written, so readers retry briefly and an unreadable file younger than 10 s counts as held; an older one is superseded.
   - A directory listing is only guaranteed to include files that are neither created nor deleted while it runs. A robot that missed the new top
     generation and its just-deleted predecessor created a lower number: two holders. So the top is found by looking up n+1, n+2, ... by name
     from the listing's highest number; holders renew and release by rewriting their own file in place (never renaming); and a generation is
     deleted only once its successor is 5 minutes old, while an acquire that takes over 60 s between reading and creating starts over. No
     generation at or above an operation's starting top can then disappear during it, so numbers only grow.
   After the fixes: no overlap and no lost lease in 6 runs of 2,400 acquisitions each. A lease is dead when its process has ended or its process
   ID belongs to a process started at another time; a process that cannot be inspected is trusted until its lease ends. Status, JSON (never
   tokens) and force release (a released generation) are in WP3 rather than WP4; WP4 keeps `ValidateLockFolder` and corrupt-file handling.
   The test assembly doubles as the child process (`dotnet ResourceLockUtils.Tests.dll stress-host ...`, its own `Main`), so no helper project
   can end up in a package; every child has a time limit, so a broken build fails the tests instead of hanging CI.
7. **Review fixes to the `Machine` scope** (WP3, Copilot review of PR #167): the process-start identity is compared exactly (the kernel's start
   tick from `/proc/[pid]/stat` on Linux, where .NET's `StartTime` moves with wall-clock adjustments; the creation time on Windows) instead of
   within a second; a lease file this account may not read is held until it is older than the longest lease (24 h), never superseded after
   the 10 s grace for half-written files; token operations use the folder and resource the lease was taken in, and only a lease confirmed lost
   stops being tracked for disposal; cleanup after a successful create is best effort, so an acquire that created its lease always returns it.
8. **Second review of PR #167**: the permissions a robot needs are listing the folder, reading its files and creating files (never deleting or
   renaming another robot's), corrected wherever the plan said "only create"; file presence tells a missing file from an inaccessible one (so
   an access-denied lease is never mistaken for absent or old); cleanup checks the parsed slot; a held slot whose lease cannot be read blocks
   allocating another slot of the resource (its kind and capacity are unknown); leases that ended unvisited are pruned before the
   per-component limit applies; the locks JSON lists unreadable held leases with null details; the test project states `OutputType` Exe. Testing the presence fix found that an access-denied name was taken as present, so in a folder that can be listed but not accessed the lookup of the top looped forever: an access-denied name is now confirmed by listing its exact name, and the lookup has a hard bound.
