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
ID, for use with `ClickDialogButton`: `Ok`, `Cancel`, `Abort`, `Retry`,
`Ignore`, `Yes`, `No`.

## Constructors

| Constructor | Description |
|---|---|
| `DialogUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `DialogUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Find & Click

| Method | Description |
|---|---|
| `IntPtr FindDialog(string titlePattern, bool exactMatch = true)` | Finds a top-level dialog window by its title (exact or substring match). |
| `IntPtr FindButtonByText(IntPtr hDialog, string buttonText)` | Finds a button on a dialog by its visible text. |
| `IntPtr FindButtonById(IntPtr hDialog, int controlId)` | Finds a control on a dialog by its control ID. |
| `void ClickButton(IntPtr hButton)` | Invokes a button by sending it `BM_CLICK`. |
| `void ClickDialogButton(IntPtr hDialog, DialogButton button)` | Invokes a standard dialog button by its well-known control ID. |
| `void ClickDialogButtonByText(IntPtr hDialog, string buttonText)` | Finds a button by its visible text and invokes it. |

### Read Text

| Method | Description |
|---|---|
| `string GetDialogText(IntPtr hDialog)` | Gets a dialog's message body (its first `Static`-class child control's text). |
| `string GetControlText(IntPtr hControl)` | Gets any control's text (buttons, labels, edit fields, title bars). |

### Wait-for-Dialog Polling

| Method | Description |
|---|---|
| `bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)` | Polls for a dialog matching the title until it appears or the timeout elapses. |
| `bool WaitForDialogToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until a dialog handle is no longer valid, or the timeout elapses. |

## Notes & Caveats

- **`ClickButton`/`ClickDialogButton`/`ClickDialogButtonByText`** only work against standard
  Win32 dialogs built from real `Button`/`Static`/`Edit` controls; owner-drawn or non-standard
  dialogs (some modern WPF/Electron/browser-rendered "dialogs" that are really just styled
  windows) may not respond to `BM_CLICK` at all — for those, use
  [KeyboardUtils](../keyboardutils/README.md) or
  [MouseUtils](../mouseutils/MouseUtils.cs)'s window-relative click methods instead.
- **`SendMessage`'s return value for `BM_CLICK`** carries no reliable success/failure signal,
  so `ClickButton` never throws based on it — a click that had no visible effect usually means
  the target wasn't actually a clickable `Button` control, not a Win32-level failure.
- **`FindDialog`/`FindButtonByText`** do a linear scan of top-level windows / child controls
  each call; on a system with many open windows this is a few milliseconds, not a concern for
  interactive automation use.
- **Dialog handles (`IntPtr`) become invalid once the dialog closes.** Re-find the dialog
  (or use `WaitForDialog`) rather than caching a handle across a long-running step.
