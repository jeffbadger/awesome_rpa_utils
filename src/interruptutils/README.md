# InterruptAutomation

A Pega Robot Studio-ready component (`InterruptUtils`) that watches for known
popups on its own background threads and dismisses them while the automation is
busy. An automation's steps run one at a time on a single thread and cannot be
interrupted, so a popup that appears during a long wait (a session-timeout
warning, a licence nag, a "Save changes?" prompt) would otherwise stall the run
until someone clicked it. This is the one way to handle it.

You describe each popup once with a rule (by title, message text and/or owning
process, plus the button to click), call `Start`, and carry on. The component
notices new windows through window events, clicks the named button on a worker
thread, checks that the popup really went away, and records what happened in a
log you can query and in events.

- Target framework: `net8.0-windows` and `net10.0-windows`
- Namespace: `InterruptAutomation`
- Assembly: `InterruptAutomation`

See the [Documentation](Documentation/README.md) folder for worked scenarios.

This component carries its own small window-event hook and dialog-click code
instead of referencing [WinEventUtils](../wineventutils/WinEventUtils.cs) or
[DialogUtils](../dialogutils/DialogUtils.cs): every component in this repo is
fully standalone, with no project references between them.

## Enums

### `InterruptButton`
A standard Windows dialog button, identified by its well-known control ID, for
`AddDismissRuleById`: `Ok` (1), `Cancel` (2), `Abort` (3), `Retry` (4),
`Ignore` (5), `Yes` (6), `No` (7).

## Constructors

| Constructor | Description |
|---|---|
| `InterruptUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `InterruptUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Types

### `InterruptPopupEventArgs`
Raised with `PopupDetected`, `PopupDismissed` and `PopupDismissFailed`:
`RuleName`, `Title`, `MessageText`, `ProcessName`, `ProcessId`, `ButtonClicked`
(`(close)` for a close rule), `Attempts`, `TimestampUtc`, and `Detail` (why a
dismissal failed; otherwise empty).

### `InterruptErrorEventArgs`
Raised with `InterruptError`: `RuleName`, `Message`, `TimestampUtc`.

## Events

All events are raised on a **worker thread, not the automation's thread**.
Handlers must be quick and thread-safe; an exception thrown by one subscriber
is contained and never stops the others or the watch. See
[Events](Documentation/Events.md), and [Results](#results) for reading the same
information without an event.

| Event | Type | Description |
|---|---|---|
| `PopupDismissed` | `EventHandler<InterruptPopupEventArgs>` | A popup was dismissed by a rule. |
| `PopupDismissFailed` | `EventHandler<InterruptPopupEventArgs>` | A popup matched a rule but could not be dismissed; `Detail` says why. |
| `PopupDetected` | `EventHandler<InterruptPopupEventArgs>` | A popup matching a watch-only rule appeared (it is not touched). |
| `InterruptError` | `EventHandler<InterruptErrorEventArgs>` | The handler has a problem, such as a rule that stopped itself for dismissing too many popups. |

## Methods

Every method except `IsRunning` returns `bool` (success) with an `out string message`
explaining why on failure; `IsRunning` returns just the `bool`. None of them throws.

### Rules

At least one of `titleContains`, `messageContains` and `processName` is required
on every rule, so no rule can mean "click whatever dialog appears". Text is
matched by case-insensitive substring (literally: no wildcards or regular expressions); every
criterion you set must match. `messageContains` looks only at the first non-empty text control
in the popup. While looking for a matching rule it is read only for a rule whose class, title and
process already matched (reading it is a call into the popup's application), so set `titleContains`
or `processName` as well; once a rule is chosen (and has not stopped itself for dismissing too many
popups) it is also read for the events and the log, and again each time the popup is looked at. Rules are tried in the order added and the first match wins. See
[Rules](Documentation/Rules.md).

| Method | Signature | Description |
|---|---|---|
| `AddDismissRuleByText` | `bool AddDismissRuleByText(string ruleName, string titleContains, string messageContains, string processName, string buttonText, out string message, bool exactButtonText = true, string className = null)` | Dismisses a matching popup by clicking the button with this text (ignoring case and the `&` access-key marker). |
| `AddDismissRuleById` | `bool AddDismissRuleById(string ruleName, string titleContains, string messageContains, string processName, InterruptButton button, out string message, string className = null)` | Dismisses a matching popup by clicking a standard button (OK, Cancel, Yes...) by control ID; independent of the language of the button text. |
| `AddCloseWindowRule` | `bool AddCloseWindowRule(string ruleName, string titleContains, string messageContains, string processName, out string message, string className = null)` | Dismisses a matching popup by closing its window, for a popup with no button to click. |
| `AddWatchOnlyRule` | `bool AddWatchOnlyRule(string ruleName, string titleContains, string messageContains, string processName, out string message, string className = null)` | Only reports a matching popup (event and log); never touches it. |
| `RemoveRule` | `bool RemoveRule(string ruleName, out string message)` | Removes a rule and its dismissal count. |
| `ClearRules` | `bool ClearRules(out string message)` | Removes every rule. The log is kept. |
| `SetRuleEnabled` | `bool SetRuleEnabled(string ruleName, bool enabled, out string message)` | Turns a rule off or on without removing it. Turning it on also clears a runaway stop, makes it look again at popups already open, and retries popups it had given up on; turning it off returns once any dismissal already under way has finished. |
| `ListRulesJson` | `bool ListRulesJson(out string rulesJson, out string message)` | Lists the rules with their state, whether each has stopped itself, and its dismissal count, as JSON. |

### Lifecycle

| Method | Signature | Description |
|---|---|---|
| `Start` | `bool Start(out string message, int sweepIntervalMs = 1000, int maxAttempts = 3, int maxDismissalsPerMinute = 20)` | Starts watching on background threads; returns once the window-event hooks are installed (normally milliseconds). Stops any other instance still running that has this guard, in this process or another, and waits up to about 5 s for it first. |
| `Stop` | `bool Stop(out string message)` | Stops watching. Rules, counts and the log are kept. Succeeds when not running. |
| `IsRunning` | `bool IsRunning()` | Whether watching is running. |
| `Pause` | `bool Pause(out string message)` | Stops the handler touching popups until `Resume`, without stopping the watch. Returns once any dismissal already under way has finished. |
| `Resume` | `bool Resume(out string message)` | Lets the handler dismiss popups again. Popups that appeared meanwhile and are still open are then dealt with. |

### Results

| Method | Signature | Description |
|---|---|---|
| `GetDismissalCount` | `bool GetDismissalCount(string ruleName, out int count, out string message)` | How many popups a rule has dismissed. |
| `GetTotalDismissals` | `bool GetTotalDismissals(out int total, out string message)` | How many popups all rules have dismissed together. |
| `HasUnresolvedPopup` | `bool HasUnresolvedPopup(out bool hasUnresolvedPopup, out string message)` | Whether a popup a dismiss rule matched is still open (being retried, or given up on). |
| `GetLastEventJson` | `bool GetLastEventJson(out string eventJson, out string message)` | The most recent log entry as JSON (`{}` if none). |
| `GetLogJson` | `bool GetLogJson(int maxEntries, out string logJson, out string message)` | The most recent log entries, oldest first, as a JSON array. The log keeps the last 500. |
| `ClearLog` | `bool ClearLog(out string message)` | Empties the log. Counts are kept. |

## Notes & Caveats

- **Nothing runs on the automation's thread.** A hook thread receives window
  events and does almost nothing (a class check and a hand-off), because a stalled
  event pump stalls every event delivered to it. A separate worker thread does the
  reading, clicking and event-raising. `Start` returns as soon as the hooks are installed (normally a few
  milliseconds), not after any popup has been handled.
- **A periodic scan backs up the events.** Every `sweepIntervalMs` (default 1 s) the
  worker also checks every visible top-level window. That finds popups that were
  already open when you called `Start`, popups whose applications do not raise
  standard window events, and windows in the automation's own process, which the
  event hook cannot see. Pass `0` to rely on events alone.
- **A window announces itself before its controls exist.** So a popup is looked at
  again at about 0, 150, 400, 1000 and 2000 ms after it is first seen, until a rule
  matches, it is dismissed, or it goes away.
- **A dismissal is only counted once the popup is gone.** After clicking, the worker
  checks the window closed; if not, it retries up to `maxAttempts` times and then raises
  `PopupDismissFailed`. A click on a disabled button is ignored by Windows, so a popup
  whose button never enables ends as a failure, not a silent success.
- **Popups owned by the automation's own process are never touched.** A step that
  drives its own dialog is safe from the handler by default. For the other case - a
  popup from another application that a step is deliberately driving, for example with
  [DialogUtils](../dialogutils/README.md) - use `Pause`/`Resume` or `SetRuleEnabled`.
- **A popup that keeps coming back cannot loop forever.** A rule that has dismissed
  `maxDismissalsPerMinute` popups in the last minute stops itself and raises
  `InterruptError`; `SetRuleEnabled(rule, true)` turns it back on. While stopped it still claims
  its popup (so a later rule does not act on it) and the popup stays counted by
  `HasUnresolvedPopup`.
- **Pause and switching a rule off are a hard stop.** They wait for a dismissal that is
  already under way, so nothing the handler does can land after they return. That can take
  a few seconds against a slow application.
- **Rules can change while watching.** A rule you add or switch on takes effect on popups
  that are already open, even with the periodic scan off. Removing a rule forgets what it
  had decided about any popup it had matched.
- **A rule is a decision.** Clicking Yes or No on a "Save changes?" popup changes what
  happens to your data. Write the rule for a popup you have decided how to answer;
  there is no "default button" guessing.
- **Not every popup has buttons to click.** WinUI/UWP and custom-drawn popups have no
  native `Button` controls; the failure message says so. Use a close rule, or
  [KeyboardUtils](../keyboardutils/README.md) / [UIAutomationUtils](../uiautomationutils/README.md).
  Message text is read from the popup's static text controls, so a popup that draws its
  message itself can match on title or process but not on `messageContains`.
- **Attended sessions only.** Window events are not delivered while the screen is locked
  or on a secure desktop (a UAC prompt, the lock screen), so this suits an attended
  session or an unattended one with an active desktop. Electron and Java applications
  may not raise standard window events; the periodic scan helps for them.
- **Events are unproven in Robot Studio.** Read `GetDismissalCount`,
  `HasUnresolvedPopup` and `GetLogJson` from a later step to learn what happened rather
  than depending on an event.
- **Clean-up.** Disposing the component (or `Stop`) unhooks the window events and ends
  both threads. It is safe to dispose while running.
- **One watcher at a time.** `Start` stops any other instance that is still running and has this
  guard, in this process or another in the same session, so an old instance that was never stopped
  cannot keep dismissing popups. An older build without the guard cannot be found or stopped this
  way and keeps running until its process is closed. The stopped instance keeps its rules and log and can be started again
  (which stops the newer one). See [Lifecycle](Documentation/Lifecycle.md).
