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

`InterruptUtils` needs its popup to come from a *different process* than the
automation dismissing it — a same-process dialog would not catch a real
regression. Instead of the normal harness window, `TestHarness.exe` can run
as that second process:

```bash
TestHarness.exe --delayed-popup [--title=T] [--message=M] [--delay-ms=N]
  [--button=OK|OKCancel|YesNo|YesNoCancel|AbortRetryIgnore|RetryCancel]
  [--repeat=N] [--interval-ms=N] [--no-button] [--delayed-button]
```

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
  on a timer tick after `--delay-ms`, for "a form whose button is created a
  moment after the window appears" (pass `className: "*"` on the
  `InterruptUtils` side, since it's not a native `#32770` dialog).
