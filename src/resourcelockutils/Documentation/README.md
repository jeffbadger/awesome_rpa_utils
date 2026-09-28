# ResourceLockUtils documentation

`ResourceLockUtils` gives automations locks on named resources (a legacy login, a license seat, a shared workbook, a rate-limited portal) that
**any thread can release** and that **every robot on a machine shares**, including Server Bots running in other sessions under other Windows
accounts. It fills two gaps in Robot Studio's `Lock` component: a `Lock` can only be released by the thread that took it, and it lives inside
one Robot Runtime.

| Page | What it covers |
|---|---|
| [QuickStart](QuickStart.md) | The smallest complete flow: take a lock, see who has it, release it; the Robot Studio wiring with Try/Catch. |
| [CrossThread](CrossThread.md) | Why a token instead of a thread owns the lock; releasing from another event or an asynchronous link; renewing; waiting. |
| [ServerBots](ServerBots.md) | The `Machine` scope: setting up the lock folder for Server Bots, checking it with `ValidateLockFolder`, what happens when a robot crashes or hangs. |
| [Slots](Slots.md) | Pools with a capacity (a few licenses, logins or sessions) instead of a single lock. |
| [Limits](Limits.md) | Every limit and timing, and what happens when one is reached. |

The [component README](../README.md) holds the method reference. Conventions shared by every method:

- Every method returns `bool` and ends with `out string message`. `False` plus a message means the call could not be done (bad input, a folder
  that cannot be used, a disposed component). It never throws.
- Finding a lock taken, or learning that a lease was lost, is a **normal outcome**, not a failure: the call returns `True` and the `acquired`,
  `renewed` or `released` output says what happened.
- On failure every output holds its failure value: null strings, 0 counts, `False` flags.
- Messages never contain resource names, holders or tokens.
