# ScreenCaptureUtils Pega API Usability Review

## Summary

Almost every public port is a Pega-friendly scalar. `CaptureWindowToFile` is the
only method with a non-scalar input, and its `IntPtr` is a reasonable chain value
from WindowUtils or DialogUtils. The larger usability risks are clipboard thread
requirements, an unsafe legacy overload, and numeric Win32 color values.

## Method review

| Method group | Rating | Assessment |
|---|---|---|
| `CaptureScreenToFile`, `CaptureRegionToFile`, `CaptureAroundPointToFile` | Direct | Inputs and outputs are scalar. Region coordinates are easy to wire from MouseUtils scalar coordinate methods. |
| `CaptureWindowToFile` | Chainable | `hWnd` cannot be typed naturally in Pega, but WindowUtils and DialogUtils produce it. Add a producer-to-consumer wiring example; do not encourage persisted or manually entered handles. |
| `CaptureActiveWindowToFile` | Direct | Useful scalar alternative when the foreground window is the intended target. It does not replace handle-based capture when focus can change. |
| `CaptureToClipboard(out string)` and `CaptureToClipboard()` | Direct ports; runtime friction | Clipboard access requires an STA calling thread, which an automation should not have to know or control. The parameterless overload throws and is easy to select accidentally. Marshal clipboard work internally to an STA thread and hide, obsolete, or remove the throwing overload. |
| `CaptureStepEvidence` | Direct | All ports are strings/Boolean. Returning `fullPath` as a scalar makes the result easy to pass to later steps. |
| `GetRegionHash` | Direct | Returns a scalar hash string; no image or bitmap proxy escapes the component. |
| `WaitForRegionToChange` | Direct | All ports are scalar. `false` represents both timeout and failure, so the caller must inspect `message`; a separate `timedOut` output or status enum would make branching clearer. |
| `CompareRegionToBaseline` | Direct | All ports are scalar. `false` similarly means either a completed mismatch or an operational failure; separate completion/match outputs would be easier to automate. |
| `DrawHighlightBox`, `DrawArrowToPoint`, `RedactRegion` | Direct but awkward | `colorRef` is an integer, but its `0x00BBGGRR` byte order is non-obvious and hexadecimal entry may be inconvenient in the designer. Add RGB-component or named-color overloads. Other ports are scalar. |

## Recommended changes

1. Make clipboard capture independent of the caller's apartment state by doing
   clipboard work on an internal STA thread and reporting failure through the
   Boolean/message pattern.
2. Hide, obsolete, or remove the throwing `CaptureToClipboard()` overload from
   the Pega design surface.
3. Add RGB-component overloads for annotation and redaction colors.
4. Distinguish a normal negative result from an execution failure in wait and
   comparison methods.
5. Document the WindowUtils/DialogUtils handle-output to
   `CaptureWindowToFile` input connection.

## Verdict

No ScreenCaptureUtils method is blocked by an unproducible complex input. The
window handle is intentionally chainable. Clipboard apartment-state handling is
the most important Pega-specific gap; the throwing overload is also inconsistent
with the repository's never-throws standard.
