# EventUtils Pega API Usability Review

## Summary

Lifecycle, subscription configuration, filtering, and queue management use
Pega-friendly scalar strings, integers, and Booleans. Event consumption
previously returned only `EventData` objects or arrays, with no single-event
scalar/JSON adapter, and `EventData.Hwnd` was a 32-bit `uint` that could
truncate a 64-bit native handle. Both are resolved below.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `Initialize`, `Stop`, `CancelWaits` | Direct | Boolean/message ports are straightforward. Lifecycle ordering is stateful but clearly expressible in an automation. |
| `Start` | Direct | CSV avoids requiring an enum collection. Category names are typo-prone; out of scope for this pass (not a numbered recommendation). |
| `Subscribe`, `Unsubscribe` | Direct; filter builder added | Categories, filter JSON, and subscription ID are strings. `BuildFilterJson` now builds the filter JSON from scalar parameters, avoiding hand-escaped JSON. |
| `GetNextEvent` | Direct; handle bridge fixed; JSON overload added | Now also has a `GetNextEvent(..., out string eventJson, out IntPtr hwnd, ...)` overload. `EventData.Hwnd` is now a 64-bit `long` (was `uint`) and its members are properties (were public fields), so the original `EventData` overload is also fixed, not just supplemented. |
| `GetNextEvents` | Proxy friction; JSON overload added | `GetNextEventsJson` now returns the drained events as a JSON array, the same shape `DumpRecentEvents` produces. |
| `HasEvents`, `ClearQueue` | Direct | Subscription ID, count, Boolean, and message are scalar. |
| `SetDebounce` | Direct; enum overload added | A new overload takes the repository-owned `EventName` enum instead of a free-form string; the string overload remains. |
| `SetQueueLimits` | Direct; enum overload added | A new overload takes the repository-owned `EventOverflowPolicy` enum instead of a free-form string; the string overload (and `Block`'s documented `DropNewest`-equivalent behavior) remain unchanged. |
| `DumpRecentEvents` | Direct | JSON is a good scalar escape hatch for diagnostics and avoids `EventData[]`. It does not identify/dequeue the exact event returned by a subscription or wait. |
| `WasWindowCreated` | Direct; filter builder available | All ports are scalar. `BuildFilterJson` now covers the filter-authoring friction. |
| `WaitForWindowCreated`, `WaitForWindowDestroyed`, `WaitForWindowShown`, `WaitForForegroundChanged`, `WaitForDialogAppeared`, `WaitForMenuOpened` | Direct; JSON+handle overload added | Each now also has an overload reporting the matched event as JSON plus a chainable `IntPtr` window handle; the original `EventData` overloads remain. |
| `WaitForTitleChanged`, `WaitForStateChanged` | Direct; JSON+handle overload added | Same as above, alongside their `titleRegex`/`stateRegex` scalar inputs. |

## Public supporting types

- **Done.** `EventData`'s members are now properties (`{ get; internal set; }`)
  rather than public fields, for predictable proxy exposure. `ToJson()`'s
  serializer options no longer need `IncludeFields` since properties
  serialize by default.
- **Done.** `GetNextEvent`/every `WaitForX` method now has a JSON+handle
  overload, so Pega does not depend on successfully receiving an `EventData`
  object proxy just to read an event.
- **Done.** `EventFilter`'s fluent API remains object-based for .NET callers
  (`AnyOfProcesses(params string[])` included) - `BuildFilterJson` is the new
  Pega-facing scalar alternative, covering the same filter fields including a
  `processesCsv` equivalent to `AnyOfProcesses`.

## Recommended changes

1. **Done.** `EventData.Hwnd` is now a signed 64-bit `long` (was `uint`),
   capturing the full native handle without truncation
   (`WinEventEngine.Enrich` now does `hwnd.ToInt64()` instead of
   `unchecked((uint)hwnd.ToInt64())`). For direct chaining, the new JSON+handle
   overloads also return a ready-to-use `IntPtr` (`new IntPtr(eventData.Hwnd)`);
   the `long` value itself serializes losslessly via `ToJson()`/the JSON
   overloads for any invariant/unsigned representation a consumer needs.
2. **Done.** `EventData`'s public fields are now properties.
3. **Done.** Added a JSON+handle overload for `GetNextEvent` and every
   `WaitForX` method (9 methods total), sharing existing private cores.
4. **Done.** Added `GetNextEventsJson` for batch subscription consumption.
5. **Done.** Added `BuildFilterJson`, building the filter JSON from scalar
   parameters (`process`, `processesCsv`, `className`, `titleContains`,
   `titleMatches`, `hasButtonChildren`, `excludeSelf`).
6. **Done.** Added `EventName` and `EventOverflowPolicy` enums, with enum
   overloads of `SetDebounce`/`SetQueueLimits`; the original string-based
   overloads remain.

## Verdict

Configuration, diagnostics, and now primary event consumption are all
Pega-friendly: every `EventData`-returning method has a JSON (plus, where a
handle is relevant, `IntPtr`) companion overload, and `BuildFilterJson` removes
the hand-authored-JSON requirement for the common filter cases. The 32-bit
window-handle field was a correctness and composability defect, not merely
designer friction - it is fixed at the source (`WinEventEngine`) and
propagates through `EventFilter`'s button-child cache and
`ThrottleDebounce`'s per-handle dedupe key, both of which were also keyed by
the truncated `uint` before this pass. All six recommended changes are
implemented additively: every original overload remains for backward
compatibility or .NET consumers.
