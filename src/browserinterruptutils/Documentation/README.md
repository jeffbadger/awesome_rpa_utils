# BrowserInterruptUtils Documentation

Worked examples for the browser interrupt handler. Every method returns `bool` with an
`out string message` and never throws; the examples check `message` only where it helps.

> The recipes describe how the component is designed to behave with a real browser. That
> behaviour has not yet been verified on a Windows host with Chrome, Edge or Firefox; see
> the pending checks in [TESTING.md](../../../TESTING.md#browserinterruptutils-needs-a-desktop-and-a-real-browser-live-checks-pending).
> Run a watch-only rule first (see [Rules](Rules.md#watch-only-find-out-what-a-real-page-shows))
> to confirm what your browser actually exposes before relying on a dismiss rule.

| Page | What it covers |
|---|---|
| [Rules](Rules.md) | Describing a known browser popup: the two scopes, the matching fields (name, automation ID, role, message, process), the kinds of rule, rule order, and how to discover the names and roles a rule needs. |
| [Lifecycle](Lifecycle.md) | Start, stop, pause and resume; the settings on `Start`; when to use `Pause` around a step that drives a popup itself; the caveat that `Pause` can block on a hung browser. |
| [Events](Events.md) | The four events, the worker-thread rule, and reading the log and counts instead. |
| [Long-wait recipes](LongWaitRecipes.md) | Dismissing a Chrome `confirm()` by clicking OK; a cookie banner by button name; a watch-only rule; diagnosing a popup that did not match. |

See the component [README](../README.md) for the full method list and caveats. For ordinary
Windows dialogs use [InterruptUtils](../../interruptutils/README.md); to fill in or click
standard Win32 dialogs from a step use [DialogUtils](../../dialogutils/README.md); to
inspect a browser's UI Automation tree use [UIAutomationUtils](../../uiautomationutils/README.md).
