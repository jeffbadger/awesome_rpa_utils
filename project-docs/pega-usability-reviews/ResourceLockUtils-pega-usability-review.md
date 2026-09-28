# ResourceLockUtils Pega usability review

This review covers the public surface as designed in `project-docs/plans/2026-09-27-resourcelockutils-design.md` and as implemented. The
component exists because of two limits of Robot Studio's `Lock` component: a lock can only be released by the thread that took it, and it
lives inside one Robot Runtime, so Server Bots on one machine cannot coordinate with it.

## Summary

Every port is a scalar (`string`, `bool`, `int`) or the `LockScope` drop-down. No collection, object or generic type crosses the public
surface. A lock is identified by a resource name the designer chooses and owned by a token string, which travels on a data link or in a
variable like any other text.

| Area | Rating | Notes |
|---|---|---|
| Acquire | Direct | `TryAcquireLock` tries once; `AcquireLock` waits up to `waitMilliseconds`. `acquired` feeds a Decision; `currentHolder` says who has it, ready for a log step. |
| Hold | Direct | `RenewLock` and `ReleaseLock` take the token and work from any thread, so the release can sit on a Catch path or an asynchronous link. |
| Slots | Direct | `TryAcquireSlot`/`AcquireSlot` add one `int` (the capacity) to the same shape; `holderCount` reports how full the pool is. |
| Inspect | Direct | `GetLockStatus` returns scalars; `GetLocksJson` gives the full picture for a dashboard or log. |
| Setup | Direct | `ConfigureLockFolder` takes one path; `ValidateLockFolder` returns `usable` plus a JSON report with plain-language warnings. |

## Findings applied

- **The token, not the thread, owns the lock.** This is the point of the component: a Robot Studio automation cannot control which thread runs
  a step, so ownership that follows a string is the only kind a designer can rely on.
- **Taken and lost are outcomes, not errors.** `acquired`, `renewed` and `released` are `bool` outputs on calls that return `True`; `False` plus
  `message` is kept for calls that could not run. An automation branches with a Decision, not with Try/Catch.
- **A lease that ends on its own** protects against the automation that never reaches its release step (a crash, a stuck adapter, a closed
  session). For the Machine scope, a holder whose process has ended frees its lock at once, across Windows accounts too.
- **One scope drop-down** chooses between one runtime (`Process`) and the whole machine (`Machine`); the rest of the calls are identical.
- **Failure values are uniform**: null strings, 0 counts, `False` flags, so a data link never carries a stale token.
- **No overloads or optional parameters**: the waiting forms have distinct names (`AcquireLock`, `AcquireSlot`).
- **Messages never contain resource names, holders or tokens.**

## Remaining friction

- Robot Studio has no `finally`: the release must be wired on both the success path and the Catch path. The documentation shows the pattern.
- A wait (`AcquireLock`, `AcquireSlot`) blocks its thread; it belongs on an asynchronous link.
- The Machine scope needs a one-time folder setup per server (rights to list the folder, read its files, create files and write to the files it creates, for every robot account);
  `ValidateLockFolder` makes the check a single call.
- There is no fairness between waiters; ordered processing belongs in `LocalQueueUtils`.
