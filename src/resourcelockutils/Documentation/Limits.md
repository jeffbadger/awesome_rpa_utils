# Limits

| Limit | Minimum | Maximum |
|---|---|---|
| Lease (`leaseSeconds`) | 5 | 86,400 |
| Wait (`waitMilliseconds`) | 0 | 3,600,000 |
| Slots in a pool (`capacity`) | 1 | 100 |
| Characters in a resource name | 1 | 100 |
| Characters in a holder | 1 | 128 |
| Characters in a lock folder path | 0 | 200 |
| Locks and slots one component holds at a time | 0 | 1,000 |

- An input outside a limit fails the call with a message naming the rule, and nothing changes.
- Resource names: ASCII letters, digits, `-`, `_` and `.`, starting with a letter or digit, not ending with a dot, not a Windows device name
  (`CON`, `NUL`, `COM1`, `LPT1`, ...), compared ignoring case. Holders: no line breaks and no `|`.
- At 1,000 held locks and slots, a component first stops counting leases that have already ended, then refuses new ones until some are released.

## Timings

| Timing | Value |
|---|---|
| Process scope: expired leases are swept at least every | 60 s |
| Machine scope: a waiting acquire checks the lock files every | 50 ms, growing to 1 s |
| Machine scope: a lease file still being written counts as held for | 10 s |
| Machine scope: a lease file this account cannot read counts as held for up to | 86,400 s |
| Machine scope: an old lease file is deleted once its successor is | 5 min old |
| Machine scope: an acquire that takes longer than this between reading and creating starts over | 60 s |
