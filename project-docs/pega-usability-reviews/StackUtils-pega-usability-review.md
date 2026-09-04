# StackUtils Pega usability review

This review was completed before implementation to keep the API usable from
Pega Robot Studio's component tray and expression blocks.

## Findings applied

- One component instance owns one stack, avoiding names, handles, setup calls,
  shared state, and ambiguous lifecycle ownership.
- Every public operation has a unique name, returns `bool`, initializes scalar
  `out` values, and places outputs after inputs. No overloads or collection
  types are exposed.
- `StackItemKind` makes mixed-item routing designer-friendly. Empty pop/peek is
  a successful polling outcome through `itemAvailable`, not an exception.
- JSON arrays, multiline text, and file discovery provide bulk bridges without
  requiring a Pega collection proxy. Bulk mutation is atomic.
- Categories split configuration, push, read, query, and removal methods on the
  designer surface. Descriptions state never-throws and ownership behavior.
- `MaximumItems` is directly configurable in the Property Grid with a visible
  default; `SetMaximumItems` remains available for runtime wiring.
- Immediate pop semantics are explicit. Durable work belongs in
  `LocalQueueUtils`; StackUtils has no lease/retry vocabulary that could imply
  recovery guarantees.

## Accepted caveats

The stack exists only for the component instance's runtime lifetime. File paths
can become stale after push. Snapshot JSON is for inspection and is not a
restore format. Separate component instances never share items.
