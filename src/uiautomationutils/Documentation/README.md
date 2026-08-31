# UIAutomationUtils Documentation

Real-world usage examples for every method on the `UIAutomationUtils`
component, organized by the same categories used in the source code and the
top-level [README](../README.md).

- [Find](Find.md) — locating elements by AutomationId, name, class, or control type
- [Properties](Properties.md) — reading an element's name, bounds, and state
- [Actions](Actions.md) — invoking, setting values, toggling, expanding, and selecting
- [Wait](Wait.md) — polling for an element to appear
- [Visual](Visual.md) — highlighting an element on screen
- [OneShot](OneShot.md) — window-handle-scoped find-and-act methods that skip the intermediate element proxy

All examples assume a `UIAutomationUtils` instance named `uia`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var uia = new UIAutomationUtils();`).
