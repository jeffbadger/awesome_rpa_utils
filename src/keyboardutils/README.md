# KeyboardAutomation

A Pega Robot Studio-ready component (`KeyboardUtils`) that injects keyboard
input — key presses, holds, combos, and typed text — using the Windows
`SendInput` API, plus a clipboard-paste fallback and keyboard/modifier state
queries via `GetAsyncKeyState`.

- Target framework: `net10.0-windows`
- Namespace: `KeyboardAutomation`
- Assembly: `KeyboardAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

Designed to be used alongside [MouseUtils](../mouseutils/MouseUtils.cs)
(`MouseAutomation`): this component owns keyboard/text input, MouseUtils owns
cursor/click input. Input injection requires an interactive, unlocked
desktop; it is blocked on the lock screen / secure desktop and when the
target application runs at a higher integrity level (UIPI).

## Enums

### `VirtualKey`
A keyboard key identified by its Windows virtual-key code: letters (`A`-`Z`),
digits (`D0`-`D9`), function keys (`F1`-`F24`), navigation keys (`Left`,
`Right`, `Up`, `Down`, `Home`, `End`, `PageUp`, `PageDown`), editing keys
(`Enter`, `Tab`, `Space`, `Back`, `Delete`, `Insert`, `Escape`),
modifier keys (`Control`, `Shift`, `Alt`, and side-specific `LControl`/
`RControl`/`LShift`/`RShift`/`LAlt`/`RAlt`), `LWin`/`RWin`, numpad keys
(`Numpad0`-`Numpad9`, `Multiply`, `Add`, `Subtract`, `Decimal`, `Divide`,
`Separator`), and lock keys (`CapsLock`, `NumLock`, `ScrollLock`).

### `ModifierKeys` (Flags)
Modifier keys combinable in `PressKeyWithModifiers` and reported by
`GetActiveModifiers`: `None`, `Control`, `Shift`, `Alt`, `Win`.

## Constructors

| Constructor | Description |
|---|---|
| `KeyboardUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `KeyboardUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Core Press/Hold/Combo

| Method | Description |
|---|---|
| `bool KeyDown(VirtualKey key, out string message)` | Presses and holds a key. Pair with `KeyUp`. Returns True on success; never throws. |
| `bool KeyUp(VirtualKey key, out string message)` | Releases a key previously pressed with `KeyDown`. Returns True on success; never throws. |
| `bool PressKey(VirtualKey key, out string message)` | Presses and releases a key (~20 ms between down and up). Returns True on success; never throws. |
| `bool PressKeyWithModifiers(VirtualKey key, ModifierKeys modifiers, out string message)` | Presses a key while holding modifier keys, injected as one atomic batch. Returns True on success; never throws. |
| `bool PressKeyCombo(out string message, params VirtualKey[] keys)` | Presses all keys down in order, then releases in reverse order, as one atomic batch (e.g. Ctrl+Shift+Esc). Returns True on success; never throws. |
| `bool HoldKey(VirtualKey key, int holdMilliseconds, out string message)` | Holds a key down for the given duration, then releases it. Returns True on success; never throws. |

### Text Typing

| Method | Description |
|---|---|
| `bool TypeText(string text, out string message)` | Types a string via `KEYEVENTF_UNICODE` (default ~10 ms/character). Returns True on success; never throws. |
| `bool TypeText(string text, int delayMilliseconds, out string message)` | Types a string with a custom per-character delay; handles surrogate pairs for non-BMP characters (e.g. emoji). Returns True on success; never throws. |

### Clipboard-Paste Fallback

| Method | Description |
|---|---|
| `bool PasteText(string text, out string message)` | Saves the current clipboard text, sets the clipboard to `text`, sends Ctrl+V, then restores the original clipboard contents. Returns True on success; never throws. |

### State Query & Modifiers

| Method | Description |
|---|---|
| `bool IsKeyDown(VirtualKey key)` | Returns `true` while the given key is currently held down. |
| `bool IsModifierDown(ModifierKeys modifier)` | Returns `true` if every modifier flag set in the argument is currently held down. |
| `ModifierKeys GetActiveModifiers()` | Returns the combination of Ctrl/Shift/Alt/Win currently held, as flags. |

## Notes & Caveats

- **Every input-injecting method returns `bool` with an `out string message`** rather than
  throwing — `SendInput`/clipboard failures (locked desktop, UAC/secure desktop, UIPI
  blocking a higher-integrity target) and invalid arguments (e.g. a null `text`) are both
  reported this way, with `message` set to a human-readable reason whenever the method
  returns `false`. Only the State Query & Modifiers methods (never able to fail) are plain
  `bool`/enum returns with no `message` parameter.
- **`PressKeyWithModifiers`/`PressKeyCombo`** inject their entire sequence as a single
  `SendInput` batch, so real user input cannot interleave mid-sequence.
- **`TypeText`** sends one `SendInput` call per UTF-16 code unit; characters outside the
  Basic Multilingual Plane (many emoji, some CJK extension characters) are sent as two
  code units (a surrogate pair), each with its own key-down/up pair.
- **PasteText** uses raw Win32 clipboard calls directly (not System.Windows.Forms.Clipboard),
  so it has no STA-thread requirement; OpenClipboard can transiently fail if another process
  briefly holds the clipboard open. It also only restores the original clipboard when that
  original was plain text or genuinely empty — if the clipboard held other content (an image,
  files), that content can't be restored and the pasted text is left in place instead of being
  silently cleared.
- **`IsKeyDown`/`IsModifierDown`/`GetActiveModifiers`** are point-in-time polls of real
  physical key state via `GetAsyncKeyState` — they do not distinguish real user input from
  this component's own injected input.
