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

| Method | Description |
|---|---|
| `bool CaptureScreenToFile(string filePath, out string message)` | Captures the entire virtual screen (all monitors) to an image file. Returns True on success; never throws. |
| `bool CaptureRegionToFile(int left, int top, int width, int height, string filePath, out string message)` | Captures a specific screen region to an image file. Returns True on success; never throws. |
| `bool CaptureWindowToFile(IntPtr hWnd, string filePath, out string message)` | Captures a window via `PrintWindow` — works even if the window is covered by other windows. Returns True on success; never throws. |
| `bool CaptureActiveWindowToFile(string filePath, out string message)` | Captures the current foreground window to an image file. Returns True on success; never throws. |
| `bool CaptureAroundPointToFile(int x, int y, int width, int height, string filePath, out string message)` | Captures a region centered on a point (e.g. `MouseUtils.GetX/GetY`) to an image file. Returns True on success; never throws. |
| `void CaptureToClipboard()` | Captures the entire virtual screen and copies it to the clipboard as an image. |
| `bool CaptureStepEvidence(string stepName, string folderPath, out string fullPath, out string message)` | Captures the screen to an auto-named, sequentially-numbered evidence file (`001_StepName_20260826_143201.png`). `fullPath` receives the file path written. Returns True on success; never throws. |

### Verification & Comparison

| Method | Description |
|---|---|
| `bool GetRegionHash(int left, int top, int width, int height, out string hash, out string message)` | Computes a lightweight perceptual hash of a screen region, for cheap "did this change" checks. Returns True on success; never throws. |
| `bool WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen region until its appearance changes, or the timeout elapses. `message` is only set if a real failure aborted the poll early. Never throws. |
| `bool CompareRegionToBaseline(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent, out string message)` | Compares a screen region against a saved baseline image and reports whether the difference is within tolerance. `message` is only set if a real failure (missing baseline, size mismatch) prevented the comparison. Never throws. |

### Annotation & Redaction

| Method | Description |
|---|---|
| `bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int colorRef, out string message, int lineWidth = 3)` | Draws a rectangular highlight box onto a saved screenshot, overwriting it in place. Returns True on success; never throws. |
| `bool DrawArrowToPoint(string imagePath, int x, int y, int colorRef, out string message, int length = 40, int lineWidth = 3)` | Draws an arrow pointing at the given coordinates onto a saved screenshot, overwriting it in place. Returns True on success; never throws. |
| `bool RedactRegion(string imagePath, int left, int top, int width, int height, out string message, int colorRef = 0x000000)` | Fills a rectangular region of a saved screenshot with a solid color (default black), permanently redacting it in place. Returns True on success; never throws. |

## Notes & Caveats

- **Every method except `CaptureToClipboard` returns `bool` with an `out string message`**
  instead of throwing — bad dimensions, an invalid file path, a missing image file, and
  Win32 failures (`GetWindowRect`/`PrintWindow`) are all reported this way, with `message`
  set to a human-readable reason whenever the method returns `false`. `CaptureToClipboard`
  has no failure-prone inputs (fixed virtual-screen bounds, no file path) and is unchanged.
- **`WaitForRegionToChange`/`CompareRegionToBaseline`** overload the meaning of a `false`
  return: it covers both a normal "didn't change"/"outside tolerance" outcome
  (`message == null`) and a real failure that aborted the check early (`message` set) —
  check `message` to tell them apart.
- **`colorRef`** parameters use the same `0x00BBGGRR` format as MouseUtils'
  `colorRef` parameters (e.g. `FlashCursorHighlight`), for consistency.
- **`CaptureWindowToFile`/`CaptureActiveWindowToFile`** use `PW_RENDERFULLCONTENT`
  so DirectComposition/DirectX-backed windows render correctly, but some
  exclusive-fullscreen or protected-content windows may still capture as black.
- **`CaptureToClipboard`** requires the calling thread to be STA, as with any
  Windows Forms clipboard access.
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
