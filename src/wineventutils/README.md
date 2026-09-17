# WinEventAutomation

A Pega Robot Studio-ready component (`WinEventUtils`) that watches Windows UI events
via the `SetWinEventHook` API and delivers them the moment they happen — no
polling. It exposes **two consumption models over one engine**:

1. **Wait model** — synchronous `WaitForX(...)` calls that block until a matching
   event or timeout (fits the style of the existing utils).
2. **Subscribe model** — start a background watcher; events land in a
   per-subscription queue the robot polls with `GetNextEvent`. Enables
   session-wide monitors (e.g. a "guardian" that watches for unexpected dialogs
   all run long).

- Target framework: `net10.0-windows`
- Namespace: `WinEventAutomation`
- Assembly: `WinEventAutomation`

Designed to be used alongside the other components in this library: eventUtils
produces *triggers* (a dialog appeared, a window was created), and the other
utils act on them — `screencaptureutils` to capture, `dialogutils` to dismiss,
`keyboardutils`/`mouseutils` to drive input.

See the [Documentation](Documentation/README.md) folder for worked examples
of each method category.

## Types

### `WinEventCategory`
A family of WinEvents to watch: `Windows`, `Foreground`, `Dialogs`, `Titles`,
`States`, `Menus`, `WindowOps`, `Session`. `Start`/`Subscribe` each take a
single value directly; `StartCategories`/`SubscribeCategories` take one
Boolean per value.

### `WinEventName`
A specific WinEvent name (as reported in `WinEventData.Category`), for
designer-selectable input to `SetDebounce`'s enum overload instead of a
free-form string: `WindowCreated`, `WindowDestroyed`, `WindowShown`,
`WindowHidden`, `ForegroundChanged`, `FocusChanged`, `DialogAppeared`,
`DialogClosed`, `TitleChanged`, `StateChanged`, `ValueChanged`, `MenuOpened`,
`MenuClosed`, `MenuPopupOpened`, `MenuPopupClosed`, `WindowMinimized`,
`WindowRestored`, `WindowMoved`, `WindowMoveEnded`, `SessionSwitched`.

### `WinEventOverflowPolicy`
What a subscription's queue does when full, for `SetQueueLimits`'s enum
overload: `DropOldest`, `DropNewest`, `Block` (behaves exactly like
`DropNewest` - documented under [Tuning / ops](#tuning--ops)).

### `WinEventFilterField`
Which single field to match on, for `BuildFilterJson`'s designer-selectable
enum overload instead of remembering which of its seven named parameters to
fill in: `Process`, `ProcessesCsv`, `ClassName`, `TitleContains`,
`TitleMatches` (documented under [Filters](#filters)).

## 30-second overview

```csharp
var events = new WinEventUtils();
events.Initialize(out _);
events.Start(WinEventCategory.Windows, out _);

// Wait model: block until notepad's window appears.
bool ok = events.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out WinEventData created, out _);
if (ok)
    Console.WriteLine("notepad appeared: " + created.ToJson());
```

`Start` installs **one** WinEvent hook covering the whole SYSTEM+OBJECT range on a
dedicated background thread with its own message pump. The callback enriches each
event (class, title, pid, process name) with a hard ~30 ms budget and fans it out
to matching subscriptions and waiters. `EVENT_OBJECT_LOCATIONCHANGE` is never
subscribed — see [Known limitations](#known-limitations).

## Lifecycle

| Method | Signature | Description |
|---|---|---|
| `Initialize` | `bool Initialize(out string message)` | Starts the background hook thread (idempotent). Returns True on success; `message` is null on success, a reason otherwise. Never throws. |
| `Start` | `bool Start(WinEventCategory category, out string message)` | Activates a single category and installs the hook — the common case for a simple wait. Fails if categories are already active (call `Stop` first). Returns True on success; `message` is null on success, a reason otherwise. Never throws. |
| `StartCategories` | `bool StartCategories(bool? windows, bool? foreground, bool? dialogs, bool? titles, bool? states, bool? menus, bool? windowOps, bool? session, out string message)` | Same as `Start`, but with one Boolean checkbox per category instead of a comma-separated, typo-prone string. Every flag is nullable — an unwired port on the Pega design surface is treated the same as an explicit `False`. Fails if categories are already active, or if every flag is `false`/unset. Never throws. |
| `Stop` | `bool Stop(out string message)` | Unhooks; queues are preserved so they can still be drained. Returns False with a message when the component was never initialized. Never throws. |
| `Dispose` | `void Dispose()` | Full teardown — unhooks, stops the thread, clears subscriptions/waiters. Safe to call multiple times; the instance is final afterwards — a later `Initialize()` returns False, so create a new instance. |

Call `Initialize()` then `Start(...)`/`StartCategories(...)` before any `WaitForX`
or `Subscribe` call. If the engine is not running, a wait returns False
immediately with a message. To change which categories are watched, call
`Stop()` first — calling `Start`/`StartCategories` again while categories are
already active fails rather than silently replacing them.

## Worked example 1 — Wait: wait for notepad, then click OK

```csharp
var events = new WinEventUtils();
events.Initialize(out _);
events.StartCategories(
    windows: true, foreground: null, dialogs: true,
    titles: null, states: null, menus: null, windowOps: null, session: null,
    out _);

// Launch notepad and wait for its window to appear.
System.Diagnostics.Process.Start("notepad.exe");
bool ok = events.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out WinEventData created, out _);
if (!ok) { /* handle: message explains a timeout vs. a real error */ }

// ... later, a Save dialog appears. Wait for it, then dismiss it with dialogUtils.
ok = events.WaitForDialogAppeared("{\"process\":\"notepad\"}", 10000, out WinEventData dialog, out _);
if (ok)
{
    var dialogs = new DialogAutomation.DialogUtils();
    dialogs.ClickDialogButtonByText(dialog.Hwnd, "OK", out bool wasEnabled, out _);
}
```

## Worked example 2 — Guardian: watch for unexpected dialogs all run long

```csharp
var events = new WinEventUtils();
events.Initialize(out _);
events.Start(WinEventCategory.Dialogs, out _);
events.Subscribe(WinEventCategory.Dialogs, "{\"process\":\"myapp\"}", "guardian", out _);

while (true)
{
    bool hasEvent = events.GetNextEvent("guardian", 5000, out WinEventData dialog, out _);
    if (!hasEvent) continue;

    // Capture the dialog, then dismiss it.
    var capture = new ScreenCaptureAutomation.ScreenCaptureUtils();
    capture.CaptureWindow(dialog.Hwnd, out _, out _);
    var dialogs = new DialogAutomation.DialogUtils();
    dialogs.ClickDialogButtonByText(dialog.Hwnd, "OK", out bool wasEnabled, out _);
}
```

## Worked example 3 — Black box: dump recent events in the exception handler

```csharp
try
{
    // ... robot steps ...
}
catch (Exception ex)
{
    events.DumpRecentEvents(50, out string recent, out _);   // last 50 events as a JSON array
    Log.Write("Automation failed: " + ex.Message + "\nRecent events:\n" + recent);
}
```

The engine keeps the last 500 events in a ring buffer regardless of
subscriptions, so `DumpRecentEvents` and `WasWindowCreated` work even when nothing
was subscribed.

## Categories and their underlying WinEvents

| Category | WinEvents | Event names |
|---|---|---|
| `Windows` | `EVENT_OBJECT_CREATE/DESTROY/SHOW/HIDE` | `WindowCreated`, `WindowDestroyed`, `WindowShown`, `WindowHidden` |
| `Foreground` | `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_FOCUS` | `ForegroundChanged`, `FocusChanged` |
| `Dialogs` | `EVENT_SYSTEM_DIALOGSTART/END` + heuristic: `CREATE`/`SHOW` with class `#32770` | `DialogAppeared`, `DialogClosed` |
| `Titles` | `EVENT_OBJECT_NAMECHANGE` | `TitleChanged` |
| `States` | `EVENT_OBJECT_STATECHANGE` (`VALUECHANGE` is opt-in) | `StateChanged`, `ValueChanged` |
| `Menus` | `EVENT_SYSTEM_MENUSTART/END`, `MENUPOPUPSTART/END` | `MenuOpened`, `MenuClosed`, `MenuPopupOpened`, `MenuPopupClosed` |
| `WindowOps` | `EVENT_SYSTEM_MINIMIZESTART/END`, `MOVESIZE`, `MOVESIZEEND` | `WindowMinimized`, `WindowRestored`, `WindowMoved`, `WindowMoveEnded` |
| `Session` | `EVENT_SYSTEM_SWITCHSTART` | `SessionSwitched` |

## Filters

Every `WaitForX`/`Subscribe` call takes a filter as a JSON string. All fields
are optional — an unset field is a wildcard. There are three ways to produce
that string; **prefer `BuildFilterJson` over hand-authoring the JSON
yourself** — it's easy to typo a key name or forget to escape a quote, and a
malformed filter fails at the call site with a message instead of at compile
time:

```csharp
// Don't: hand-authored JSON — easy to typo a key, forget a quote/comma, or
// misspell "titleContains" and silently get an unfiltered (match-all) result
// instead of an error, since unknown JSON keys are ignored rather than rejected.
events.Subscribe(WinEventCategory.Windows, "{\"process\":\"notepad\",\"class\":\"#32770\",\"titleContains\":\"Save\"}", "sub", out _);

// Do: BuildFilterJson — same result, no JSON to author or escape.
string filterJson = events.BuildFilterJson(process: "notepad", className: "#32770", titleContains: "Save");
events.Subscribe(WinEventCategory.Windows, filterJson, "sub", out _);
```

`BuildFilterJson(process, processesCsv, className, titleContains, titleMatches, hasButtonChildren, excludeSelf)`
takes every field as its own named, optional parameter — omit whichever ones
you don't need. `processesCsv` (a comma-separated list) is the scalar
alternative to `WinEventFilter.AnyOfProcesses(params string[])`, which isn't
Pega-friendly:

```csharp
// Only the fields you pass are included; the rest are wildcards.
string byProcess = events.BuildFilterJson(process: "notepad");
string byClassAndTitle = events.BuildFilterJson(className: "#32770", titleContains: "Save As");
string byProcessList = events.BuildFilterJson(processesCsv: "notepad, wordpad, calc");
```

It never throws, returning `"{}"` (match-all) if every parameter is omitted
or left blank.

### Filtering on one field: the `WinEventFilterField` enum overload

The most common case is filtering on exactly *one* field, where naming seven
parameters just to fill in one is more typing than the JSON it replaces. For
that case, pass an `WinEventFilterField` instead of a named parameter:

```csharp
// Same filter, three ways — pick whichever fits how the value is chosen:

// 1. Hand-authored JSON (don't — shown only for comparison)
string manual = "{\"titleContains\":\"Save As\"}";

// 2. BuildFilterJson with a named parameter (the field is fixed at design time)
string named = events.BuildFilterJson(titleContains: "Save As");

// 3. BuildFilterJson with WinEventFilterField (the field can be a runtime/upstream
//    value - e.g. selected by a designer dropdown or driven by config - while
//    the value itself comes from anywhere)
string viaEnum = events.BuildFilterJson(WinEventFilterField.TitleContains, "Save As");

events.Subscribe(WinEventCategory.Windows, viaEnum, "sub", out _);
```

Use the enum overload when the *field to filter on* is itself a variable in
the automation (a designer-selected dropdown, a value read from config); use
the full `BuildFilterJson` overload when multiple non-Boolean fields are known
at design time, or for `excludeSelf`, which the single-field overload doesn't
cover. Both overloads produce identical JSON for the same field/value.

The enum overload also takes an optional third `hasButtonChildren` parameter,
since that field is rarely a useful filter *by itself* (most windows have a
button somewhere) but is common as a second criterion alongside one other
field:

```csharp
// "A dialog-shaped window from myapp mentioning Save" — one primary field
// (process) plus the dialog heuristic, without needing the full 7-param overload:
string filterJson = events.BuildFilterJson(WinEventFilterField.Process, "myapp", hasButtonChildren: true);
```

Supported JSON keys, for anyone parsing or hand-authoring filter JSON
directly (e.g. from another system): `process`, `processes` (array), `class`,
`titleContains`, `titleMatches` (regex, IgnoreCase|Compiled, 250 ms match
timeout), `hasButtonChildren`, `excludeSelf`.

### Fluent construction (.NET callers)

.NET code with a compile-time-known filter can skip JSON entirely:

```csharp
WinEventFilter.Create().Process("saplogon").Class("#32770").TitleContains("Save As");
```

This isn't available from Pega Robot Studio, which can only wire scalar
inputs — use `BuildFilterJson` there instead.

Invalid input is rejected, never silently ignored: malformed filter JSON and
an uncompilable `titleMatches` regex both make `Subscribe`/`WaitForX` return
False with a message naming the problem. `Start`/`StartCategories`/
`Subscribe`/`SubscribeCategories` all take `WinEventCategory` directly, so
there's no category name to typo. A fluent `TitleMatches("[Bad")` fails
closed — the filter matches nothing.

## Wait methods

| Method | Signature | Description |
|---|---|---|
| `WaitForWindowCreated` | `bool WaitForWindowCreated(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Matches `WindowCreated`. |
| `WaitForWindowDestroyed` | `bool WaitForWindowDestroyed(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Matches `WindowDestroyed`. |
| `WaitForWindowShown` | `bool WaitForWindowShown(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Matches `WindowShown`. |
| `WaitForForegroundChanged` | `bool WaitForForegroundChanged(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Matches `ForegroundChanged`. |
| `WaitForTitleChanged` | `bool WaitForTitleChanged(string filterJson, string titleRegex, int timeoutMs, out WinEventData eventData, out string message)` | Matches `TitleChanged` + title regex. |
| `WaitForDialogAppeared` | `bool WaitForDialogAppeared(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Matches `DialogAppeared`/`DialogClosed` + `#32770` heuristic. |
| `WaitForStateChanged` | `bool WaitForStateChanged(string filterJson, string stateRegex, int timeoutMs, out WinEventData eventData, out string message)` | Matches `StateChanged` + state regex. |
| `WaitForMenuOpened` | `bool WaitForMenuOpened(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Matches `MenuOpened`/`MenuPopupOpened`. |
| `IsWindow` | `bool IsWindow(string filterJson, out IntPtr hwnd, out string message)` | Non-blocking check for a matching live top-level window and returns its handle. |
| `IsDialog` | `bool IsDialog(string filterJson, out IntPtr hwnd, out string message)` | Non-blocking check for a matching live `#32770` dialog and returns its handle. |
| `IsMenu` | `bool IsMenu(string filterJson, out IntPtr hwnd, out string message)` | Non-blocking check for a matching live `#32768` menu window and returns its handle. |
| `WasWindowCreated` | `bool WasWindowCreated(string filterJson, int withinLastMs, out string message)` | Non-blocking lookback over the ring buffer. Returns True if found, False if not found (normal, `message` null) or on failure (`message` set). |
| `CancelWaits` | `bool CancelWaits(out string message)` | Releases all pending waits (each reports a timeout). |

A wait always resolves to exactly one event — the first match, then it stops
listening — so every `WaitForX` method returns a single `WinEventData` object
directly; there's no separate JSON+handle overload or `...AsEventData`
suffix. Reconstruct a chainable `IntPtr` yourself when needed:

```csharp
bool ok = events.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out WinEventData created, out _);
if (ok)
    window.ActivateWindow(created.Hwnd, out _); // ready to pass to WindowUtils directly
```

A wait returns False on any failure — a timeout, a stopped engine, a malformed
filter, or an uncompilable regex — with `message` explaining which (`eventData`
is null either way; there is no separate `timedOut` output).
A timeout's message reads `"Timed out after <n> ms waiting for <event>."`; check
for that prefix if an automation needs to branch on timeout specifically rather
than treat every failure the same way. A `timeoutMs` of 0 or negative is also an
immediate timeout (nothing keeps waiting) — `GetNextEvent`/`GetNextEvents`
treat 0 the same way. **Do not use `WaitForX` for file or
process waits** — these are UI-event waits; a process that never creates a window
(or a file operation that never touches a window) will simply time out. Use
`commandlineutils`/`serviceutils` for those.

## Subscribe methods

| Method | Signature | Description |
|---|---|---|
| `Subscribe` | `bool Subscribe(WinEventCategory category, string filterJson, string subscriptionId, out string message)` | Registers a subscription for a single category — the common case. Returns True on success; `message` is null on success, a reason otherwise (malformed filter JSON, duplicate id). Never throws. |
| `SubscribeCategories` | `bool SubscribeCategories(bool? windows, bool? foreground, bool? dialogs, bool? titles, bool? states, bool? menus, bool? windowOps, bool? session, string filterJson, string subscriptionId, out string message)` | Same as `Subscribe`, but with one Boolean checkbox per category instead of a comma-separated, typo-prone string. Every flag is nullable — an unwired port is treated the same as an explicit `False`. Returns False if every flag is `false`/unset. Never throws. |
| `Unsubscribe` | `bool Unsubscribe(string subscriptionId, out string message)` | Removes a subscription and drops its queue; a blocked `GetNextEvent` is woken and returns False with a message. Returns True if it existed; `message` is null on success, a reason otherwise. |
| `GetNextEvent` | `bool GetNextEvent(string subscriptionId, int timeoutMs, out WinEventData eventData, out string message)` | Blocks up to `timeoutMs` for the next queued event — always exactly one, so it returns `WinEventData` directly. Returns True when an event was dequeued; False on a normal timeout with an empty queue (`message` null) or a real failure such as an unknown subscription (`message` set). |
| `GetNextEvents` | `bool GetNextEvents(string subscriptionId, int maxCount, int drainMs, out WinEventData[] events, out string message)` | Drains up to `maxCount` events into `events`. Returns True on success; `message` is null on success, a reason otherwise. |
| `GetNextEventsJson` | `bool GetNextEventsJson(string subscriptionId, int maxCount, int drainMs, out string json, out string message)` | Same, as a JSON array (the same shape `DumpRecentEvents` produces), for designers without an `WinEventData[]` proxy. |
| `HasEvents` | `bool HasEvents(string subscriptionId, out int count, out string message)` | Reports queued-event count. Returns True when the subscription exists; `message` is null on success, a reason otherwise (unknown subscription). |
| `ClearQueue` | `bool ClearQueue(string subscriptionId, out string message)` | Drops all queued events. Returns True when the subscription exists; `message` is null on success, a reason otherwise. |

## Tuning / ops

| Method | Signature | Description |
|---|---|---|
| `SetDebounce` | `bool SetDebounce(string eventName, int debounceMs, out string message)` | Dedupes per (hwnd, event-name). Defaults: 150 ms for `WindowShown`/`WindowHidden`, 0 otherwise. Save dialogs fire 4-6 SHOWs in ~200 ms; debounce coalesces them to 1. Returns True on success; `message` is null on success, a reason otherwise. |
| `SetDebounce` | `bool SetDebounce(WinEventName eventName, int debounceMs, out string message)` | Same, taking the repository-owned `WinEventName` enum instead of a free-form, typo-prone string. |
| `SetQueueLimits` | `bool SetQueueLimits(int maxEvents, string overflowPolicy, out string message)` | Per-subscription queue bound + policy: `"DropOldest"` (default), `"DropNewest"`, `"Block"`. `"Block"` is accepted for compatibility but behaves exactly like `"DropNewest"` — the hook thread must never block, so a full queue always drops the arriving event. Returns True on success; `message` is null on success, a reason otherwise (invalid arguments). |
| `SetQueueLimits` | `bool SetQueueLimits(int maxEvents, WinEventOverflowPolicy overflowPolicy, out string message)` | Same, taking the repository-owned `WinEventOverflowPolicy` enum (`DropOldest`, `DropNewest`, `Block`) instead of a free-form string. |
| `DumpRecentEvents` | `bool DumpRecentEvents(int count, out string json, out string message)` | Last N events as a JSON array in `json` (ring buffer, max 500). Returns True on success; `message` is null on success, a reason otherwise. |

## Notes & Caveats

- **Every public method returns `bool` and never throws** — abnormal results
  (hook install failure, malformed filter JSON, invalid regex, categories
  already active, unknown subscription) are reported through an `out string
  message` (null on success). Where a method's real answer is itself a yes/no
  (found a match?
  dequeued an event?), that answer **is** the return value rather than a
  separate output — `WaitForX`, `WasWindowCreated`, `GetNextEvent`, and
  `GetNextEvent` all fold a timeout/not-found/empty-queue result
  into `False` plus `message`, rather than an extra `out bool timedOut`/
  `wasCreated`/`hasEvent`. `message` is `null` for that normal negative and set
  for a real operational failure (engine not started, unknown subscription,
  malformed input) — check it when an automation needs to tell the two `False`
  cases apart. Failures are also logged at debug level via
  `System.Diagnostics.Debug.WriteLine`. Hook install failure logs the Win32
  error from `Marshal.GetLastWin32Error()`.
- **`WinEventData` objects are per-consumer copies** — subscriptions and
  waiters each receive their own clone, so mutating one delivered event cannot
  affect another consumer. Treat the object as read-only anyway (its members
  are properties with an `internal` setter, so external code cannot mutate
  them even by choice).
- **`WinEventData.Hwnd` is a signed 64-bit `long`**, not a 32-bit value — a
  32-bit field would truncate a real 64-bit window handle on 64-bit Windows.
  Reconstruct a chainable `IntPtr` for WindowUtils/UIAutomationUtils with
  `eventData.Hwnd`.
- **`Unsubscribe` wakes a blocked `GetNextEvent`** — instead of waiting out its
  full timeout on a removed subscription, the blocked call returns False with a
  message promptly.
- **`EVENT_OBJECT_VALUECHANGE` is opt-in only** — every keystroke fires it, so it
  is not part of the `States` category by default. To receive it, subscribe to
  `States` and call `SetDebounce("ValueChanged", ms)` (or the `WinEventName`-enum
  overload: `SetDebounce(WinEventName.ValueChanged, ms, out _)`).
- **`EVENT_OBJECT_LOCATIONCHANGE` is never subscribed** in any category mask — it
  fires constantly (every move/resize of every window) and would flood the pump.
- **Destroyed-window events still carry Title/ProcessName** — enrichment happens
  inside the callback while the window still exists; after the callback returns
  the window is gone.
- **The callback does near-zero work** — no UIA, OCR, file IO, or blocking calls.
  If the pump stalls, all system event delivery stalls.
- **`Dispose()` is deterministic** — it unhooks and stops the pump thread even if
  the robot re-initializes the component mid-run. A leaked hook would otherwise
  show up later as "events silently stopped".

## Known limitations

- **No guarantee of events from Electron/Java apps.** Electron and Java (Swing/AWT)
  render into a single top-level window and often do not raise the standard
  WinEvents for their internal UI. Prefer `uiautomationutils` for those.
- **Ordering across processes is not guaranteed.** WinEvents from different
  processes can arrive out of order; use `WinEventData.Timestamp` (UTC ticks) for
  any ordering decisions.
- **Attended vs unattended.** WinEvent delivery requires an interactive desktop
  session. On a locked screen or secure desktop, events are not delivered. This
  component is for attended automations and unattended sessions with an active
  interactive desktop.
- **`WINEVENT_SKIPOWNPROCESS`** is set, so events from the robot's own process are
  not delivered. Use `ExcludeSelf` in a filter only when you need to exclude a
  *different* process that happens to be the engine host.
