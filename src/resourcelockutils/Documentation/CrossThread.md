# Releasing from another thread

Robot Studio's `Lock` component (`RequestLock`/`ReleaseLock`) can only be released by the thread that took it. Automations hop threads: an
asynchronous link, an adapter or web event, a `ParallelProcess` branch or a Catch path may run on another thread, and a release there fails and
leaves the lock held.

In `ResourceLockUtils` a lock belongs to its **token**, not to a thread:

- The acquire call returns `token`. Store it in a string variable or pass it on a data link.
- `RenewLock` and `ReleaseLock` need the token and work from **any thread** and any component instance in the same Robot Runtime.
- Without the token, nothing can release the lock except `ForceReleaseLock` (an operator's tool) or the lease ending.

```text
Event A (adapter thread):   TryAcquireLock(Process, "Workbook-Q3", "Robot 1", 120) → store token in a variable
Event B (asynchronous link): ReleaseLock(Process, "Workbook-Q3", token)              → released True
```

## Renewing

A lease ends after `leaseSeconds` unless it is renewed. For long work, renew between steps:

- `RenewLock(scope, resource, token, leaseSeconds)` sets the lease to `leaseSeconds` from now and returns `renewed` True with
  `expiresInSeconds`.
- `renewed` False means the lease was already lost: it expired, or it was released or forced. Stop using the resource; another holder may
  already have it.
- `ReleaseLock` returns `released` False in the same situations. The work may then have overlapped with another holder's, so treat that item
  as needing a check.

## Waiting

- `TryAcquireLock` tries once. `AcquireLock` waits up to `waitMilliseconds` and takes the lock as soon as it is free. In the `Process` scope a
  release wakes the waiter at once; in the `Machine` scope the lock files are checked with growing pauses (50 ms up to 1 s).
- When the wait ends, one last attempt is made, so `waitMilliseconds` 0 is a single attempt, like `TryAcquireLock`.
- There is no queue: when several automations wait, whichever checks first after a release wins. For ordered work, use `LocalQueueUtils`.
- Disposing the component ends its own waits at once and releases every lock it still holds.
