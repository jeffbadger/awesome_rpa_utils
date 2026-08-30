# Never-Throws Implementation Record

Branch: `codex/never-throws-standard`

Worktree: `/mnt/disk0/csharp/awesome_rpa_utils-never-throws`

## Applied boundary

Every public `bool` API with an `out string` failure channel in the ten utility
assemblies now initializes its outputs and catches recoverable exceptions at the
Pega boundary through a project-local `NeverThrowsGuard`.

| Utility | Guarded public methods |
|---|---:|
| MouseUtils | 58 |
| KeyboardUtils | 9 |
| WindowUtils | 7 |
| DialogUtils | 1 |
| ScreenCaptureUtils | 13 |
| OcrUtils | 6 |
| UIAutomationUtils | 25 |
| CommandLineUtils | 4 |
| ServiceUtils | 11 |
| EventUtils and WaitMethods | 22 |
| **Total** | **156** |

Expression-bodied MouseUtils convenience methods delegate directly to guarded
methods. Existing public compatibility APIs without a message channel were not
silently changed to discard new diagnostic information; they require separate
`Try...` overload decisions during their method reviews.

## Cleanup corrections

- Mouse `Click`, `ClickAndHold`, `DragAndDrop`, `DragAndHold`, and
  `BezierDragAndDrop` now attempt button-up from `finally` after acquiring the
  button.
- Mouse compound results preserve primary and cleanup failures.
- Mouse `ClickAndRestore` now returns `false` if cursor restoration fails.
- Keyboard `PressKey` and `HoldKey` now release acquired keys from `finally`.
- Keyboard combo failure performs best-effort key/modifier release.
- Keyboard clipboard restoration failure no longer disappears after an otherwise
  successful paste.

## Verification status

- Serial solution build: zero errors and zero warnings.
- Linux-runnable tests passed: Mouse 14, Keyboard 38, Dialog 20, CommandLine 42
  with 9 Windows cases skipped, Event 46, Window 10, Service 34.
- ScreenCapture, OCR, and UI Automation test hosts require
  `Microsoft.WindowsDesktop.App` and could not run on this Linux host.
- Real Windows desktop, Pega, DPI, integrity, RDP, and state-cleanup verification
  remains required before release.
