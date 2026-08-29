# EventAutomation

A Pega Robot Studio-ready component (`EventUtils`) that watches Windows UI events
via the `SetWinEventHook` API and delivers them the moment they happen — no
polling. It exposes **two consumption models over one engine**:

1. **Wait model** — synchronous `WaitForX(...)` calls that block until a matching
   event or timeout (fits the style of the existing utils).
2. **Subscribe model** — start a background watcher; events land in a
   per-subscription queue the robot polls with `GetNextEvent`. Enables
   session-wide monitors (e.g. a "guardian" that watches for unexpected dialogs
   all run long).

- Target framework: `net10.0-windows`
- Namespace: `EventAutomation`
- Assembly: `EventAutomation`

Designed to be used alongside the other components in this library: eventUtils
produces *triggers* (a dialog appeared, a window was created), and the other
utils act on them — `screencaptureutils` to capture, `dialogutils` to dismiss,
`keyboardutils`/`mouseutils` to drive input.

## 30-second overview

```csharp
var events = new EventUtils();
events.Initialize(out _);
events.Start("Windows,Foreground,Dialogs", out _);   // which categories to watch

// Wait model: block until notepad's window appears.
bool ok = events.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out EventData created, out bool timedOut, out _);
if (ok && !timedOut)
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
| `Start` | `bool Start(string categoriesCsv, out string message)` | Activates the given categories (e.g. `"Windows,Foreground,Dialogs"`) and installs the hook. Returns True on success; `message` is null on success, a reason otherwise. Never throws. |
| `Stop` | `bool Stop(out string message)` | Unhooks; queues are preserved so they can still be drained. Returns True on success; `message` is null on success, a reason otherwise. Never throws. |
| `Dispose` | `void Dispose()` | Full teardown — unhooks, stops the thread, clears subscriptions/waiters. Safe to call multiple times; a later `Initialize()` restarts the component. |

Call `Initialize()` then `Start(...)` before any `WaitForX` or `Subscribe` call.
If the engine is not running, a wait returns False immediately with a message.

## Worked example 1 — Wait: wait for notepad, then click OK

```csharp
var events = new EventUtils();
events.Initialize(out _);
events.Start("Windows,Dialogs", out _);

// Launch notepad and wait for its window to appear.
System.Diagnostics.Process.Start("notepad.exe");
bool ok = events.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out EventData created, out bool timedOut, out _);
if (!ok || timedOut) { /* handle */ }

// ... later, a Save dialog appears. Wait for it, then dismiss it with dialogUtils.
ok = events.WaitForDialogAppeared("{\"process\":\"notepad\"}", 10000, out EventData dialog, out timedOut, out _);
if (ok && !timedOut)
{
    var dialogs = new DialogAutomation.DialogUtils();
    dialogs.ClickDialogButtonByText(new IntPtr(dialog.Hwnd), "OK", out _);
}
```

## Worked example 2 — Guardian: watch for unexpected dialogs all run long

```csharp
var events = new EventUtils();
events.Initialize(out _);
events.Start("Dialogs", out _);
events.Subscribe("Dialogs", "{\"process\":\"myapp\"}", "guardian", out _);

while (true)
{
    bool ok = events.GetNextEvent("guardian", 5000, out EventData dialog, out bool hasEvent, out _);
    if (!ok || !hasEvent) continue;

    // Capture the dialog, then dismiss it.
    var capture = new ScreenCaptureAutomation.ScreenCaptureUtils();
    capture.CaptureWindow(new IntPtr(dialog.Hwnd), out _, out _);
    var dialogs = new DialogAutomation.DialogUtils();
    dialogs.ClickDialogButtonByText(new IntPtr(dialog.Hwnd), "OK", out _);
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

Filters are built fluently or parsed from the compact JSON form that crosses the
Pega boundary. All fields are optional — an unset field is a wildcard.

```csharp
// Fluent
EventFilter.Create().Process("saplogon").Class("#32770").TitleContains("Save As");

// JSON (unknown keys are ignored; malformed JSON makes Subscribe return False)
events.Subscribe("Windows", "{\"process\":\"notepad\",\"class\":\"#32770\",\"titleContains\":\"Save\"}", "sub");
```

Supported JSON keys: `process`, `processes` (array), `class`, `titleContains`,
`titleMatches` (regex, IgnoreCase|Compiled), `hasButtonChildren`, `excludeSelf`.

## Wait methods

| Method | Signature | Description |
|---|---|---|
| `WaitForWindowCreated` | `bool WaitForWindowCreated(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)` | Matches `WindowCreated`. |
| `WaitForWindowDestroyed` | `bool WaitForWindowDestroyed(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)` | Matches `WindowDestroyed`. |
| `WaitForWindowShown` | `bool WaitForWindowShown(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)` | Matches `WindowShown`. |
| `WaitForForegroundChanged` | `bool WaitForForegroundChanged(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)` | Matches `ForegroundChanged`. |
| `WaitForTitleChanged` | `bool WaitForTitleChanged(string filterJson, string titleRegex, int timeoutMs, out EventData eventData, out bool timedOut, out string message)` | Matches `TitleChanged` + title regex. |
| `WaitForDialogAppeared` | `bool WaitForDialogAppeared(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)` | Matches `DialogAppeared`/`DialogClosed` + `#32770` heuristic. |
| `WaitForStateChanged` | `bool WaitForStateChanged(string filterJson, string stateRegex, int timeoutMs, out EventData eventData, out bool timedOut, out string message)` | Matches `StateChanged` + state regex. |
| `WaitForMenuOpened` | `bool WaitForMenuOpened(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)` | Matches `MenuOpened`/`MenuPopupOpened`. |
| `WasWindowCreated` | `bool WasWindowCreated(string filterJson, int withinLastMs, out bool wasCreated, out string message)` | Non-blocking lookback over the ring buffer. |
| `CancelWaits` | `bool CancelWaits(out string message)` | Releases all pending waits (each reports a timeout). |

A wait returns False with `timedOut = true` on timeout (`eventData` is null and
`message` explains the timeout); it returns False with `timedOut = false` on an
error such as the engine not being started. **Do not use `WaitForX` for file or
process waits** — these are UI-event waits; a process that never creates a window
(or a file operation that never touches a window) will simply time out. Use
`commandlineutils`/`serviceutils` for those.

## Subscribe methods

| Method | Signature | Description |
|---|---|---|
| `Subscribe` | `bool Subscribe(string categoriesCsv, string filterJson, string subscriptionId, out string message)` | Registers a subscription. Returns True on success; `message` is null on success, a reason otherwise (malformed filter JSON, duplicate id, invalid category). Never throws. |
| `Unsubscribe` | `bool Unsubscribe(string subscriptionId, out string message)` | Removes a subscription and drops its queue. Returns True if it existed; `message` is null on success, a reason otherwise. |
| `GetNextEvent` | `bool GetNextEvent(string subscriptionId, int timeoutMs, out EventData eventData, out bool hasEvent, out string message)` | Blocks up to `timeoutMs` for the next queued event. Returns True when the call succeeded; `hasEvent` is True when `eventData` holds an event (False on timeout). `message` is null on success, a reason otherwise (unknown subscription). |
| `GetNextEvents` | `bool GetNextEvents(string subscriptionId, int maxCount, int drainMs, out EventData[] events, out string message)` | Drains up to `maxCount` events into `events`. Returns True on success; `message` is null on success, a reason otherwise. |
| `HasEvents` | `bool HasEvents(string subscriptionId, out int count, out string message)` | Reports queued-event count. Returns True when the subscription exists; `message` is null on success, a reason otherwise (unknown subscription). |
| `ClearQueue` | `bool ClearQueue(string subscriptionId, out string message)` | Drops all queued events. Returns True when the subscription exists; `message` is null on success, a reason otherwise. |

## Tuning / ops

| Method | Signature | Description |
|---|---|---|
| `SetDebounce` | `bool SetDebounce(string eventName, int debounceMs, out string message)` | Dedupes per (hwnd, event-name). Defaults: 150 ms for `WindowShown`/`WindowHidden`, 0 otherwise. Save dialogs fire 4-6 SHOWs in ~200 ms; debounce coalesces them to 1. Returns True on success; `message` is null on success, a reason otherwise. |
| `SetQueueLimits` | `bool SetQueueLimits(int maxEvents, string overflowPolicy, out string message)` | Per-subscription queue bound + policy: `"DropOldest"` (default), `"DropNewest"`, `"Block"`. Returns True on success; `message` is null on success, a reason otherwise (invalid arguments). |
| `DumpRecentEvents` | `bool DumpRecentEvents(int count, out string json, out string message)` | Last N events as a JSON array in `json` (ring buffer, max 500). Returns True on success; `message` is null on success, a reason otherwise. |

## Notes & Caveats

- **Every public method returns `bool` and never throws** — abnormal results
  (hook install failure, malformed filter JSON, unknown subscription) are
  reported through an `out string message` (null on success), and timeouts
  through `out bool timedOut` / `out bool hasEvent` where a timeout is a normal
  outcome. Failures are also logged at debug level via
  `System.Diagnostics.Debug.WriteLine`. Hook install failure logs the Win32
  error from `Marshal.GetLastWin32Error()`.
- **`EVENT_OBJECT_VALUECHANGE` is opt-in only** — every keystroke fires it, so it
  is not part of the `States` category by default. To receive it, subscribe to
  `States` and call `SetDebounce("ValueChanged", ms)`.
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
  processes can arrive out of order; use `EventData.Timestamp` (UTC ticks) for
  any ordering decisions.
- **Attended vs unattended.** WinEvent delivery requires an interactive desktop
  session. On a locked screen or secure desktop, events are not delivered. This
  component is for attended automations and unattended sessions with an active
  interactive desktop.
- **`WINEVENT_SKIPOWNPROCESS`** is set, so events from the robot's own process are
  not delivered. Use `ExcludeSelf` in a filter only when you need to exclude a
  *different* process that happens to be the engine host.
