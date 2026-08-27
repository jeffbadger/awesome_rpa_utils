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
| `void KeyDown(VirtualKey key)` | Presses and holds a key. Pair with `KeyUp`. |
| `void KeyUp(VirtualKey key)` | Releases a key previously pressed with `KeyDown`. |
| `void PressKey(VirtualKey key)` | Presses and releases a key (~20 ms between down and up). |
| `void PressKeyWithModifiers(VirtualKey key, ModifierKeys modifiers)` | Presses a key while holding modifier keys, injected as one atomic batch. |
| `void PressKeyCombo(params VirtualKey[] keys)` | Presses all keys down in order, then releases in reverse order, as one atomic batch (e.g. Ctrl+Shift+Esc). |
| `void HoldKey(VirtualKey key, int holdMilliseconds)` | Holds a key down for the given duration, then releases it. |

### Text Typing

| Method | Description |
|---|---|
| `void TypeText(string text)` | Types a string via `KEYEVENTF_UNICODE` (default ~10 ms/character). |
| `void TypeText(string text, int delayMilliseconds)` | Types a string with a custom per-character delay; handles surrogate pairs for non-BMP characters (e.g. emoji). |

### Clipboard-Paste Fallback

| Method | Description |
|---|---|
| `void PasteText(string text)` | Saves the current clipboard text, sets the clipboard to `text`, sends Ctrl+V, then restores the original clipboard contents. |

### State Query & Modifiers

| Method | Description |
|---|---|
| `bool IsKeyDown(VirtualKey key)` | Returns `true` while the given key is currently held down. |
| `bool IsModifierDown(ModifierKeys modifier)` | Returns `true` if every modifier flag set in the argument is currently held down. |
| `ModifierKeys GetActiveModifiers()` | Returns the combination of Ctrl/Shift/Alt/Win currently held, as flags. |

## Notes & Caveats

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
