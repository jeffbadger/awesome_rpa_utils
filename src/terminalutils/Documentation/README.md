# TerminalUtils Documentation

Real-world usage examples for every method on the `TerminalUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [StartAndAttach](StartAndAttach.md) — launching a console app and checking whether a process can be attached to
- [ReadAndCapture](ReadAndCapture.md) — cursor position, structured rows as JSON, and plain-text screen capture
- [WaitAndDetectChange](WaitAndDetectChange.md) — waiting for a prompt/pattern and waiting for the screen to change
- [WriteInput](WriteInput.md) — injecting keystrokes into a console, focused or not

All examples assume a `TerminalUtils` instance named `tu`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var tu = new TerminalUtils();`).
