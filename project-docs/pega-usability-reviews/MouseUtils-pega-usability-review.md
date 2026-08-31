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
