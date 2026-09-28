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

## Measured cost

On a Linux development machine (Intel i9-14900KF, .NET 10, Release build), each workload in its own process, on two file systems: ext4 on an
NVMe disk, and NTFS (through FUSE, the nearest to a Windows server's disk available there). The measurements are repeatable: set
`RESOURCELOCK_MEASURE=1` (and `RESOURCELOCK_MEASURE_FOLDER` for the file system) and run the `MeasurementTests` one at a time.

| Workload | ext4 | NTFS |
|---|---|---|
| `Process` scope: acquire and release | 4.5 µs | 4.5 µs |
| `Machine` scope: acquire and release (2,050 files in the folder by the end) | 2.5 ms | 2.9 ms |
| `Machine` scope: acquire and release in a busy folder (800 files, 51 resources) | 3.1 ms | 3.3 ms |
| `Machine` scope: 8 processes contending for one lock, 300 acquisitions each (every child checked, 2,400 acquired) | 299 per second | 276 per second |
| `Machine` scope: a waiting `AcquireLock` notices a release (after a long wait) | 0.4 s on average, 1 s at most | 0.5 s on average, 1 s at most |
| `Process` scope: a waiting `AcquireLock` notices a release | 0.3 ms | 0.4 ms |
| `ValidateLockFolder` (50 lease files) | 0.8 ms | 2.2 ms |

- A lock taken a few times a minute keeps its folder small, so each operation costs about a millisecond. The cost grows with the number of files
  in the folder, since each operation lists it; a lock taken every second leaves about 300 files (5 minutes' worth).
- A `Machine`-scope waiter checks the files with growing pauses, up to about a second, so after a long wait it may take up to a second to notice
  a release. Use the `Process` scope when all the contenders run in one Robot Runtime.
- Timings on a Windows server will differ; treat these as an order of magnitude.

