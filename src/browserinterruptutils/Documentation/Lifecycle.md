# Lifecycle

## Start and stop

```csharp
browserInterrupt.AddNativeDialogDismissRuleByName(
    "confirm-ok", "", "leave this page", "chrome", "OK", out _);

if (!browserInterrupt.Start(out string message))
{
    Logger.Error($"Browser interrupt handler did not start: {message}");
}

// ... the automation carries on, including long waits ...

browserInterrupt.Stop(out _);
```

`Start` returns as soon as the UI Automation hooks are installed (normally milliseconds; it
waits at most 5 seconds for them). Popups matching a rule are dismissed on background threads from
then on. `Stop` removes the UI Automation handlers and signals the worker to exit; rules, counts and
the log are kept, so `Start` can resume later. Once `Stop` returns, no new UI Automation invoke or close
call can start; a call already under way must finish first, so a hung browser can delay `Stop`.
UI Automation returns before the browser acts, so an action issued before `Stop` may still take effect
afterwards, and its dismissal result or an in-progress discovery pass may raise events after `Stop`
returns. `Stop` succeeds even if it was not running, and disposing the component stops it too.

`Start` returns `false` (with a message) if it is already running, a setting is out of range,
UI Automation events could not be started in this session, a previous run is still shutting down, or
another instance would not stop (see below).

### Only one instance watches at a time

Starting an instance stops any other `BrowserInterruptUtils` that is still running, whether it is in
the same process or another one in the same Windows session (a named mutex says who is watching; a
named event asks the holder to stop). So a host that creates a new component for each run, or never
stops or disposes the old one, cannot leave an earlier instance dismissing popups in the background.
`Start` waits up to about five seconds for the other instance to stop and returns `false` with a message
if it has not by then. The instance that was stopped keeps its rules, counts and log, and `IsRunning` on
it returns `false`; starting it again stops the newer one.

The guard is separate from `InterruptUtils`'s, so a browser watcher and a window watcher run side by
side. If the system will not let the component create the named mutex and event, `Start` goes ahead
without the guard.

## The settings on `Start`

```csharp
browserInterrupt.Start(out _, sweepIntervalMs: 1000, overlaySweepIntervalMs: 2000,
    maxAttempts: 3, maxDismissalsPerMinute: 20, maxOverlayNodes: 5000, maxOverlayDepth: 50);
```

| Setting | Default | Meaning |
|---|---|---|
| `sweepIntervalMs` | 1000 | How often (0-60000 ms) to scan the browser's top-level windows for native dialogs as a safety net. It finds dialogs that were already open at `Start` and ones the UI Automation event missed. `0` relies on events alone. Only visible windows of a process some enabled native rule names (or that a page-overlay rule wants to watch) are examined; a rule added later is applied to already-open windows on the next scan. |
| `overlaySweepIntervalMs` | 2000 | How often (0-60000 ms) to walk every watched browser window's page for overlays. `0` turns the periodic walk off (overlays are then found only when the page's structure changes). A value above 0 is raised to 500 ms, because each walk is a series of calls into the browser. |
| `maxAttempts` | 3 | How many times (1-10) to try to dismiss one popup before reporting `PopupDismissFailed`. |
| `maxDismissalsPerMinute` | 20 | How many popups (1-1000) one rule may dismiss in a minute before it stops itself. |
| `maxOverlayNodes` | 5000 | The most elements (1-100000) one overlay walk may visit. A larger page has more room but a slower walk. |
| `maxOverlayDepth` | 50 | The deepest (1-1000) one overlay walk may descend. |

Page changes wake an overlay look at most once per 250 ms per browser window: the first change
wakes one immediately, and any changes inside that interval collapse into one more look when it
ends (a leading edge plus one trailing notification per burst). So even with
`overlaySweepIntervalMs = 0` (periodic sweep off) a burst of page changes always ends with a final
look. That figure has not yet been tuned against real pages.

The first look at a fresh browser tab can be slow because Chromium builds its accessibility tree lazily.

## Pause around a step that drives a popup itself

A rule that matches a popup you *want* to handle in a step would take it from you. Say a step
opens a page that shows a `confirm()` you want to answer *Cancel* on yourself, while a broad rule that
answers OK is active:

```csharp
browserInterrupt.Pause(out _);
try
{
    // ... the step that drives the popup itself ...
}
finally
{
    browserInterrupt.Resume(out _);
}
```

While paused, popups are noticed but not touched. A popup that a dismiss rule matches during the
pause is selected and counted (`HasUnresolvedPopup` is `true` for it) and only the rule's click or
close is held back; a watch-only rule still raises `PopupDetected`, since reporting does not touch the
popup. When you `Resume`, any such popup that is *still open* is dealt with promptly, so a genuine
interruption that arrived during the step is not lost.

To exclude one kind of popup for a longer stretch instead, switch just that rule off with
`SetRuleEnabled`. Popups owned by the automation's own process are never touched.

### Pause can block on a hung browser

`Pause` is a hard stop for *starting* clicks: it returns only once a click call the worker has
already begun has finished, and no further click call starts after it returns. `RemoveRule`,
`ClearRules` and `SetRuleEnabled(rule, false)` wait the same way. The UI Automation call being waited for has **no
timeout**. If the browser (or the popup's process) is frozen, a call into it can hang, and these
methods block for as long as it does; there is no way to abandon the wait. Normally the wait is as
long as one click, which is short. Do not call them from a step that must never stall while a browser
that may be hung is the target.

### An already-issued dismissal may still take effect

`Pause` cannot recall a click that was already delivered. UI Automation returns before the browser
acts, and the handler confirms the result 400 ms (`VerifyDelayMs`) later. So a click issued just
before `Pause` may still close the popup you are about to drive, and its `PopupDismissed` or
`PopupDismissFailed` event may be raised after `Pause` returns. The confirmation only reads state, so
it keeps running while paused; if the popup is still open it schedules a retry, and that retry is
held back until `Resume`.

To be certain a popup is untouched, pause before it appears. Otherwise, after `Pause` check
`HasUnresolvedPopup`, or wait about 400 ms, and re-check that the popup is still there before driving
it. `HasUnresolvedPopup` is `true` for any popup a dismiss rule matched, including one that opened
after `Pause` and is waiting for `Resume`, so it does not by itself say whether a click was already
issued: the 400 ms wait covers that.

## A dismissal is confirmed before it is counted

A successful invoke or close only means UI Automation delivered the call; the browser may not have
acted on it. So the handler keeps the popup, still counted by `HasUnresolvedPopup`, and looks at it
again after 400 ms (`VerifyDelayMs`). If it has closed, `PopupDismissed` is raised and the dismissal is
counted. If it is still open, the call did nothing: no `PopupDismissed` is raised, one of `maxAttempts`
is spent, and the handler tries again (`Pause` during that wait holds the retry back). After
`maxAttempts` it raises `PopupDismissFailed` with a detail saying the action succeeded but the popup is
still open. Stopping the component within those 400 ms drops the confirmation, so a popup closed just
before `Stop` may not be counted.

## A popup that goes away on its own is forgotten

A popup matched by a watch-only rule, or one whose dismissal failed, is left open and is only looked
at again when a sweep finds it again. So that a banner that comes and goes by itself does not sit in
the handler's tracking table until it is full (2000 popups), the worker checks every 2000 ms
(`ReapIntervalMs`) whether each such page overlay - or parked native window, including a browser window
tracked only so its page can be watched - still exists and stops tracking the ones that do not (this
runs even when `sweepIntervalMs` is 0, which only turns native discovery off; a closed window is also un-watched;
at most 256 checks per pass; the rest wait for the next). Only a definite "gone" counts: not being
found by a bounded page walk is never taken as the popup having closed. A popup dropped this way raises
no event (`PopupDismissed` is only for a dismissal the handler performed and saw take effect), and it no
longer counts toward `HasUnresolvedPopup`.

## A popup that keeps coming back

If a rule's popup reappears every time it is dismissed - the page is complaining about something the
click does not fix - the handler would click it forever. Instead a rule that has dismissed
`maxDismissalsPerMinute` popups within a minute (confirmed closed, plus any it has just acted on and is still waiting 400 ms to
confirm, so a burst of simultaneous popups is cut off at the limit) **stops itself**: it raises `InterruptError` and is
listed as `"stopped": true` by `ListRulesJson`. Its popup is then left open, so the problem is visible
instead of hidden.

An action that is later found to have left the popup open stops counting, but the rule stays stopped once it has tripped.
Sort out why it recurs, then `SetRuleEnabled(rule, true, ...)` turns the rule back on.

## Clean-up

Disposing the component stops the watch. It is safe to dispose while running: the UI Automation
handlers are removed and both threads end. If the worker is in the middle of a click on a very slow
browser, it finishes that click and then ends, and a `Start` in that moment reports that the previous
run is still shutting down.
