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
| `GetPosition` | `out Point position` | Proxy friction | Pega must expose a `Point` proxy to read X/Y. `GetX` and `GetY` are scalar alternatives, but two calls are not one atomic snapshot. Add `GetPosition(out int x, out int y, out string message)`. |
| `GetCursorClip` | `out Rectangle clip` | Proxy friction | No primitive-output alternative exists. Add an overload returning left, top, width, and height. |
| `GetWindowBounds` | `IntPtr hWnd`, `out Rectangle bounds` | Chainable input; proxy-friction output | The handle can come from WindowUtils or `GetWindowAtPoint`; consuming the rectangle requires proxy configuration. Add primitive bound outputs. |
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

1. Add a scalar `GetPosition` overload returning `x` and `y` from one native read.
2. Add scalar-output overloads for `GetCursorClip` and `GetWindowBounds`.
3. Add a short Pega example that wires `WindowUtils.FindWindowByTitle` into a
   MouseUtils handle consumer.
4. Verify flags-enum selection for `ModifierKeys` on the actual Robot Studio
   design surface.

## Verdict

`MouseUtils` has no non-scalar input that is effectively unusable. Its `IntPtr`
inputs are deliberate chain types. The three structure outputs are usable through
proxies but deserve primitive overloads to reduce design-surface friction.
