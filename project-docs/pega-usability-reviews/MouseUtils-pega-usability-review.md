# MouseUtils Pega API Usability Review

## Summary

Most `MouseUtils` methods are directly usable from Robot Studio because their
ports are primitive values or enums. Window handles are not typeable constants,
but the affected methods are generally chainable from `WindowUtils` find/wait
methods or from `MouseUtils.GetWindowAtPoint`.

No public method has an array, list, dictionary, delegate, or arbitrary framework
object as an input. The main friction is in complex outputs.

## Non-scalar port review

| Method group | Non-scalar port | Rating | Assessment |
|---|---|---|---|
| `GetPosition` | `out Point position` | Direct scalar overload added | `GetX`/`GetY` were already scalar alternatives, but two calls are not one atomic snapshot. Added `GetPosition(out int x, out int y, out string message)`, reading both from a single native call; the `Point` overload remains for callers with a proxy. |
| `GetCursorClip` | `out Rectangle clip` | Direct scalar overload added | Added `GetCursorClip(out int left, out int top, out int width, out int height, out string message)`; the `Rectangle` overload remains for callers with a proxy. |
| `GetWindowBounds` | `IntPtr hWnd`, `out Rectangle bounds` | Chainable input; direct scalar overload added | The handle can come from WindowUtils or `GetWindowAtPoint`. Added `GetWindowBounds(IntPtr hWnd, out int left, out int top, out int width, out int height, out string message)`; the `Rectangle` overload remains for callers with a proxy. |
| `ClickWindow`, `ClickWindowAtPoint`, `ClickWindowAtClientPoint`, `DoubleClickWindowAtClientPoint` | `IntPtr hWnd` | Chainable | Intended to consume a handle from WindowUtils/DialogUtils or `GetWindowAtPoint`. Fine when utilities share one automation, but awkward as a standalone MouseUtils workflow. Document the producer→consumer chain prominently. |
| `ClientPointToScreen`, `ScreenPointToClient`, `ClickAtClientPoint`, `ClickAtRelativePosition` | `IntPtr hWnd` | Chainable | Same handle chain. Coordinate outputs are already primitive. No adapter required. |
| `SafeClickAt` | `IntPtr expectedWindowHandle` | Chainable | The safety value must come from a prior find/wait operation. This is appropriate because manually typing a stale numeric handle would undermine the safety guarantee. |
| `GetWindowAtPoint` | returns `IntPtr` | Chainable producer | The result is useful as a direct data link into handle-consuming methods. It is not meant for manual entry or persistence. |

## Enum ports

`MouseButton`, `ModifierKeys`, and `SystemCursorType` should appear as selectable
enum constants and are rated direct. Verify this in the supported Robot Studio
version; if flags enums cannot be combined conveniently, add named convenience
methods for common combinations rather than integer-mask inputs.

## Recommended changes

1. **Done.** Added a scalar `GetPosition(out int x, out int y, out string message)`
   overload, reading both coordinates from one native call.
2. **Done.** Added scalar-output overloads for `GetCursorClip` and
   `GetWindowBounds`.
3. **Done.** Added a producer → consumer example wiring
   `WindowUtils.FindWindowByTitle` into `GetWindowBounds`/`ClickAtClientPoint` to
   [Documentation/WindowRelativeTargeting.md](../../src/mouseutils/Documentation/WindowRelativeTargeting.md).
4. Verify flags-enum selection for `ModifierKeys` on the actual Robot Studio
   design surface. *(Out of scope for this pass.)*

## Verdict

`MouseUtils` has no non-scalar input that is effectively unusable. Its `IntPtr`
inputs are deliberate chain types. `GetPosition`, `GetCursorClip`, and
`GetWindowBounds` now have primitive-output overloads alongside their structure
overloads, removing the proxy-configuration friction for designers without one.
`ModifierKeys` flag-combining support on the actual Robot Studio design surface
remains an open question for a future pass.

## Addendum: naming ambiguity fix

The additive scalar overloads above solved usability, but they introduced a
new problem: two same-named methods whose Pega-visible (non-`out`)
parameter lists became identical, differing only in an `out` parameter's
type. C# overload resolution handles this fine, but Pega Robot Studio's
designer surface cannot disambiguate two overloads by `out` parameter type
alone - both entries in the designer's method picker would look the same.

Three method groups in this component had this problem: `GetPosition`,
`GetCursorClip`, `GetWindowBounds`. In each case, one overload returns a
`Point`/`Rectangle` via a single `out` parameter and the other returns the
same data as multiple scalar `out int` parameters - the non-`out` inputs
(none, for `GetPosition`/`GetCursorClip`; just `IntPtr hWnd` for
`GetWindowBounds`) are identical either way.

Fixed by renaming the struct-returning overload (the one Pega's designer
cannot chain into a native output proxy without extra configuration)
rather than the newer, already-scalar overload, following this
repository's convention of breaking changes over compatibility shims:

| Old name | New name | Reason |
|---|---|---|
| `GetPosition(out Point, out string)` | `GetPositionAsPoint` | Return type, not an extra output, was the only difference from the scalar overload |
| `GetCursorClip(out Rectangle, out string)` | `GetCursorClipAsRectangle` | Same |
| `GetWindowBounds(IntPtr, out Rectangle, out string)` | `GetWindowBoundsAsRectangle` | Same |

The plain (un-suffixed) name in each pair now belongs to the scalar
overload - the one most usable directly from a Pega Robot Studio flow -
per the naming convention documented in the component `README.md`.

## Addendum: correctness/robustness remediation (closing out REVIEW.md/REMEDIATION_PLAN.md)

Everything above concerns API *shape* (is a port usable from a Pega design
surface); this addendum covers a separate remediation pass on API
*correctness and robustness*, done to close out `src/mouseutils/REVIEW.md`
and `REMEDIATION_PLAN.md` - two files unique to this component, left over
from an earlier, separate review done outside this repo's normal process
(a `codex/never-throws-standard` branch, merged as PR #37, plus one
follow-up commit). A background review cross-checked every finding in both
files against the current code rather than trusting old "Resolved" labels,
confirming several genuinely-fixed items alongside a real, verified punch
list. That work shipped as five sequential PRs, summarized here; both
source files are deleted as of this addendum, fully superseded by this
document.

**PR 1 - confirmed bugs + guard consistency:**
- `SafeClickAt`: `expectedWindowHandle == IntPtr.Zero` is now rejected
  outright. Previously, a point over empty desktop (where the
  window-under-the-point lookup also returns zero) made
  `actual == expectedWindowHandle` trivially true, authorizing a click on
  nothing - defeating the entire point of this method.
- `ClickAtRelativePosition`: now rejects `NaN`/infinity explicitly. Every
  comparison against `NaN` is `false` in C#, so the existing `[0.0, 1.0]`
  range check alone silently let a `NaN` fraction through into the position
  math.
- `RubberBandSelect`: its modifier-key release result is no longer
  discarded (`out _`); it now routes through the same
  `CompleteCompoundOperation` helper `DragAndDrop`/`DragAndHold` already
  use, so a failed release surfaces instead of vanishing.
- `ClickWithModifiers`: falls back to releasing each up-event individually
  when the whole-batch release fails twice, instead of only ever retrying
  the identical batch.
- `IsProcessDpiAware`: now uses the same `NeverThrowsGuard.IsRecoverable`
  policy every other method in the file uses, instead of a one-off
  `catch (EntryPointNotFoundException)`.
- **Decision, no change:** `NeverThrowsGuard.IsRecoverable`'s blanket
  "anything except OutOfMemoryException/StackOverflowException/
  AccessViolationException" policy stays as-is. This matches the same
  pattern already used in `SessionUtils`, `FileWatchUtils`, and every other
  reviewed component's own `NeverThrowsGuard`. `REMEDIATION_PLAN.md`'s ask
  for a narrow, project-specific exception allowlist reads as the more
  literal interpretation of `project-docs/coding-standards/never-throws-standard.md`,
  but every already-shipped component converged on the simpler
  blanket-exclude pattern instead; narrowing this one component alone would
  make it the outlier rather than fix anything.

**PR 2 - validation tightening + missing convenience wrappers:** rejects
invalid numeric/enum input instead of silently coercing it across
`SmoothMoveTo`, `JiggleMouse`, `ClickAndHold`, `DragAndHold`,
`MoveMouseBezier`, `ScrollUp`/`ScrollDown`/`ScrollRight`/`ScrollLeft`,
`ClickWithRetry`, `FlashCursorHighlight`, `ClickWithModifiers`, and
`RubberBandSelect` - each previously clamped or `Math.Abs`'d an invalid
value rather than rejecting it, which could hide a caller-side bug behind a
plausible-looking result. Also added `MiddleClickAt`, `MiddleDoubleClick`,
`MiddleDoubleClickAt`, `RightDoubleClickAt` (filling gaps in the Left/
Right/Middle × Click/ClickAt/DoubleClick/DoubleClickAt matrix) and a
vertical `ScrollAt` paralleling `ScrollHorizontalAt`, which now also
restores the operator's cursor position afterward like `ClickAndRestore`
already does.

**PR 3 - `Dispose` cleanup:** `REVIEW.md`'s one "High" finding.
`Dispose` previously did nothing despite this class acquiring several
kinds of state across calls. Now best-effort cleans up: buttons held via
the public, explicitly stateful `MouseDown`/`MouseUp` pair (every compound
click/drag method already guarantees its own release, so this only covers
a `MouseDown` caller that never reached `MouseUp`); a `BlockUserInput`
block and a `HideCursor` hide, each only when disposing on the same
thread that acquired them (both are thread-affine Win32 APIs - a real,
honest, documented limitation, not something `Dispose` can fully solve);
a `ClipCursor` confinement, restored to the exact prior state (`ClipCursor`
now saves what was in effect before it runs, fixing "clears a clip another
app owns" as a side effect); and system cursor slots replaced via
`SetCursor`/`ReplaceSystemCursor`/`SetCursorFromFile`, restoring only the
specific slots this instance touched rather than the broader
`ResetSystemCursors()` reset the plan explicitly warned against.

**PR 4 - bounded (not cancellable) waits:** a design decision, not just a
code change. Pega Robot Studio automations execute steps sequentially on
one thread, so there is no mechanism for a separate step to interrupt a
call already blocked inside this component - `REVIEW.md`'s framing of this
as "cancellable" isn't achievable in that execution model. Implemented
bounding instead: `WaitForPixelColor`/`WaitForPixelChange`/
`WaitForIdleCursor`'s `timeoutMs` capped at 30 minutes (a genuine external
wait can legitimately take that long); `ClickAndHold`/`DragAndHold`/
`MoveMouseBezier`/`FlashCursorHighlight`/`SmoothMoveTo`'s durations capped
at 60 seconds total (synthetic actions this component performs itself,
with no legitimate reason to run long).

**PR 5 - fault-injection test seams:** added an `InternalsVisibleTo`-based
seam (`SendInputOverride`/`GetCursorPosOverride`/`SetCursorPosOverride`),
following the same pattern already used elsewhere in this repo (e.g.
FileWatchUtils), plus 9 new tests exercising exactly the scenarios
`REMEDIATION_PLAN.md`'s testing goal asked for: a fault-injected exception
converting to `false` + message; compound operations still releasing what
they acquired when the release step itself fails; `ClickAndRestore` never
reporting success after a simulated restore failure; the PR 1 fixes to
`ClickWithModifiers`/`RubberBandSelect`; and `MouseDown`/`Dispose`
interaction.

Not covered by this pass: fault-injection-based automated tests for the
cursor-hide/clip/system-cursor-restore paths in `Dispose` (these call real
Win32 state-changing APIs the fault-injection seam doesn't reach), and full
Windows/Pega-host verification - both remain manual per `TESTING.md`.
