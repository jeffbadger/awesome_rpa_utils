# Processing and recovery

Drive a loop from `TryTakeNext`:

```text
TryTakeNext
  itemAvailable = false -> finish parent work
  itemAvailable = true  -> process payload
      success           -> CompleteItem
      temporary failure -> RetryItem
      invalid input     -> RejectItem
```

Pass the returned `leaseToken` to every mutation. Call `RenewLease` before the
expiry time during long operations. On startup, call `RecoverExpiredLeases`;
`TryTakeNext` also performs that recovery automatically before selecting work.

After processing, use `GetCounts` to decide whether the parent Robot Manager
work succeeds, fails, or requires review. Report rejected-item details through
the parent work rather than treating this local queue as the system of record.

Use `DeleteQueueIfEmpty` for normal cleanup. To discard an abandoned or test
queue that still contains work, call `DeleteQueue` with
`confirmDeleteNonEmpty: true`. It removes queue-owned imported files but never
external files recorded by `AddFileReferences`.
