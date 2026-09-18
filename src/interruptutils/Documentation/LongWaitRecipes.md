# Long-wait recipes

## Session timeout during a 10-minute wait

**Scenario:** The automation starts a report in a legacy application and waits up to ten
minutes for it to finish. That application pops up "Your session will expire. Stay signed
in?" after five idle minutes. The wait step is blocked, so nothing else in the automation
can click Yes, and the session dies.

```csharp
// Once, before the long step:
interrupt.AddDismissRuleByText(
    "keep-session", titleContains: "Session Timeout", messageContains: "will expire",
    processName: "ClaimsApp", buttonText: "Yes", out _);
interrupt.Start(out string message);

// The long step - this thread is blocked here, but the popup is still dismissed:
files.WaitForFileToExistSimple(@"C:\Reports\claims.csv", 600000, 1000, out _);

// Afterwards - did we have to intervene?
interrupt.GetDismissalCount("keep-session", out int times, out _);
if (times > 0)
    Logger.Info($"Kept the session alive {times} time(s) during the wait.");

interrupt.Stop(out _);
```

The wait does not need to know about the popup. The handler clicked it on a background
thread while the automation's thread sat in `WaitForFileToExistSimple`.

## Answer two popups differently

A "Save changes?" prompt and a "Disk almost full" warning need different answers. Give each
its own rule; the first match wins, so put the more specific one first.

```csharp
interrupt.AddDismissRuleByText("discard-unsaved", "Confirm", "unsaved changes", "ClaimsApp", "Don't Save", out _);
interrupt.AddDismissRuleByText("ack-disk-warning", "Warning", "disk", "ClaimsApp", "OK", out _);
```

Choosing `Don't Save` here is a decision about your data: only add the rule if discarding is
what you want whenever that prompt appears.

## Find out what appears before choosing how to answer

Run once with watch-only rules and read the log:

```csharp
interrupt.AddWatchOnlyRule("anything-from-app", "", "", "ClaimsApp", out _, className: "*");
interrupt.Start(out _);
// ... run the automation normally ...
interrupt.GetLogJson(100, out string json, out _);   // titles, messages, and owner of every window that appeared
```

Then replace it with specific dismiss rules.

## A popup that was not dismissed

`HasUnresolvedPopup` is `true`, or the log has a `DismissFailed` entry, or nothing happened
at all. In order:

1. **Nothing in the log?** No rule matched. Add a watch-only rule for the application, as
   above, and compare the title, message and process it reports with your rule. A message
   the popup draws itself cannot be matched with `messageContains`; use the title or process.
2. **`DismissFailed`: "no native buttons"?** It is a WinUI/UWP or custom-drawn popup. Use a
   close rule, `KeyboardUtils` (Enter/Escape) or `UIAutomationUtils`.
3. **`DismissFailed`: "no button matching the rule"?** The detail lists the buttons that were
   there; fix `buttonText` (or use `AddDismissRuleById`).
4. **`DismissFailed`: "still open after N attempts"?** The click did not close it: the button
   may be disabled until something is filled in, or the application shows another popup next.
5. **`InterruptError` about too many dismissals?** The popup returns each time; see
   [Lifecycle](Lifecycle.md#a-popup-that-keeps-coming-back).
6. **Is the popup owned by the automation's own process?** It is never touched.
7. **Was the screen locked?** Window events are not delivered while it is, and popups on the
   secure desktop (UAC) cannot be reached at all.
