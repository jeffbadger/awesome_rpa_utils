# InterruptUtils Documentation

Worked examples for the interrupt handler. Every method returns `bool` with an
`out string message` and never throws; the examples check `message` only where it helps.

| Page | What it covers |
|---|---|
| [Rules](Rules.md) | Describing a known popup: matching by title, message and process; the four kinds of rule; rule order; class names. |
| [Lifecycle](Lifecycle.md) | Start, stop, pause and resume; the settings on `Start`; when to use `Pause` around a step that drives a dialog. |
| [Events](Events.md) | The four events, the worker-thread rule, and reading the log and counts instead. |
| [Long-wait recipes](LongWaitRecipes.md) | Session-timeout during a 10-minute wait; a popup you must answer differently; diagnosing a popup that did not match. |

See the component [README](../README.md) for the full method list and caveats.
