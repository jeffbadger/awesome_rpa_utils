# MouseUtils Documentation

Real-world usage examples for every method on the `MouseUtils` component, organized
by the same categories used in the source code and the top-level [README](../README.md).

- [Position](Position.md) — reading and moving the cursor
- [Clicks](Clicks.md) — click, double-click, hold, and modifier-key clicks
- [Drag & Drop](DragAndDrop.md) — left-button drag sequences
- [Wheel & Scrolling](Scrolling.md) — vertical and horizontal scroll wheel
- [Cursor Appearance](CursorAppearance.md) — system cursor, visibility, confinement
- [Button State & Screen Info](ButtonStateAndScreenInfo.md) — polling state and screen geometry
- [Input Blocking](InputBlocking.md) — locking out the operator during critical sequences
- [Background Clicks](BackgroundClicks.md) — PostMessage clicks without stealing focus
- [Window-Relative Targeting](WindowRelativeTargeting.md) — real clicks and coordinates relative to a window, plus misclick guards
- [DPI & Physical Coordinates](DpiAndPhysicalCoordinates.md) — high-DPI monitor handling
- [Cursor Highlight](CursorHighlight.md) — the demo/recording highlight ring
- [Human-like Movement](HumanLikeMovement.md) — Bezier-curve cursor movement
- [Verification & Synchronization](VerificationAndSynchronization.md) — pixel checks and busy-cursor waits instead of guessed delays

All examples assume a `MouseUtils` instance named `mouse`, as it would appear
dropped onto a Pega Robot Studio automation's design surface (or instantiated
directly: `var mouse = new MouseUtils();`).
