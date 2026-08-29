# Testing Plan: Pega Robotic Automation

A step-by-step plan for testing all nine components (`DialogUtils`,
`KeyboardUtils`, `MouseUtils`, `OcrUtils`, `ScreenCaptureUtils`, `WindowUtils`,
`UIAutomationUtils`, `CommandLineUtils`, `ServiceUtils`) using Pega Robot
Studio's Unit Testing framework.

Unlike Pega's usual caution against fully-automated RPA test suites (that guidance
targets fragile, ever-changing *target-application* automations — see the RPA
Automated Testing Strategy manual), these nine classes are your own deterministic
library code with a stable, versioned API. That's exactly the case where
component-level automated testing pays off.

## Phase 0 — Project setup

1. `dotnet build src/AwesomeRpaUtils.sln` to produce the nine DLLs in `src/bin/`.
2. Create a new Robot Studio project, e.g. `AwesomeRpaUtils.Tests`. Add each DLL
   as a Toolbox reference (project asset) so `DialogUtils`, `KeyboardUtils`,
   `MouseUtils`, `OcrUtils`, `ScreenCaptureUtils`, `WindowUtils`,
   `UIAutomationUtils`, `CommandLineUtils`, and `ServiceUtils` all appear in the Toolbox and
   can be dragged onto automation surfaces.
3. Build a small **Test Harness** WinForms app (a few native `Button`/`Edit`/
   `Static`/`ListBox` controls with fixed, known control IDs, plus one that opens
   a `MessageBox`). Don't rely on Notepad/Calculator as the target — Notepad's
   Windows 11 dialogs are WinUI3 (no native `Button` controls), which is a
   documented gap in `DialogUtils` itself. The harness gives you deterministic
   controls for `DialogUtils`, `KeyboardUtils`, `MouseUtils`, and `WindowUtils`
   to drive. **This already exists** — see [`test-harness/`](test-harness/README.md)
   at the repo root (kept outside `src/` and out of the main solution, since
   it's test tooling, not a shipped component). It also has a `CheckBox` and
   `TreeView` with known names, covering `UIAutomationUtils`' `Toggle`/
   `Expand`/`Collapse`/`Select` methods.
4. Create a `Unit Tests` folder in the project (Setup/Cleanup automations must
   live in subfolders of it). Under it, one subfolder per component:
   `Unit Tests/DialogUtils`, `.../KeyboardUtils`, etc.
5. In each component subfolder, build one **Setup** automation (launch the Test
   Harness, or the OS dialog/app needed) and one **Cleanup** automation (close
   it, call any "reset" method the component exposes — `ResetSystemCursors`,
   restore clipboard, `ReleaseCursorClip`, `ShowCursor` if hidden). Wire these
   into that automation's Setup & Cleanup tab so they run once per test session.

## Phase 1 — One testable automation per method (or tight method group)

For each public method, build a small automation with a defined entry point
(inputs) and exit point (result/output params) — that's what makes it a
"testable automation" Test Explorer can attach cases to. Then add 2-4 unit test
cases per automation: a happy path, a not-found/empty case, and an error case
where the method is documented to throw.

### DialogUtils (needs Setup: harness shows a MessageBox or custom dialog)

- `FindDialog` (exact + substring match; not-found → returns `false`, `out`
  params zeroed; also assert `canDismiss` — `true` for the harness's native
  `Button` dialog, `false` if you can simulate a WinUI-style one, or document
  that case as manual-only)
- `CanDismissDialog` (native `Button` dialog → true; simulate a WinUI-style
  dialog if you can, or just document as manual-only)
- `FindButtonByText` (exact/substring; mnemonic-stripping case — button labeled
  `"&Yes"` should match input `"Yes"`; not-found → returns `false`, never throws)
- `FindButtonById`, `ClickDialogButtonById` (happy path — assert `wasEnabled`;
  not-found case for an unknown ID → both return `false`, never throw)
- `ClickDialogButtonByText` (verify dialog closes; retry path — button that
  doesn't close the dialog should return `false` after `maxAttempts` with
  `message` explaining why; not-found case → `false` with `message` set,
  never throws)
- `GetDialogText`, `GetControlText`, `ListDialogControls` (assert count/text/
  class/enabled state against harness's known controls)
- `ClickButton` (assert the `bool` return matches whether the target was
  actually enabled when clicked — force the disabled case if the harness can
  hold a button disabled briefly)
- `HighlightControl` (assert the `bool` return — `true` for a valid handle;
  verify the actual flash manually/via screenshot, see Phase 3)
- `WaitForDialog`, `WaitForDialogToClose` (timeout-not-met and found-in-time
  cases; use a delayed-launch step in Setup to hit the "found before timeout"
  branch)

### KeyboardUtils (needs Setup: harness with a focused text field)

`KeyDown`/`KeyUp`/`PressKey`/`PressKeyWithModifiers`/`PressKeyCombo`/`HoldKey`/
`TypeText`/`PasteText` all return `bool` with an `out string message` and never
throw — for these, replace Phase 2's "exception condition on the invalid-input
case" with an outcome condition asserting `false` plus a non-null `message` on
the invalid-input case (e.g. a null `text`), instead of an `Automation
exception` condition.

- `KeyDown`/`KeyUp`/`PressKey` (assert via `IsKeyDown` mid-hold, or via harness
  field echoing the key)
- `PressKeyWithModifiers`, `PressKeyCombo` (drive Ctrl+A/Ctrl+C in the harness
  field, verify effect)
- `HoldKey` (duration-honored — combine with a stopwatch or `IsKeyDown` polling)
- `TypeText` (both overloads — assert harness field's resulting text; include a
  non-BMP character, e.g. an emoji, to test the surrogate-pair path; null
  `text` → `false` + message)
- `PasteText` (assert field content; assert clipboard is restored to its
  pre-call value afterward — this is the one most worth a dedicated case, since
  the restore logic has three branches: had text, was empty, had non-text)
- `IsKeyDown`, `IsModifierDown`, `GetActiveModifiers` (state query — hold a key
  via `KeyDown` in the same test, then assert; unchanged by the API review,
  still plain `bool`/enum returns with no `message` parameter)

### MouseUtils (needs Setup: harness with a click target and a scrollable list)

Nearly every method here (all except `HideCursor`/`ShowCursor`/`IsCursorVisible`,
the button-state/screen-geometry queries, `UnblockUserInput`, `GetWindowAtPoint`,
and `IsProcessDpiAware`) returns `bool` with an `out string message` and never
throws — for these, replace Phase 2's "exception condition on the invalid-input
case" with an outcome condition asserting `false` plus a non-null `message` on
the invalid-input case, instead of an `Automation exception` condition.
`WaitForPixelColor`/`WaitForPixelChange`/`IsBusyCursorActive`/`WaitForIdleCursor`
keep their original `bool` meaning (matched/idle vs. not); a Win32-failure case
for these should assert `false` with a non-null `message`, distinct from a
genuine timeout case (`false` with `message == null`).

- `GetX`/`GetY`/`GetPosition`, `MoveTo`, `MoveBy`, `SmoothMoveTo` (assert final
  position; for `SmoothMoveTo`, just assert the destination, not the path)
- `JiggleMouse` (assert position unchanged before/after)
- `Click`/`ClickAt`/`LeftClick`/`RightClick`/`MiddleClick` and the `*At`
  variants (assert harness button fired)
- `DoubleClick`/`DoubleClickAt`/`LeftDoubleClick`/`LeftDoubleClickAt`,
  `TripleClick` (assert harness's click-count label)
- `MouseDown`/`MouseUp`/`ClickAndHold` (assert `IsLeftButtonDown` mid-hold)
- `ClickWithModifiers` (Ctrl+Click multi-select in harness list; assert
  selection count)
- `ClickAndRestore` (assert cursor position unchanged after, and that the click
  still registered)
- `ClickWithRetry` (hard to force a transient input-injection failure — cover
  the retry-count/delay logic path superficially, note as low-value automated
  coverage)
- `DragAndDrop`, `DragAndHold`, `RubberBandSelect` (assert harness's
  drag-result / multi-select state; assert modifiers are released even when the
  drag fails — simulate by passing an invalid `MouseButton` cast)
- `Scroll`/`ScrollUp`/`ScrollDown`/`ScrollHorizontal*` (assert harness list's
  scroll position changed)
- `SetCursor`/`ReplaceSystemCursor`/`SetCursorFromFile`/`ResetSystemCursors`
  (hard to assert programmatically — screenshot-diff via `ScreenCaptureUtils`
  before/after, see Phase 3)
- `HideCursor`/`ShowCursor`/`IsCursorVisible` (assert the internal flag flips
  correctly, including double-hide/double-show no-ops)
- `ClipCursor`/`ReleaseCursorClip`/`GetCursorClip` (assert `GetCursorClip`
  reflects the rect you set, and reverts to full virtual screen after release)
- `GetDoubleClickTimeMs`/`SetDoubleClickTimeMs` (set → get → restore original;
  out-of-range case → `false` + message instead of an exception)
- `GetScreenWidth`/`GetScreenHeight` and remaining screen-info methods (sanity
  assert > 0)

### WindowUtils (needs Setup: harness app + one child window)

`GetWindowBounds`, `SetWindowBounds`, `MoveWindow`, `ResizeWindow`,
`CloseWindow`, `ActivateWindow`, and `SetAlwaysOnTop` return `bool` with an
`out string message` and never throw — for these, replace Phase 2's
"exception condition on the invalid-input case" with an outcome condition
asserting `false` plus a non-null `message` on the invalid-input case (an
invalid handle, negative width/height), instead of an `Automation exception`
condition.

- `GetTopLevelWindows`, `FindWindowByTitle` (exact/substring/not-found),
  `FindWindowByClass`, `FindWindowsByProcessId`, `GetForegroundWindow`
- `GetWindowBounds`, `SetWindowBounds`, `MoveWindow`, `ResizeWindow` (assert
  bounds before/after; invalid handle/negative dimensions → `false` + message)
- `GetWindowTitle`, `GetWindowClassName`, `GetWindowProcessId`,
  `IsWindowVisible`, `IsWindowResponding`
- `SetWindowState` (Minimize/Maximize/Restore/Hide/Normal — assert via
  `IsWindowVisible`/bounds)
- `CloseWindow` (assert via `WaitForWindowToClose`; invalid handle → `false` +
  message)
- `ActivateWindow` (assert `GetForegroundWindow` matches; note the
  foreground-lock caveat — may need the harness to not be minimized; invalid
  handle → `false` + message)
- `SetAlwaysOnTop` (hard to assert programmatically — visual/manual; invalid
  handle → `false` + message)
- `WaitForWindow`, `WaitForWindowToClose`, `WaitForWindowActive` (both a
  within-timeout and a timeout-exceeded case)
- `GetChildWindows`, `FindChildWindow`

### OcrUtils (needs Setup: a fixed test image file with known text, plus a screen region showing known text — e.g. the harness's own label)

All methods here except `GetAvailableLanguages` return `bool` with an
`out string message` and never throw — for these, replace Phase 2's
"exception condition on the invalid-input case" with an outcome condition
asserting `false` plus a non-null `message` on the invalid-input case (bad
dimensions, a missing image file, a missing OCR language pack), instead of an
`Automation exception` condition.

- `GetTextFromRegion`, `GetTextFromImageFile` (assert exact/substring
  recognized text against a fixture image checked into the test project; bad
  dimensions/missing file → `false` + message)
- `GetStructuredTextFromRegion` (assert line/word count and bounding-rect
  sanity — non-empty, within the requested region)
- `FindTextLocation` (found case → `true` + populated `location`; not-found
  case → `false` + `message == null`; a forced real-failure case (e.g. bad
  dimensions) → `false` + non-null `message`, to verify the two `false`
  outcomes are distinguishable)
- `GetAvailableLanguages` (assert non-empty on the CI/build machine — flag as
  an environment dependency, see Phase 4)
- `WaitForTextToAppear` (found-in-time and timeout cases — pair with a Setup
  step that renders the text after a delay; same not-found-vs-real-failure
  distinction as `FindTextLocation`)

### ScreenCaptureUtils (some cases need no live screen at all — see Phase 3)

- `CaptureScreenToFile`, `CaptureRegionToFile`, `CaptureWindowToFile`,
  `CaptureActiveWindowToFile`, `CaptureAroundPointToFile` (assert file exists,
  non-zero size, correct pixel dimensions)
- `CaptureToClipboard` (assert `Clipboard.ContainsImage()` — note: STA thread
  requirement)
- `CaptureStepEvidence` (assert filename pattern
  `{counter}_{step}_{timestamp}.png` and that the counter increments across
  calls on the same instance)
- `GetRegionHash` (assert same region → same hash twice; assert changed region
  → different hash)
- `WaitForRegionToChange` (change-in-time and timeout cases)
- `CompareRegionToBaseline` (within-tolerance and exceeds-tolerance cases,
  using fixture baseline images; exception case for mismatched dimensions)
- `DrawHighlightBox`, `DrawArrowToPoint`, `RedactRegion` (assert output file's
  pixels changed as expected — spot-check specific pixel colors, or diff
  against a pre-rendered "expected annotated" fixture)

### UIAutomationUtils (needs Setup: harness app with known AutomationIds/Names on a button, checkbox, text field, and tree; Cleanup: close it)

- `GetRootElement`, `FromWindowHandle`, `FromPoint` (basic bridging sanity checks against the harness window)
- `FindByAutomationId`, `FindByName`, `FindByClassName`, `FindByControlType`, `FindAllByControlType`, `GetChildren` (found and not-found cases against harness controls; `descendantsOnly` true/false cases)
- `GetName`, `GetAutomationId`, `GetClassName`, `GetControlTypeName`, `GetBoundingRectangle`, `IsEnabled`, `IsOffscreen` (assert against known harness control properties)
- `IsElementAvailable` (true for a live control; false after closing the harness window and re-checking a cached reference)
- `Invoke`, `SetValue`/`GetValue`, `Toggle`/`IsToggled`, `Expand`/`Collapse`, `Select`/`IsSelected` (exercise against harness button/text field/checkbox/tree/list; exception case calling the wrong action on the wrong control type, e.g. `Toggle` on a button)
- `WaitForElementByAutomationId`, `WaitForElementByName` (found-in-time and timeout cases, e.g. a harness control that appears after a delay)
- `HighlightElement` (visual-only — verify manually/via screenshot, same as `DialogUtils.HighlightControl`)

Note: this component needs a WinForms/WPF test harness with native `AutomationId`/`Name` values set explicitly (plain WinForms controls without explicit AutomationIds fall back to less predictable auto-generated ones) — reuse or extend the Test Harness from Phase 0 rather than building a second one.

### CommandLineUtils (needs Setup: none — it launches its own target processes; Cleanup: none)

All four methods return `bool` with an `out string message` and never throw —
for these, replace Phase 2's "exception condition on the invalid-input case"
with an outcome condition asserting `false` plus a non-null `message` on the
invalid-input case (a nonexistent executable), instead of an `Automation
exception` condition.

- `Run` (happy path against a known-good executable, asserting the `true`
  return and `out CommandResult`; timeout case against a deliberately slow
  command, assert `result.TimedOut = true` and that the child process is gone
  afterward; nonexistent-executable case → `false` + message)
- `RunShellCommand` (happy path using a shell built-in like `dir`/`echo`;
  same timeout and nonexistent-executable cases as `Run`)
- `RunElevated` (manual-only — triggers a real UAC prompt, so it can't run
  unattended in a batch; verify `out int exitCode` against a known elevated
  command, and separately verify `out bool timedOut` against a deliberately
  slow elevated command — note it may report `timedOut = true` while leaving
  the process running if the test session itself isn't elevated, since a
  non-elevated caller can't always terminate an elevated child; nonexistent
  executable/cancelled UAC prompt → `false` + message)
- `StartFireAndForget` (assert the call returns quickly, `true`, and the
  `out int processId` corresponds to a running process, via
  `WindowUtils.FindWindowsByProcessId` or a direct process check; nonexistent
  executable → `false` + message)

### ServiceUtils (needs Setup: install a small disposable test service — e.g. via `sc create ZZTestSvc binPath= ...` against a trivial do-nothing executable — never test against a real system service; Cleanup: stop and `sc delete` it)

- `IsServiceInstalled` (true for the test service; false for a made-up name)
- `IsRunning` (true after `StartService`; false after `StopService`; false for a made-up name — confirm it never throws, unlike `GetStatus`)
- `GetStatus` (assert against the test service's actual state after each Control method call below)
- `GetStartType` (assert against each value set via `SetStartType`, including `AutomaticDelayedStart` specifically — this is the one case with no equivalent already-proven code elsewhere in the repo, so it deserves the most scrutiny)
- `ListServiceNames`, `FindServiceNamesByDisplayName` (assert the test service's name/display name appear; exact vs substring cases)
- `StartService`, `StopService`, `RestartService` (happy path; timeout case against a deliberately slow-starting test service if you can construct one, or note as difficult to force and rely on hand-traced logic instead)
- `PauseService`/`ResumeService` (only if the test service is written to support `SERVICE_ACCEPT_PAUSE_CONTINUE` — otherwise this is exception-only coverage, which is still worth a test case)
- `WaitForServiceStatus` (found-in-time and timeout cases)
- `SetStartType` (all six `ServiceStartType` values against the test service; specifically verify switching from `AutomaticDelayedStart` back to `Automatic` actually clears the delayed flag — check via `sc qc <name>` or the registry, not just via `GetStartType` which is implemented by the same component under test)

**Never point any of this component's tests at a real system service** (a database engine, a network service, anything another process depends on) — a disposable, purpose-built test service is the only safe target, unlike every other component in this repo, which only ever touches a throwaway test-harness app.

## Phase 2 — Outcome conditions

For every automation above, add outcome conditions covering: the returned
value/object, the exit point taken, and — for methods documented to throw
(`Win32Exception`, `ArgumentException`, `FileNotFoundException`,
`InvalidOperationException`) — an `Automation exception` condition asserting
the expected exception type on the invalid-input test case.

## Phase 3 — Pure-logic and fixture-based cases (no live desktop needed)

Isolate everything that doesn't strictly need a live screen/window into its own
cases so they're fast and stable in CI:

- Mnemonic stripping (via `FindButtonByText`/`ListDialogControls` against
  harness labels you control)
- `GetRegionHash`/`CompareRegionToBaseline` against pre-saved fixture PNGs
  committed to the test project (round-trip these through the Lookup Table
  Data Editor's Import/Export if you want them driven from a data table)
- Annotation methods (`DrawHighlightBox`/`DrawArrowToPoint`/`RedactRegion`)
  against a fixture input image rather than a live capture

## Phase 4 — Batch run and CI gate

1. Run each component's tests as a batch from Test Explorer during development;
   use **Convert to unit test** on any manual run you like the shape of, rather
   than hand-authoring every case.
2. Flag environment-dependent tests (`OcrUtils.GetAvailableLanguages`/anything
   OCR, since it needs a language pack installed) with Batch execution options
   so they can be skipped or restricted to an environment that has the pack.
3. Deploy the project — Robot Studio bundles all unit tests into one `.pTests`
   file automatically. Wire `Pega.Tester.exe -p <project.pega> -o <results.txt>`
   into your CI pipeline alongside `Pega.ProjectAnalyzer`/`Pega.Deploy`, and
   gate the build on its exit code.
4. Because this suite drives real screen/window/input APIs, run it on an
   unlocked, interactive session (a physical or RDP-with-active-session
   runner) — headless/locked CI agents will fail every UI-touching case (this
   is the one place the attended-vs-unattended distinction matters for the
   *test runner* itself).

## Phase 5 — Debugging as you build cases

Use breakpoints + Automation Values while authoring each test case to inspect
live control handles/bounds before locking in expected values, and use
`HighlightControl`/`CaptureStepEvidence` calls inside test automations
themselves to leave a visual trail when a case fails in CI.
