# ScreenCaptureUtils Pega API Usability Review

## Summary

Almost every public port is a Pega-friendly scalar. `CaptureWindowToFile` is the
only method with a non-scalar input, and its `IntPtr` is a reasonable chain value
from WindowUtils or DialogUtils, now documented with a producer → consumer
example. The previous usability risks — clipboard thread requirements, an
unsafe legacy overload, and numeric Win32 color values — are resolved below.

## Method review

| Method group | Rating | Assessment |
|---|---|---|
| `CaptureScreenToFile`, `CaptureRegionToFile`, `CaptureAroundPointToFile` | Direct | Inputs and outputs are scalar. Region coordinates are easy to wire from MouseUtils scalar coordinate methods. |
| `CaptureWindowToFile` | Chainable | `hWnd` cannot be typed naturally in Pega, but WindowUtils and DialogUtils produce it. Add a producer-to-consumer wiring example; do not encourage persisted or manually entered handles. |
| `CaptureActiveWindowToFile` | Direct | Useful scalar alternative when the foreground window is the intended target. It does not replace handle-based capture when focus can change. |
| `CaptureToClipboard(out string)` | Direct; apartment-independent | Clipboard access requires an STA thread; the clipboard write now runs on an internal STA thread regardless of the calling thread's apartment state, so the automation does not need to know or control it. The throwing parameterless `CaptureToClipboard()` overload has been removed. |
| `CaptureStepEvidence` | Direct | All ports are strings/Boolean. Returning `fullPath` as a scalar makes the result easy to pass to later steps. |
| `GetRegionHash` | Direct | Returns a scalar hash string; no image or bitmap proxy escapes the component. |
| `WaitForRegionToChange` | Direct, disambiguated | All ports are scalar. A `timedOut`-output overload now lets the caller distinguish timeout from operational failure without inspecting `message`; the original `message`-only overload delegates to it. |
| `CompareRegionToBaseline` | Direct, disambiguated | All ports are scalar. A `comparisonCompleted`-output overload now lets the caller distinguish a completed mismatch from an operational failure without inspecting `message`; the original overload delegates to it. |
| `DrawHighlightBox`, `DrawArrowToPoint`, `RedactRegion` | Direct, additional overloads added | `colorRef`'s `0x00BBGGRR` byte order is non-obvious for hexadecimal entry. Each method now also has an RGB-component overload (`red`/`green`/`blue`, 0-255, clamped) and a `System.Drawing.Color` overload (e.g. `Color.Red`, or any named/system color) alongside the original `colorRef` overload. |

## Recommended changes

1. **Done.** Clipboard capture is now independent of the caller's apartment
   state: `CaptureToClipboard(out string message)` runs the clipboard write on
   an internal STA thread (`TrySetClipboardImageOnStaThread`) and reports
   failure through the existing Boolean/message pattern.
2. **Done.** Removed the throwing `CaptureToClipboard()` overload entirely
   (not just hidden/obsoleted) — `CaptureToClipboard(out string message)` is
   now the only overload.
3. **Done.** Added RGB-component overloads (`red`/`green`/`blue`, 0-255,
   clamped) and `System.Drawing.Color` overloads for `DrawHighlightBox`,
   `DrawArrowToPoint`, and `RedactRegion`, alongside the original `colorRef`
   overloads.
4. **Done.** Added a `timedOut`-output overload of `WaitForRegionToChange` and
   a `comparisonCompleted`-output overload of `CompareRegionToBaseline`, so
   both can distinguish a normal negative result from an execution failure
   without a null-message test; the original overloads delegate to them.
5. **Done.** Added a producer → consumer example wiring
   `WindowUtils.FindWindowByTitle` into `CaptureWindowToFile` to the README
   (this component has no `Documentation/` folder, unlike some others in the
   repo, so the example lives in `README.md` directly).

## Verdict

No ScreenCaptureUtils method is blocked by an unproducible complex input. The
window handle is intentionally chainable, and its producer → consumer wiring
is now documented. Clipboard apartment-state handling no longer depends on the
caller's thread, the throwing overload is gone, annotation/redaction colors
have RGB and `Color` alternatives to the hex `colorRef`, and the wait/compare
methods can disambiguate a negative result from a failure without inspecting
`message`. All five recommended changes are implemented.
