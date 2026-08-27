# KeyboardUtils Documentation

Real-world usage examples for every method on the `KeyboardUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Press/Hold/Combo](PressHoldCombo.md) — single keys, modifier combos, and holds
- [Text Typing](TextTyping.md) — typing whole strings
- [Clipboard-Paste](ClipboardPaste.md) — the Ctrl+V paste fallback
- [State Query](StateQuery.md) — polling key/modifier state

All examples assume a `KeyboardUtils` instance named `keyboard`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var keyboard = new KeyboardUtils();`).
