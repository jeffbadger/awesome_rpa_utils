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
| `btnOpenSecondChildWindow` | Button | Opens `SecondChildForm`, an owned top-level window (not `WS_CHILD`) with an explicit, distinct Win32 window class from `ChildForm`'s — registered via `RegisterClassEx` (plain WinForms `Form`s don't get a distinct class per CLR subtype, and `CreateParams.ClassName` alone doesn't register one) — for `WindowUtils.TryFindWindowByRegex`'s `classNamePattern` and `GetTopLevelWindows`/`FindWindowByClass`'s duplicate-vs-distinct-class cases |
| `btnShowNonNativeDialog` | Button | Opens `NonNativeDialogForm`, whose dismiss control is a `Label` styled as a button rather than a real `Button` — `DialogUtils.CanDismissDialog`/`FindDialog`'s `canDismiss` reliably reports `false` against it, covering the "non-native dialog" case without a WinUI3 dependency |
| `btnShowDuplicateDialogs` | Button | Opens two `DuplicateDialogForm` instances at once, both titled "Duplicate Dialog" with a real `Button` (`btnDuplicateOk`) — covers `FindDialog`'s first-match and `FindAllDialogs`' multi-match (returns both) behavior |
| `btnShowDisabledButtonDialog` | Button | Opens `DisabledButtonDialogForm`, whose `btnConfirm` starts `Enabled = false` — covers `ClickDialogButtonByText`/`ById`'s `wasEnabled = false` case; its `chkConfirmEnabled` checkbox lets a tester flip it enabled mid-wait to also cover the `true` case |
| `pnlCursorHand` | Panel | `Cursor = Cursors.Hand` — hover inside and assert `MouseUtils.GetCurrentCursorType` reports `Hand` |
| `pnlCursorSizeAll` | Panel | `Cursor = Cursors.SizeAll` — expect `CurrentCursorType.SizeAll` |
| `pnlCursorNo` | Panel | `Cursor = Cursors.No` — expect `CurrentCursorType.No` |
| `pnlCursorCross` | Panel | `Cursor = Cursors.Cross` — expect `CurrentCursorType.Crosshair` (name differs from the WinForms `Cursors.Cross` value) |
| `pnlCursorWait` | Panel | `Cursor = Cursors.WaitCursor` — expect `CurrentCursorType.Wait` |
| `cboOptions` | ComboBox (drop-down list) | A different UIA tree shape from `lstItems`, for `UIAutomationUtils.Select`/`IsSelected` — call `Expand` first, its items aren't in the UIA tree while collapsed |
| `trkVolume` | TrackBar (0-100, starts at 50) | Only exposes `RangeValuePattern` — negative case for `SetValue`/`Toggle`/`Select` against a control supporting none of those patterns |
| `grpRadioOptions` | GroupBox | Contains `radOptionA`/`radOptionB`/`radOptionC` |
| `radOptionA`, `radOptionB`, `radOptionC` | RadioButton | Expose `SelectionItemPattern`, not `TogglePattern` — negative case for `UIAutomationUtils.Toggle`/`IsToggled`, complementing `chkOption` |

Each control's `Name` doubles as its Win32 window text lookup key and, for
standard WinForms controls, its UI Automation `AutomationId` — though per
`UIAutomationUtils`' own README caveat, this mapping isn't guaranteed for
every control type, so verify the actual `AutomationId` Robot Studio sees
before writing a test case against it.

## Second-process modes

`InterruptUtils` needs its popup to come from a *different process* than the
automation dismissing it — a same-process dialog would not catch a real
regression. `ClipboardUtils` needs a real clipboard owned by a **different
process**, and a window in another process that can take the focus. Instead
of the normal harness window, `TestHarness.exe` can run as that second
process:

```bash
TestHarness.exe --delayed-popup [--title=T] [--message=M] [--delay-ms=N]
  [--button=OK|OKCancel|YesNo|YesNoCancel|AbortRetryIgnore|RetryCancel]
  [--repeat=N] [--interval-ms=N] [--no-button]
  [--delayed-button] [--button-delay-ms=N]
TestHarness.exe --clipboard-owner [--text=T] [--html=H] [--rtf=R]
  [--image=path] [--files=path1;path2] [--custom-format=name:payload]
  [--hang] [--render-on-demand]
TestHarness.exe --focus-textbox
```

### `--delayed-popup` (InterruptUtils)

Sleeps `--delay-ms` (default 3000), then shows a real `MessageBox` from its
own process — satisfying TESTING.md's "a second process that shows popups on
demand." Defaults: `--title="Test Harness Popup"`,
`--message="This is a delayed popup."`, `--button=OK`.

- `--repeat=N --interval-ms=N`: shows the popup `N` times, `--interval-ms`
  apart, for `InterruptUtils`' `maxDismissalsPerMinute` case (a popup that
  returns every time).
- `--no-button`: shows a fixed-dialog window (`FormBorderStyle.FixedDialog`,
  so it still has a title bar and border) with no `Button`-classed child at
  all, for "a button-less window is closed by a close rule."
- `--delayed-button`: shows the window immediately, then adds its `Button`
  on a timer tick after `--button-delay-ms` (default 500, **not**
  `--delay-ms`), for "a form whose button is created a moment after the
  window appears" (pass `className: "*"` on the `InterruptUtils` side, since
  it's not a native `#32770` dialog). The default is deliberately short:
  `InterruptUtils` only retries a newly seen popup at 0/150/400/1000/2000 ms
  after first detecting it before giving up (`PopupDismissFailed`), so a
  button delayed by `--delay-ms`'s 3000 ms default would never be caught.

### `--clipboard-owner` / `--focus-textbox` (ClipboardUtils)

`--clipboard-owner` puts a rich clipboard on the system clipboard — one entry
per format flag supplied (text, HTML, RTF, an image file, a file-drop list, a
custom registered format) — then shows a small "Clipboard Owner" window and
blocks until it's closed, keeping this process alive as the clipboard's owner
for `SaveClipboard`/`RestoreClipboard` and similar cases to act on from
another process. (This project is a `WinExe` with no guaranteed attached
console, so the lifetime signal is a visible window, not console input.) By
default it uses the managed `Clipboard`/`DataObject` API, which renders every
format eagerly and flushes it so the data survives this process exiting.

- `--hang`: uses delayed (lazy) rendering, and blocks its own message loop
  forever the moment `WM_RENDERFORMAT` for any requested format arrives —
  for `ClipboardUtils`' "hung owner" case, where `SaveClipboard` must fail
  fast with a reason rather than hang itself. `WM_RENDERFORMAT` is delivered
  via a synchronous cross-process call, so once this fires the harness
  process is genuinely unresponsive, including its own "Clipboard Owner"
  window's Close button — **the only way out is to kill the process** (e.g.
  Task Manager or `taskkill`), matching TESTING.md's documented recovery.
- `--render-on-demand`: also uses delayed rendering, but renders the real data
  the moment `WM_RENDERFORMAT` asks for it — for the "another process owns
  the clipboard [and] renders it on demand" case.
- `--hang` and `--render-on-demand` are mutually exclusive, and only support
  the `--text`/`--html`/`--rtf`/`--custom-format` formats — combining either
  with `--image`/`--files`, or combining both together, is rejected with an
  error rather than silently doing something other than what was asked for.

`--focus-textbox` opens a small window with a single, immediately-focused
`TextBox` (`txtClipboardTarget`) — the "small text-box window that can take
the focus" `PasteText`'s real-focus test case needs.
