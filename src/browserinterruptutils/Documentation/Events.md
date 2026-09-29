# Events and results

The handler reports what it did two ways: **events**, and a **log with counts** that a later step can
read. Prefer the second when in doubt - events are only useful if your automation surface can subscribe
to them (they are unproven in Robot Studio), and they arrive on a different thread.

## The events

| Event | Raised when |
|---|---|
| `PopupDismissed` | A popup was dismissed by a rule: the invoke or close succeeded and, 400 ms later, the popup was gone. |
| `PopupDismissFailed` | A popup matched a rule but could not be dismissed. `Detail` says why (for example the element to invoke was not found, or supports neither invoke nor toggle, or the attempts ran out, including when the action succeeded but the popup stayed open). |
| `PopupDetected` | A popup matching a *watch-only* rule appeared. It is not touched. |
| `InterruptError` | The handler has a problem, such as a rule that stopped itself for dismissing too many popups. |

The first three carry a `BrowserPopupEventArgs`:

| Property | Meaning |
|---|---|
| `RuleName` | The rule that matched. |
| `Scope` | `NativeDialog` or `PageOverlay`. |
| `Name` | The popup's UI Automation name. |
| `MessageText` | The popup's message (its first non-empty text element), or empty if it has none or it could not be read. |
| `Role` | The popup's UI Automation localized control type, such as `dialog`. |
| `ProcessName`, `ProcessId` | The browser process that owns the popup. |
| `TargetInvoked` | The element that was invoked or closed; `null` for a watch or a failure. |
| `Attempts` | How many dismissals were attempted (0 for a watch-only detection). |
| `TimestampUtc` | When it happened. |
| `Detail` | Why a dismissal failed; otherwise empty. |

`InterruptError` carries a `BrowserInterruptErrorEventArgs` (`RuleName`, `Message`, `TimestampUtc`).

## Events arrive on a worker thread

Handlers run on the handler's worker thread, **not the automation's thread**. So:

- Keep a handler short. A slow handler delays the next popup.
- Do not touch anything that must stay on the automation's thread from inside it.
- Do not call `Stop` or dispose the component from a handler.
- An exception in one subscriber is contained: the other subscribers and the watch carry on.

## Reading the same information without an event

```csharp
// After a long wait: did anything get dismissed?
browserInterrupt.GetDismissalCount("confirm-ok", out int count, out _);
browserInterrupt.GetTotalDismissals(out int total, out _);

// Is a popup that matched a dismiss rule still open (being retried, or given up on)?
browserInterrupt.HasUnresolvedPopup(out bool stuck, out _);

// What happened, oldest first:
browserInterrupt.GetLogJson(20, out string json, out _);
// [{"kind":"Dismissed","rule":"confirm-ok","scope":"NativeDialog","name":"Confirm","role":"dialog",
//   "message":"Leave this page?","process":"chrome","processId":4821,"target":"OK",
//   "attempts":1,"timestampUtc":"2026-09-29T19:58:03.1621413Z","detail":""}]
// (illustrative: the real name, role and message come from what the browser exposes)

browserInterrupt.GetLastEventJson(out string last, out _);   // just the newest; "{}" if none
browserInterrupt.ClearLog(out _);                            // counts are kept
```

`kind` is `Detected`, `Dismissed`, `DismissFailed` or `Error`. The log keeps the newest 500 entries.
`HasUnresolvedPopup` reflects the handler's most recent pass, so it can lag a popup's arrival by a
moment.

Counts are kept until the rule is removed; `GetTotalDismissals` counts every popup dismissed since the
component was created.
