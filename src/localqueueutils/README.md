# LocalQueueAutomation

A Pega Robot Studio-ready component (`LocalQueueUtils`) that stores a variable
number of local JSON, text, or file work items and exposes a simple
take/process/complete loop without requiring a Pega collection proxy.

- Target frameworks: `net8.0-windows`, `net10.0-windows`
- Namespace: `LocalQueueAutomation`
- Assembly: `LocalQueueAutomation`
- Default root: `%LOCALAPPDATA%\AwesomeRpaUtils\Queues`

Queues are per-Windows-user and machine-local. They persist on disk across
automation failures, Pega Runtime restarts, logoff, and reboot. They are for
child work inside an automation, not Robot Manager business-work assignment.
See [Documentation](Documentation/README.md) for worked examples.

## Methods

| Category | Method and signature | Purpose |
|---|---|---|
| Setup | `bool CreateQueue(string queueName, QueueLifetime lifetime, string runId, out string queuePath, out bool alreadyExisted, out string message)` | Creates/reopens a queue. Run queues require `runId`. |
| Setup | `bool OpenQueue(string queuePath, out string message)` | Opens and exclusively locks an existing queue. |
| Setup | `bool ValidateQueue(string queuePath, out string reportJson, out string message)` | Validates without locking. |
| Add | `bool AddJson(string queuePath, string payloadJson, out string itemId, out bool duplicate, out string message, string businessKey = null, int priority = 50, int delaySeconds = 0, int maximumAttempts = 3)` | Adds one JSON value. |
| Add | `bool AddJsonArray(string queuePath, string jsonArray, out int addedCount, out string message, int priority = 50, int delaySeconds = 0, int maximumAttempts = 3)` | Adds every array element. |
| Add | `bool AddLines(string queuePath, string text, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Adds every non-empty line. |
| Add | `bool AddFileReferences(string queuePath, string sourceDirectory, string searchPattern, bool recursive, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Queues file paths without copying. |
| Add | `bool ImportFiles(string queuePath, string sourceDirectory, string searchPattern, bool recursive, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Copies files into queue storage and queues them. |
| Process | `bool TryTakeNext(string queuePath, out bool itemAvailable, out string itemId, out string payloadJson, out string storedFilePath, out int attempt, out string leaseToken, out string leaseExpiresUtc, out string message, int leaseSeconds = 300)` | Leases the next item. An empty queue is successful with `itemAvailable == false`. |
| Process | `bool RenewLease(string queuePath, string itemId, string leaseToken, out string leaseExpiresUtc, out string message, int leaseSeconds = 300)` | Extends a valid lease. |
| Process | `bool CompleteItem(string queuePath, string itemId, string leaseToken, out string message, string resultJson = null)` | Completes an item. |
| Process | `bool RetryItem(string queuePath, string itemId, string leaseToken, string errorMessage, out bool willRetry, out bool rejected, out string message, int delaySeconds = 0)` | Retries/delays or rejects after the attempt limit. |
| Process | `bool RejectItem(string queuePath, string itemId, string leaseToken, string reason, out string message)` | Immediately rejects an item. |
| Query | `bool GetCounts(string queuePath, out int ready, out int delayed, out int inProgress, out int completed, out int rejected, out int corrupt, out string message)` | Returns scalar state counts. |
| Query | `bool GetItem(string queuePath, string itemId, out bool found, out string state, out string itemJson, out string message)` | Returns item state/metadata; not-found is normal. |
| Recovery | `bool RecoverExpiredLeases(string queuePath, out int recoveredCount, out int rejectedCount, out string message)` | Recovers expired work. |
| Recovery | `bool RetryRejectedItem(string queuePath, string itemId, bool resetAttemptCount, out string message, int delaySeconds = 0)` | Returns rejected work to ready/delayed. |
| Cleanup | `bool DeleteCompletedItems(string queuePath, int olderThanDays, out int removedCount, out string message)` | Deletes old completed metadata and imported files. |
| Cleanup | `bool DeleteRejectedItems(string queuePath, int olderThanDays, out int removedCount, out string message)` | Deletes old rejected metadata and imported files. |
| Cleanup | `bool DeleteQueueIfEmpty(string queuePath, out bool deleted, out string message)` | Deletes only a completely empty queue. |
| Cleanup | `bool DeleteQueue(string queuePath, bool confirmDeleteNonEmpty, out bool deleted, out int deletedItemCount, out string message)` | Deletes the entire queue; non-empty deletion requires explicit confirmation. |

## Queue behavior

Items move through `ready`, `delayed`, `in-progress`, `completed`, or
`rejected`. Corrupt artifacts have a reserved `corrupt` state. Selection is
priority (100 highest), availability time, creation time, then item ID.

Every processing mutation requires the opaque lease token returned by
`TryTakeNext`. The default lease is five minutes. An expired lease consumes
the attempt already started; recovery rejects an item once its maximum attempts
are exhausted. This is at-least-once processing, not exactly-once delivery—use
a business key and verify external-system state before repeating side effects.

Only one `LocalQueueUtils` component/process may open a queue at once. A second
open returns `false` with a message. Disposing the component releases its OS
file lock. UNC paths and arbitrary queue roots are intentionally rejected.

`DeleteQueue` removes all queue metadata and files imported into queue-owned
storage. It never deletes external source files referenced by
`AddFileReferences`. A non-empty queue requires `confirmDeleteNonEmpty` to be
`true`; otherwise nothing is changed. `DeleteQueueIfEmpty` remains the safer
routine-cleanup operation.

## Never-throws contract

Every public operation catches recoverable argument, JSON, and filesystem
failures. Success returns `true` with `message == null`; operational failure
returns `false` with an actionable message and initialized outputs. Normal
negative results are explicit: `TryTakeNext` returns `true` with
`itemAvailable == false`, and `GetItem` returns `true` with `found == false`.

## Non-goals

This component does not schedule robots, distribute business work between
machines, implement SLAs, or replace Robot Manager. It does not support shared
UNC queues, multiple runtimes consuming one queue, or exactly-once delivery.
