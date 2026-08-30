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

| Method | Signature | Description |
|---|---|---|
| `KeyDown` | `bool KeyDown(VirtualKey key, out string message)` | Presses and holds a key. Pair with `KeyUp`. Returns True on success; never throws. |
| `KeyUp` | `bool KeyUp(VirtualKey key, out string message)` | Releases a key previously pressed with `KeyDown`. Returns True on success; never throws. |
| `PressKey` | `bool PressKey(VirtualKey key, out string message)` | Presses and releases a key (~20 ms between down and up). Returns True on success; never throws. |
| `PressKeyWithModifiers` | `bool PressKeyWithModifiers(VirtualKey key, ModifierKeys modifiers, out string message)` | Presses a key while holding modifier keys, injected as one atomic batch. Returns True on success; never throws. |
| `PressKeyCombo` | `bool PressKeyCombo(out string message, params VirtualKey[] keys)` | Presses all keys down in order, then releases in reverse order, as one atomic batch (e.g. Ctrl+Shift+Esc). Returns True on success; never throws. |
| `HoldKey` | `bool HoldKey(VirtualKey key, int holdMilliseconds, out string message)` | Holds a key down for the given duration, then releases it. Returns True on success; never throws. |

### Text Typing

| Method | Signature | Description |
|---|---|---|
| `TypeText` | `bool TypeText(string text, out string message)` | Types a string via `KEYEVENTF_UNICODE` (default ~10 ms/character). Returns True on success; never throws. |
| `TypeText` | `bool TypeText(string text, int delayMilliseconds, out string message)` | Types a string with a custom per-character delay; handles surrogate pairs for non-BMP characters (e.g. emoji). Returns True on success; never throws. |

### Clipboard-Paste Fallback

| Method | Signature | Description |
|---|---|---|
| `PasteText` | `bool PasteText(string text, out string message, int postPasteDelayMilliseconds = 50)` | Saves the current clipboard text, sets the clipboard to `text`, sends Ctrl+V, waits `postPasteDelayMilliseconds`, then restores the original clipboard contents. Returns True on success; never throws. |

### State Query & Modifiers

| Method | Signature | Description |
|---|---|---|
| `IsKeyDown` | `bool IsKeyDown(VirtualKey key)` | Returns `true` while the given key is currently held down. |
| `IsModifierDown` | `bool IsModifierDown(ModifierKeys modifier)` | Returns `true` if every modifier flag set in the argument is currently held down. |
| `GetActiveModifiers` | `ModifierKeys GetActiveModifiers()` | Returns the combination of Ctrl/Shift/Alt/Win currently held, as flags. |

## Notes & Caveats

- **Every input-injecting method returns `bool` with an `out string message`** rather than
  throwing — `SendInput`/clipboard failures (locked desktop, UAC/secure desktop, UIPI
  blocking a higher-integrity target) and invalid arguments (e.g. a null `text`) are both
  reported this way, with `message` set to a human-readable reason whenever the method
  returns `false`. Only the State Query & Modifiers methods (never able to fail) are plain
  `bool`/enum returns with no `message` parameter.
- **`PressKeyWithModifiers`/`PressKeyCombo`** inject their entire sequence as a single
  `SendInput` batch, so real user input cannot interleave mid-sequence. If injection
  fails partway through a batch, they send a best-effort release batch for every
  requested key and modifier and report cleanup failure in `message`.
- **`PressKey`/`HoldKey`** attempt key release from guaranteed cleanup after a
  successful key-down. A failed release makes the method return `false`.
- **`TypeText`** sends one `SendInput` call per UTF-16 code unit; characters outside the
  Basic Multilingual Plane (many emoji, some CJK extension characters) are sent as two
  code units (a surrogate pair) in the canonical order — high surrogate down, low
  surrogate down, low surrogate up, high surrogate up — so both code units are held
  before either is released and the target composes a single character.
- **PasteText** uses raw Win32 clipboard calls directly (not System.Windows.Forms.Clipboard),
  so it has no STA-thread requirement. `OpenClipboard` is retried every 25 ms for up to
  ~250 ms, so a clipboard manager briefly holding the clipboard open usually does not
  cause a failure. Restore semantics: a clipboard that was empty is restored empty and a
  clipboard that held plain text — Unicode (`CF_UNICODETEXT`) or ANSI (`CF_TEXT`) — has
  that text restored afterward. **Any other content (an image, files) is destroyed when
  the paste text is set and cannot be restored** — the clipboard is left holding the
  pasted text.
- **Paste text briefly sits on the system clipboard**, so any process monitoring the
  clipboard can observe it while the paste is in flight; prefer `TypeText` for sensitive
  values.
- **Paste delivery is asynchronous at the target's pace** — `PasteText` waits
  `postPasteDelayMilliseconds` (default 50) after sending Ctrl+V before restoring the
  original clipboard. If a target reads the clipboard slowly, it can race the restore
  and receive the *original* text; raise the delay for such targets.
- **`IsKeyDown`/`IsModifierDown`/`GetActiveModifiers`** are point-in-time polls of real
  physical key state via `GetAsyncKeyState` — they do not distinguish real user input from
  this component's own injected input. `IsModifierDown(ModifierKeys.None)` returns `false`
  (no modifier is "held" when none is requested).
