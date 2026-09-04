# LocalQueueAutomation

A Pega Robot Studio-ready component (`LocalQueueUtils`) that stores a variable
number of local JSON, text, or file work items and exposes a durable
take/process/complete loop without requiring a Pega collection proxy. Like
every component in this suite, it honors the never-throws contract: invalid
input and runtime failures return `False` with a descriptive message.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `LocalQueueAutomation`
- Assembly: `LocalQueueAutomation`
- Default root: `%LOCALAPPDATA%\AwesomeRpaUtils\Queues`

Queues are per-Windows-user and machine-local. They persist across automation
failures, Pega Runtime restarts, logoff, and reboot. See the
[Documentation](Documentation/README.md) folder for real-world usage examples.

## Types

### `QueueLifetime`

Controls queue ownership: `Run` creates a run-scoped queue requiring a `runId`;
`Persistent` creates a named queue that later runs can reopen.

## Constructors

| Constructor | Description |
|---|---|
| `LocalQueueUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `LocalQueueUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Setup

| Method | Signature | Description |
|---|---|---|
| `CreateQueue` | `bool CreateQueue(string queueName, QueueLifetime lifetime, string runId, out string queuePath, out bool alreadyExisted, out string message)` | Creates/reopens a queue and acquires its exclusive process lock. Run queues require `runId`. |
| `OpenQueue` | `bool OpenQueue(string queuePath, out string message)` | Opens and exclusively locks an existing queue. |
| `ValidateQueue` | `bool ValidateQueue(string queuePath, out string reportJson, out string message)` | Validates a queue and returns a JSON report without locking it. |

### Add

| Method | Signature | Description |
|---|---|---|
| `AddJson` | `bool AddJson(string queuePath, string payloadJson, out string itemId, out bool duplicate, out string message, string businessKey = null, int priority = 50, int delaySeconds = 0, int maximumAttempts = 3)` | Adds one JSON value; an active duplicate business key returns the existing item. |
| `AddJsonArray` | `bool AddJsonArray(string queuePath, string jsonArray, out int addedCount, out string message, int priority = 50, int delaySeconds = 0, int maximumAttempts = 3)` | Adds every array element as a separate work item. |
| `AddLines` | `bool AddLines(string queuePath, string text, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Adds every non-empty line as a JSON-string work item. |
| `AddFileReferences` | `bool AddFileReferences(string queuePath, string sourceDirectory, string searchPattern, bool recursive, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Adds matching file paths without copying, moving, or owning them. |
| `ImportFiles` | `bool ImportFiles(string queuePath, string sourceDirectory, string searchPattern, bool recursive, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Copies matching files into queue-owned storage and adds them as work. |

### Process

| Method | Signature | Description |
|---|---|---|
| `TryTakeNext` | `bool TryTakeNext(string queuePath, out bool itemAvailable, out string itemId, out string payloadJson, out string storedFilePath, out int attempt, out string leaseToken, out string leaseExpiresUtc, out string message, int leaseSeconds = 300)` | Leases the next item. Empty is successful with `itemAvailable == false`. |
| `RenewLease` | `bool RenewLease(string queuePath, string itemId, string leaseToken, out string leaseExpiresUtc, out string message, int leaseSeconds = 300)` | Extends a valid in-progress lease. |
| `CompleteItem` | `bool CompleteItem(string queuePath, string itemId, string leaseToken, out string message, string resultJson = null)` | Completes a leased item and optionally records result JSON. |
| `RetryItem` | `bool RetryItem(string queuePath, string itemId, string leaseToken, string errorMessage, out bool willRetry, out bool rejected, out string message, int delaySeconds = 0)` | Retries/delays failed work or rejects it after its attempt limit. |
| `RejectItem` | `bool RejectItem(string queuePath, string itemId, string leaseToken, string reason, out string message)` | Immediately rejects a leased item with a reason. |

### Query

| Method | Signature | Description |
|---|---|---|
| `GetCounts` | `bool GetCounts(string queuePath, out int ready, out int delayed, out int inProgress, out int completed, out int rejected, out int corrupt, out string message)` | Returns scalar counts for every queue state. |
| `GetItem` | `bool GetItem(string queuePath, string itemId, out bool found, out string state, out string itemJson, out string message)` | Returns one item's state and JSON metadata; not found is normal. |

### Recovery

| Method | Signature | Description |
|---|---|---|
| `RecoverExpiredLeases` | `bool RecoverExpiredLeases(string queuePath, out int recoveredCount, out int rejectedCount, out string message)` | Recovers expired work, rejecting items whose attempts are exhausted. |
| `RetryRejectedItem` | `bool RetryRejectedItem(string queuePath, string itemId, bool resetAttemptCount, out string message, int delaySeconds = 0)` | Returns rejected work to ready or delayed state. |

### Cleanup

| Method | Signature | Description |
|---|---|---|
| `DeleteCompletedItems` | `bool DeleteCompletedItems(string queuePath, int olderThanDays, out int removedCount, out string message)` | Deletes old completed metadata and associated imported files. |
| `DeleteRejectedItems` | `bool DeleteRejectedItems(string queuePath, int olderThanDays, out int removedCount, out string message)` | Deletes old rejected metadata and associated imported files. |
| `DeleteQueueIfEmpty` | `bool DeleteQueueIfEmpty(string queuePath, out bool deleted, out string message)` | Deletes only a completely empty queue. |
| `DeleteQueue` | `bool DeleteQueue(string queuePath, bool confirmDeleteNonEmpty, out bool deleted, out int deletedItemCount, out string message)` | Deletes the queue; non-empty deletion requires explicit confirmation. |

## Notes & Caveats

- **Queues are durable and machine-local.** Items move through `ready`,
  `delayed`, `in-progress`, `completed`, or `rejected`; corrupt artifacts use
  the reserved `corrupt` state.
- **Selection is deterministic.** Work is selected by priority (100 highest),
  availability time, creation time, then item ID.
- **Processing is at least once, not exactly once.** Every in-progress mutation
  requires the opaque lease token returned by `TryTakeNext`. An expired lease
  consumes the attempt already started. Verify external state before repeating
  side effects.
- **Only one component/process may own a queue.** A second open returns `False`
  with an ownership message. Disposal releases the OS file lock.
- **UNC paths and arbitrary queue roots are rejected.** Storage remains beneath
  the component's per-user LocalApplicationData root.
- **Never throws.** Success returns `True` with `message == null`; recoverable
  failures return `False` with initialized outputs and an actionable message.
  Empty `TryTakeNext` and unmatched `GetItem` calls are successful normal
  negative results.
- **Imported and referenced files have different ownership.** Cleanup removes
  files copied by `ImportFiles`; it never deletes external files added through
  `AddFileReferences`.
- **Destructive cleanup is explicit.** `DeleteQueue` refuses a non-empty queue
  unless `confirmDeleteNonEmpty` is `True`. `DeleteQueueIfEmpty` is safer for
  routine cleanup.
- **This is child-work infrastructure, not Robot Manager.** It does not
  schedule robots, distribute work between machines, implement SLAs, support
  shared UNC/multi-consumer queues, or guarantee exactly-once delivery.
