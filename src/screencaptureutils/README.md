# ScreenCaptureAutomation

A Pega Robot Studio-ready component (`ScreenCaptureUtils`) that captures the
screen, a region, or a window to a file/clipboard, compares captures against a
saved baseline for visual verification, and annotates or redacts saved
screenshots for audit evidence.

Designed to be used alongside [MouseUtils](../mouseutils/MouseUtils.cs)
(`MouseAutomation`), not to duplicate it: this component owns
pixels-to-image concerns, MouseUtils owns cursor/input concerns.

- Target framework: `net10.0-windows` (requires `UseWindowsForms` for
  `System.Drawing`/clipboard image support)
- Namespace: `ScreenCaptureAutomation`
- Assembly: `ScreenCaptureAutomation`

All coordinates are absolute screen pixels, consistent with MouseUtils.

## Constructors

| Constructor | Description |
|---|---|
| `ScreenCaptureUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `ScreenCaptureUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Core Capture

| Method | Signature | Description |
|---|---|---|
| `CaptureScreenToFile` | `bool CaptureScreenToFile(string filePath, out string message)` | Captures the entire virtual screen (all monitors) to an image file. Returns True on success; never throws. |
| `CaptureRegionToFile` | `bool CaptureRegionToFile(int left, int top, int width, int height, string filePath, out string message)` | Captures a specific screen region to an image file. Returns True on success; never throws. |
| `CaptureWindowToFile` | `bool CaptureWindowToFile(IntPtr hWnd, string filePath, out string message)` | Captures a window via `PrintWindow` — works even if the window is covered by other windows. `hWnd` is meant to come from `WindowUtils`/`DialogUtils`, not to be typed in by hand — see the producer→consumer example below. Returns True on success; never throws. |
| `CaptureActiveWindowToFile` | `bool CaptureActiveWindowToFile(string filePath, out string message)` | Captures the current foreground window to an image file. Returns True on success; never throws. |
| `CaptureAroundPointToFile` | `bool CaptureAroundPointToFile(int x, int y, int width, int height, string filePath, out string message)` | Captures a region centered on a point (e.g. `MouseUtils.GetX/GetY`) to an image file. Returns True on success; never throws. |
| `CaptureToClipboard` | `bool CaptureToClipboard(out string message)` | Captures the entire virtual screen and copies it to the clipboard as an image, via an internal STA thread regardless of the caller's apartment state. Returns True on success; never throws. |
| `CaptureStepEvidence` | `bool CaptureStepEvidence(string stepName, string folderPath, out string fullPath, out string message)` | Captures the screen to an auto-named, sequentially-numbered evidence file (`001_StepName_20260826_143201.png`). `fullPath` receives the file path written. Returns True on success; never throws. |

### Verification & Comparison

| Method | Signature | Description |
|---|---|---|
| `GetRegionHash` | `bool GetRegionHash(int left, int top, int width, int height, out string hash, out string message)` | Computes a lightweight perceptual hash of a screen region, for cheap "did this change" checks. Returns True on success; never throws. |
| `WaitForRegionToChangeSimple` | `bool WaitForRegionToChangeSimple(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen region until its appearance changes, or the timeout elapses. `message` is only set if a real failure aborted the poll early. Never throws. |
| `WaitForRegionToChange` | `bool WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Same, plus a `timedOut` output so the automation can branch on timeout vs. execution failure without a null-message test. Never throws. |
| `CompareRegionToBaselineSimple` | `bool CompareRegionToBaselineSimple(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent, out string message)` | Compares a screen region against a saved baseline image and reports whether the difference is within tolerance. `message` is only set if a real failure (missing baseline, size mismatch) prevented the comparison. Never throws. |
| `CompareRegionToBaseline` | `bool CompareRegionToBaseline(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent, out bool comparisonCompleted, out string message)` | Same, plus a `comparisonCompleted` output so the automation can branch on out-of-tolerance vs. execution failure without a null-message test. Never throws. |

### Annotation & Redaction

| Method | Signature | Description |
|---|---|---|
| `DrawHighlightBox` | `bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int colorRef, out string message, int lineWidth = 3)` | Draws a rectangular highlight box onto a saved screenshot, overwriting it in place. Returns True on success; never throws. |
| `DrawHighlightBox` | `bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int red, int green, int blue, out string message, int lineWidth = 3)` | Same, as RGB color components (0-255 each) instead of a packed colorRef. |
| `DrawHighlightBox` | `bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, Color color, out string message, int lineWidth = 3)` | Same, taking a `System.Drawing.Color` (e.g. `Color.Red`, or any named/system color). |
| `DrawArrowToPoint` | `bool DrawArrowToPoint(string imagePath, int x, int y, int colorRef, out string message, int length = 40, int lineWidth = 3)` | Draws an arrow pointing at the given coordinates onto a saved screenshot, overwriting it in place. Returns True on success; never throws. |
| `DrawArrowToPoint` | `bool DrawArrowToPoint(string imagePath, int x, int y, int red, int green, int blue, out string message, int length = 40, int lineWidth = 3)` | Same, as RGB color components (0-255 each) instead of a packed colorRef. |
| `DrawArrowToPoint` | `bool DrawArrowToPoint(string imagePath, int x, int y, Color color, out string message, int length = 40, int lineWidth = 3)` | Same, taking a `System.Drawing.Color`. |
| `RedactRegion` | `bool RedactRegion(string imagePath, int left, int top, int width, int height, out string message, int colorRef = 0x000000)` | Fills a rectangular region of a saved screenshot with a solid color (default black), permanently redacting it in place. Returns True on success; never throws. |
| `RedactRegion` | `bool RedactRegion(string imagePath, int left, int top, int width, int height, int red, int green, int blue, out string message)` | Same, as RGB color components (0-255 each) instead of a packed colorRef. |
| `RedactRegion` | `bool RedactRegion(string imagePath, int left, int top, int width, int height, Color color, out string message)` | Same, taking a `System.Drawing.Color`. |

## Producer → consumer: `WindowUtils`/`DialogUtils` into `CaptureWindowToFile`

`CaptureWindowToFile`'s `hWnd` isn't meant to be typed in by hand — it comes from a
window lookup earlier in the same automation, most often `WindowUtils.FindWindowByTitle`
or a `DialogUtils` find/wait method:

```csharp
IntPtr hWnd = windowUtils.FindWindowByTitle("Order Entry", exactMatch: true);
if (hWnd == IntPtr.Zero)
{
    Logger.Error("Order Entry window not found.");
    return;
}

screenCapture.CaptureWindowToFile(hWnd, @"C:\evidence\order_entry.png", out string message);
```

## Notes & Caveats

- **Every method returns `bool` with an `out string message`** instead of throwing —
  bad dimensions, an invalid file path, a missing image file, and Win32 failures
  (`GetWindowRect`/`PrintWindow`) are all reported this way, with `message` set to a
  human-readable reason whenever the method returns `false`.
- **Capture regions that lie entirely outside the virtual screen** (all monitors) are
  rejected with a message rather than capturing a solid black rectangle; a partially
  overlapping region is allowed, and the off-screen part comes back black.
- **`WaitForRegionToChangeSimple`/`CompareRegionToBaselineSimple`** overload the meaning
  of a `false` return: it covers both a normal "didn't change"/"outside tolerance" outcome
  (`message == null`) and a real failure that aborted the check early (`message` set) —
  check `message` to tell them apart. Each has a disambiguated overload
  (`WaitForRegionToChange`'s `out bool timedOut` / `CompareRegionToBaseline`'s
  `out bool comparisonCompleted`) that reports this directly, for designers who would
  rather branch on a Boolean than test `message` for `null`.
- **`colorRef`** parameters use the same `0x00BBGGRR` format as MouseUtils'
  `colorRef` parameters (e.g. `FlashCursorHighlight`), for consistency. `DrawHighlightBox`,
  `DrawArrowToPoint`, and `RedactRegion` each also have an RGB-component overload
  (`red`/`green`/`blue`, each 0-255 and clamped) and a `System.Drawing.Color` overload
  (e.g. `Color.Red`, or any named/system color), for designers who find hexadecimal
  colorRef entry inconvenient or who have a `Color` proxy available.
- **`CaptureWindowToFile`/`CaptureActiveWindowToFile`** use `PW_RENDERFULLCONTENT`
  so DirectComposition/DirectX-backed windows render correctly, but some
  exclusive-fullscreen or protected-content windows may still capture as black.
- **`CaptureToClipboard`** runs its clipboard write on an internal STA thread
  regardless of the calling thread's apartment state, so the automation does not
  need to know or control it — unlike raw Windows Forms clipboard access, which
  requires an STA caller.
- **`CompareRegionToBaseline`** requires the baseline image's dimensions to
  exactly match the requested region size, and uses a small per-channel
  tolerance internally to absorb anti-aliasing/rendering noise — it is not an
  exact byte-for-byte comparison.
- **`RedactRegion`** uses a solid opaque fill rather than a blur, deliberately:
  blurred text/numbers can sometimes be partially reconstructed, whereas a
  solid fill permanently discards the underlying pixels.
- **`DrawHighlightBox`/`DrawArrowToPoint`/`RedactRegion`** all load, modify,
  and overwrite the image file in place — pass a copy of the path if you need
  to keep the un-annotated original.
- **`CaptureStepEvidence`**'s sequence counter is per component instance and
  resets when the automation creates a new instance (typically once per run),
  so evidence from a single run sorts in step order by filename.
