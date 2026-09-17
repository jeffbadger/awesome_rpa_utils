# WinEventUtils Pega API Usability Review

## Summary

Lifecycle, subscription configuration, filtering, and queue management use
Pega-friendly scalar strings, integers, and Booleans. Event consumption
previously returned only `WinEventData` objects or arrays, with no single-event
scalar/JSON adapter, and `WinEventData.Hwnd` was a 32-bit `uint` that could
truncate a 64-bit native handle. Both are resolved below.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `Initialize`, `Stop`, `CancelWaits` | Direct | Boolean/message ports are straightforward. Lifecycle ordering is stateful but clearly expressible in an automation. |
| `Start` | Direct; typo-prone CSV removed | Now takes `WinEventCategory` directly for the common single-category case (see addendum below); `StartCategories` (one Boolean per category) covers multi-category activation. |
| `Subscribe`, `Unsubscribe` | Direct; typo-prone CSV removed; filter builder added | `Subscribe` now takes `WinEventCategory` directly for the common single-category case (see addendum below); `SubscribeCategories` (one Boolean per category) covers multi-category subscriptions. Filter JSON and subscription ID are strings; `BuildFilterJson` builds the filter JSON from scalar parameters, avoiding hand-escaped JSON. |
| `GetNextEvent` | Direct; handle field fixed; consolidated to `WinEventData` (see addendum below) | `WinEventData.Hwnd` is now a 64-bit `long` (was `uint`) and its members are properties (were public fields). The JSON+handle overload and `...AsEventData` split were later removed — `GetNextEvent` always dequeues exactly one event, so it returns `WinEventData` directly. |
| `GetNextEvents` | Proxy friction; JSON overload added | `GetNextEventsJson` now returns the drained events as a JSON array, the same shape `DumpRecentEvents` produces. Unlike the singular `GetNextEvent`, this genuinely returns a variable-length collection, so the JSON array form still earns its place. |
| `HasEvents`, `ClearQueue` | Direct | Subscription ID, count, Boolean, and message are scalar. |
| `SetDebounce` | Direct; enum overload added | A new overload takes the repository-owned `WinEventName` enum instead of a free-form string; the string overload remains. |
| `SetQueueLimits` | Direct; enum overload added | A new overload takes the repository-owned `WinEventOverflowPolicy` enum instead of a free-form string; the string overload (and `Block`'s documented `DropNewest`-equivalent behavior) remain unchanged. |
| `DumpRecentEvents` | Direct | JSON is a good scalar escape hatch for diagnostics and avoids `WinEventData[]`. It does not identify/dequeue the exact event returned by a subscription or wait. |
| `WasWindowCreated` | Direct; filter builder available | All ports are scalar. `BuildFilterJson` now covers the filter-authoring friction. |
| `WaitForWindowCreated`, `WaitForWindowDestroyed`, `WaitForWindowShown`, `WaitForForegroundChanged`, `WaitForDialogAppeared`, `WaitForMenuOpened` | Direct; consolidated to `WinEventData` (see addendum below) | Each returns `WinEventData` directly — a wait always resolves to exactly one event, so the JSON+handle overload and `...AsEventData` split were removed as redundant. |
| `WaitForTitleChanged`, `WaitForStateChanged` | Direct; consolidated to `WinEventData` | Same as above, alongside their `titleRegex`/`stateRegex` scalar inputs. |

## Public supporting types

- **Done.** `WinEventData`'s members are now properties (`{ get; internal set; }`)
  rather than public fields, for predictable proxy exposure. `ToJson()`'s
  serializer options no longer need `IncludeFields` since properties
  serialize by default.
- **Superseded.** `GetNextEvent`/every `WaitForX` method briefly had a
  JSON+handle overload so Pega did not depend on receiving an `WinEventData`
  object proxy just to read an event; this was later removed in favor of
  returning `WinEventData` directly (see the consolidation addendum below).
- **Done.** `WinEventFilter`'s fluent API remains object-based for .NET callers
  (`AnyOfProcesses(params string[])` included) - `BuildFilterJson` is the new
  Pega-facing scalar alternative, covering the same filter fields including a
  `processesCsv` equivalent to `AnyOfProcesses`.

## Recommended changes

1. **Done.** `WinEventData.Hwnd` is now a signed 64-bit `long` (was `uint`),
   capturing the full native handle without truncation
   (`WinEventEngine.Enrich` now does `hwnd.ToInt64()` instead of
   `unchecked((uint)hwnd.ToInt64())`). For direct chaining, the new JSON+handle
   overloads also return a ready-to-use `IntPtr` (`new IntPtr(eventData.Hwnd)`);
   the `long` value itself serializes losslessly via `ToJson()`/the JSON
   overloads for any invariant/unsigned representation a consumer needs.
2. **Done.** `WinEventData`'s public fields are now properties.
3. **Superseded.** Added a JSON+handle overload for `GetNextEvent` and every
   `WaitForX` method (9 methods total), sharing existing private cores. Later
   removed in favor of returning `WinEventData` directly (see the consolidation
   addendum below).
4. **Done.** Added `GetNextEventsJson` for batch subscription consumption.
5. **Done.** Added `BuildFilterJson`, building the filter JSON from scalar
   parameters (`process`, `processesCsv`, `className`, `titleContains`,
   `titleMatches`, `hasButtonChildren`, `excludeSelf`).
6. **Done.** Added `WinEventName` and `WinEventOverflowPolicy` enums, with enum
   overloads of `SetDebounce`/`SetQueueLimits`; the original string-based
   overloads remain.

## Verdict

Configuration, diagnostics, and primary event consumption are all
Pega-friendly: every single-event method (`WaitForX`, `GetNextEvent`) returns
`WinEventData` directly, since a wait/dequeue always resolves to exactly one
event; the genuinely multi-event methods (`GetNextEvents`, `DumpRecentEvents`)
keep their JSON array form, where it actually earns its place. `BuildFilterJson`
removes the hand-authored-JSON requirement for the common filter cases. The 32-bit
window-handle field was a correctness and composability defect, not merely
designer friction - it is fixed at the source (`WinEventEngine`) and
propagates through `WinEventFilter`'s button-child cache and
`ThrottleDebounce`'s per-handle dedupe key, both of which were also keyed by
the truncated `uint` before this pass. All six recommended changes were
implemented additively at the time (every original overload remained for
backward compatibility or .NET consumers); recommendation #3's JSON+handle
overload was later removed rather than kept, once it became clear the
single-event guarantee made it redundant rather than complementary — see the
consolidation addendum below.

## Addendum: naming ambiguity fix

A later audit found that `GetNextEvent` and every `WaitForX` method above
still violated the same overload-ambiguity concern the CommandLineUtils
review flagged explicitly (two same-named methods differing only in
`out`-parameter shape, indistinguishable by input alone). Fixed by renaming
the `WinEventData`-object-returning overload of each to `...AsEventData`,
leaving the Pega-friendly JSON+handle overload with the plain name - the same
"most-usable-keeps-the-plain-name" convention applied across this fix wave.
`GetNextEventsJson` and `SetDebounce`/`SetQueueLimits`'s enum overloads were
already distinctly named or type-distinguishable and needed no change.

## Addendum: `Start` category CSV removed in favor of `WinEventCategory`

The original `Start`'s typo-prone category CSV, flagged above as "out of
scope for this pass," was later addressed directly: `Start(string
categoriesCsv, out string message)` was removed (a breaking change, not an
additive overload) and replaced with `Start(WinEventCategory category, out
string message)` for the common single-category case — the most frequent
one, since a simple wait typically only needs to watch one category.
Multi-category activation was already covered by `StartCategories`'s
one-Boolean-per-category shape (added in an earlier pass alongside its own
`bool?` nullability fix, so an unwired Pega port reads as `false`), making it
unnecessary to keep a CSV escape hatch on `Start` for that case.

## Addendum: `Start`/`StartCategories` fail instead of silently replacing active categories

A follow-up review found that calling `Start` or `StartCategories` while
categories were already active silently replaced the active category set
(`_activeCategories = cats` was a plain assignment) rather than merging or
rejecting the call — a caller who called `Start(WinEventCategory.Windows)` then
later `Start(WinEventCategory.Dialogs)`, intending to add `Dialogs`, would
silently stop watching `Windows` instead, with no error. This was
inconsistent with `Subscribe`'s own duplicate-subscription-id check, which
already fails rather than silently overwriting an existing subscription.
Fixed by having the shared `StartCore` reject the call
(`_activeCategories.Count > 0` → `False` with a message) when categories are
already active; the caller must call `Stop()` first, then `Start`/
`StartCategories` succeeds again with the new categories. `Initialize`,
`Stop`, and `Dispose` remain idempotent; only `Start`/`StartCategories`
changed from idempotent-replace to fail-if-already-active.

## Addendum: `Subscribe` moved to the same `WinEventCategory`/Boolean-flags pattern as `Start`

`Subscribe`'s own typo-prone category CSV — left unchanged by the first
addendum above, since it wasn't part of that pass — was later moved to match
`Start`/`StartCategories` for consistency. `Subscribe(string categoriesCsv,
string filterJson, string subscriptionId, out string message)` was removed
(a breaking change, not an additive overload) and replaced with
`Subscribe(WinEventCategory category, string filterJson, string subscriptionId,
out string message)` for the common single-category case, plus a new
`SubscribeCategories(bool? windows, ..., bool? session, string filterJson,
string subscriptionId, out string message)` for multi-category subscriptions
— the same nullable-flag shape as `StartCategories`, so an unwired Pega port
reads as `false`. The now-unused private `TryParseCategories` CSV parser was
removed along with it. Unlike `Start`/`StartCategories`, `Subscribe`/
`SubscribeCategories` were not changed to fail when a category is already
being watched elsewhere — each subscription is independent and keyed by its
own `subscriptionId`, which already fails on a duplicate rather than
silently replacing the existing subscription; there is no shared
"currently active categories" state analogous to `Start`'s to protect.

## Addendum: `WaitForX`/`GetNextEvent` consolidated to a single `WinEventData`-returning method

A later review reconsidered the JSON+handle vs. `WinEventData` split created by
the naming-ambiguity-fix addendum above. The premise behind that split —
Pega might not have a usable `WinEventData` object proxy, so a JSON+handle
escape hatch was needed alongside it — didn't actually need two *methods*:
every `WaitForX` call and `GetNextEvent` resolves to **exactly one** event
(the wait registry removes its waiter the instant the first match completes;
see `WaiterRegistry.Match`), never a list. A single-event result doesn't need
a JSON representation to convey its shape the way a genuinely multi-event
result (`GetNextEvents`/`GetNextEventsJson`, `DumpRecentEvents`) does — so
carrying two full methods per family (one JSON+handle, one `...AsEventData`)
was solving a problem that didn't exist for this shape of data.

Fixed by removing the JSON+handle overload and its `eventJson`/`hwnd` outputs
entirely, and renaming each surviving `...AsEventData` method back down to
the plain, shorter name:

- `WaitForWindowCreated`, `WaitForWindowDestroyed`, `WaitForWindowShown`,
  `WaitForForegroundChanged`, `WaitForTitleChanged`, `WaitForDialogAppeared`,
  `WaitForStateChanged`, `WaitForMenuOpened` (8 methods) — now
  `(string filterJson[, string xRegex], int timeoutMs, out WinEventData
  eventData, out string message)`.
- `GetNextEvent` — now `(string subscriptionId, int timeoutMs, out WinEventData
  eventData, out string message)`.

This is a breaking change, not an additive one — unlike every earlier
recommendation in this review, which kept the original overload alongside
the new one. `ToJsonAndHwnd` (the private helper that built the JSON+handle
shape) was removed as dead code. `GetNextEvents`/`GetNextEventsJson` and
`DumpRecentEvents` are unaffected — they return a genuinely variable-length
collection, where the JSON array form still earns its place; only the
single-event methods lost their JSON sibling. A caller who needs a chainable
`IntPtr` (previously the `hwnd` output) now builds one from the returned
object: `new IntPtr(eventData.Hwnd)` — the same expression `...AsEventData`
callers already had to write.

## Addendum: renamed from EventUtils (2026-09)

Renamed the component (class, namespace/assembly, all `Event*`-prefixed
public/internal types, folder) from `EventUtils`/`EventAutomation` to
`WinEventUtils`/`WinEventAutomation`. `EventUtils` was easy to confuse with
`EventLogUtils` - both start with "Event" but cover entirely unrelated
subsystems (WinEvent/UI-accessibility hooks via `SetWinEventHook` vs.
Windows Event Log entries via `EventLogReader`). `WinEventUtils` names the
actual underlying Win32 API (`SetWinEventHook`/"WinEvent") this component
wraps - already the internal terminology used by this component's own
`WinEventEngine.cs`/`WinEventInterop.cs`, so the rename brings the
public-facing name in line with existing internal naming. Renamed types:
`EventCategory`→`WinEventCategory`, `EventData`→`WinEventData`,
`EventFilter`→`WinEventFilter`, `EventFilterField`→`WinEventFilterField`,
`EventName`→`WinEventName`, `EventOverflowPolicy`→`WinEventOverflowPolicy`.
No behavior change - pure rename, done pre-adoption so no compatibility
shim was needed. `EventLogUtils`' and `SessionUtils`' own
"not to be confused with" cross-reference notes were updated to the new
name.
