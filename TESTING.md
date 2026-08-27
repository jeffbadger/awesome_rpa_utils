# Testing Plan: Pega Robotic Automation

A step-by-step plan for testing all six components (`DialogUtils`, `KeyboardUtils`,
`MouseUtils`, `OcrUtils`, `ScreenCaptureUtils`, `WindowUtils`) using Pega Robot
Studio's Unit Testing framework.

Unlike Pega's usual caution against fully-automated RPA test suites (that guidance
targets fragile, ever-changing *target-application* automations — see the RPA
Automated Testing Strategy manual), these six classes are your own deterministic
library code with a stable, versioned API. That's exactly the case where
component-level automated testing pays off.

## Phase 0 — Project setup

1. `dotnet build src/AwesomeRpaUtils.sln` to produce the six DLLs in `src/bin/`.
2. Create a new Robot Studio project, e.g. `AwesomeRpaUtils.Tests`. Add each DLL
   as a Toolbox reference (project asset) so `DialogUtils`, `KeyboardUtils`,
   `MouseUtils`, `OcrUtils`, `ScreenCaptureUtils`, `WindowUtils` all appear in the
   Toolbox and can be dragged onto automation surfaces.
3. Build a small **Test Harness** WinForms app (a few native `Button`/`Edit`/
   `Static`/`ListBox` controls with fixed, known control IDs, plus one that opens
   a `MessageBox`). Don't rely on Notepad/Calculator as the target — Notepad's
   Windows 11 dialogs are WinUI3 (no native `Button` controls), which is a
   documented gap in `DialogUtils` itself. The harness gives you deterministic
   controls for `DialogUtils`, `KeyboardUtils`, `MouseUtils`, and `WindowUtils`
   to drive.
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

- `FindDialog` (exact + substring match; not-found → `IntPtr.Zero`)
- `CanDismissDialog` (native `Button` dialog → true; simulate a WinUI-style
  dialog if you can, or just document as manual-only)
- `FindButtonByText` (exact/substring; mnemonic-stripping case — button labeled
  `"&Yes"` should match input `"Yes"`)
- `FindButtonById`, `ClickDialogButtonById` (happy path; exception case for
  unknown ID)
- `ClickDialogButtonByText` (verify dialog closes; retry path — button that
  doesn't close the dialog should return `false` after `maxAttempts`)
- `GetDialogText`, `GetControlText`, `ListDialogControls` (assert count/text/
  class/enabled state against harness's known controls)
- `HighlightControl` (no return value — verify manually/via screenshot, see
  Phase 3)
- `WaitForDialog`, `WaitForDialogToClose` (timeout-not-met and found-in-time
  cases; use a delayed-launch step in Setup to hit the "found before timeout"
  branch)

### KeyboardUtils (needs Setup: harness with a focused text field)

- `KeyDown`/`KeyUp`/`PressKey` (assert via `IsKeyDown` mid-hold, or via harness
  field echoing the key)
- `PressKeyWithModifiers`, `PressKeyCombo` (drive Ctrl+A/Ctrl+C in the harness
  field, verify effect)
- `HoldKey` (duration-honored — combine with a stopwatch or `IsKeyDown` polling)
- `TypeText` (both overloads — assert harness field's resulting text; include a
  non-BMP character, e.g. an emoji, to test the surrogate-pair path)
- `PasteText` (assert field content; assert clipboard is restored to its
  pre-call value afterward — this is the one most worth a dedicated case, since
  the restore logic has three branches: had text, was empty, had non-text)
- `IsKeyDown`, `IsModifierDown`, `GetActiveModifiers` (state query — hold a key
  via `KeyDown` in the same test, then assert)

### MouseUtils (needs Setup: harness with a click target and a scrollable list)

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
- `ClickWithRetry` (hard to force a transient Win32 failure — cover the
  retry-count/delay logic path superficially, note as low-value automated
  coverage)
- `DragAndDrop`, `DragAndHold`, `RubberBandSelect` (assert harness's
  drag-result / multi-select state; assert modifiers are released even when the
  drag throws — simulate by passing an invalid coordinate)
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
  exception case for >5000ms)
- `GetScreenWidth`/`GetScreenHeight` and remaining screen-info methods (sanity
  assert > 0)

### WindowUtils (needs Setup: harness app + one child window)

- `GetTopLevelWindows`, `FindWindowByTitle` (exact/substring/not-found),
  `FindWindowByClass`, `FindWindowsByProcessId`, `GetForegroundWindow`
- `GetWindowBounds`, `SetWindowBounds`, `MoveWindow`, `ResizeWindow` (assert
  bounds before/after)
- `GetWindowTitle`, `GetWindowClassName`, `GetWindowProcessId`,
  `IsWindowVisible`, `IsWindowResponding`
- `SetWindowState` (Minimize/Maximize/Restore/Hide/Normal — assert via
  `IsWindowVisible`/bounds)
- `CloseWindow` (assert via `WaitForWindowToClose`)
- `ActivateWindow` (assert `GetForegroundWindow` matches; note the
  foreground-lock caveat — may need the harness to not be minimized)
- `SetAlwaysOnTop` (hard to assert programmatically — visual/manual)
- `WaitForWindow`, `WaitForWindowToClose`, `WaitForWindowActive` (both a
  within-timeout and a timeout-exceeded case)
- `GetChildWindows`, `FindChildWindow`

### OcrUtils (needs Setup: a fixed test image file with known text, plus a screen region showing known text — e.g. the harness's own label)

- `GetTextFromRegion`, `GetTextFromImageFile` (assert exact/substring
  recognized text against a fixture image checked into the test project)
- `GetStructuredTextFromRegion` (assert line/word count and bounding-rect
  sanity — non-empty, within the requested region)
- `FindTextLocation` (found and not-found cases)
- `GetAvailableLanguages` (assert non-empty on the CI/build machine — flag as
  an environment dependency, see Phase 4)
- `WaitForTextToAppear` (found-in-time and timeout cases — pair with a Setup
  step that renders the text after a delay)

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
