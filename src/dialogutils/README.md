# DialogAutomation

A Pega Robot Studio-ready component (`DialogUtils`) that finds and dismisses
native dialogs (message boxes, common dialogs) by button text or control ID,
via `BM_CLICK` — no cursor movement required, and it works even if the
dialog is behind other windows.

- Target framework: `net10.0-windows`
- Namespace: `DialogAutomation`
- Assembly: `DialogAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

This component implements its own minimal internal window/child-window
enumeration (the same `EnumWindows`/`EnumChildWindows` technique
[WindowUtils](../windowutils/WindowUtils.cs) uses) rather than referencing
the WindowUtils project — fully standalone, like every other component in
this repo.

## Enums

### `DialogButton`
A standard Windows MessageBox button, identified by its well-known control
ID, for use with `ClickDialogButtonById` (cast to `int`, e.g. `(int)DialogButton.Yes`):
`Ok`, `Cancel`, `Abort`, `Retry`, `Ignore`, `Yes`, `No`.

## Constructors

| Constructor | Description |
|---|---|
| `DialogUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `DialogUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Types

### `DialogControlInfo`
Describes one control returned by `ListDialogControls`: `Handle` (`IntPtr`), `Id`
(`int`, the control ID used by `FindButtonById`), `Text` (`string`), `ClassName`
(`string`, the window class, e.g. `Button`/`Static`/`Edit`), and `Enabled` (`bool`,
via `IsWindowEnabled` — a disabled `Button` silently ignores `BM_CLICK`).

## Methods

### Find & Click

| Method | Signature | Description |
|---|---|---|
| `FindDialog` | `bool FindDialog(string titlePattern, out IntPtr hDialog, out bool canDismiss, bool exactMatch = true, int processId = 0)` | Finds a visible top-level dialog window by its title (exact or substring match, case-insensitive; optionally scoped to a process ID via `GetWindowThreadProcessId`) and reports whether it has a native `Button` control DialogUtils can click. Hidden windows are skipped, so it can't match a dialog before it actually appears. Returns True if found; never throws. |
| `CanDismissDialog` | `bool CanDismissDialog(IntPtr hDialog)` | Checks whether a dialog has at least one native `Button` control that `ClickButton`/`ClickDialogButtonById`/`ClickDialogButtonByText` can target. |
| `FindButtonByText` | `bool FindButtonByText(IntPtr hDialog, out IntPtr hButton, string buttonText, bool exactMatch = true)` | Finds a button on a dialog by its visible text (exact match, or substring when `exactMatch: false`). Returns True if found; never throws. |
| `FindButtonById` | `bool FindButtonById(IntPtr hDialog, out IntPtr hButton, int controlId)` | Finds a control on a dialog by its control ID. Returns True if found; never throws. |
| `ClickButton` | `bool ClickButton(IntPtr hButton, int waitForEnabledMs = 500, int pollIntervalMs = 25)` | Invokes a button by sending it `BM_CLICK`, waiting briefly for the button to become enabled first. Returns True if it was enabled when clicked. |
| `ClickDialogButtonById` | `bool ClickDialogButtonById(IntPtr hDialog, int controlId, out bool wasEnabled, int waitForEnabledMs = 500, int pollIntervalMs = 25)` | Invokes a button by its control ID — a well-known `DialogButton` value cast to `int`, or a custom ID from `ListDialogControls`. Returns True if the control was found (and clicked); never throws. |
| `ClickDialogButtonByText` | `bool ClickDialogButtonByText(IntPtr hDialog, string buttonText, out string message, bool exactMatch = true, int maxAttempts = 3, int retryDelayMs = 300)` | Finds a button by its visible text (exact match, or substring when `exactMatch: false`) and invokes it, verifying the dialog actually closed and re-clicking (up to `maxAttempts`) if it didn't. Returns whether it closed; `message` explains why when it returns False. Never throws. |

### Read Text

| Method | Signature | Description |
|---|---|---|
| `GetDialogText` | `string GetDialogText(IntPtr hDialog)` | Gets a dialog's message body (the first `Static`-class child control with non-empty text — skips icon controls, which have no text). |
| `GetControlText` | `string GetControlText(IntPtr hControl)` | Gets any control's text (buttons, labels, edit fields, title bars). |

### Discover & Highlight

| Method | Signature | Description |
|---|---|---|
| `ListDialogControls` | `List<DialogControlInfo> ListDialogControls(IntPtr hDialog)` | Lists every control on a dialog (including nested controls) with its ID, text, and window class name. |
| `HighlightControl` | `bool HighlightControl(IntPtr hControl, int flashes = 3, int flashMs = 200, int lineWidth = 3, int colorRef = 0x0000FF)` | Flashes an inverting rectangle around a control (e.g. a handle from `ListDialogControls`) to visually confirm which on-screen control it is. Blocks the calling thread for ~`flashes`×2×`flashMs` (~1.2 s by default). Returns True on success; never throws. |

### Wait-for-Dialog Polling

| Method | Signature | Description |
|---|---|---|
| `WaitForDialog` | `bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd, int processId = 0)` | Polls for a visible top-level dialog matching the title (substring, case-insensitive; optionally scoped to a process ID) until it appears or the timeout elapses. |
| `WaitForDialogToClose` | `bool WaitForDialogToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until a dialog handle is no longer valid, or the timeout elapses. |

## Notes & Caveats

- **`ClickButton`/`ClickDialogButtonById`/`ClickDialogButtonByText`** only work against standard
  Win32 dialogs built from real `Button`/`Static`/`Edit` controls; owner-drawn or non-standard
  dialogs (some modern WPF/Electron/browser-rendered "dialogs" that are really just styled
  windows) may not respond to `BM_CLICK` at all — for those, use
  [KeyboardUtils](../keyboardutils/README.md) or
  [MouseUtils](../mouseutils/MouseUtils.cs)'s window-relative click methods instead.
- **WinUI3/UWP app dialogs (e.g. the Windows 11 Notepad "Do you want to save changes?"
  prompt) have no `DialogUtils`-visible controls at all** — confirmed by enumerating a live
  instance: the prompt is a XAML `ContentDialog` composited inside the app's existing
  top-level window, not a separate dialog window, and its child HWNDs are all
  `Microsoft.UI.Content.DesktopChildSiteBridge`/`InputSiteWindowClass` composition hosts —
  there is no `Button`/`Static` HWND per on-screen control for `ListDialogControls` to find or
  `ClickButton` to target. Because the prompt reuses the app's existing top-level window rather
  than opening a new one, `FindDialog` will still "find" it (matching the app's window title),
  which is why `FindDialog`'s `out bool canDismiss` parameter / `CanDismissDialog` exist — check
  `canDismiss` before assuming a found window's buttons are clickable. When it's `false`, skip
  `DialogUtils` entirely and drive the dialog with
  [KeyboardUtils](../keyboardutils/README.md) instead — the prompt is modal, so keyboard input
  goes to it regardless of the parent window's focus:
  ```csharp
  bool found = dialog.FindDialog("helloworld.cs", out IntPtr hWnd, out bool canDismiss, exactMatch: false);
  if (found && !canDismiss)
  {
      keyboard.PressKey(VirtualKey.Enter);   // activates the highlighted default button (Save)
      keyboard.PressKey(VirtualKey.Escape);  // Cancel (closes the prompt, keeps the app open with changes unsaved)
      // "Don't Save": Tab to it, then Enter
      keyboard.PressKey(VirtualKey.Tab);
      keyboard.PressKey(VirtualKey.Tab);
      keyboard.PressKey(VirtualKey.Enter);
  }
  ```
- **`FindDialog`/`WaitForDialog` only consider visible top-level windows**, so an app's
  hidden, pre-created windows whose titles already match (a common WinForms pattern) can't
  trigger a match before the dialog actually appears. They also accept an optional
  `processId` (resolved via `GetWindowThreadProcessId`) — pass the target app's process ID
  (e.g. from [WindowUtils](../windowutils/README.md)'s `GetWindowProcessId`/
  `FindWindowsByProcessId`) so a title that coincidentally appears in another app's window
  can never be matched or clicked:
  ```csharp
  dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100,
                       out IntPtr hWnd, processId: targetPid);
  ```
- **`ClickButton` delivers `BM_CLICK` via `SendMessageTimeout` with `SMTO_ABORTIFHUNG`** (2 s
  timeout), so a hung target application aborts the click instead of blocking the robot
  forever. `SendMessageTimeout`'s return value for `BM_CLICK` still carries no reliable
  success/failure signal, so `ClickButton` never throws based on it — a click that had no
  visible effect usually means the target wasn't actually a clickable `Button` control,
  not a Win32-level failure.
- **A button found immediately via `FindDialog`/`GetDlgItem` can still be temporarily
  disabled** — some dialogs finish enabling their buttons slightly after the window and
  controls are created (e.g. while completing modal setup), and Windows silently ignores
  `BM_CLICK` on a disabled control. `ClickButton`/`ClickDialogButtonById` poll `IsWindowEnabled`
  for up to `waitForEnabledMs` (default 500 ms) before clicking to avoid this race; pass
  `waitForEnabledMs: 0` to click immediately without waiting. `ClickDialogButtonByText` uses
  `ClickButton`'s default wait internally (not separately tunable through it) — see the next
  point for why it handles a stuck click differently.
- **A click that's found the right button and isn't blocked by a disabled state can still
  have no visible effect the first time** — some apps' click handlers ignore a click for
  reasons that aren't visible through Win32 at all (app-internal validation/state not
  reflected in `IsWindowEnabled`), so waiting for "enabled" doesn't help here; the only
  reliable fix is to verify the outcome and retry. `ClickDialogButtonByText` does this by
  default: it clicks, checks whether the dialog actually closed, and re-clicks (up to
  `maxAttempts`, default 3, `retryDelayMs` apart, default 300 ms) if not, returning whether it
  closed. This only makes sense for a click expected to close the dialog — for a button that
  intentionally keeps it open (e.g. "Apply"), it'll use up every attempt and return `false`;
  pass `maxAttempts: 1` to click exactly once with no retry.
- **`FindButtonByText`/`ClickDialogButtonByText` strip the `&` access-key mnemonic** from
  both the button's raw text and the text you pass before comparing — a standard `MessageBox`'s
  Yes/No buttons are literally `"&Yes"`/`"&No"` per `GetWindowText` (Windows only draws the `&`
  as an underline; it isn't stripped from the text itself), so matching plain `"Yes"` against
  it would otherwise fail every time — not a timing issue, and not specific to a non-default
  button, retrying it wouldn't have helped either. A literal `&` in a label (escaped as `&&`)
  is preserved as one `&` rather than stripped.
- **`FindDialog`/`FindButtonByText`** do a linear scan of top-level windows / child controls
  each call; on a system with many open windows this is a few milliseconds, not a concern for
  interactive automation use.
- **Dialog handles (`IntPtr`) become invalid once the dialog closes.** Re-find the dialog
  (or use `WaitForDialog`) rather than caching a handle across a long-running step.
- **`GetDialogText`** skips `Static`-class children with empty text (such as an icon control on a MessageBox with `MessageBoxIcon.Warning`/`Error`/etc.) and returns the first one with actual text, so it correctly finds the message body regardless of whether — or where — an icon control appears among the dialog's children.
- **`HighlightControl`** draws with `R2_NOTXORPEN`, the same erase-exactly XOR technique as [MouseUtils](../mouseutils/MouseUtils.cs)'s `FlashCursorHighlight` — if the control repaints while the rectangle is visible, the second XOR pass may not fully erase it, and the apparent color varies with what's underneath. It blocks the calling thread for roughly `flashes` × 2 × `flashMs` (~1.2 s with the defaults), and under DPI virtualization (a process that isn't DPI-aware on a scaled display) the rectangle can be drawn offset from the control.
