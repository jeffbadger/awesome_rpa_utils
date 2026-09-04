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
  that case as manual-only. New: a null/empty `titlePattern` → `false`; hidden
  windows never match (toggle the harness window's visibility and re-run);
  `processId` scoping — pass the harness's real PID → found, a wrong PID →
  not found; first-match behavior — open two matching windows and assert it
  returns one of them)
- `FindAllDialogs` (new: same matching as `FindDialog` but returns every match —
  open two matching windows and assert both come back; null/empty pattern →
  empty list; `processId` scoping)
- `CanDismissDialog` (native `Button` dialog → true; simulate a WinUI-style
  dialog if you can, or just document as manual-only)
- `FindButtonByText` (exact/substring; mnemonic-stripping case — button labeled
  `"&Yes"` should match input `"Yes"`; not-found → returns `false`, never throws)
- `FindButtonById`, `ClickDialogButtonById` (happy path — assert `wasEnabled`;
  not-found case for an unknown ID → both return `false`, never throw)
- `ClickDialogButtonByText` (happy path — assert `wasEnabled` and a single
  click is sent, no retry/close verification; not-found case → `false` with
  `message` set, never throws; `waitForEnabledMs`/`pollIntervalMs` — assert
  they flow through to the click, e.g. a large `waitForEnabledMs` still
  clicks a slow-enabling button)
- `GetDialogText`, `GetControlText`, `ListDialogControls` (assert count/text/
  class/enabled state against harness's known controls)
- `ClickButton` (assert the `bool` return matches whether the target was
  actually enabled when clicked — force the disabled case if the harness can
  hold a button disabled briefly)
- `HighlightControl` (assert the `bool` return — `true` for a valid handle;
  verify the actual flash manually/via screenshot, see Phase 3)
- `WaitForDialog`, `WaitForDialogToClose` (timeout-not-met and found-in-time
  cases; use a delayed-launch step in Setup to hit the "found before timeout"
  branch; new `exactMatch` — a dialog titled "Confirm changes" should match
  `"Confirm"` with `exactMatch: false` but not `exactMatch: true`)

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
  non-BMP character, e.g. an emoji, to test the surrogate-pair path, which must
  compose a single character (high-down, low-down, low-up, high-up); null
  `text` → `false` + message)
- `PasteText` (assert field content; assert clipboard is restored to its
  pre-call value afterward — this is the one most worth a dedicated case, since
  the restore logic has three branches: had text, was empty, had non-text)
- `IsKeyDown`, `IsModifierDown`, `GetActiveModifiers` (state query — hold a key
  via `KeyDown` in the same test, then assert; `IsModifierDown(ModifierKeys.None)`
  must return `false`; still plain `bool`/enum returns with no `message` parameter)

### MouseUtils (Setup: run the test harness — it provides a click target with a button-detail label, a scrollable multi-select list, and a drag target)

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
  variants (assert harness button fired; distinguish buttons via the harness's
  last-button label, `lblClickDetail`)
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
- `DragAndDrop`, `DragAndHold`, `RubberBandSelect` (assert the harness drag
  target's status label, `lblDragStatus` — press/release coordinates — for
  drag results, and the list's selection state for rubber-band; assert
  modifiers are released even when the drag fails — simulate by passing an
  invalid `MouseButton` cast)
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

- `GetTopLevelWindows`, `FindWindowByTitle` (exact/substring/not-found; a
  null/empty title returns `IntPtr.Zero` rather than throwing),
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
- `GetChildWindows`, `FindChildWindow` (both exact and `exactMatch: false`
  substring matching; null/empty filters skip that axis)

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

The null/empty-text guards, non-positive width/height guards, and the
missing/corrupt-file and no-poll-stall never-throw paths already have xunit
coverage in `src/ocrutils/OcrUtils.Tests`
(`dotnet test src/ocrutils/OcrUtils.Tests/OcrUtils.Tests.csproj` — like
`UIAutomation.Tests`, this project runs only on Windows because the
component's `UseWindowsForms` flows the WindowsDesktop runtime requirement
into the test project; `dotnet build` works anywhere).

### ScreenCaptureUtils (some cases need no live screen at all — see Phase 3)

All methods here except `CaptureToClipboard` return `bool` with an
`out string message` and never throw — for these, replace Phase 2's
"exception condition on the invalid-input case" with an outcome condition
asserting `false` plus a non-null `message` on the invalid-input case (bad
dimensions, a missing image file, an invalid window handle), instead of an
`Automation exception` condition. `WaitForRegionToChange`/
`CompareRegionToBaseline` additionally need a case that distinguishes a real
failure (`message` set) from a normal not-changed/exceeds-tolerance result
(`message == null`).

- `CaptureScreenToFile`, `CaptureRegionToFile`, `CaptureWindowToFile`,
  `CaptureActiveWindowToFile`, `CaptureAroundPointToFile` (assert file exists,
  non-zero size, correct pixel dimensions; bad dimensions/invalid handle →
  `false` + message)
- `CaptureToClipboard` (assert `Clipboard.ContainsImage()` — note: STA thread
  requirement)
- `CaptureStepEvidence` (assert filename pattern
  `{counter}_{step}_{timestamp}.png` and that the counter increments across
  calls on the same instance; blank step name/folder path → `false` + message)
- `GetRegionHash` (assert same region → same hash twice; assert changed region
  → different hash; bad dimensions → `false` + message)
- `WaitForRegionToChange` (change-in-time and timeout cases; bad dimensions →
  `false` + non-null message, distinct from the timeout case's `false` +
  `message == null`)
- `CompareRegionToBaseline` (within-tolerance and exceeds-tolerance cases,
  using fixture baseline images; missing file/mismatched dimensions → `false`
  + non-null message, distinct from the exceeds-tolerance case's `false` +
  `message == null`)
- `DrawHighlightBox`, `DrawArrowToPoint`, `RedactRegion` (assert output file's
  pixels changed as expected — spot-check specific pixel colors, or diff
  against a pre-rendered "expected annotated" fixture; missing image file →
  `false` + message)

### UIAutomationUtils (needs Setup: harness app with known AutomationIds/Names on a button, checkbox, text field, and tree; Cleanup: close it)

All methods here except `GetRootElement`, `FromWindowHandle`, `FromPoint`, and
`IsElementAvailable` return `bool` with an `out string message` and never
throw — for these, replace Phase 2's "exception condition on the
invalid-input case" with an outcome condition asserting `false` plus a
non-null `message` on the invalid-input case (a null element/parent, an
unsupported action pattern), instead of an `Automation exception` condition.
The `FindBy*` methods additionally need a case that distinguishes a real
argument error (`false` + non-null `message`) from a normal not-found result
(`false` + `message == null`).

- `GetRootElement`, `FromWindowHandle`, `FromPoint` (basic bridging sanity checks against the harness window)
- `FindByAutomationId`, `FindByName`, `FindByClassName`, `FindByControlType`, `FindAllByControlType`, `GetChildren` (found and not-found cases against harness controls; `descendantsOnly` true/false cases; null parent → `false` + non-null message)
- `GetName`, `GetAutomationId`, `GetClassName`, `GetControlTypeName`, `GetBoundingRectangle`, `IsEnabled`, `IsOffscreen` (assert against known harness control properties; null element → `false` + message)
- `IsElementAvailable` (true for a live control; false after closing the harness window and re-checking a cached reference)
- `Invoke`, `SetValue`/`GetValue`, `Toggle`/`IsToggled`, `Expand`/`Collapse`, `Select`/`IsSelected` (exercise against harness button/text field/checkbox/tree/list; wrong-pattern case calling the wrong action on the wrong control type, e.g. `Toggle` on a button, → `false` + message instead of an exception)

The platform-independent input guards, `UiControlType` mapping, and Wait
abort-on-argument-error paths have xunit coverage in
`src/uiautomationutils/UIAutomation.Tests` — run it with
`dotnet test src/uiautomationutils/UIAutomation.Tests/UIAutomation.Tests.csproj`
(Windows only: the component's UIA types cannot load on non-Windows hosts,
so a solution-wide `dotnet test` on Linux reports this project's testhost
as unable to start while every other project still runs).
- `WaitForElementByAutomationId`, `WaitForElementByName` (found-in-time and timeout cases, e.g. a harness control that appears after a delay; null parent → `false` + non-null message, distinct from the timeout case's `false` + `message == null`)
- `HighlightElement` (visual-only — verify manually/via screenshot, same as `DialogUtils.HighlightControl`; null element → `false` + message)

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
  afterward; nonexistent-executable case → `false` + message; non-default
  `outputEncoding` case — a child writing UTF-8 decoded with
  `Encoding.UTF8` vs garbled under the default; a chatty child vs the
  capture cap → `result.OutputTruncated = true`, and a single oversized
  newline-free line → truncated rather than blowing past the cap; a
  null-key `environmentVariables` entry → `false` + message, never throws.
  Most of these already have xunit coverage in
  `src/commandlineutils/CommandLineUtils.Tests` —
  `dotnet test src/commandlineutils/CommandLineUtils.Tests/CommandLineUtils.Tests.csproj`)
- `RunShellCommand` (happy path using a shell built-in like `dir`/`echo`;
  same timeout and nonexistent-executable cases as `Run`; embedded-quote
  quoting pin cases — a command with embedded `"` and one ending in a
  trailing `\` must execute verbatim under `cmd /d /s /c`; allowlist cases —
  allowed program → runs, disallowed or empty segment → `false` + message
  naming the segment, compound command where only the *second* segment is
  disallowed → still `false` before anything runs. The tokenizer and the
  Windows quoting pins are covered by
  `CommandLineUtils.Tests/CommandLineUtilsTests.cs`)
- `StartFireAndForget` (assert the call returns quickly, `true`, and the
  `out int processId` corresponds to a running process, via
  `WindowUtils.FindWindowsByProcessId` or a direct process check; nonexistent
  executable → `false` + message; also visually confirm **no console window
  appears** for a console executable — `CreateNoWindow` is set, but it's a
  desktop-visible behavior worth one manual look)
- `RunElevated` (manual-only — triggers a real UAC prompt, so it can't run
  unattended in a batch; verify `out int exitCode` against a known elevated
  command, and separately verify `out bool timedOut` against a deliberately
  slow elevated command — note it may report `timedOut = true` while leaving
  the process running if the test session itself isn't elevated, since a
  non-elevated caller can't always terminate an elevated child; nonexistent
  executable/cancelled UAC prompt → `false` + message; a relative `fileName`
  → `false` + message, rejected before any UAC prompt)
- `StartFireAndForget` (assert the call returns quickly, `true`, and the
  `out int processId` corresponds to a running process, via
  `WindowUtils.FindWindowsByProcessId` or a direct process check; nonexistent
  executable → `false` + message)

### EventUtils (needs Setup: a real window to create/destroy — notepad.exe works; Cleanup: kill it)

Every public method returns `bool` and never throws — abnormal results are
reported through an `out string message` (null on success), and timeouts through
`out bool timedOut` / `out bool hasEvent`. For these, replace Phase 2's
"exception condition on the invalid-input case" with an outcome condition
asserting `false` (plus a non-null `message`), instead of an `Automation
exception` condition. The engine is Windows-only: `Initialize()` returns `false`
off-Windows, and the real-window cases below are the ones that prove the hook
actually delivers events.

- `Initialize`/`Start`/`Stop`/`Dispose` (idempotent: call `Start` twice, `Dispose`
  twice; `Initialize` after `Dispose` restarts the engine; `Stop` unhooks — no
  events arrive after it, and `Start` re-hooks; invalid category CSV → `Start`
  returns `false`)
- `WaitForWindowCreated` (launch notepad → `true` with an event whose
  `ProcessName` is "notepad" and `Hwnd` is non-zero within the timeout; no
  window → `false` + `timedOut = true` + a timeout message; engine not started →
  immediate `false` + `timedOut = false` + a message)
- `WaitForWindowDestroyed` (close the window → event whose `Title` was captured
  before destruction — proves enrichment-before-death; no close → timeout)
- `WaitForWindowShown`, `WaitForForegroundChanged`, `WaitForTitleChanged` (drive
  the change — show/hide the window, switch foreground, rename — and assert the
  matching event; `titleRegex` filters the title)
- `WaitForDialogAppeared` (a `#32770` dialog — e.g. a `MessageBox` — matches via
  the class heuristic; no dialog → timeout)
- `WaitForStateChanged` (minimize/restore the window → `State` "Minimized"/
  "Visible"; `stateRegex` filters)
- `WaitForMenuOpened` (open a menu in the harness → event; no menu → timeout)
- `WasWindowCreated` (non-blocking lookback: true right after a create, false
  after the window is long gone or for a non-matching filter)
- `CancelWaits` (start two waits, cancel → both return within 100 ms with
  `timedOut = true`)
- `Subscribe`/`Unsubscribe`/`GetNextEvent`/`GetNextEvents`/`HasEvents`/
  `ClearQueue` (two subscriptions with different filters/categories → independent
  queues, no cross-feed; unknown subscription id → `GetNextEvent` returns `false`
  with a non-null `message`; malformed filter JSON → `Subscribe` returns `false`
  with a non-null `message`, unknown JSON keys are ignored; duplicate id →
  `false` with a non-null `message`)
- `SetDebounce` (rapidly show/hide a window 10× → SHOW count after debounce ≤ 3;
  `SetDebounce("WindowShown", 0)` disables coalescing)
- `SetQueueLimits` (limit 5 + "DropOldest", flood 50 events → `HasEvents` == 5,
  oldest gone; "DropNewest" keeps oldest; "Block" never exceeds the limit)
- `DumpRecentEvents` (returns valid JSON; bounded at 500 events regardless of
  subscriptions)

### ServiceUtils (needs Setup: install a small disposable test service — e.g. via `sc create ZZTestSvc binPath= ...` against a trivial do-nothing executable — never test against a real system service; Cleanup: stop and `sc delete` it)

- `IsServiceInstalled` (true for the test service; false + message for a made-up name — never an exception)
- `IsRunning` (true after `StartService`; false after `StopService`; false + message for a made-up name)
- `TryGetStatus` (true + status against the test service's actual state after each Control method call below; false + message for a made-up name)
- `TryGetStartType` (true + value asserting against each type set via `SetStartType`, including `AutomaticDelayedStart` specifically — this is the one case with no equivalent already-proven code elsewhere in the repo, so it deserves the most scrutiny)
- `ListServiceNames`, `FindServiceNamesByDisplayName` (assert the test service's name/display name appear; exact vs substring cases; empty filter → empty list, not every service)
- `StartService`, `StopService`, `RestartService` (happy path; idempotent cases — `StartService` on an already-running service and `StopService` on an already-stopped one both return `true` with an "already" note in the message; `RestartService` on a stop-phase failure must skip the start phase and report that failure in the message; timeout case against a deliberately slow-starting test service if you can construct one, or note as difficult to force and rely on hand-traced logic instead)
- `PauseService`/`ResumeService` (only if the test service is written to support `SERVICE_ACCEPT_PAUSE_CONTINUE` — then also cover the idempotent already-paused/already-running `true` cases; otherwise this is exception-path-only coverage, which is still worth a test case)
- `WaitForServiceStatus` (found-in-time and timeout cases; negative `timeoutMs` → `false` + message immediately)
- `SetStartType` (all six `ServiceStartType` values against the test service; specifically verify switching from `AutomaticDelayedStart` back to `Automatic` actually clears the delayed flag — check via `sc qc <name>` or the registry, not just via `TryGetStartType` which is implemented by the same component under test; an undefined cast value → `false` + message, never an exception)

The null/empty-name, negative-timeout, undefined-enum, and enum-to-Win32
mapping guards already have Linux-runnable xunit coverage in
`src/serviceutils/ServiceUtils.Tests`
(`dotnet test src/serviceutils/ServiceUtils.Tests/ServiceUtils.Tests.csproj` —
unlike `UIAutomation.Tests`, this project runs on non-Windows machines because
`ServiceController` arrives as a NuGet package rather than a `UseWPF` framework
reference, so no WindowsDesktop runtime requirement flows into the test project).

**Never point any of this component's tests at a real system service** (a database engine, a network service, anything another process depends on) — a disposable, purpose-built test service is the only safe target, unlike every other component in this repo, which only ever touches a throwaway test-harness app.

### EventLogUtils (needs Setup: register a disposable test source/log, e.g. `ZZTestEventLogUtils`, via `CreateEventSourceSimple` from an elevated session — never test against Application/System/Security directly; Cleanup: remove the source via `eventvwr.msc`/PowerShell `Remove-EventLog`)

- `ListLogNames`/`ListLogNamesDelimited`, `DoesLogExist`/`DoesLogExistSimple`, `DoesSourceExist`/`DoesSourceExistSimple`, `TryGetLogNameForSource` (against the real registry/log list — assert the test log/source appear; a made-up name returns false + message, never an exception)
- `CreateEventSource`/`CreateEventSourceSimple` (happy path against the test log; idempotent case — calling again with the same source+log returns true + `alreadyExisted = true`; hard-failure case — calling again with a *different* log name for the same source returns false + a message explaining Windows doesn't allow reassignment. **Requires an elevated test session** — cannot run unattended in plain CI, same caveat as `ServiceUtils.SetStartType`)
- `WriteEntry`/`WriteEntrySimple` (write a known message/level/event id via the test source, then verify it via `TryGetMostRecentEntry`/`QueryRecentEntriesJson`; a message longer than the practical write limit is truncated — assert the truncation note appears in `errorMessage` on an otherwise-successful write)
- `TryGetMostRecentEntry`, `CountMatchingEntries`, `QueryRecentEntriesJson`, `DumpRecentEntriesJson`, `QueryByXPath` (write several known entries via `WriteEntry` first, then assert each filter dimension — source, level, event id, `sinceIso8601` time range, `messageContains` substring — in isolation and combined; a genuine "no match" returns false + null message, distinct from a real failure such as a bad log name, which returns false + a non-null message)
- `WaitForEntry`/`WaitForEntrySimple` (found-in-time case — write the matching entry from a delayed background action after the wait starts; timeout case — nothing written, assert `timedOut = true`; a pre-existing matching entry from *before* the wait started must NOT satisfy it)
- `ExportFilteredLog` + `QueryExportedLog` (export the test log's entries to a temporary `.evtx`, then query the exported file and assert the same entries come back; a missing or corrupt `.evtx` path returns false + a non-null message, distinct from the live-log "not found" case)
- Reading the **Security** log from a non-elevated session (assert the specific "access is denied ... run elevated or add the caller to Event Log Readers" message text, not a generic failure — a good manual-only negative-path case)

The null/empty-argument, negative-timeout/negative-maxCount, undefined-enum,
and filter-parsing/XPath-building guards already have Linux-runnable xunit
coverage in `src/eventlogutils/EventLogUtils.Tests`
(`dotnet test src/eventlogutils/EventLogUtils.Tests/EventLogUtils.Tests.csproj` —
like `ServiceUtils.Tests`, this project runs on non-Windows machines because
`System.Diagnostics.EventLog` arrives as a NuGet package rather than a
`UseWPF`/`UseWindowsForms` framework reference, so no WindowsDesktop runtime
requirement flows into the test project).

**Never point any of this component's tests at the Application/System/Security logs directly** — always create and use a disposable test log/source, and never delete or clear a real system log.

### SessionUtils (needs an interactive Windows logon; several cases below additionally need a second concurrent session — fast user switching or a second RDP connection — or an actual Windows service; no Setup/Cleanup fixture is created or destroyed, unlike ServiceUtils/EventLogUtils, since this component only ever reads/acts on sessions that already exist)

This component's Linux-testable guard/logic surface is much smaller than
EventLogUtils' — most methods either take no input to guard, or reach a
native call on their very first line with nothing to validate first. Nearly
everything below genuinely requires a live Windows session; be realistic
about that rather than assuming a plain guard-test pass covers it.

- `GetCurrentSessionId`, `GetActiveConsoleSessionId`, `IsCurrentSessionOnConsole`, `GetCurrentSessionKind`, `IsRunningAsServiceSession` (against the actual interactive logon; `GetCurrentSessionKind` returning `Rdp` specifically needs an actual RDP session, not just the console)
- `GetSessionKind`, `GetCurrentSessionConnectState`/`GetSessionConnectState`, `IsCurrentSessionDisconnected`/`IsSessionDisconnected`, `GetCurrentSessionUser`/`GetSessionUser`, `EnumerateSessionsJson` for a session other than the caller's (needs a second concurrent session; a made-up/negative session ID returns false + message, never an exception)
- `IsWorkstationLockedSimple`/`IsWorkstationLocked` (lock the workstation with Win+L during the test and confirm `true`; separately trigger a UAC consent prompt and confirm it stays `false` — the core hypothesis under test, per the component README's Notes & Caveats)
- `IsInputDesktopAvailableSimple`/`IsInputDesktopAvailable` (needs both a lock and a UAC/Ctrl+Alt+Del trigger to exercise both "unavailable" paths, plus an actual Session-0 service context for the "no desktop at all" path)
- `IsSessionInteractiveSimple`/`IsSessionInteractive` (needs a real Windows service, running in Session 0, to exercise the `false` path — console/RDP sessions only exercise `true`)
- `GetIdleTimeMilliseconds` (manual, timing-sensitive — idle the session for a known number of seconds and assert the result is within a reasonable tolerance)
- `WaitForSessionConnectState`/`Simple`, `WaitForInputDesktopAvailable`/`Simple`, `WaitForWorkstationUnlocked`/`Simple` (found-in-time case — trigger the state change from a second session/RDP client partway through the wait; timeout case — nothing changes, assert `timedOut = true`; negative `timeoutMs`/non-positive `pollIntervalMs` → `false` + message immediately, never an exception)
- `LockWorkstation` (disruptive to whoever's session runs it — run this from a disposable/secondary RDP session, last in the manual pass, never from the primary console you're working on)
- `DisconnectSession`/`DisconnectCurrentSession` (needs a disposable second RDP session — never disconnect the primary session — and admin rights to exercise the "disconnect someone else's session" privilege-checked path; disconnecting your own session needs no special privilege)

The null/negative-argument guards and the pure
`TryToSessionConnectState`/`TryToSessionKind`/`TryParseConnectStates`
enum-mapping/filter-parsing logic already have Linux-runnable xunit coverage
in `src/sessionutils/SessionUtils.Tests`
(`dotnet test src/sessionutils/SessionUtils.Tests/SessionUtils.Tests.csproj` —
like `ServiceUtils.Tests`/`EventLogUtils.Tests`, this project has zero NuGet
packages and no `UseWPF`/`UseWindowsForms` framework reference, so no
WindowsDesktop runtime requirement flows into the test project).

**Never run `LockWorkstation` or `DisconnectSession`/`DisconnectCurrentSession` against a session you or someone else is actively relying on** — always test these two from a disposable, expendable RDP session set up specifically for this purpose.

### FileWatchUtils (no Setup/Cleanup fixture needed — every test uses a disposable per-test temp directory it creates and removes itself)

Unlike every other component above, this one needs only a light manual note
here — its `.Tests` project already exercises real functional behavior for
almost the entire surface (real temp-directory files, real exclusive-lock
detection, real `FileSystemWatcher` events, real concurrent `ClaimFile`
races, real hashing), not just guard clauses, because every operation is
plain cross-platform BCL file I/O with zero P/Invoke. Only two scenarios
genuinely can't be exercised there:

- `ReplaceFile`'s real happy path — `File.Replace` throws
  `PlatformNotSupportedException` on non-Windows by .NET design, so its
  success case self-skips outside Windows in the xunit project; verify it
  manually on Windows (replace an existing file's contents, with and
  without a backup path, and confirm the backup file's content matches the
  original destination).
- A real *other application* (not the test process itself) holding a file
  open — `IsFileLocked`/`WaitForFileUnlocked` are exercised against a lock
  the test process holds on itself, which is the real code path, but
  confirming behavior against, say, Excel or Notepad holding a file open is
  worth a manual spot-check.

The full guard-clause, enum-mapping, and real-functional-behavior xunit
coverage is in `src/filewatchutils/FileWatchUtils.Tests`
(`dotnet test src/filewatchutils/FileWatchUtils.Tests/FileWatchUtils.Tests.csproj`
— zero NuGet packages and no `UseWPF`/`UseWindowsForms` framework
reference, so it runs the same way on Linux as on Windows).

### ArchiveUtils (no Setup/Cleanup fixture needed — every test uses a disposable per-test temp directory it creates and removes itself)

Same story as `FileWatchUtils`: this component is plain
`System.IO.Compression` + `System.IO` with zero P/Invoke, so its `.Tests`
project exercises real functional behavior for nearly the entire surface —
real archives built and extracted, a real reproduced zip-slip attempt (an
entry named `"../../evil.txt"` extracted via `ExtractSingleFile`, asserting
it never lands outside the destination directory), a real corrupted-CRC
entry (one byte flipped directly in the archive's raw on-disk bytes), and
real zip-bomb-style fixtures (an artificially high compression ratio; many
entries whose summed declared size trips a total-size limit). Only one
scenario genuinely can't be exercised there:

- A real password-encrypted `.zip` entry — `System.IO.Compression`'s own
  writer has no encryption support at all, so the xunit fixture instead
  sets the ZIP general-purpose bit flag's encryption bit directly in raw
  bytes to exercise `IsEncrypted`/`HasEncryptedEntries`/the
  `ValidateArchiveCrcJson` skip-encrypted path. If you want to confirm
  behavior against a genuinely password-protected archive (e.g. one
  created by 7-Zip or WinRAR with a password), do it manually: confirm
  `HasEncryptedEntries`/`ListArchiveContentsJson`'s `IsEncrypted` field
  report `true`, and that no method in this component attempts to extract
  or decrypt it.

The full guard-clause and real-functional-behavior xunit coverage is in
`src/archiveutils/ArchiveUtils.Tests`
(`dotnet test src/archiveutils/ArchiveUtils.Tests/ArchiveUtils.Tests.csproj`
— zero NuGet packages and no `UseWPF`/`UseWindowsForms` framework
reference, so it runs the same way on Linux as on Windows).

### TerminalUtils (needs a genuine Windows console session; most cases below launch their own target console process — Setup: none beyond that; Cleanup: kill any console process left running by a test)

Unlike `FileWatchUtils`/`ArchiveUtils`, this component is fundamentally
Win32 console-API/P/Invoke-heavy (`AttachConsole`, `GetConsoleScreenBufferInfo`,
`ReadConsoleOutputW`, `WriteConsoleInputW`) — almost everything below
genuinely requires a live Windows console, not just a guard-test pass. Be
realistic about that.

- `StartConsoleProcess` — launch a real console app (e.g. `cmd.exe`); confirm the returned `processId` corresponds to a process with its own real, visible console window (not hidden, not redirected).
- `IsConsoleAttachable` — `true` against the PID from the case above; `false` against a GUI-only/no-console process's PID; `false` (with a message, never an exception) against a PID from an already-exited process.
- `GetCursorPosition`/`ReadScreenRowsJson`/`CaptureScreenText` — against a scripted console app that prints known fixed text at known positions (e.g. a small test script that writes a few labeled lines and moves the cursor to a known spot). Assert row/column/text/fields match expectations, including a case with `Console.ForegroundColor`/`BackgroundColor` changes mid-line to exercise ANSI reconstruction (`CaptureScreenText(..., preserveAnsi: true, ...)`), and a case confirming `preserveAnsi: false` never contains an escape character.
- `WaitForScreenText`/`Simple` — found-before-timeout (the target app prints the awaited text partway through the wait) and genuine-timeout cases, in both substring and regex mode; an invalid regex pattern returns `false` + message immediately, before any polling starts.
- `WaitForScreenChange`/`Simple` — drive the target app to redraw or print something new partway through the wait and confirm `changed = true`; assert `timedOut = true` against a static, unchanging screen; confirm `timeoutMs = 0` times out immediately without ever comparing again.
- `WriteText`/`WriteLine` — against a real shell REPL (e.g. `cmd.exe` at its prompt), with the console window **unfocused or minimized**, confirm the shell actually receives and processes the injected keystrokes (this validates the no-focus-required design, the actual reason this component uses `WriteConsoleInputW` instead of focus-dependent keyboard simulation). Also confirm `WriteLine`'s trailing carriage return actually submits the line (the shell executes the command), not just that it's appended to the buffer.
- A buffer resize between reading buffer info and reading its contents, and a target process exiting mid-attach — both hard to force deterministically; exploratory/manual only, not a required pass/fail gate.
- Windows Terminal/ConPTY behavior — an explicit, flagged-as-open-risk item: on a real Windows 11 machine with Windows Terminal as the default console host, confirm attach/read/write still work against a target process's console, since this component's design assumes (but has not verified) that `AttachConsole`/`ReadConsoleOutputW` work at the buffer level regardless of which terminal emulator hosts the visible window.

The null/non-positive-`processId`/negative-timeout argument guards and the
pure `AnsiReconstruction`/`FieldSplitting` logic already have Linux-runnable
xunit coverage in `src/terminalutils/TerminalUtils.Tests`
(`dotnet test src/terminalutils/TerminalUtils.Tests/TerminalUtils.Tests.csproj`
— every test in that project asserts the exact guard message text, not just
a `false` return, specifically so a test can't accidentally "pass" by
reaching a deeper native-call failure instead of the intended early guard).

### LocalQueueUtils (local filesystem; Setup: create a unique Run queue; Cleanup: dispose the component and remove its test queue)

- Create both Run and Persistent queues and confirm their paths remain beneath
  `%LOCALAPPDATA%\AwesomeRpaUtils\Queues`.
- Add individual JSON, a JSON array, text lines, file references, and imported
  files; confirm the added counts and payloads.
- Drive `TryTakeNext` through completion, retry/delay, rejection, renewal, and
  expired-lease recovery. Verify stale lease tokens cannot mutate work.
- Open the same queue from a second component and confirm it returns `false`
  with an ownership message rather than throwing.
- Restart the component, reopen the queue, and confirm ready/completed/rejected
  state persists. Verify priority and availability ordering.
- Confirm `DeleteQueue` refuses a non-empty queue without explicit confirmation,
  removes imported queue-owned files when confirmed, and never deletes external
  files added through `AddFileReferences`.
- Exercise invalid JSON, traversal/out-of-root paths, invalid priorities,
  malformed queue markers, and interrupted/corrupt item files.

The platform-independent xunit coverage is in
`src/localqueueutils/LocalQueueUtils.Tests`
(`dotnet test src/localqueueutils/LocalQueueUtils.Tests/LocalQueueUtils.Tests.csproj`).

### StackUtils (no setup required; Cleanup: dispose the component)

- Push mixed text, JSON, and file-reference items and verify strict LIFO order,
  peek without removal, count, empty success outputs, and clear counts.
- Exercise default capacity, valid changes, out-of-range values, refusal to
  shrink below current count, and atomic bulk failure at capacity.
- Push every JSON value kind and malformed JSON; verify raw values returned by
  pop and next-to-pop ordering in snapshot JSON.
- Push CRLF/LF/CR text with empty lines, and verify line content and stack order.
- Discover files recursively and non-recursively. Verify normalized absolute
  paths, deterministic sorting, missing paths, and invalid search patterns.
- Use parallel branches to push and pop distinct values, then confirm no lost,
  duplicate, or corrupt items. Ordering is guaranteed within each serialized
  operation, not between racing callers.
- Dispose a populated component and verify every later method returns `false`
  with initialized outputs and an actionable message.

The platform-independent xunit coverage is in
`src/stackutils/StackUtils.Tests`
(`dotnet test src/stackutils/StackUtils.Tests/StackUtils.Tests.csproj`).

### DataBagUtils (no external setup; configure properties before initialization)

- Preload typed definitions from design-time JSON and a relative/absolute JSON
  file; verify initialization is atomic and optionally sealed.
- Build a schema with `BeginInitialization`, direct typed definitions, inferred
  JSON, typed JSON, and conventional/mapped DataTables. Verify complete and
  cancel preserve the correct active bag.
- Confirm `SetValue` uses the existing declaration, native typed setters reject
  mismatches, and runtime methods cannot introduce unknown names or new types.
- Apply JSON objects and DataTable rows atomically under both unknown-name
  policies, including malformed values late in a bulk update.
- Exercise every value type, invariant numeric parsing, ISO DateTime parsing,
  raw JSON validation/round-tripping, null values, and case-sensitive versus
  case-insensitive names.
- Verify read-only and write-once enforcement, must-have-value reporting,
  sensitive snapshot redaction, default restoration, and write-once reset.
- Run concurrent updates to distinct names and verify no state corruption.
- Dispose populated and staged bags and confirm later methods fail with
  initialized outputs and actionable messages.

The platform-independent xunit coverage is in
`src/databagutils/DataBagUtils.Tests`
(`dotnet test src/databagutils/DataBagUtils.Tests/DataBagUtils.Tests.csproj`).

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
  harness labels you control) — also covered without a live desktop by the
  xunit project `src/dialogutils/DialogUtils.Tests`
  (`dotnet test src/dialogutils/DialogUtils.Tests/DialogUtils.Tests.csproj`;
  the interop cases self-skip on non-Windows). The xunit project also covers
  the null/empty-pattern paths (`FindDialog`/`FindAllDialogs`/`WaitForDialog`/
  `ClickDialogButtonByText`) and the invalid-handle never-throw contract
  (`HighlightControl`/`ClickButton`/`WaitForDialogToClose`)
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
