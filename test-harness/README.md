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
| `btnOpenChildWindow` | Button | Opens `ChildForm` for `WindowUtils` child-window tests |

Each control's `Name` doubles as its Win32 window text lookup key and, for
standard WinForms controls, its UI Automation `AutomationId` — though per
`UIAutomationUtils`' own README caveat, this mapping isn't guaranteed for
every control type, so verify the actual `AutomationId` Robot Studio sees
before writing a test case against it.

## Second-process modes

`InterruptUtils` needs its popup to come from a *different process* than the
automation dismissing it — a same-process dialog would not catch a real
regression. Instead of the normal harness window, `test-harness.exe` can run
as that second process:

```bash
test-harness.exe --delayed-popup [--title=T] [--message=M] [--delay-ms=N]
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
- `--no-button`: shows a borderless window with no `Button`-classed child at
  all, for "a button-less window is closed by a close rule."
- `--delayed-button`: shows the window immediately, then adds its `Button`
  on a timer tick after `--delay-ms`, for "a form whose button is created a
  moment after the window appears" (pass `className: "*"` on the
  `InterruptUtils` side, since it's not a native `#32770` dialog).
