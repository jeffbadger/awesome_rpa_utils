# EventUtils Pega API Usability Review

## Summary

Lifecycle, subscription configuration, filtering, and queue management use
Pega-friendly scalar strings, integers, and Booleans. Event consumption returns
`EventData` objects or arrays, however, and there is no single-event scalar/JSON
adapter. More critically, `EventData.Hwnd` is a 32-bit `uint`, so it can truncate
a 64-bit native handle and cannot directly feed the repository's `IntPtr` inputs.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `Initialize`, `Stop`, `CancelWaits` | Direct | Boolean/message ports are straightforward. Lifecycle ordering is stateful but clearly expressible in an automation. |
| `Start` | Direct | CSV avoids requiring an enum collection. Category names are typo-prone; a validation/helper method or documented constants would improve configuration. |
| `Subscribe`, `Unsubscribe` | Direct | Categories, filter JSON, and subscription ID are strings. JSON is a useful scalar boundary, though hand-escaping JSON inside a Pega string is awkward. Add common scalar-filter overloads or a filter-JSON builder. |
| `GetNextEvent` | Proxy friction; broken handle bridge | Returns `EventData`, requiring an object proxy. Its public fields may be less reliably surfaced than properties by Pega proxy tooling. `Hwnd` is `uint`, which is not pointer-sized and cannot directly connect to an `IntPtr` consumer. Add flattened outputs or JSON plus a pointer-sized handle output. |
| `GetNextEvents` | Significant proxy friction | Returns `EventData[]`, requiring array and object proxy handling. Add a JSON-array output method analogous to `DumpRecentEvents`. |
| `HasEvents`, `ClearQueue` | Direct | Subscription ID, count, Boolean, and message are scalar. |
| `SetDebounce` | Direct but stringly typed | All ports are scalar, but event names are free-form strings. A repository enum would provide designer selection and validation. |
| `SetQueueLimits` | Direct but stringly typed | `overflowPolicy` should be an enum. Accepting `Block` while treating it as `DropNewest` is especially surprising on a design surface. |
| `DumpRecentEvents` | Direct | JSON is a good scalar escape hatch for diagnostics and avoids `EventData[]`. It does not identify/dequeue the exact event returned by a subscription or wait. |
| `WasWindowCreated` | Direct | All ports are scalar. Filter JSON remains the only authoring friction. |
| `WaitForWindowCreated`, `WaitForWindowDestroyed`, `WaitForWindowShown`, `WaitForForegroundChanged`, `WaitForDialogAppeared`, `WaitForMenuOpened` | Proxy-friction output | Inputs and timeout state are scalar, but each returns `EventData`. Add JSON or flattened event outputs, including a chainable pointer-sized window handle. |
| `WaitForTitleChanged`, `WaitForStateChanged` | Proxy-friction output | Same event-output issue; regex strings are scalar but require careful escaping and can be difficult to author in the designer. |

## Public supporting types

- `EventData` contains scalar data, but packages it as a complex object with public
  fields rather than properties. Convert fields to read-only/public properties for
  predictable proxy exposure.
- `EventData.ToJson()` is useful only after Pega has successfully received the
  object proxy. Component-level `GetNextEventJson` and `WaitFor...Json` methods
  would avoid that dependency.
- `EventFilter`'s fluent API is intended for .NET callers and remains object-based;
  `AnyOfProcesses(params string[])` is not Pega-friendly. The existing JSON filter
  boundary is the appropriate Pega alternative.

## Recommended changes

1. Replace `EventData.Hwnd` with a pointer-sized representation. For direct
   chaining, expose `IntPtr`; for serialization, also expose an unsigned 64-bit or
   invariant string representation without truncation.
2. Replace public fields on `EventData` with properties.
3. Add flattened or JSON variants for single-event dequeue and every wait method.
4. Add `GetNextEventsJson` for batch subscription consumption.
5. Add scalar helpers for common filters so Pega users need not hand-author JSON.
6. Replace event-name and overflow-policy strings with repository-owned enums or
   add enum overloads.

## Verdict

Configuration and diagnostics are mostly Pega-friendly, but primary event
consumption is proxy-dependent. The 32-bit window-handle field is a correctness
and composability defect, not merely designer friction, and should be addressed
before EventUtils is used as a handle producer on 64-bit systems.
