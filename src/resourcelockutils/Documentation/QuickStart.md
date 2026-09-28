# Quick start

Two automations in one Robot Runtime share one legacy login, `SAP-User-BATCH01`, which must never be used by both at once.

```csharp
locks.TryAcquireLock(LockScope.Process, "SAP-User-BATCH01", "Robot 1", 300, out bool acquired, out string token, out string currentHolder, out string message);
// acquired True, token (32 characters), currentHolder null

locks.TryAcquireLock(LockScope.Process, "SAP-User-BATCH01", "Robot 2", 300, out acquired, out string secondToken, out currentHolder, out message);
// acquired False, secondToken null, currentHolder "Robot 1"

locks.ReleaseLock(LockScope.Process, "SAP-User-BATCH01", token, out bool released, out message);
// released True: the login is free again
```

- `LockScope.Process` shares the lock between every thread and automation in this Robot Runtime. For robots in other sessions or processes on
  the same machine, use `LockScope.Machine` (see [ServerBots](ServerBots.md)); the calls are the same.
- `300` is the lease in seconds. The lock ends by itself after that unless it is renewed, so a crashed or stuck automation cannot block the login
  forever. Choose a lease longer than the work usually takes, and renew it for long work (see [CrossThread](CrossThread.md)).
- Keep the `token`: only it can renew or release the lock, from any thread.

## Wiring it in Robot Studio

Robot Studio has no `finally`, so release the lock on both paths:

```text
TryAcquireLock → acquired?
  True  → Try → [log in, do the work, log out] → ReleaseLock(token)
          Catch → ReleaseLock(token) → handle the error
  False → retry later (LocalQueueUtils.RetryItem with a delay), or log currentHolder
```

To wait for the lock instead of trying once, use `AcquireLock` with `waitMilliseconds` (for example 60000) on an asynchronous link, so the wait
does not block the user interface.
