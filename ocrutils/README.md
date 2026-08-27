# OcrAutomation

A Pega Robot Studio-ready component (`OcrUtils`) that recognizes text from a
screen region or an image file using Windows' built-in OCR engine
(`Windows.Media.Ocr`), returning either plain text or structured
(per-line/per-word positioned) results.

- Target framework: `net10.0-windows10.0.19041.0` (a versioned Windows TFM,
  required to consume the WinRT `Windows.Media.Ocr` API — the other
  components in this repo use the unversioned `net10.0-windows`)
- Namespace: `OcrAutomation`
- Assembly: `OcrAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

Designed to be used alongside [ScreenCaptureUtils](../screencaptureutils/ScreenCaptureUtils.cs)
(`ScreenCaptureAutomation`) for visual verification and
[MouseUtils](../mouseutils/MouseUtils.cs) (`MouseAutomation`) for
clicking on text `FindTextLocation` locates — this component does its own
internal screen capture, though, so it has no project reference to either.

## Types

`OcrResult`, `OcrLine`, and `OcrWord` are plain result classes (not enums):

| Type | Members |
|---|---|
| `OcrResult` | `string Text` (full recognized text), `List<OcrLine> Lines` |
| `OcrLine` | `string Text`, `Rectangle Bounds` (union of its words' bounds), `List<OcrWord> Words` |
| `OcrWord` | `string Text`, `Rectangle Bounds` |

## Constructors

| Constructor | Description |
|---|---|
| `OcrUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `OcrUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Region/Image to Plain Text

| Method | Description |
|---|---|
| `string GetTextFromRegion(int left, int top, int width, int height, string languageTag = null)` | Captures a screen region and returns its recognized text. |
| `string GetTextFromImageFile(string filePath, string languageTag = null)` | Loads an image file and returns its recognized text. |

### Structured Results

| Method | Description |
|---|---|
| `OcrResult GetStructuredTextFromRegion(int left, int top, int width, int height, string languageTag = null)` | Captures a screen region and returns its recognized text as lines/words with screen-space bounding rectangles. |
| `Rectangle FindTextLocation(string searchText, int left, int top, int width, int height)` | Searches a region for matching text and returns its screen-space bounding rectangle, or `Rectangle.Empty` if not found. |

### Language

| Method | Description |
|---|---|
| `List<string> GetAvailableLanguages()` | Gets the BCP-47 language tags of every OCR language pack currently installed. |

### Wait-for-Text Polling

| Method | Description |
|---|---|
| `bool WaitForTextToAppear(int left, int top, int width, int height, string expectedText, int timeoutMs, int pollIntervalMs)` | Polls a screen region until it contains matching text, or the timeout elapses. |

## Notes & Caveats

- **Recognition accuracy depends on an installed OCR language pack** for the requested
  language (or the user's profile languages, if none is specified) — install one via
  Windows Settings > Time & Language > Language & region. A missing pack throws
  `InvalidOperationException` naming the missing language rather than silently
  returning empty results.
- **Accuracy also depends on legibility**: very small text, low-contrast text, and
  unusual fonts recognize less reliably than typical UI text at 100% DPI scaling.
- **`FindTextLocation`** does a case-insensitive substring search, preferring a whole-line
  match before falling back to a single-word match — a search phrase spanning a line break
  won't match unless it's short enough to be contained within one recognized word or line.
- **All bounding rectangles from a region-capture call are in absolute screen
  coordinates** (the region's `left`/`top` offset is already applied); rectangles from
  `GetTextFromImageFile`/image-based structured results are in image-pixel coordinates
  instead, since there is no "screen" to offset against.
- **This component captures the screen itself** (not a dependency on ScreenCaptureUtils),
  so its OCR-region and ScreenCaptureUtils' capture-region calls do not share any deduplication —
  each capture that only needs OCR text still works standalone.
- **This component blocks synchronously on WinRT async calls** (`.AsTask().GetAwaiter().GetResult()`)
  rather than exposing an async API, matching the fully-synchronous style of every other component
  in this repo. This is safe from a deadlock perspective because `OcrEngine`/`BitmapDecoder`/`SoftwareBitmap`
  are agile (free-threaded) WinRT types with no UI-thread/dispatcher affinity to deadlock against.
