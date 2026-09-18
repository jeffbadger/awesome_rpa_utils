# Events and results

The handler reports what it did two ways: **events**, and a **log with counts** that a
later step can read. Prefer the second when in doubt - events are only useful if your
automation surface can subscribe to them, and they arrive on a different thread.

## The events

| Event | Raised when |
|---|---|
| `PopupDismissed` | A popup was dismissed by a rule. |
| `PopupDismissFailed` | A popup matched a rule but could not be dismissed. `Detail` says why (no native buttons, no button matching the rule, still open after N attempts). |
| `PopupDetected` | A popup matching a *watch-only* rule appeared. It is not touched. |
| `InterruptError` | The handler has a problem, such as a rule that stopped itself for dismissing too many popups. |

The first three carry an `InterruptPopupEventArgs`:

| Property | Meaning |
|---|---|
| `RuleName` | The rule that matched. |
| `Title` | The popup's title. |
| `MessageText` | The popup's message, or empty if it has none or it could not be read. |
| `ProcessName`, `ProcessId` | The application that owns the popup. |
| `ButtonClicked` | The button that was clicked (`(close)` for a close rule); empty for a watch or a failure. |
| `Attempts` | How many dismissals were attempted (0 for a watch-only detection). |
| `TimestampUtc` | When it happened. |
| `Detail` | Why a dismissal failed; otherwise empty. |

`InterruptError` carries an `InterruptErrorEventArgs` (`RuleName`, `Message`, `TimestampUtc`).

## Events arrive on a worker thread

Handlers run on the handler's worker thread, **not the automation's thread**. So:

- Keep a handler short. A slow handler delays the next popup.
- Do not touch anything that must stay on the automation's thread from inside it.
- Do not call `Stop` or dispose the component from a handler.
- An exception in one subscriber is contained: the other subscribers and the watch carry on.

## Reading the same information without an event

```csharp
// After a long wait: did anything get dismissed?
interrupt.GetDismissalCount("keep-session", out int count, out _);
interrupt.GetTotalDismissals(out int total, out _);

// Is a popup that matched a dismiss rule still open (being retried, or given up on)?
interrupt.HasUnresolvedPopup(out bool stuck, out _);

// What happened, oldest first:
interrupt.GetLogJson(20, out string json, out _);
// [{"kind":"Dismissed","rule":"keep-session","title":"Session Timeout",
//   "message":"Your session will expire in 30 seconds.","process":"ClaimsApp","processId":4821,
//   "button":"Yes","attempts":1,"timestampUtc":"2026-09-18T19:58:03.1621413Z","detail":""}]

interrupt.GetLastEventJson(out string last, out _);   // just the newest; "{}" if none
interrupt.ClearLog(out _);                            // counts are kept
```

`kind` is `Detected`, `Dismissed`, `DismissFailed` or `Error`. The log keeps the newest
500 entries. `HasUnresolvedPopup` reflects the handler's most recent pass, so it can lag a
popup's arrival by a moment.

Counts are kept until the rule is removed; `GetTotalDismissals` counts every popup
dismissed since the component was created.
