# BrowserInterruptAutomation

A Pega Robot Studio-ready component (`BrowserInterruptUtils`) that watches for known
popups **inside a web browser** on its own background threads and dismisses them while
the automation is busy. An automation's steps run one at a time on a single thread and
cannot be interrupted, so a browser popup that appears during a long wait (a JavaScript
`confirm()` box, a "Leave site?" prompt, a cookie banner, a session-timeout overlay)
would otherwise stall the run until someone clicked it.

[InterruptUtils](../interruptutils/README.md) does this for ordinary Windows dialogs,
but it works on window handles and cannot see or click anything a browser draws itself.
This component uses **Windows UI Automation** (the same substrate as
[UIAutomationUtils](../uiautomationutils/README.md)) to reach two kinds of browser popup:

| Scope | What it is | Rule methods |
|---|---|---|
| `NativeDialog` | A JavaScript `alert`, `confirm`, `prompt` or `beforeunload` dialog. The browser draws it in a window of its own, with no standard Windows buttons, so `InterruptUtils` and [DialogUtils](../dialogutils/README.md) cannot read or click it. | `AddNativeDialog...` |
| `PageOverlay` | An in-page overlay, such as a styled dialog, a cookie-consent banner or a permission bar. It has no window at all, only elements inside the page. | `AddPageOverlay...` |

You describe each popup once with a rule (by name, message text, role, owning process,
plus the element to click), call `Start`, and carry on. The component notices new
dialogs and page changes through UI Automation events, clicks the named element on a
worker thread, and records what happened in a log you can query and in events.

- Target framework: `net8.0-windows` and `net10.0-windows`
- Namespace: `BrowserInterruptAutomation`
- Assembly: `BrowserInterruptAutomation`

> **Status: not yet verified against a real browser.** The decision logic is covered by
> automated tests against a fake browser, and the real UI Automation code compiles, but it
> has not yet been run on a Windows host with Chrome, Edge or Firefox. What a real browser
> exposes to UI Automation (names, roles, whether a button can be invoked) is exactly what
> those live checks will confirm. See [Notes & Caveats](#notes--caveats) and the pending
> checks in [TESTING.md](../../TESTING.md#browserinterruptutils-needs-a-desktop-and-a-real-browser-live-checks-pending).

See the [Documentation](Documentation/README.md) folder for worked scenarios.

This component carries its own small UI Automation and instance-guard code instead of
referencing [InterruptUtils](../interruptutils/InterruptUtils.cs) or
[UIAutomationUtils](../uiautomationutils/UIAutomationUtils.cs): every component in this
repo is fully standalone, with no project references between them.

## Constructors

| Constructor | Description |
|---|---|
| `BrowserInterruptUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `BrowserInterruptUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Types

### `BrowserPopupEventArgs`
Raised with `PopupDetected`, `PopupDismissed` and `PopupDismissFailed`:
`RuleName`, `Scope` (`NativeDialog` or `PageOverlay`), `Name`, `MessageText`, `Role`,
`ProcessName`, `ProcessId`, `TargetInvoked` (the element that was invoked or closed; `null`
for a watch-only rule or a failure), `Attempts`, `TimestampUtc`, and `Detail` (why a
dismissal failed; otherwise empty).

### `BrowserInterruptErrorEventArgs`
Raised with `InterruptError`: `RuleName`, `Message`, `TimestampUtc`.

## Events

All events are raised on a **worker thread, not the automation's thread**.
Handlers must be quick and thread-safe; an exception thrown by one subscriber
is contained and never stops the others or the watch. See
[Events](Documentation/Events.md), and [Results](#results) for reading the same
information without an event.

| Event | Type | Description |
|---|---|---|
| `PopupDismissed` | `EventHandler<BrowserPopupEventArgs>` | A popup was dismissed by a rule. |
| `PopupDismissFailed` | `EventHandler<BrowserPopupEventArgs>` | A popup matched a rule but could not be dismissed; `Detail` says why. |
| `PopupDetected` | `EventHandler<BrowserPopupEventArgs>` | A popup matching a watch-only rule appeared (it is not touched). |
| `InterruptError` | `EventHandler<BrowserInterruptErrorEventArgs>` | The handler has a problem, such as a rule that stopped itself for dismissing too many popups. |

## Methods

All 22 methods return `bool` (success) and, except `IsRunning`, have a trailing
`out string message` that is `null` on success and explains why on failure. None of them
throws.

### Rules

Every rule must say enough to be specific, and a process name alone never is. A **native
dialog** rule needs at least one of `nameContains`, `messageContains` and `roleContains`
(a process-only rule would match every window of the browser, including the main window);
a native **close** rule needs `nameContains` or `messageContains`. A **page overlay** rule
needs at least one of `roleContains`, `nameContains` and `automationIdContains` (every
element on the page belongs to the browser process, so a process-only or message-only rule
would match them all). Text is matched by case-insensitive substring (literally: no wildcards or
regular expressions); every criterion you set must match. `messageContains` looks only at
the first non-empty text element inside the popup, and reading it is a call into the
browser, so it is read last, only for a popup that passes the other criteria. A rule with
only `nameContains` can also match the popup's own button, because the button is a
candidate too: add `roleContains` (for example `dialog`). Rules are tried in
the order added and the first match wins. See [Rules](Documentation/Rules.md).

The rule methods are named for the scope they apply to. There is deliberately **no close
rule for a page overlay**: an overlay has no window to close, so it is dismissed only by
invoking one of its own elements.

| Method | Signature | Description |
|---|---|---|
| `AddNativeDialogDismissRuleByName` | `bool AddNativeDialogDismissRuleByName(string ruleName, string nameContains, string messageContains, string processName, string targetElementName, out string message, bool exactTargetElementName = true, string roleContains = null)` | Adds a rule that dismisses a matching native dialog by invoking the named descendant element. Returns True if added; never throws. |
| `AddNativeDialogDismissRuleByAutomationId` | `bool AddNativeDialogDismissRuleByAutomationId(string ruleName, string nameContains, string messageContains, string processName, string targetAutomationId, out string message, string roleContains = null)` | Adds a rule that dismisses a matching native dialog by invoking the descendant element with the given automation ID. Returns True if added; never throws. |
| `AddNativeDialogCloseRule` | `bool AddNativeDialogCloseRule(string ruleName, string nameContains, string messageContains, string processName, out string message, string roleContains = null)` | Adds a rule that dismisses a matching native dialog by closing its window. Returns True if added; never throws. |
| `AddNativeDialogWatchOnlyRule` | `bool AddNativeDialogWatchOnlyRule(string ruleName, string nameContains, string messageContains, string processName, out string message, string roleContains = null)` | Adds a rule that only reports a matching native dialog and never touches it. Returns True if added; never throws. |
| `AddPageOverlayDismissRuleByName` | `bool AddPageOverlayDismissRuleByName(string ruleName, string nameContains, string messageContains, string processName, string targetElementName, out string message, bool exactTargetElementName = true, string roleContains = null, string automationIdContains = null)` | Adds a rule that dismisses a matching in-page overlay by invoking the named descendant element. Returns True if added; never throws. |
| `AddPageOverlayDismissRuleByAutomationId` | `bool AddPageOverlayDismissRuleByAutomationId(string ruleName, string nameContains, string messageContains, string processName, string targetAutomationId, out string message, string roleContains = null)` | Adds a rule that dismisses a matching in-page overlay by invoking the descendant element with the given automation ID. Returns True if added; never throws. |
| `AddPageOverlayWatchOnlyRule` | `bool AddPageOverlayWatchOnlyRule(string ruleName, string nameContains, string messageContains, string processName, out string message, string roleContains = null, string automationIdContains = null)` | Adds a rule that only reports a matching in-page overlay and never touches it. Returns True if added; never throws. |
| `RemoveRule` | `bool RemoveRule(string ruleName, out string message)` | Removes a rule. Waits for a dismissal already under way, so a hung browser can delay it. Returns True if it existed; never throws. |
| `ClearRules` | `bool ClearRules(out string message)` | Removes every rule. Waits for a dismissal already under way, so a hung browser can delay it. Returns True on success; never throws. |
| `SetRuleEnabled` | `bool SetRuleEnabled(string ruleName, bool enabled, out string message)` | Turns a rule off or on. Turning off waits for a dismissal already under way, so a hung browser can delay it. Turning on also clears a runaway stop. Returns True on success; never throws. |
| `ListRulesJson` | `bool ListRulesJson(out string rulesJson, out string message)` | Lists the rules with their state and dismissal counts as JSON. Returns True on success; never throws. |

### Lifecycle

| Method | Signature | Description |
|---|---|---|
| `Start` | `bool Start(out string message, int sweepIntervalMs = 1000, int overlaySweepIntervalMs = 2000, int maxAttempts = 3, int maxDismissalsPerMinute = 20, int maxOverlayNodes = 5000, int maxOverlayDepth = 50)` | Starts watching for browser popups in the background and dismissing those that match a rule. Stops any other instance still watching (one that has this guard), in this process or another, and waits a few seconds for it. Returns True on success; never throws. |
| `Stop` | `bool Stop(out string message)` | Stops watching for browser popups. Rules and the log are kept. Returns True on success, including when not running; never throws. |
| `IsRunning` | `bool IsRunning()` | Returns True while watching for browser popups is running. Never throws. |
| `Pause` | `bool Pause(out string message)` | Stops the handler touching popups until Resume, without stopping the watch. Waits for a dismissal already under way, so a hung browser can delay it. Returns True on success; never throws. |
| `Resume` | `bool Resume(out string message)` | Lets the handler dismiss popups again after Pause. Returns True on success; never throws. |

### Results

| Method | Signature | Description |
|---|---|---|
| `GetDismissalCount` | `bool GetDismissalCount(string ruleName, out int count, out string message)` | How many popups a rule has dismissed. Returns True on success; never throws. |
| `GetTotalDismissals` | `bool GetTotalDismissals(out int total, out string message)` | How many popups all rules have dismissed together. Returns True on success; never throws. |
| `HasUnresolvedPopup` | `bool HasUnresolvedPopup(out bool hasUnresolvedPopup, out string message)` | Whether a popup matched by a dismiss rule is still open (being retried, or given up on). Returns True on success; never throws. |
| `GetLastEventJson` | `bool GetLastEventJson(out string eventJson, out string message)` | The most recent log entry as JSON ({} if none). Returns True on success; never throws. |
| `GetLogJson` | `bool GetLogJson(int maxEntries, out string logJson, out string message)` | The most recent log entries as a JSON array, oldest first (the log keeps the last 500). Returns True on success; never throws. |
| `ClearLog` | `bool ClearLog(out string message)` | Empties the log. Counts are kept. Returns True on success; never throws. |

## Notes & Caveats

- **Not yet verified on a real browser.** The automated tests drive the decision logic
  with a fake browser. The real UI Automation probe and event hook compile, but have not
  been run against Chrome, Edge or Firefox on a Windows host, and the two Windows-only
  test classes (`UiaTests`, `HookThreadUiaTests`) have not yet been executed. Treat every
  browser-specific claim below as the design intent until the live checks in
  [TESTING.md](../../TESTING.md#browserinterruptutils-needs-a-desktop-and-a-real-browser-live-checks-pending)
  are done.
- **Headless browsers are not supported.** A headless browser has no window and no UI
  Automation tree, so there is nothing to watch. This is the most likely answer to "why
  does nothing happen".
- **Attended sessions only.** UI Automation events are not delivered while the screen is
  locked or on a secure desktop (a UAC prompt, the lock screen), so this suits an
  attended session or an unattended one with an active desktop.
- **UI Automation must see the browser.** Chrome, Edge and Firefox expose UI Automation;
  a browser or embedded web view that does not is out of reach and no rule will ever
  match it. There is no per-browser special-casing beyond `processName` (for example
  `chrome`, `msedge`, `firefox`).
- **Chromium builds its accessibility tree lazily.** The first UI Automation query against
  a fresh tab, or after the browser has not been asked for a while, can be slow, so the
  first sweep may find a popup later than later ones do.
- **Popups inside an iframe are unverified.** Chromium generally folds iframe content into
  one accessibility tree, so the bounded page walk should reach it, but that has not been
  confirmed against a real page.
- **Page overlays are found by a bounded walk.** Overlay discovery walks the browser
  window's UI Automation tree breadth-first, visiting at most `maxOverlayNodes` elements
  and descending at most `maxOverlayDepth` levels (a very large page can hide an overlay
  beyond that budget). It runs only for browser windows whose process a `PageOverlay` rule
  names; a rule that names no process makes every top-level window a candidate, so
  set `processName` on an overlay rule **and** a `roleContains`, `nameContains` or
  `automationIdContains` (a process name alone is refused). Only elements that pass at
  least one overlay rule's non-message criteria are tracked, and the engine tracks at most
  2000 candidates at once (it records one error if that limit is reached).
- **Structure-changed notifications are throttled.** A page change wakes an overlay look
  at most once per 250 ms per browser window; changes inside that interval are dropped
  and caught by the periodic overlay sweep (`overlaySweepIntervalMs`, default 2 s,
  minimum 500 ms unless 0). The 250 ms figure has not been tuned against real pages.
- **Nothing runs on the automation's thread.** A hook thread receives UI Automation events
  and does almost nothing (a class check and a hand-off); a separate worker thread does the
  reading, invoking and event-raising. `Start` returns as soon as the hooks are installed,
  not after any popup has been handled.
- **A popup announces itself before its contents exist.** So a popup is looked at again at
  about 0, 150, 400, 1000 and 2000 ms after it is first seen, until a rule matches, it is
  dismissed, or it goes away.
- **A dismissal is counted when the invoke succeeds, not when the popup is seen to
  close.** Unlike `InterruptUtils`, the component does not re-check afterwards that the
  popup went away (UI Automation reports success or failure of the invoke itself). If the
  invoke fails the worker retries up to `maxAttempts` times and then raises
  `PopupDismissFailed`. A popup that survives a successful invoke is found again by a later
  sweep and handled again, and the `maxDismissalsPerMinute` limit stops a loop.
- **Popups owned by the automation's own process are never touched.** For a popup from
  another process that a step is deliberately driving, use `Pause`/`Resume` or
  `SetRuleEnabled`.
- **`Pause`, `RemoveRule`, `ClearRules` and `SetRuleEnabled(false)` are a hard stop and can
  block.** They wait for a click already under way, so nothing the handler does can land
  after they return. The UI Automation call it is waiting on has no timeout: if the browser
  is frozen, these methods block for as long as it stays frozen, and there is no way to
  abandon the wait.
- **A popup that keeps coming back cannot loop forever.** A rule that has dismissed
  `maxDismissalsPerMinute` popups in the last minute stops itself and raises
  `InterruptError`; `SetRuleEnabled(rule, true)` turns it back on.
- **A rule is a decision.** Clicking OK or Cancel on a `confirm()` changes what the page
  does next (for example whether it leaves a form with unsaved data). Write a rule only for
  a popup you have decided how to answer.
- **Automation IDs are best-effort for page content.** Chromium does not guarantee that a
  page's own `id` or `data-*` attributes reach UI Automation, so match page elements by
  name and role first; `AutomationId` is more dependable for the buttons of a native
  dialog. See [Rules](Documentation/Rules.md#finding-the-names-and-roles-to-use).
- **Events are unproven in Robot Studio.** Read `GetDismissalCount`, `HasUnresolvedPopup`
  and `GetLogJson` from a later step to learn what happened rather than depending on an
  event.
- **Clean-up.** Disposing the component (or `Stop`) removes the UI Automation handlers and
  ends both threads. It is safe to dispose while running.
- **One watcher at a time.** `Start` stops any other `BrowserInterruptUtils` instance that
  is still running and has this guard, in this process or another in the same session, so an
  old instance that was never stopped cannot keep dismissing popups. It does not interfere
  with an `InterruptUtils` watcher: the two use separate guards and can run side by side.
  See [Lifecycle](Documentation/Lifecycle.md).

## See also

- [InterruptUtils](../interruptutils/README.md) for ordinary Windows dialogs (message
  boxes and other native windows), which this component cannot replace and does not need to
  duplicate.
- [DialogUtils](../dialogutils/README.md) for filling in and clicking standard Win32
  dialogs by window handle.
- [UIAutomationUtils](../uiautomationutils/README.md) for interrogating a browser's UI
  Automation tree to find the names and roles a rule needs, and for driving page elements
  from a step.
