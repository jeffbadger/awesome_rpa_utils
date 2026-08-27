# WindowUtils Documentation

Real-world usage examples for every method on the `WindowUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Enumeration & Lookup](EnumerationAndLookup.md) — finding windows by title/class/process
- [State & Geometry](StateAndGeometry.md) — bounds, title, visibility, show/hide/close
- [Activation & Z-Order](ActivationAndZOrder.md) — focus, always-on-top, wait-for helpers
- [Child Windows](ChildWindows.md) — enumerating and finding controls within a window

All examples assume a `WindowUtils` instance named `window`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var window = new WindowUtils();`).
