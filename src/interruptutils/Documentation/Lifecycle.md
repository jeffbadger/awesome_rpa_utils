# Lifecycle

## Start and stop

```csharp
interrupt.AddDismissRuleByText("keep-session", "Session Timeout", "", "ClaimsApp", "Yes", out _);

if (!interrupt.Start(out string message))
{
    Logger.Error($"Interrupt handler did not start: {message}");
}

// ... the automation carries on, including long waits ...

interrupt.Stop(out _);
```

`Start` returns as soon as the window-event hooks are installed (normally a few milliseconds; it
waits at most 5 seconds for the system to install them). Popups matching a rule are dismissed on background
threads from then on. `Stop` unhooks the window events and ends both threads; rules,
counts and the log are kept, so `Start` can resume later. `Stop` succeeds even if it
was not running, and disposing the component stops it too.

`Start` returns `false` (with a message) if it is already running, a setting is out of
range, window events are unavailable in this session, or a previous run is still
shutting down.

## The settings on `Start`

```csharp
interrupt.Start(out _, sweepIntervalMs: 1000, maxAttempts: 3, maxDismissalsPerMinute: 20);
```

| Setting | Default | Meaning |
|---|---|---|
| `sweepIntervalMs` | 1000 | How often (0-60000 ms) to scan every visible window as a safety net. It finds popups that were already open at `Start`, popups whose application raises no standard window events, and windows the event hook cannot see. `0` relies on events alone. |
| `maxAttempts` | 3 | How many times (1-10) to try to dismiss one popup before reporting `PopupDismissFailed`. |
| `maxDismissalsPerMinute` | 20 | How many popups (1-1000) one rule may dismiss in a minute before it stops itself. |

## Pause around a step that drives a dialog itself

A rule that matches a popup you *want* to handle in a step would take it from you. Say
the automation clicks Save in another application and then drives the Save As dialog with
`DialogUtils`, while a broad rule for that application's "Save" dialogs is active:

```csharp
interrupt.Pause(out _);
try
{
    dialog.WaitForDialog("Save As", 10000, 100, out IntPtr hDialog, exactMatch: true);
    dialog.SubmitFileDialog(hDialog, @"C:\Reports\claims.csv", out _);
}
finally
{
    interrupt.Resume(out _);
}
```

`Pause` returns once any dismissal already under way has finished (a few seconds at most, against
a slow application), so nothing the handler does can land after it returns. While paused, popups
are noticed but not touched. When you `Resume`, any popup that
appeared meanwhile and is *still open* is then dealt with, so a genuine interruption that
arrived during the step is not lost.

To exclude one kind of popup for a longer stretch instead, switch just that rule off with
`SetRuleEnabled`, which likewise returns only once the rule can no longer act.

Popups owned by the automation's own process are never touched, so a dialog your own
process shows needs neither.

## A popup that keeps coming back

If a rule's popup reappears every time it is dismissed - the application is complaining
about something the click does not fix - the handler would click it forever. Instead a
rule that has dismissed `maxDismissalsPerMinute` popups within a minute **stops itself**:
it raises `InterruptError` and is listed as `"stopped": true` by `ListRulesJson`. Its
popup is then left open, so the problem is visible instead of hidden.

Sort out why it recurs, then `SetRuleEnabled(rule, true, ...)` turns the rule back on.

## Clean-up

Disposing the component stops the
watch. It is safe to dispose while running: the hook is removed and both threads end.
If the worker is in the middle of a click on a very slow application, it finishes that
click and then ends, and a `Start` in that moment reports that the previous run is still
shutting down.
