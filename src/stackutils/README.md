# StackAutomation

A Pega Robot Studio-ready component (`StackUtils`) providing an instance-local,
in-memory LIFO stack for variable-count work without constructing a collection
proxy. Each dragged component owns an independent stack. Like every component
in this suite, its methods honor the never-throws contract: invalid input and
runtime failures return `False` with a descriptive message.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `StackAutomation`
- Assembly: `StackAutomation`
- Default capacity: 10,000 items (configurable from 1 to 1,000,000)

See the [Documentation](Documentation/README.md) folder for real-world usage
examples. Typical uses include depth-first traversal, undo/backtracking,
nested-window navigation, reverse-order cleanup, and variable child work when
persistence is unnecessary.

## Types

### `StackItemKind`

The kind returned by `TryPeek` or `TryPop`: `Text`, `Json`, or
`FileReference`. Every value uses a scalar `string` output, so no collection or
object proxy is required.

## Constructors

| Constructor | Description |
|---|---|
| `StackUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `StackUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Properties

| Property | Type | Description |
|---|---|---|
| `MaximumItems` | `int` | Design-time item capacity. Defaults to 10,000; valid range is 1 through 1,000,000. |

## Methods

### Configuration

| Method | Signature | Description |
|---|---|---|
| `SetMaximumItems` | `bool SetMaximumItems(int maximumItems, out string message)` | Sets runtime capacity; it cannot be outside 1 through 1,000,000 or below the current count. |

### Push

| Method | Signature | Description |
|---|---|---|
| `PushText` | `bool PushText(string value, out string message)` | Pushes text. Empty text is allowed; null is rejected. |
| `PushJson` | `bool PushJson(string valueJson, out string message)` | Validates and pushes any JSON value while preserving its raw text. |
| `PushFileReference` | `bool PushFileReference(string filePath, out string absolutePath, out string message)` | Pushes an existing file's normalized absolute path without copying or owning it. |
| `PushJsonArray` | `bool PushJsonArray(string jsonArray, out int pushedCount, out string message)` | Pushes every array element in array order as a JSON item. Atomic. |
| `PushLines` | `bool PushLines(string text, out int pushedCount, out string message)` | Pushes every non-empty line in source order. Atomic. |
| `PushFileReferences` | `bool PushFileReferences(string directoryPath, string searchPattern, bool recursive, out int pushedCount, out string message)` | Pushes matching paths sorted by absolute path. Atomic. |

### Read

| Method | Signature | Description |
|---|---|---|
| `TryPeek` | `bool TryPeek(out bool itemAvailable, out StackItemKind itemKind, out string value, out string message)` | Returns the next item without removing it. Empty is successful with `itemAvailable == false`. |

### Remove

| Method | Signature | Description |
|---|---|---|
| `TryPop` | `bool TryPop(out bool itemAvailable, out StackItemKind itemKind, out string value, out string message)` | Immediately removes and returns the next item. Empty is successful with `itemAvailable == false`. |
| `Clear` | `bool Clear(out int removedCount, out string message)` | Removes every item and returns the number removed. |

### Query

| Method | Signature | Description |
|---|---|---|
| `GetCount` | `bool GetCount(out int count, out string message)` | Returns the current item count. |
| `GetMaximumItems` | `bool GetMaximumItems(out int maximumItems, out string message)` | Returns capacity through automation-friendly output ports. |
| `GetSnapshotJson` | `bool GetSnapshotJson(out string snapshotJson, out string message)` | Returns items in next-to-pop order as `{ "kind": "...", "value": "..." }` objects. |

## Notes & Caveats

- **Ordinary stack ordering applies.** Bulk input `[A,B,C]` pushes in that
  order, so `C` pops first. Duplicate values are allowed.
- **Bulk operations are atomic.** Every input is prepared and capacity is
  confirmed before mutation. Malformed input or insufficient capacity leaves
  the stack unchanged.
- **Methods never throw for recoverable failures.** Success has
  `message == null`; failure returns `False` with initialized outputs. Empty
  peek/pop is normal success with `itemAvailable == false`, the default enum,
  and null `value`/`message`.
- **`MaximumItems` is a Property Grid setting.** Invalid property values use
  normal .NET validation exceptions. Use `SetMaximumItems` for runtime values
  that need the never-throws result/message contract.
- **Every operation is thread-safe.** Ordering is deterministic within each
  serialized call, not between racing callers.
- **Disposal clears stack memory.** Later method calls return `False` with an
  actionable disposed message.
- **JSON stays scalar.** Snapshot `value` fields contain raw JSON text rather
  than embedding it as a nested value.
- **File references are non-owning.** Existence is checked when pushed, but a
  file may later disappear. Files are never copied, moved, or deleted.
- **This is runtime-only.** It provides no persistence, cross-runtime sharing,
  leasing, retries, priorities, deduplication, imported-file ownership, or
  Robot Manager distribution. Use `LocalQueueUtils` for durable recovery.
