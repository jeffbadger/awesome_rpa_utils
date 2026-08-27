# KeyboardUtils, WindowUtils, OcrUtils & DialogUtils Design

## Overview

Add four new Pega Robot Studio-ready .NET components to the solution,
following the existing `mouseutils`/`screencaptureutils` pattern exactly:

- **KeyboardUtils** — keyboard input injection and state queries via
  `SendInput`, plus a clipboard-paste fallback for typing.
- **WindowUtils** — window enumeration, geometry, activation, and z-order
  control via the Win32 window APIs.
- **OcrUtils** — screen-region/image OCR via `Windows.Media.Ocr`, with
  structured (positioned) results and text-appearance polling.
- **DialogUtils** — finds and dismisses native dialogs (message boxes,
  common dialogs) by button text/control ID, without stealing focus.

All four fill gaps the existing components already brush up against
(MouseUtils has `GetWindowBounds`/`GetWindowAtPoint` for window-relative
*clicking*, and `WaitForPixelColor`-style polling for visual verification,
but no keyboard, window-management, OCR, or dialog-handling surface exists
yet).

## Goals

- Match the conventions established by `screencaptureutils` (the more
  recent of the two existing components): project file named `<Name>Utils.csproj`,
  assembly/namespace `<Name>Automation`, class `<Name>Utils.cs`.
- Ship with the same documentation shape: a component `README.md` with
  method tables and a "Notes & Caveats" section, plus a `Documentation/`
  folder with one usage-examples file per method category and an index
  `Documentation/README.md`.
- Follow the established error-handling convention: `Win32Exception`
  wrapping `Marshal.GetLastWin32Error()` for failed Win32 calls,
  `ArgumentException` for invalid input, and XML `<exception>` docs on
  every public method that can throw.
- Keep each component fully standalone (no project reference between
  components, or to MouseAutomation/ScreenCaptureAutomation), consistent
  with how `screencaptureutils` and `mouseutils` are independent today even
  though their READMEs note they're meant to be used together. This means
  a couple of small overlaps (e.g. a window-bounds getter existing in both
  MouseUtils and WindowUtils, or DialogUtils duplicating a minimal internal
  version of WindowUtils' window lookup) are expected and acceptable.

## Non-goals

- No global keyboard/mouse hooks or low-level listeners — only
  synthetic-input injection and point-in-time state queries
  (`GetAsyncKeyState`), matching the scope already used elsewhere in the
  repo.
- No automated test project. None of the existing components have one;
  these are thin Win32/WinRT API wrappers that are impractical to unit
  test meaningfully and are instead verified manually/interactively,
  consistent with current repo practice.

## Project Layout

| | KeyboardUtils | WindowUtils | OcrUtils | DialogUtils |
|---|---|---|---|---|
| Folder | `keyboardutils/` | `windowutils/` | `ocrutils/` | `dialogutils/` |
| csproj | `KeyboardUtils.csproj` | `WindowUtils.csproj` | `OcrUtils.csproj` | `DialogUtils.csproj` |
| Assembly / namespace | `KeyboardAutomation` | `WindowAutomation` | `OcrAutomation` | `DialogAutomation` |
| Class | `KeyboardUtils.cs` | `WindowUtils.cs` | `OcrUtils.cs` | `DialogUtils.cs` |
| Target framework | `net10.0-windows` | `net10.0-windows` | `net10.0-windows10.0.19041.0` | `net10.0-windows` |

All four `.csproj` files mirror `mouseutils/MouseAutomation.csproj`:
`LangVersion=latest`, `ImplicitUsings=disable`, `Nullable=disable`,
`Platforms=AnyCPU;x64`, `GenerateDocumentationFile=true`. Each is added as
a new project to `AwesomeRpaUtils.sln` (own solution folder, matching how
`mouseutils`/`screencaptureutils` each have one). `Directory.Build.props`
already applies repo-wide (shared `bin/` output, per-project `obj/`), so no
changes are needed there.

**OcrUtils' target framework is versioned** (`net10.0-windows10.0.19041.0`
instead of the bare `net10.0-windows` the other three use), because it
consumes `Windows.Media.Ocr`, a WinRT API — consuming WinRT from .NET
requires a specific Windows SDK contract version in the TFM. This is a
deliberate, called-out deviation from the otherwise-uniform pattern.

## KeyboardUtils

### Constructors

Same pattern as the existing components: a parameterless constructor
(required for Robot Studio's designer) and an `IContainer`-accepting
constructor.

### Enums

- **`VirtualKey`** — wraps the Win32 `VK_*` codes: letters, digits,
  function keys (F1–F24), Enter, Escape, Tab, Space, Backspace, Delete,
  Insert, Home/End, Page Up/Down, arrow keys, Control/Shift/Alt (Menu),
  LWin/RWin, CapsLock/NumLock/ScrollLock, PrintScreen, and the numpad
  keys — analogous to how `SystemCursorType` wraps `IDC_*`/`OCR_*` codes.
- **`ModifierKeys`** (Flags) — `None`, `Control`, `Shift`, `Alt`, `Win`.
  A separate enum from MouseAutomation's `ModifierKeys` since components
  don't reference each other.

### Methods

**Core Press/Hold/Combo**
- `void KeyDown(VirtualKey key)` — presses and holds a key.
- `void KeyUp(VirtualKey key)` — releases a key.
- `void PressKey(VirtualKey key)` — down+up (~20 ms), like `MouseUtils.Click`.
- `void PressKeyWithModifiers(VirtualKey key, ModifierKeys modifiers)` — one
  atomic `SendInput` batch, like `ClickWithModifiers`.
- `void PressKeyCombo(params VirtualKey[] keys)` — presses all keys down in
  order, then releases in reverse order, as one atomic batch (e.g.
  Ctrl+Shift+Esc where none of the keys is a "modifier" in the flags sense).
- `void HoldKey(VirtualKey key, int holdMilliseconds)` — holds a key down
  for the given duration, then releases — like `ClickAndHold`.

**Text Typing**
- `void TypeText(string text)` — types a string via `KEYEVENTF_UNICODE`
  (default ~10 ms/char delay).
- `void TypeText(string text, int delayMilliseconds)` — custom per-character
  delay; handles surrogate pairs for non-BMP characters.

**Clipboard-Paste Fallback**
- `void PasteText(string text)` — saves current clipboard text (if any),
  sets the clipboard to `text`, sends Ctrl+V, then restores the original
  clipboard contents. Requires an STA thread, same caveat as
  `ScreenCaptureUtils.CaptureToClipboard`.

**State Query & Modifiers**
- `bool IsKeyDown(VirtualKey key)` — wraps `GetAsyncKeyState`.
- `bool IsModifierDown(ModifierKeys modifier)` — single-modifier convenience
  check.
- `ModifierKeys GetActiveModifiers()` — returns the combination of
  Ctrl/Shift/Alt/Win currently held, as flags.

## WindowUtils

### Constructors

Same pattern: parameterless + `IContainer`-accepting.

### Enums

- **`ShowWindowCommand`** — wraps the `SW_*` constants used by
  `SetWindowState`: `Normal`, `Minimized`, `Maximized`, `Hide`, `Restore`.

### Methods

**Enumeration & Lookup**
- `List<IntPtr> GetTopLevelWindows()` — all top-level windows via `EnumWindows`.
- `IntPtr FindWindowByTitle(string title, bool exactMatch = true)`.
- `IntPtr FindWindowByClass(string className)`.
- `List<IntPtr> FindWindowsByProcessId(int processId)` — a process can own
  multiple top-level windows.
- `IntPtr GetForegroundWindow()`.

**State & Geometry**
- `Rectangle GetWindowBounds(IntPtr hWnd)`.
- `void SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height)`.
- `void MoveWindow(IntPtr hWnd, int left, int top)`.
- `void ResizeWindow(IntPtr hWnd, int width, int height)`.
- `string GetWindowTitle(IntPtr hWnd)`.
- `string GetWindowClassName(IntPtr hWnd)`.
- `int GetWindowProcessId(IntPtr hWnd)`.
- `bool IsWindowVisible(IntPtr hWnd)`.
- `bool IsWindowResponding(IntPtr hWnd)` — inverse of `IsHungAppWindow`.
- `void SetWindowState(IntPtr hWnd, ShowWindowCommand command)` — generic
  `ShowWindow` wrapper (covers minimize/maximize/restore/hide/normal).
- `void CloseWindow(IntPtr hWnd)` — sends `WM_CLOSE`.

**Activation & Z-Order**
- `void ActivateWindow(IntPtr hWnd)` — `SetForegroundWindow` wrapper.
- `void SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop)` — `SetWindowPos` with
  `HWND_TOPMOST`/`HWND_NOTOPMOST`.
- `bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)`.
- `bool WaitForWindowToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)`.
- `bool WaitForWindowActive(IntPtr hWnd, int timeoutMs, int pollIntervalMs)`.

**Child / Multi-Window Enumeration**
- `List<IntPtr> GetChildWindows(IntPtr hWndParent)` — `EnumChildWindows`.
- `IntPtr FindChildWindow(IntPtr hWndParent, string title, string className)`.

## OcrUtils

### Constructors

Same pattern: parameterless + `IContainer`-accepting.

### Types

- **`OcrResult`** — a plain class holding recognized lines: `List<OcrLine> Lines`,
  each with `string Text` and a screen-space `Rectangle Bounds`, and each
  line holding `List<OcrWord> Words` (same shape, word-level). No enum
  needed here.

### Methods

**Region/Image → Plain Text**
- `string GetTextFromRegion(int left, int top, int width, int height, string languageTag = null)`.
- `string GetTextFromImageFile(string filePath, string languageTag = null)`.

**Structured Results**
- `OcrResult GetStructuredTextFromRegion(int left, int top, int width, int height, string languageTag = null)`.
- `Rectangle FindTextLocation(string searchText, int left, int top, int width, int height)`
  — returns `Rectangle.Empty` if not found (sentinel return, matching
  `WindowUtils.FindWindowByTitle`'s convention, not an exception).

**Language**
- `List<string> GetAvailableLanguages()` — installed OCR language tags
  (`Windows.Globalization.Language` / `OcrEngine.AvailableRecognizerLanguages`).

**Wait-for-Text Polling**
- `bool WaitForTextToAppear(int left, int top, int width, int height, string expectedText, int timeoutMs, int pollIntervalMs)`.

Region capture is done internally via a small private GDI helper (BitBlt
to a `Bitmap`, then converted to a WinRT `SoftwareBitmap` for the OCR
engine) — not a dependency on ScreenCaptureUtils, keeping the component
standalone per the Goals above.

## DialogUtils

### Constructors

Same pattern: parameterless + `IContainer`-accepting.

### Enums

- **`DialogButton`** — wraps the standard Windows MessageBox control IDs:
  `Ok = 1` (IDOK), `Cancel = 2` (IDCANCEL), `Abort = 3`, `Retry = 4`,
  `Ignore = 5`, `Yes = 6`, `No = 7`.

### Methods

**Find & Click**
- `IntPtr FindDialog(string titlePattern, bool exactMatch = true)`.
- `IntPtr FindButtonByText(IntPtr hDialog, string buttonText)`.
- `IntPtr FindButtonById(IntPtr hDialog, int controlId)` — `GetDlgItem` wrapper.
- `void ClickButton(IntPtr hButton)` — sends `BM_CLICK` via `SendMessage`;
  no cursor movement, works even if the dialog is behind other windows.
- `void ClickDialogButton(IntPtr hDialog, DialogButton button)` — combines
  `GetDlgItem((int)button)` + `ClickButton`.
- `void ClickDialogButtonByText(IntPtr hDialog, string buttonText)` —
  combines `FindButtonByText` + `ClickButton`.

**Read Text**
- `string GetDialogText(IntPtr hDialog)` — the message body: the first
  child control of class `Static` found via `EnumChildWindows`.
- `string GetControlText(IntPtr hControl)` — generic `GetWindowText`
  wrapper for any control handle (buttons, static, edit fields).

**Wait-for-Dialog Polling**
- `bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)`.
- `bool WaitForDialogToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)`.

DialogUtils implements its own minimal internal window-lookup/enumeration
(same technique as WindowUtils: `EnumWindows`/`EnumChildWindows`, `GetWindowText`,
`GetClassName`) rather than referencing the WindowUtils project, per the
standalone-component rule in Goals.

## Documentation Plan

For each of the four components:
- `README.md` — mirrors the existing components' shape: intro paragraph,
  target framework/namespace/assembly line, enums/types section,
  constructors table, methods tables grouped by the categories above, a
  "Notes & Caveats" section covering things like: `PasteText`'s STA-thread
  requirement, `PressKeyCombo`'s atomic-batch behavior (real input can't
  interleave mid-combo), `TypeText`'s per-character delay/surrogate-pair
  handling, window handle lifetime (`IntPtr` handles become invalid once a
  window closes — no method here holds a handle open), `SetAlwaysOnTop`'s
  system-wide/session-persistent effect, OcrUtils' accuracy dependence on
  installed language packs and font/DPI legibility, and DialogUtils'
  `BM_CLICK` being ignored by non-standard/owner-drawn dialogs.
- `Documentation/README.md` — index linking to one file per category, same
  structure as `mouseutils/Documentation/README.md`.
- `Documentation/<Category>.md` — one file per method category, with
  worked examples assuming an instance named `keyboard`/`window`/`ocr`/`dialog`
  respectively (matching the `mouse`/`Documentation` convention).
- Root `README.md` — add a row per component to the components table.

## Error Handling

- Failed Win32 calls (`SendInput` returning 0, `SetWindowPos` failing,
  etc.) throw `Win32Exception(Marshal.GetLastWin32Error(), "<Call> failed.")`.
- Invalid arguments (e.g. an empty `VirtualKey[]` to `PressKeyCombo`, a
  negative width/height to `ResizeWindow`) throw `ArgumentException`.
- `Find*`/`WaitFor*` methods that don't find a match return
  `IntPtr.Zero`/`Rectangle.Empty`/`false` rather than throwing — matching
  how the existing components treat "not found" as a normal, checkable
  outcome rather than an error (e.g. `WaitForPixelColor` returns `bool`).
- OcrUtils' OCR-engine initialization failure (e.g. no language pack
  installed) throws `InvalidOperationException` with a message naming the
  missing language, since this is a configuration problem rather than a
  transient Win32 call failure.

## Testing / Verification

No automated test project (consistent with the rest of the repo). Manual
verification: build the solution, load each component onto a Robot Studio
design surface (or exercise it via a small throwaway console harness), and
confirm each method category against a real window/keyboard/dialog/OCR
target before merging.
