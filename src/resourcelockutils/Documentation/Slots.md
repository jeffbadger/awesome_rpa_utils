# Slots: pools with a capacity

Some resources allow a few users at once: three licenses of a desktop application, five sessions on a portal, two logins to a mainframe. A
**slot pool** lets up to `capacity` holders in and makes the rest wait.

```csharp
locks.TryAcquireSlot(LockScope.Machine, "Portal-Sessions", 3, "Robot 1", 600, out bool acquired, out string token1, out int holderCount, out string message);
// acquired True, holderCount 1
locks.TryAcquireSlot(LockScope.Machine, "Portal-Sessions", 3, "Robot 2", 600, out acquired, out string token2, out holderCount, out message);
// acquired True, holderCount 2
locks.TryAcquireSlot(LockScope.Machine, "Portal-Sessions", 3, "Robot 3", 600, out acquired, out string token3, out holderCount, out message);
// acquired True, holderCount 3
locks.TryAcquireSlot(LockScope.Machine, "Portal-Sessions", 3, "Robot 4", 600, out acquired, out string token4, out holderCount, out message);
// acquired False, token4 null, holderCount 3: the pool is full
locks.GetLockStatus(LockScope.Machine, "Portal-Sessions", out bool held, out string holders, out int expiresInSeconds, out int count, out message);
// held True, holders "Robot 1|Robot 2|Robot 3", count 3
```

- Each slot has its own token; `RenewLock` and `ReleaseLock` work on slots exactly as on locks. Releasing any one slot lets the next robot in.
- **Every caller must give the same capacity.** While the pool is held, a call with another capacity fails with a message; so does using the
  same name as a single lock. Once nothing holds it, the capacity can change. If two robots start the two different uses at the same instant this
  is not always caught, so give each use its own name.
- `AcquireSlot` waits like `AcquireLock`, up to `waitMilliseconds`.
- Holders cannot contain `|`, which separates them in `GetLockStatus`.
