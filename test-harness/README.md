# Test Harness

The WinForms target app referenced throughout [TESTING.md](../TESTING.md)'s
Phase 0 — a small app with fixed, known control names so Pega Robot Studio
unit tests have something deterministic to point `DialogUtils`,
`KeyboardUtils`, `MouseUtils`, `WindowUtils`, and `UIAutomationUtils` at,
instead of relying on Notepad/Calculator (which vary by Windows version and,
for Notepad, use non-native WinUI3 dialogs).

This is a standalone project, deliberately kept outside `src/` and out of
`AwesomeRpaUtils.sln` — it's test tooling, not one of the shipped components.

## Building and running

Requires Windows (WinForms doesn't run headless). On a Windows machine with
the .NET 10 SDK:

```bash
dotnet run --project test-harness/TestHarness.csproj
```

It also compiles (but can't run) on non-Windows hosts via
`EnableWindowsTargeting`, so `dotnet build test-harness/TestHarness.csproj`
can still catch compile errors in CI or on a Linux dev machine.

## Controls

| Name | Type | Purpose |
|---|---|---|
| `btnClick` | Button | Click target for `MouseUtils`/`UIAutomationUtils.Invoke`; increments `lblClickCount` |
| `lblClickCount` | Label | Shows the running click count |
| `txtInput` | TextBox | Focused text field for `KeyboardUtils`/`UIAutomationUtils.SetValue`/`GetValue` |
| `chkOption` | CheckBox | Toggle target for `UIAutomationUtils.Toggle`/`IsToggled` |
| `lstItems` | ListBox (multi-select) | Scrollable, multi-selectable list for `MouseUtils` click/scroll/`ClickWithModifiers` |
| `treeSample` | TreeView | "Documents" node with two children, for `UIAutomationUtils.Expand`/`Collapse`/`Select` |
| `btnShowMessageBox` | Button | Opens a Yes/No `MessageBox` for `DialogUtils` |
| `btnOpenChildWindow` | Button | Opens `ChildForm` for `WindowUtils` child-window tests; no singleton guard, so repeated clicks open multiple same-class, same-title windows — useful for `FindDialog`/`GetChildWindows` multi-match cases |
| `btnShowNonNativeDialog` | Button | Opens `NonNativeDialogForm`, whose dismiss control is a `Label` styled as a button rather than a real `Button` — `DialogUtils.CanDismissDialog`/`FindDialog`'s `canDismiss` reliably reports `false` against it, covering the "non-native dialog" case without a WinUI3 dependency |
| `btnShowDuplicateDialogs` | Button | Opens two `DuplicateDialogForm` instances at once, both titled "Duplicate Dialog" with a real `Button` (`btnDuplicateOk`) — covers `FindDialog`'s first-match and `FindAllDialogs`' multi-match (returns both) behavior |
| `btnShowDisabledButtonDialog` | Button | Opens `DisabledButtonDialogForm`, whose `btnConfirm` starts `Enabled = false` — covers `ClickDialogButtonByText`/`ById`'s `wasEnabled = false` case; its `chkConfirmEnabled` checkbox lets a tester flip it enabled mid-wait to also cover the `true` case |
| `pnlCursorHand` | Panel | `Cursor = Cursors.Hand` — hover inside and assert `MouseUtils.GetCurrentCursorType` reports `Hand` |
| `pnlCursorSizeAll` | Panel | `Cursor = Cursors.SizeAll` — expect `CurrentCursorType.SizeAll` |
| `pnlCursorNo` | Panel | `Cursor = Cursors.No` — expect `CurrentCursorType.No` |
| `pnlCursorCross` | Panel | `Cursor = Cursors.Cross` — expect `CurrentCursorType.Crosshair` (name differs from the WinForms `Cursors.Cross` value) |
| `pnlCursorWait` | Panel | `Cursor = Cursors.WaitCursor` — expect `CurrentCursorType.Wait` |

Each control's `Name` doubles as its Win32 window text lookup key and, for
standard WinForms controls, its UI Automation `AutomationId` — though per
`UIAutomationUtils`' own README caveat, this mapping isn't guaranteed for
every control type, so verify the actual `AutomationId` Robot Studio sees
before writing a test case against it.

## Second-process modes

`ClipboardUtils` needs a real clipboard owned by a **different process**, and
a window in another process that can take the focus. Instead of the normal
harness window, `TestHarness.exe` can run as that second process:

```bash
TestHarness.exe --clipboard-owner [--text=T] [--html=H] [--rtf=R]
  [--image=path] [--files=path1;path2] [--custom-format=name:payload]
  [--hang] [--render-on-demand]
TestHarness.exe --focus-textbox
```

`--clipboard-owner` puts a rich clipboard on the system clipboard — one entry
per format flag supplied (text, HTML, RTF, an image file, a file-drop list, a
custom registered format) — then shows a small "Clipboard Owner" window and
blocks until it's closed, keeping this process alive as the clipboard's owner
for `SaveClipboard`/`RestoreClipboard` and similar cases to act on from
another process. (This project is a `WinExe` with no guaranteed attached
console, so the lifetime signal is a visible window, not console input.) By
default it uses the managed `Clipboard`/`DataObject` API, which renders every
format eagerly and flushes it so the data survives this process exiting.

- `--hang`: uses delayed (lazy) rendering instead, and never answers
  `WM_RENDERFORMAT` for any requested format — for `ClipboardUtils`' "hung
  owner" case, where `SaveClipboard` must fail fast with a reason rather than
  hang.
- `--render-on-demand`: also uses delayed rendering, but renders the real data
  the moment `WM_RENDERFORMAT` asks for it — for the "another process owns
  the clipboard [and] renders it on demand" case. (`--hang` and
  `--render-on-demand` only support the `--text`/`--html`/`--rtf`/
  `--custom-format` formats, not `--image`/`--files` — those need the default
  managed, eager path.)

`--focus-textbox` opens a small window with a single, immediately-focused
`TextBox` (`txtClipboardTarget`) — the "small text-box window that can take
the focus" `PasteText`'s real-focus test case needs.
