# ResourceLockAutomation

A Pega Robot Studio component (`ResourceLockUtils`) for locks on named resources (a legacy login, a license seat, a shared workbook, a
rate-limited portal) that **any thread can release** and that **Server Bots on one machine share**. It fills two gaps in Robot Studio's `Lock`
component: a `Lock` can only be released by the thread that took it, and it is in memory inside one Robot Runtime, so it cannot stop two Server
Bots from using the same resource.

> **Status: under construction.** Work packages 1 and 2 of the [design plan](../../project-docs/plans/2026-09-27-resourcelockutils-design.md)
> are in: the public methods are fixed, every input is validated, and the **`Process` scope works** (locks and slots shared by every thread and
> automation in one Robot Runtime, released from any thread). The `Machine` scope and `ValidateLockFolder` report that they are not implemented
> yet. Do not use the component until a release says otherwise. It is registered in the solution so it builds and its
> tests run in CI, but it is deliberately not in any release, the root README or `CrossReference.md` yet.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `ResourceLockAutomation`
- Assembly: `ResourceLockAutomation`
- No NuGet dependencies. Messages never contain resource names, holders or tokens.

## How it works

- A lock is a **lease owned by a token**. An acquire call returns a `token`; `RenewLock` and `ReleaseLock` need it and work from **any thread**,
  so a lock taken in one event handler can be released from an asynchronous link or another event.
- A lease ends after `leaseSeconds` unless it is renewed, so a crashed or hung holder cannot block everyone forever. For the `Machine` scope a
  holder whose process has ended frees its lock at once.
- **Scope** (`LockScope` drop-down): `Process` shares locks between every thread and automation in one Robot Runtime (in memory); `Machine`
  shares them between every robot on the machine, including Server Bots in other sessions under other Windows accounts (files in the lock folder).
- **Slots** allow up to `capacity` holders of one resource (a pool of licenses, logins or sessions).
- **`Process` scope details:** leases are timed with a monotonic clock, so changing the system clock neither expires nor extends one. A release
  wakes waiting `AcquireLock`/`AcquireSlot` calls at once (the `Machine` scope checks its lock files repeatedly instead). While a resource is held it is either a lock or a slot pool with one capacity; a call
  that disagrees fails. Disposing the component releases every lock and slot it still holds, including one an in-flight call was about to take. One component holds at
  most 1,000 at a time, and expired leases are cleared at least once a minute.
- Finding a lock taken, or learning that a lease was lost, is a normal outcome with a `bool` output (`acquired`, `renewed`, `released`), not a
  failure. `False` from a method means the call itself could not be done.

## Inputs

- **resource**: ASCII letters (`A`–`Z`, `a`–`z`, no accented letters), digits, `-`, `_` and `.` (the name becomes part of a file name), starting with a letter or digit, not ending with a dot, not a Windows device name (`CON`,
  `NUL`, `COM1`, `LPT1`, ...), at most 100 characters. Names are compared ignoring case.
- **holder**: who holds the lock, as other robots see it (typically the robot name): 1 to 128 characters, no line breaks and no `|` (it separates
  holders in `GetLockStatus`). Never put secrets in it.
- **leaseSeconds** 5 to 86,400; **waitMilliseconds** 0 to 3,600,000; **capacity** 1 to 100.
- **token**: exactly as an acquire call returned it.
- **folderPath**: an absolute local folder, or empty for the default `ProgramData\AwesomeRpaUtils\Locks`. Network paths are not supported by the
  `Machine` scope.

## Method reference

All 11 methods and their signatures. On any failure a method sets every output to its failure value (null strings, 0 counts,
`False` flags) and returns a message naming the operation and the broken rule, never the value; success has no message.

### Acquire

| Method | Signature | Description |
|---|---|---|
| `TryAcquireLock` | `bool TryAcquireLock(LockScope scope, string resource, string holder, int leaseSeconds, out bool acquired, out string token, out string currentHolder, out string message)` | Takes a lock on a resource now if it is free, without waiting. acquired is False when another holder has it, and currentHolder then names who. Keep the token: RenewLock and ReleaseLock need it, from any thread. The lease ends after leaseSeconds unless it is renewed. |
| `AcquireLock` | `bool AcquireLock(LockScope scope, string resource, string holder, int leaseSeconds, int waitMilliseconds, out bool acquired, out string token, out string currentHolder, out string message)` | Waits up to waitMilliseconds for a lock on a resource and takes it as soon as it is free. acquired is False when the wait ended first, and currentHolder then names who has it. Run it on an asynchronous link so the wait does not block the user interface. |
| `TryAcquireSlot` | `bool TryAcquireSlot(LockScope scope, string resource, int capacity, string holder, int leaseSeconds, out bool acquired, out string token, out int holderCount, out string message)` | Takes one of capacity slots on a resource now if one is free (a pool of licenses, logins or sessions), without waiting. holderCount is how many slots are taken. Every caller must give the same capacity for the resource. The token works with RenewLock and ReleaseLock. |
| `AcquireSlot` | `bool AcquireSlot(LockScope scope, string resource, int capacity, string holder, int leaseSeconds, int waitMilliseconds, out bool acquired, out string token, out int holderCount, out string message)` | Waits up to waitMilliseconds for one of capacity slots on a resource and takes it as soon as one is free. acquired is False when the wait ended first. Run it on an asynchronous link so the wait does not block the user interface. |

### Hold

| Method | Signature | Description |
|---|---|---|
| `RenewLock` | `bool RenewLock(LockScope scope, string resource, string token, int leaseSeconds, out bool renewed, out int expiresInSeconds, out string message)` | Extends a held lock or slot to leaseSeconds from now, from any thread that has the token. renewed is False when the lease was already lost (it expired or another holder took it over): stop using the resource. |
| `ReleaseLock` | `bool ReleaseLock(LockScope scope, string resource, string token, out bool released, out string message)` | Releases a lock or slot, from any thread that has the token. released is False when the lease had already been lost (it expired or another holder took it over), so the work may have overlapped with another holder's. |

### Inspect

| Method | Signature | Description |
|---|---|---|
| `GetLockStatus` | `bool GetLockStatus(LockScope scope, string resource, out bool held, out string holders, out int expiresInSeconds, out int holderCount, out string message)` | Reports whether a resource is held, by whom (the holders of slots separated by \|), in how many seconds the first lease ends, and how many holders it has. Changes nothing. |
| `GetLocksJson` | `bool GetLocksJson(LockScope scope, out string locksJson, out string message)` | Returns every held lock and slot in the scope as JSON: the resource, holder and lease end, and for the Machine scope the holder's machine, session and process. Changes nothing. |

### Setup and operations

| Method | Signature | Description |
|---|---|---|
| `ForceReleaseLock` | `bool ForceReleaseLock(LockScope scope, string resource, bool confirmForceRelease, out int releasedCount, out string message)` | Releases a resource whatever holds it, for an operator who knows the holder is gone; confirmForceRelease must be True. releasedCount is how many holders lost their lease; each is told so by RenewLock or ReleaseLock. |
| `ConfigureLockFolder` | `bool ConfigureLockFolder(string folderPath, out string message)` | Sets the folder that holds Machine-scope locks for this component: an absolute local path, or empty for the default (ProgramData\AwesomeRpaUtils\Locks). Every robot that shares locks must use the same folder. |
| `ValidateLockFolder` | `bool ValidateLockFolder(out bool usable, out string reportJson, out string message)` | Checks the Machine-scope lock folder: usable is True when this robot can create lock files there, and reportJson also says whether it can delete its own and other robots' files. Run it once under each robot account when setting up a server. |
