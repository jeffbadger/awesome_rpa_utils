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
| `void CaptureScreenToFile(string filePath)` | Captures the entire virtual screen (all monitors) to an image file. |
| `void CaptureRegionToFile(int left, int top, int width, int height, string filePath)` | Captures a specific screen region to an image file. |
| `void CaptureWindowToFile(IntPtr hWnd, string filePath)` | Captures a window via `PrintWindow` — works even if the window is covered by other windows. |
| `void CaptureActiveWindowToFile(string filePath)` | Captures the current foreground window to an image file. |
| `void CaptureAroundPointToFile(int x, int y, int width, int height, string filePath)` | Captures a region centered on a point (e.g. `MouseUtils.GetX/GetY`) to an image file. |
| `void CaptureToClipboard()` | Captures the entire virtual screen and copies it to the clipboard as an image. |
| `string CaptureStepEvidence(string stepName, string folderPath)` | Captures the screen to an auto-named, sequentially-numbered evidence file (`001_StepName_20260826_143201.png`). Returns the file path written. |

### Verification & Comparison

| Method | Description |
|---|---|
| `string GetRegionHash(int left, int top, int width, int height)` | Computes a lightweight perceptual hash of a screen region, for cheap "did this change" checks. |
| `bool WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs)` | Polls a screen region until its appearance changes, or the timeout elapses. |
| `bool CompareRegionToBaseline(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent)` | Compares a screen region against a saved baseline image and reports whether the difference is within tolerance. |

### Annotation & Redaction

| Method | Description |
|---|---|
| `void DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int colorRef, int lineWidth = 3)` | Draws a rectangular highlight box onto a saved screenshot, overwriting it in place. |
| `void DrawArrowToPoint(string imagePath, int x, int y, int colorRef, int length = 40, int lineWidth = 3)` | Draws an arrow pointing at the given coordinates onto a saved screenshot, overwriting it in place. |
| `void RedactRegion(string imagePath, int left, int top, int width, int height, int colorRef = 0x000000)` | Fills a rectangular region of a saved screenshot with a solid color (default black), permanently redacting it in place. |

## Notes & Caveats

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
