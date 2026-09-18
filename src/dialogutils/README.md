# DialogAutomation

A Pega Robot Studio-ready component (`DialogUtils`) that finds and dismisses
native dialogs (message boxes, common dialogs) by button text or control ID,
via `BM_CLICK` — no cursor movement required, and it works even if the
dialog is behind other windows. It also fills dialogs in (text boxes, check boxes,
radio buttons, drop-downs, and Open/Save As file dialogs); those were verified with
the dialog in front, not behind other windows.

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

### `ControlCheckState`
The state of a check box or radio button, as reported by `TryGetControlCheckState`
(values are the Win32 `BST_*` constants): `Unchecked`, `Checked` (also the selected
radio button), `Indeterminate`.

## Constructors

| Constructor | Description |
|---|---|
| `DialogUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `DialogUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Types

### `DialogControlInfo`
Describes one control returned by `ListDialogControls`: `Handle` (`IntPtr`), `Id`
(`int`, the control ID used by `FindButtonById`), `Text` (`string`), `ClassName`
(`string`, the window class, e.g. `Button`/`Static`/`Edit`), `Enabled` (`bool`,
via `IsWindowEnabled` — a disabled `Button` silently ignores `BM_CLICK`), and
`IsPassword` (`bool`, true for an edit box with the password style; its `Text` is
always empty because `ListDialogControls` never reads it, and `ToString()` prints
`(password)` instead of a value).

## Methods

### Find & Click

| Method | Signature | Description |
|---|---|---|
| `FindDialog` | `bool FindDialog(string titlePattern, out IntPtr hDialog, out bool canDismiss, bool exactMatch, int processId = 0)` | Finds a visible top-level dialog window by its title (exact or substring match, case-insensitive; optionally scoped to a process ID via `GetWindowThreadProcessId`) and reports whether it has a native `Button` control DialogUtils can click. Hidden windows are skipped, so it can't match a dialog before it actually appears. Returns the first match; use `FindAllDialogs` when more than one window could match. Returns True if found; never throws. |
| `FindAllDialogs` | `List<IntPtr> FindAllDialogs(string titlePattern, bool exactMatch = true, int processId = 0)` | Finds every visible top-level window whose title matches (exact or substring, case-insensitive; optionally process-scoped) — the same matching as `FindDialog`, but returning all matches instead of just the first. Returns an empty list if none match; never throws. |
| `CanDismissDialog` | `bool CanDismissDialog(IntPtr hDialog)` | Checks whether a dialog has at least one native `Button` control that `ClickButton`/`ClickDialogButtonById`/`ClickDialogButtonByText` can target. |
| `FindButtonByText` | `bool FindButtonByText(IntPtr hDialog, out IntPtr hButton, string buttonText, bool exactMatch = true)` | Finds a button on a dialog by its visible text (exact match, or substring when `exactMatch: false`). Returns True if found; never throws. |
| `FindButtonById` | `bool FindButtonById(IntPtr hDialog, out IntPtr hButton, int controlId)` | Finds a control on a dialog by its control ID. Returns True if found; never throws. |
| `ClickButton` | `bool ClickButton(IntPtr hButton, int waitForEnabledMs = 500, int pollIntervalMs = 25)` | Invokes a button by sending it `BM_CLICK`, waiting briefly for the button to become enabled first. Returns True if it was enabled when clicked. |
| `ClickDialogButtonById` | `bool ClickDialogButtonById(IntPtr hDialog, int controlId, out bool wasEnabled, int waitForEnabledMs = 500, int pollIntervalMs = 25)` | Invokes a button by its control ID — a well-known `DialogButton` value cast to `int`, or a custom ID from `ListDialogControls`. Returns True if the control was found (and clicked); never throws. |
| `ClickDialogButtonByText` | `bool ClickDialogButtonByText(IntPtr hDialog, string buttonText, out bool wasEnabled, out string message, bool exactMatch = true, int waitForEnabledMs = 500, int pollIntervalMs = 25)` | Finds a button by its visible text and invokes it once. Returns whether it was found; `wasEnabled` reports whether it was enabled when clicked. Never throws. |

### Read Text

| Method | Signature | Description |
|---|---|---|
| `GetDialogText` | `string GetDialogText(IntPtr hDialog)` | Gets a dialog's message body (the first `Static`-class child control with non-empty text — skips icon controls, which have no text). |
| `GetControlText` | `string GetControlText(IntPtr hControl)` | Gets any control's text (buttons, labels, edit fields, drop-downs, title bars), including controls in another process. |

### Set Values

| Method | Signature | Description |
|---|---|---|
| `SetControlText` | `bool SetControlText(IntPtr hControl, string text, out string message)` | Sets a control's text (a text box, or the editable part of a drop-down) with `WM_SETTEXT` and reads it back to confirm. The text is never included in a failure message. Never throws. |
| `TryGetControlCheckState` | `bool TryGetControlCheckState(IntPtr hControl, out ControlCheckState state, out string message)` | Reports whether a check box or radio button is checked. Never throws. |
| `SetControlChecked` | `bool SetControlChecked(IntPtr hControl, bool isChecked, out string message)` | Checks/unchecks a check box or selects a radio button, clicking only if needed, and confirms the result. Never throws. |
| `SelectComboItem` | `bool SelectComboItem(IntPtr hCombo, string itemText, out string message, bool exactMatch = true)` | Selects a drop-down item by its text (exact, or first containing) and notifies the dialog. When nothing matches, the message lists the items. Never throws. |

### File Dialogs

| Method | Signature | Description |
|---|---|---|
| `SetFileDialogPath` | `bool SetFileDialogPath(IntPtr hDialog, string path, out string message)` | Types a path into an Open/Save As dialog's File name box without confirming it. Never throws. |
| `SelectFileDialogFileType` | `bool SelectFileDialogFileType(IntPtr hDialog, string fileTypeText, out string message, bool exactMatch = true)` | Chooses an entry in the dialog's file-type list (for example `CSV (*.csv)`, or `*.csv` with `exactMatch: false`). Never throws. |
| `SubmitFileDialog` | `bool SubmitFileDialog(IntPtr hDialog, string path, out string message, int closeTimeoutMs = 5000)` | Types the path, clicks Open/Save, and waits for the dialog to close. `false` with a message if it stays open (an overwrite confirmation or a file-not-found box). Never throws. |

### Discover & Highlight

| Method | Signature | Description |
|---|---|---|
| `ListDialogControls` | `List<DialogControlInfo> ListDialogControls(IntPtr hDialog)` | Lists every control on a dialog (including nested controls) with its ID, text, and window class name. Password edit boxes are listed without their text (`IsPassword` is true). |
| `HighlightControl` | `bool HighlightControl(IntPtr hControl, Color color, int flashes = 3, int flashMs = 200, int lineWidth = 3)` | Flashes an inverting rectangle of the requested color around a control (e.g. a handle from `ListDialogControls`). Blocks the calling thread for ~`flashes`×2×`flashMs` (~1.2 s by default). Returns True on success; never throws. |

### Wait-for-Dialog Polling

| Method | Signature | Description |
|---|---|---|
| `WaitForDialog` | `bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd, bool exactMatch, int processId = 0)` | Polls for a visible top-level dialog matching the title (exact or substring, case-insensitive; optionally scoped to a process ID) until it appears or the timeout elapses. The required `exactMatch` value makes matching behavior explicit. |
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
                       out IntPtr hWnd, exactMatch: false, processId: targetPid);
  ```
- **`FindDialog`/`WaitForDialog` return the first matching window** — if more than one
  window could match (e.g. two apps both showing a "Confirm" dialog), use
  `FindAllDialogs` to get every match and pick the right one, or scope with `processId`.
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
  `waitForEnabledMs: 0` to click immediately without waiting. `ClickDialogButtonByText`
  passes its own `waitForEnabledMs`/`pollIntervalMs` through to `ClickButton`.
- **A click that's found the right button and isn't blocked by a disabled state can still
  have no visible effect the first time** — some apps' click handlers ignore a click for
  reasons that aren't visible through Win32 at all (app-internal validation/state not
  reflected in `IsWindowEnabled`). `ClickDialogButtonByText` sends one click and reports
  whether the button was found and enabled; it cannot report whether the application acted
  on the click. Use `WaitForDialogToClose` separately when disappearance of the original
  dialog handle is the outcome the automation needs to observe.
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
- **`GetControlText` now reads edit boxes and drop-downs in other applications.** It asks the
  control directly (`WM_GETTEXT`, with a timeout that gives up at once on a hung application)
  because `GetWindowText` deliberately returns an empty string for another process's edit box;
  earlier versions therefore read every such control as empty. `FindDialog`/`WaitForDialog`
  still match titles with the non-blocking `GetWindowText`, so scanning the desktop can never
  stall on an unresponsive application.
- **Password boxes are not read by `ListDialogControls`.** Because `GetControlText` can now read
  another process's edit boxes, it can also read a masked password box's real contents (the mask
  is only drawn, not stored). So `ListDialogControls` skips reading any edit box with the
  password style: its `Text` is empty and `IsPassword` is true, so a routine control dump or log
  line never carries a password. Calling `GetControlText` on that specific handle is an explicit
  request and still returns the real text - do not log the result.
- **A zero dialog handle now means "no controls"**, not "every window". `ListDialogControls`,
  `GetDialogText`, and `FindButtonByText` on `IntPtr.Zero` used to walk every top-level window
  on the desktop (Win32's `EnumChildWindows(NULL)` behavior); they now return empty.
- **Set-value methods confirm by reading back.** `SetControlText` returns `false` if the control
  did not keep the text (read-only, length-limited, reformatting), and `SetControlChecked`/
  `SelectComboItem` verify the resulting state. A `true` means the control is in the requested
  state, not merely that a message was sent. `SetControlText` never puts the text in a failure
  message, so a password typed into a login dialog is not echoed into a log.
- **`SetControlChecked` clicks; it does not set the state directly.** The click runs the
  application's own handling so it finds out (setting the state directly changes the box on
  screen without telling the app). It only clicks when a change is needed, a radio button
  cannot be unchecked (select another in its group), and a three-state box may take two clicks.
  Applications that draw their own check boxes and radio buttons - WinForms does - have no
  Win32 state, so these methods refuse them with a message pointing at
  [UIAutomationUtils](../uiautomationutils/README.md) (`IsToggled`/`Toggle`).
- **`SelectComboItem` tells the dialog the selection changed** by sending `CBN_SELCHANGE` and
  then `CBN_SELENDOK` to the parent. Both matter: ordinary dialogs and WinForms react to the
  first, but the Open/Save dialogs act only on the second and ignore a lone `CBN_SELCHANGE`
  (the file-type filter would stay as it was).
- **The message boxes Windows shows over Open/Save dialogs have buttons with control ID 0.**
  The overwrite confirmation and the file-not-found error are DirectUI-laid-out, so
  `ClickDialogButtonById(...)` cannot find their Yes/No/OK; use `ClickDialogButtonByText`. Their
  message text is also drawn by DirectUI, so `GetDialogText` reads it as empty. Their titles and
  button text are in the operating system's language.
- **`SetFileDialogPath`/`SelectFileDialogFileType`/`SubmitFileDialog` find the File name box and
  file-type list structurally**, not by control ID: some dialogs give them fixed IDs and others
  bury them in a DirectUI host with ID 0, and every Open/Save dialog also contains an Explorer
  address bar and search box (edit boxes too) that are skipped. They refuse a dialog with no
  folder view, so pointing one at (say) a Font dialog fails instead of typing into its
  font-name box. Verified against the .NET Open and Save dialogs (Windows 11); an unusual
  dialog can be driven by handle with `ListDialogControls` + `SetControlText`/`SelectComboItem`.
- **`SubmitFileDialog`'s `true` means the dialog closed after Open/Save was clicked** - not that
  the file exists or was written, and a dialog closed with Cancel would look the same. It
  returns `false` with a message when the dialog stays open (usually an overwrite confirmation
  or a file-not-found box; answer it, then continue).
