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

| Method | Signature | Description |
|---|---|---|
| `GetTextFromRegion` | `bool GetTextFromRegion(int left, int top, int width, int height, out string text, out string message, string languageTag = null)` | Captures a screen region and returns its recognized text. Returns True on success; never throws. |
| `GetTextFromImageFile` | `bool GetTextFromImageFile(string filePath, out string text, out string message, string languageTag = null)` | Loads an image file and returns its recognized text. Returns True on success; never throws. |

### Structured Results

| Method | Signature | Description |
|---|---|---|
| `GetStructuredTextFromRegion` | `bool GetStructuredTextFromRegion(int left, int top, int width, int height, out OcrResult result, out string message, string languageTag = null)` | Captures a screen region and returns its recognized text as lines/words with screen-space bounding rectangles. Returns True on success; never throws. |
| `FindTextLocation` | `bool FindTextLocation(string searchText, int left, int top, int width, int height, out Rectangle location, out string message)` | Searches a region for matching text and returns its screen-space bounding rectangle. Returns True if found; `location` is `Rectangle.Empty` both when not found and on a real failure — check `message` to tell them apart. Never throws. |

### Language

| Method | Signature | Description |
|---|---|---|
| `GetAvailableLanguages` | `List<string> GetAvailableLanguages()` | Gets the BCP-47 language tags of every OCR language pack currently installed. |

### Wait-for-Text Polling

| Method | Signature | Description |
|---|---|---|
| `WaitForTextToAppear` | `bool WaitForTextToAppear(int left, int top, int width, int height, string expectedText, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen region until it contains matching text, or the timeout elapses. `message` is only set if a real failure aborted the poll early. Never throws. |

## Notes & Caveats

- **Every method that could previously throw now returns `bool` with an `out string message`**
  instead — bad dimensions, a missing image file, and a missing OCR language pack are all
  reported this way, with `message` set to a human-readable reason whenever the method
  returns `false`. Only `GetAvailableLanguages` (never able to fail) is unchanged.
- **`FindTextLocation`/`WaitForTextToAppear`** overload the meaning of a `false` return: it
  covers both a normal "not found"/"timed out" outcome (`message == null`) and a real failure
  that aborted the search/poll early (`message` set) — check `message` to tell them apart.
- **Recognition accuracy depends on an installed OCR language pack** for the requested
  language (or the user's profile languages, if none is specified) — install one via
  Windows Settings > Time & Language > Language & region. A missing pack is reported via
  `message` naming the missing language, rather than silently returning empty results.
- **Accuracy also depends on legibility**: very small text, low-contrast text, and
  unusual fonts recognize less reliably than typical UI text at 100% DPI scaling.
- **`FindTextLocation`** does a case-insensitive substring search, preferring a whole-line
  match before falling back to a single-word match — a search phrase spanning a line break
  won't match unless it's short enough to be contained within one recognized word or line.
- **All bounding rectangles are in absolute screen coordinates** (the region's
  `left`/`top` offset is already applied by `GetStructuredTextFromRegion`/`FindTextLocation`) —
  there is currently no structured/positioned-result method for image-file input;
  `GetTextFromImageFile` returns plain text only, with no bounding rectangles.
- **`GetTextFromImageFile`** reports `false` with a message (not a thrown exception) if the
  given path doesn't exist, and reads the file into memory up front rather than holding it
  open, so the source file isn't locked during the OCR pass.
- **This component captures the screen itself** (not a dependency on ScreenCaptureUtils),
  so its OCR-region and ScreenCaptureUtils' capture-region calls do not share any deduplication —
  each capture that only needs OCR text still works standalone.
- **This component blocks synchronously on WinRT async calls** (`.AsTask().GetAwaiter().GetResult()`)
  rather than exposing an async API, matching the fully-synchronous style of every other component
  in this repo. `OcrEngine`/`BitmapDecoder`/`SoftwareBitmap` are documented as agile (free-threaded)
  WinRT types with no UI-thread/dispatcher affinity, so blocking here should not deadlock against a
  UI/dispatcher thread — though this hasn't been verified against real Windows hardware, since this
  repo is developed on a non-Windows host.
- **Guard tests.** `OcrUtils.Tests` (in this folder) covers the null/empty-text,
  non-positive-dimension, missing/corrupt-file, and no-poll-stall never-throw paths.
  It runs on Windows only (see `TESTING.md` at the repo root for why, and for the
  live-OCR test plan).
