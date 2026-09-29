# Long-wait recipes

These are the same patterns as the [InterruptUtils long-wait recipes](../../interruptutils/Documentation/LongWaitRecipes.md),
applied to popups inside a browser. They describe the designed behaviour; it has not yet been run
against a real browser, so confirm the names and roles with a watch-only rule first (see
[Rules](Rules.md#watch-only-find-out-what-a-real-page-shows)).

## A `confirm()` during a long report

**Scenario:** The automation clicks *Export* in a web application and waits up to ten minutes for the
download. Partway through, the application shows a JavaScript `confirm("Session about to expire.
Stay signed in?")` box. The wait step is blocked, so nothing else in the automation can click OK.

```csharp
// Once, before the long step:
browserInterrupt.AddNativeDialogDismissRuleByName(
    "stay-signed-in", nameContains: "", messageContains: "stay signed in", processName: "chrome",
    targetElementName: "OK", out _);
browserInterrupt.Start(out string message);

// The long step - this thread is blocked here, but the dialog is still dismissed:
files.WaitForFileToExistSimple(@"C:\Reports\export.csv", 600000, 1000, out _);

// Afterwards - did we have to intervene?
browserInterrupt.GetDismissalCount("stay-signed-in", out int times, out _);
if (times > 0)
    Logger.Info($"Kept the session alive {times} time(s) during the wait.");

browserInterrupt.Stop(out _);
```

The wait does not need to know about the dialog. The handler invoked OK on a background thread while
the automation's thread sat in `WaitForFileToExistSimple`.

## A cookie banner that covers the page

**Scenario:** A site shows a cookie-consent banner over its content on the first visit, and the
step that clicks a button on the page cannot reach it.

```csharp
browserInterrupt.AddPageOverlayDismissRuleByName(
    "cookie-banner", nameContains: "cookie", messageContains: "", processName: "msedge",
    targetElementName: "Accept all", out _, roleContains: "dialog");
browserInterrupt.Start(out _);
```

The banner is an element inside the page, found by walking the browser window's UI Automation tree, so
it is noticed within roughly the overlay sweep interval (default 2 s) or sooner when the page changes.
Start the handler *before* navigating, and give the page a moment to raise the banner before the
next step depends on it being gone: check `GetDismissalCount("cookie-banner", ...)` if a step must not
proceed until it is dismissed.

## Answer two dialogs differently

A "Discard unsaved changes?" `confirm()` and a "Refresh needed" `alert()` need different answers.
Give each its own rule; the first match wins, so put the more specific one first.

```csharp
browserInterrupt.AddNativeDialogDismissRuleByName("discard-changes", "", "unsaved changes", "chrome", "OK", out _);
browserInterrupt.AddNativeDialogDismissRuleByName("ack-refresh", "", "refresh needed", "chrome", "OK", out _);
```

Choosing OK on a discard prompt is a decision about the page's data: add the rule only if that is what
you want whenever it appears.

## Find out what appears before choosing how to answer

Run once with watch-only rules and read the log:

```csharp
browserInterrupt.AddNativeDialogWatchOnlyRule("chrome-dialogs-native", "", "", "chrome", out _, roleContains: "dialog");
browserInterrupt.AddPageOverlayWatchOnlyRule("chrome-dialogs", "", "", "chrome", out _, roleContains: "dialog");
browserInterrupt.Start(out _);
// ... run the automation normally ...
browserInterrupt.GetLogJson(100, out string json, out _);   // name, role, message and process of what matched
```

Then replace them with specific dismiss rules.

## A popup that was not dismissed

`HasUnresolvedPopup` is `true`, or the log has a `DismissFailed` entry, or nothing happened at all. In
order:

1. **Nothing in the log?** No rule matched (or the popup is out of reach). Add a watch-only rule for the
   browser, as above, and compare the name, role, message and process it reports with your rule. Check
   that the browser is not headless, is not running on a locked screen, and is a browser that exposes UI
   Automation.
2. **Works for a while, then stops seeing overlays?** A large page can exceed `maxOverlayNodes` or
   `maxOverlayDepth`; raise them in `Start`.
3. **`DismissFailed`: the element was not found?** The `Detail` names what was being looked for; fix
   `targetElementName` (or use the by-automation-ID rule, remembering that IDs are best-effort for page
   content).
4. **`DismissFailed`: supports neither invoke nor toggle?** The element you named is not clickable
   through UI Automation. Target its clickable parent or child instead, or use
   [UIAutomationUtils](../../uiautomationutils/README.md) from a step.
5. **`InterruptError` about too many dismissals?** The popup returns each time; see
   [Lifecycle](Lifecycle.md#a-popup-that-keeps-coming-back).
6. **Is the popup owned by the automation's own process?** It is never touched.
7. **Did a step hang around `Pause`/`RemoveRule`?** A hung browser can block them; see
   [Lifecycle](Lifecycle.md#pause-can-block-on-a-hung-browser).
