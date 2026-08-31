using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace OcrAutomation
{
    /// <summary>A single recognized word and its screen-space (or image-space, for file input) bounding rectangle.</summary>
    public sealed class OcrWord
    {
        /// <summary>The recognized text of this word.</summary>
        public string Text { get; set; }

        /// <summary>The word's bounding rectangle, in the same coordinate space as the input (screen pixels for a region capture, image pixels for a file).</summary>
        public Rectangle Bounds { get; set; }
    }

    /// <summary>A single recognized line: its full text, its words, and the union of its words' bounding rectangles.</summary>
    public sealed class OcrLine
    {
        /// <summary>The recognized text of this line.</summary>
        public string Text { get; set; }

        /// <summary>The line's bounding rectangle (the union of all its words' bounding rectangles).</summary>
        public Rectangle Bounds { get; set; }

        /// <summary>The words that make up this line, in reading order.</summary>
        public List<OcrWord> Words { get; } = new List<OcrWord>();
    }

    /// <summary>The structured result of an OCR recognition pass: the full text plus per-line/per-word positions.</summary>
    public sealed class OcrResult
    {
        /// <summary>The full recognized text, with lines joined by newlines.</summary>
        public string Text { get; set; }

        /// <summary>The recognized lines, in reading order.</summary>
        public List<OcrLine> Lines { get; } = new List<OcrLine>();
    }

    /// <summary>
    /// Pega Robot Studio-ready component that recognizes text from a screen region or an
    /// image file using <c>Windows.Media.Ocr</c>, with plain-text and structured
    /// (positioned) result shapes.
    /// </summary>
    [Description("Recognizes text from the screen or an image file via Windows OCR. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class OcrUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public OcrUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public OcrUtils(IContainer container)
        {
            container?.Add(this);
        }

        /// <summary>
        /// Releases the resources used by the component. OcrUtils holds no unmanaged
        /// handles of its own (bitmaps, streams, and the WinRT objects are all released
        /// in using blocks on each call), so this override exists to give you a cleanup
        /// hook and to follow the standard designer-component teardown pattern used by
        /// the other components in this repo.
        /// </summary>
        /// <param name="disposing">
        /// True when called from the public Dispose() method during teardown;
        /// false when called from the finalizer.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // No managed or unmanaged resources to release.
            }

            base.Dispose(disposing);
        }

        #region Region/Image to Plain Text

        /// <summary>Captures a screen region and returns its recognized text.</summary>
        /// <param name="left">The X-coordinate of the top-left corner of the region to capture.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to capture.</param>
        /// <param name="width">The width of the region to capture, in pixels.</param>
        /// <param name="height">The height of the region to capture, in pixels.</param>
        /// <param name="text">The recognized text, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason recognition failed.</param>
        /// <param name="languageTag">A BCP-47 language tag (e.g. <c>"en-US"</c>), or <c>null</c> to use the user's profile languages.</param>
        /// <returns><c>true</c> on success; <c>false</c> if width/height are not positive, the region lies entirely outside the virtual screen, or no matching OCR language pack is installed. Never throws.</returns>
        /// <remarks>
        /// In a DPI-unaware automation process the OS virtualizes screen coordinates, so
        /// the region captured here can be the wrong physical area on scaled displays -
        /// check <c>MouseUtils.IsProcessDpiAware</c> (in the MouseAutomation component)
        /// when results look shifted or cropped on high-DPI monitors.
        /// </remarks>
        [Category("OCR - Plain Text")]
        [Description("Captures a screen region and returns its recognized text. Returns True on success; never throws.")]
        public bool GetTextFromRegion(int left, int top, int width, int height, out string text, out string message, string languageTag = null)
        {
            text = default;
            message = default;
            try
            {
                text = null;
                if (!TryCaptureRegionToBitmap(left, top, width, height, out Bitmap bitmap, out message))
                    return false;

                using (bitmap)
                {
                    if (!TryRecognizeText(bitmap, languageTag, out OcrResult result, out message, left, top))
                        return false;
                    text = result.Text;
                    return true;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                text = NeverThrowsGuard.Failure("GetTextFromRegion", ex);
                return false;
            }
        }

        /// <summary>Loads an image file and returns its recognized text.</summary>
        /// <param name="filePath">Path to the image file to recognize.</param>
        /// <param name="text">The recognized text, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason recognition failed.</param>
        /// <param name="languageTag">A BCP-47 language tag (e.g. <c>"en-US"</c>), or <c>null</c> to use the user's profile languages.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="filePath"/> does not exist, or no matching OCR language pack is installed. Never throws.</returns>
        [Category("OCR - Plain Text")]
        [Description("Loads an image file and returns its recognized text. Returns True on success; never throws.")]
        public bool GetTextFromImageFile(string filePath, out string text, out string message, string languageTag = null)
        {
            text = default;
            message = default;
            try
            {
                text = null;
                if (!TryLoadBitmapWithoutLockingFile(filePath, out Bitmap bitmap, out message))
                    return false;

                using (bitmap)
                {
                    if (!TryRecognizeText(bitmap, languageTag, out OcrResult result, out message))
                        return false;
                    text = result.Text;
                    return true;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                text = NeverThrowsGuard.Failure("GetTextFromImageFile", ex);
                return false;
            }
        }

        #endregion

        #region Structured Results

        /// <summary>Captures a screen region and returns its recognized text as lines/words with screen-space bounding rectangles.</summary>
        /// <param name="left">The X-coordinate of the top-left corner of the region to capture.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to capture.</param>
        /// <param name="width">The width of the region to capture, in pixels.</param>
        /// <param name="height">The height of the region to capture, in pixels.</param>
        /// <param name="languageTag">A BCP-47 language tag (e.g. <c>"en-US"</c>), or <c>null</c> to use the user's profile languages.</param>
        /// <param name="result">The recognized text as positioned lines and words, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason recognition failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if width/height are not positive, or no matching OCR language pack is installed. Never throws.</returns>
        [Category("OCR - Structured Results")]
        [Description("Captures a screen region and returns its recognized text as positioned lines and words. Returns True on success; never throws.")]
        public bool GetStructuredTextFromRegion(int left, int top, int width, int height, out OcrResult result, out string message, string languageTag = null)
        {
            result = default;
            message = default;
            try
            {
                result = null;
                if (!TryCaptureRegionToBitmap(left, top, width, height, out Bitmap bitmap, out message))
                    return false;

                using (bitmap)
                {
                    return TryRecognizeText(bitmap, languageTag, out result, out message, left, top);
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetStructuredTextFromRegion", ex);
                return false;
            }
        }

        /// <summary>
        /// Captures a screen region and returns its recognized text as lines/words with
        /// bounds, serialized to a JSON string, for designers that cannot construct an
        /// <see cref="OcrResult"/> proxy.
        /// </summary>
        /// <param name="left">The X-coordinate of the top-left corner of the region to capture.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to capture.</param>
        /// <param name="width">The width of the region to capture, in pixels.</param>
        /// <param name="height">The height of the region to capture, in pixels.</param>
        /// <param name="languageTag">A BCP-47 language tag (e.g. <c>"en-US"</c>), or <c>null</c> to use the user's profile languages.</param>
        /// <param name="json">
        /// The recognized text as JSON: <c>{"text":"...","lines":[{"text":"...","bounds":{"left":0,"top":0,"width":0,"height":0},"words":[{"text":"...","bounds":{...}}]}]}</c>.
        /// <c>null</c> if this method returns <c>false</c>.
        /// </param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason recognition failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if width/height are not positive, or no matching OCR language pack is installed. Never throws.</returns>
        [Category("OCR - Structured Results")]
        [Description("Captures a screen region and returns its recognized text as lines/words with bounds, as a JSON string. Returns True on success; never throws.")]
        public bool GetStructuredTextFromRegionAsJson(int left, int top, int width, int height, out string json, out string message, string languageTag = null)
        {
            json = default;
            message = default;
            try
            {
                json = null;
                if (!GetStructuredTextFromRegion(left, top, width, height, out OcrResult result, out message, languageTag))
                    return false;

                json = SerializeToJson(result);
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetStructuredTextFromRegionAsJson", ex);
                return false;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static string SerializeToJson(OcrResult result)
        {
            var payload = new
            {
                text = result.Text,
                lines = result.Lines.ConvertAll(line => new
                {
                    text = line.Text,
                    bounds = ToJsonRect(line.Bounds),
                    words = line.Words.ConvertAll(word => new
                    {
                        text = word.Text,
                        bounds = ToJsonRect(word.Bounds)
                    })
                })
            };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static object ToJsonRect(Rectangle bounds)
        {
            return new { left = bounds.Left, top = bounds.Top, width = bounds.Width, height = bounds.Height };
        }

        /// <summary>
        /// Searches a screen region for text matching <paramref name="searchText"/> (a
        /// case-insensitive substring match — e.g. searching for "OK" also matches inside
        /// "BOOK") and returns its bounding rectangle in screen coordinates via
        /// <paramref name="location"/>. Matches are reported in reading order: per line,
        /// a match of the whole line's text wins (returning the line's bounds); otherwise
        /// the first matching word within that line is used. The first line containing
        /// the text in either form is the match — earlier lines win over later ones.
        /// </summary>
        /// <param name="searchText">The text to search for (case-insensitive substring match).</param>
        /// <param name="left">The X-coordinate of the top-left corner of the region to search.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to search.</param>
        /// <param name="width">The width of the region to search, in pixels.</param>
        /// <param name="height">The height of the region to search, in pixels.</param>
        /// <param name="location">The matched text's bounding rectangle, or <see cref="Rectangle.Empty"/> if this method returns <c>false</c> (both for "not found" and for a real failure — check <paramref name="message"/> to tell them apart).</param>
        /// <param name="message"><c>null</c> if the search completed (found or genuinely not found); otherwise a human-readable reason a real failure (bad dimensions, missing language pack) prevented the search.</param>
        /// <returns><c>true</c> if matching text was found; <c>false</c> if it wasn't found, or if a real failure prevented the search (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("OCR - Structured Results")]
        [Description("Searches a screen region for text and returns its bounding rectangle. Returns True if found; never throws.")]
        public bool FindTextLocation(string searchText, int left, int top, int width, int height, out Rectangle location, out string message)
        {
            location = default;
            message = default;
            try
            {
                location = Rectangle.Empty;
                if (string.IsNullOrEmpty(searchText))
                {
                    message = "Search text must not be null or empty.";
                    return false;
                }
                if (!GetStructuredTextFromRegion(left, top, width, height, out OcrResult result, out message))
                    return false;

                foreach (var line in result.Lines)
                {
                    if (line.Text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        location = line.Bounds;
                        return true;
                    }

                    foreach (var word in line.Words)
                    {
                        if (word.Text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            location = word.Bounds;
                            return true;
                        }
                    }
                }

                message = null;
                return false;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("FindTextLocation", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="FindTextLocation(string, int, int, int, int, out Rectangle, out string)"/>,
        /// but reports the matched bounding rectangle as scalar left/top/width/height outputs
        /// for designers without a <c>Rectangle</c> proxy.
        /// </summary>
        /// <param name="searchText">The text to search for (case-insensitive substring match).</param>
        /// <param name="left">The X-coordinate of the top-left corner of the region to search.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to search.</param>
        /// <param name="width">The width of the region to search, in pixels.</param>
        /// <param name="height">The height of the region to search, in pixels.</param>
        /// <param name="foundLeft">Left edge of the matched text's bounding rectangle, or <c>0</c> if not found or on failure.</param>
        /// <param name="foundTop">Top edge of the matched text's bounding rectangle, or <c>0</c> if not found or on failure.</param>
        /// <param name="foundWidth">Width of the matched text's bounding rectangle, or <c>0</c> if not found or on failure.</param>
        /// <param name="foundHeight">Height of the matched text's bounding rectangle, or <c>0</c> if not found or on failure.</param>
        /// <param name="message"><c>null</c> if the search completed (found or genuinely not found); otherwise a human-readable reason a real failure prevented the search.</param>
        /// <returns><c>true</c> if matching text was found; <c>false</c> if it wasn't found, or if a real failure prevented the search (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("OCR - Structured Results")]
        [Description("Searches a screen region for text and returns its bounding rectangle as scalar left/top/width/height. Returns True if found; never throws.")]
        public bool FindTextLocation(string searchText, int left, int top, int width, int height, out int foundLeft, out int foundTop, out int foundWidth, out int foundHeight, out string message)
        {
            foundLeft = default;
            foundTop = default;
            foundWidth = default;
            foundHeight = default;
            bool found = FindTextLocation(searchText, left, top, width, height, out Rectangle location, out message);
            foundLeft = location.Left;
            foundTop = location.Top;
            foundWidth = location.Width;
            foundHeight = location.Height;
            return found;
        }

        #endregion

        #region Language

        /// <summary>Gets the BCP-47 language tags of every OCR language pack currently installed. Never throws; returns an empty list if the language list cannot be queried.</summary>
        [Category("OCR - Language")]
        [Description("Gets the language tags of every OCR language pack currently installed. Never throws.")]
        public List<string> GetAvailableLanguages()
        {
            return TryGetAvailableLanguages(out List<string> tags, out _) ? tags : new List<string>();
        }

        /// <summary>Gets the BCP-47 language tags of every OCR language pack currently installed.</summary>
        /// <param name="tags">The installed language tags, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the language list could not be queried.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the OCR language list could not be queried. Never throws.</returns>
        [Category("OCR - Language")]
        [Description("Gets the language tags of every OCR language pack currently installed. Returns True on success; never throws.")]
        public bool TryGetAvailableLanguages(out List<string> tags, out string message)
        {
            tags = default;
            message = default;
            try
            {
                tags = null;
                try
                {
                    var result = new List<string>();
                    foreach (Language language in OcrEngine.AvailableRecognizerLanguages)
                        result.Add(language.LanguageTag);
                    tags = result;
                    message = null;
                    return true;
                }
                catch (Exception ex)
                {
                    message = "Failed to query installed OCR language packs: " + ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("TryGetAvailableLanguages", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the BCP-47 language tags of every installed OCR language pack as a single
        /// delimited string, for designers without a <c>List&lt;string&gt;</c> proxy. The
        /// list-returning overloads remain available for .NET consumers that need a
        /// collection.
        /// </summary>
        /// <param name="tags">The installed language tags joined by <paramref name="delimiter"/> (e.g. <c>"en-US,fr-FR"</c>), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the language list could not be queried.</param>
        /// <param name="delimiter">The separator placed between tags; defaults to <c>","</c>. A <c>null</c> value also falls back to <c>","</c>.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the OCR language list could not be queried. Never throws.</returns>
        [Category("OCR - Language")]
        [Description("Gets the installed OCR language tags as a single delimited string (default comma-separated). Returns True on success; never throws.")]
        public bool GetAvailableLanguagesDelimited(out string tags, out string message, string delimiter = ",")
        {
            tags = default;
            message = default;
            try
            {
                tags = null;
                if (!TryGetAvailableLanguages(out List<string> list, out message))
                    return false;

                tags = string.Join(delimiter ?? ",", list);
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetAvailableLanguagesDelimited", ex);
                return false;
            }
        }

        #endregion

        #region Wait-for-Text Polling

        /// <summary>Polls a screen region until it contains text matching <paramref name="expectedText"/> (case-insensitive substring), or the timeout elapses.</summary>
        /// <remarks>
        /// The actual wait can exceed <paramref name="timeoutMs"/> by up to one poll interval
        /// plus the time of a single capture+OCR pass, since the timeout is checked between passes.
        /// </remarks>
        /// <param name="left">The X-coordinate of the top-left corner of the region to poll.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to poll.</param>
        /// <param name="width">The width of the region to poll, in pixels.</param>
        /// <param name="height">The height of the region to poll, in pixels.</param>
        /// <param name="expectedText">The text to wait for (case-insensitive substring match).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds; must not be negative.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="message"><c>null</c> if the poll completed (found or genuinely timed out); otherwise a human-readable reason a real failure (bad dimensions, missing language pack, negative timeout) aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the expected text appeared before the timeout; <c>false</c> if it timed out, or if a real failure aborted the poll (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("OCR - Wait for Text")]
        [Description("Polls a screen region until it contains the expected text, or the timeout elapses. Returns True if found in time; never throws.")]
        public bool WaitForTextToAppear(int left, int top, int width, int height, string expectedText, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForTextToAppear(left, top, width, height, expectedText, timeoutMs, pollIntervalMs, out _, out message);
        }

        /// <summary>
        /// Same as <see cref="WaitForTextToAppear(int, int, int, int, string, int, int, out string)"/>,
        /// but also reports whether the wait ended because the timeout elapsed, so the
        /// automation can branch on timeout vs. execution failure without a null-message test.
        /// </summary>
        /// <remarks>
        /// The actual wait can exceed <paramref name="timeoutMs"/> by up to one poll interval
        /// plus the time of a single capture+OCR pass, since the timeout is checked between passes.
        /// </remarks>
        /// <param name="left">The X-coordinate of the top-left corner of the region to poll.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to poll.</param>
        /// <param name="width">The width of the region to poll, in pixels.</param>
        /// <param name="height">The height of the region to poll, in pixels.</param>
        /// <param name="expectedText">The text to wait for (case-insensitive substring match).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds; must not be negative.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="timedOut"><c>true</c> if this method returned <c>false</c> because the timeout elapsed; <c>false</c> on success or on a real failure (check <paramref name="message"/> for the latter).</param>
        /// <param name="message"><c>null</c> if the poll completed (found or genuinely timed out); otherwise a human-readable reason a real failure (bad dimensions, missing language pack, negative timeout) aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the expected text appeared before the timeout; <c>false</c> if it timed out, or if a real failure aborted the poll (check <paramref name="timedOut"/>/<paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("OCR - Wait for Text")]
        [Description("Polls a screen region until it contains the expected text, or the timeout elapses; reports whether the wait timed out. Returns True if found in time; never throws.")]
        public bool WaitForTextToAppear(int left, int top, int width, int height, string expectedText, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (string.IsNullOrEmpty(expectedText))
                {
                    message = "Expected text must not be null or empty.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "Timeout must not be negative.";
                    return false;
                }
                if (pollIntervalMs < 1) pollIntervalMs = 1;

                int start = Environment.TickCount;
                while (true)
                {
                    if (!GetTextFromRegion(left, top, width, height, out string text, out message))
                        return false;
                    if (text.IndexOf(expectedText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        message = null;
                        return true;
                    }
                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForTextToAppear", ex);
                return false;
            }
        }

        #endregion

        #region Internal Helpers

        private static bool TryCaptureRegionToBitmap(int left, int top, int width, int height, out Bitmap bitmap, out string message)
        {
            bitmap = null;
            if (width <= 0 || height <= 0)
            {
                message = "Capture width and height must both be positive.";
                return false;
            }

            if (!TryEnsureRegionOnScreen(left, top, width, height, out message))
                return false;

            try
            {
                Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    // CopyFromScreen throws (Win32Exception) when the desktop is
                    // inaccessible - locked session, secure desktop, service context -
                    // which would otherwise break the documented never-throws contract.
                    g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                }
                bitmap = bmp;
                message = null;
                return true;
            }
            catch (Exception ex)
            {
                message = $"Screen region capture failed at ({left},{top}) {width}x{height}: {ex.Message}";
                return false;
            }
        }

        private static bool TryLoadBitmapWithoutLockingFile(string filePath, out Bitmap bitmap, out string message)
        {
            bitmap = null;
            if (!File.Exists(filePath))
            {
                message = $"Image file not found: '{filePath}'.";
                return false;
            }

            try
            {
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(filePath)))
                {
                    // new Bitmap(Stream) fully decodes raster formats (PNG/JPEG/BMP/GIF)
                    // into memory, so disposing the stream here is safe for the inputs OCR
                    // supports; a file that exists but is not a valid image surfaces as
                    // ArgumentException, which the catch below turns into false + message
                    // (never-throws contract). No second copy is needed.
                    bitmap = new Bitmap(ms);
                }
                message = null;
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to load image file '{filePath}': {ex.Message}. Expected a valid image file.";
                return false;
            }
        }

        /// <summary>
        /// Converts a GDI+ <see cref="Bitmap"/> to a WinRT <see cref="SoftwareBitmap"/> in
        /// Bgra8/Premultiplied format, the format <see cref="OcrEngine.RecognizeAsync"/>
        /// requires, via a direct pixel-buffer copy: GDI+ Format32bppArgb is BGRA in
        /// memory with straight alpha, so the copy is copy-only (no encode/decode round
        /// trip through PNG, which dominated the cost of every OCR call), and the alpha
        /// premultiplication is done in one pass by <c>SoftwareBitmap.Convert</c>.
        /// </summary>
        private static SoftwareBitmap BitmapToSoftwareBitmap(Bitmap bitmap)
        {
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                // A 32bpp bitmap's stride is always Width * 4 (no padding needed), so
                // the locked rows form a tightly packed Bgra8 buffer with no waste.
                int stride = Math.Abs(data.Stride);
                int height = bitmap.Height;
                byte[] buffer = new byte[stride * height];

                if (data.Stride > 0)
                {
                    Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                }
                else
                {
                    // Negative stride = bottom-up rows; copy them flipped.
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(data.Scan0 + (height - 1 - y) * data.Stride, buffer, y * stride, stride);
                }

                SoftwareBitmap straight = SoftwareBitmap.CreateCopyFromBuffer(
                    buffer.AsBuffer(), BitmapPixelFormat.Bgra8, bitmap.Width, height, BitmapAlphaMode.Straight);
                using (straight)
                {
                    return SoftwareBitmap.Convert(straight, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>
        /// Resolves an <see cref="OcrEngine"/> for the given language tag (or the user's
        /// profile languages), returning <c>false</c> with a message instead of throwing
        /// when no matching OCR language pack is installed. Install one via Windows
        /// Settings &gt; Time &amp; Language &gt; Language &amp; region.
        /// </summary>
        private static bool TryGetOcrEngine(string languageTag, out OcrEngine engine, out string message)
        {
            engine = null;
            if (string.IsNullOrEmpty(languageTag))
            {
                engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
            else
            {
                // new Language throws ArgumentException for a tag that isn't valid BCP-47
                // (e.g. "en_US" with an underscore) - surface it as false + message instead
                // of breaking the documented never-throws contract.
                Language language;
                try
                {
                    language = new Language(languageTag);
                }
                catch (ArgumentException)
                {
                    message = $"'{languageTag}' is not a valid BCP-47 language tag.";
                    return false;
                }

                if (!OcrEngine.IsLanguageSupported(language))
                {
                    message = $"OCR language pack for '{languageTag}' is not installed. Install it via Windows Settings > Time & Language > Language & region.";
                    return false;
                }
                engine = OcrEngine.TryCreateFromLanguage(language);
            }

            if (engine == null)
            {
                message = "No OCR language pack is installed. Install one via Windows Settings > Time & Language > Language & region.";
                return false;
            }

            message = null;
            return true;
        }

        private static Rectangle ToScreenRectangle(Windows.Foundation.Rect rect, int offsetX, int offsetY)
        {
            // AwayFromZero so .5-pixel bounds round to the nearest whole pixel rather than
            // banker's rounding to even (which would bias coordinates on odd-sized words).
            return new Rectangle(
                offsetX + (int)Math.Round(rect.X, MidpointRounding.AwayFromZero),
                offsetY + (int)Math.Round(rect.Y, MidpointRounding.AwayFromZero),
                (int)Math.Round(rect.Width, MidpointRounding.AwayFromZero),
                (int)Math.Round(rect.Height, MidpointRounding.AwayFromZero));
        }

        private static Rectangle UnionBounds(List<OcrWord> words)
        {
            if (words.Count == 0)
                return Rectangle.Empty;

            Rectangle union = words[0].Bounds;
            for (int i = 1; i < words.Count; i++)
                union = Rectangle.Union(union, words[i].Bounds);
            return union;
        }

        /// <summary>
        /// Rejects capture regions that lie entirely outside the virtual screen (all
        /// monitors). A partially overlapping region is allowed - Windows copies the
        /// on-screen part and the off-screen part comes back black - but a region
        /// entirely off-screen would OCR a solid black rectangle into empty text,
        /// so it surfaces as false + message instead.
        /// </summary>
        private static bool TryEnsureRegionOnScreen(int left, int top, int width, int height, out string message)
        {
            int vsLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vsTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vsRight = vsLeft + GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vsBottom = vsTop + GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // long arithmetic so extreme left/width inputs can't overflow the bounds check.
            long right = (long)left + width;
            long bottom = (long)top + height;

            if (right <= vsLeft || left >= vsRight || bottom <= vsTop || top >= vsBottom)
            {
                message = $"Capture region ({left},{top}) {width}x{height} lies entirely outside the " +
                          $"virtual screen ({vsLeft},{vsTop}) {vsRight - vsLeft}x{vsBottom - vsTop}; nothing can be recognized there." +
                          " Note the coordinates are virtualized in a DPI-unaware process (see MouseUtils.IsProcessDpiAware).";
                return false;
            }
            message = null;
            return true;
        }

        /// <summary>
        /// Runs OCR on a bitmap and returns our structured <see cref="OcrResult"/>, with
        /// bounding rectangles offset by (<paramref name="offsetX"/>, <paramref name="offsetY"/>)
        /// so region-capture results come back in screen coordinates.
        /// </summary>
        private static bool TryRecognizeText(Bitmap bitmap, string languageTag, out OcrResult result, out string message, int offsetX = 0, int offsetY = 0)
        {
            result = null;
            if (!TryGetOcrEngine(languageTag, out OcrEngine engine, out message))
                return false;

            try
            {
                // BitmapToSoftwareBitmap can throw (LockBits/OutOfMemory on a huge image),
                // so it lives inside the try to keep the never-throws contract.
                using (SoftwareBitmap softwareBitmap = BitmapToSoftwareBitmap(bitmap))
                {
                    // Sync-over-async: Pega robot flows run on MTA threads with no
                    // SynchronizationContext, so blocking here cannot deadlock.
                    Windows.Media.Ocr.OcrResult native = engine.RecognizeAsync(softwareBitmap).AsTask().GetAwaiter().GetResult();

                    var ocrResult = new OcrResult { Text = native.Text };
                    foreach (Windows.Media.Ocr.OcrLine nativeLine in native.Lines)
                    {
                        var line = new OcrLine { Text = nativeLine.Text };
                        foreach (Windows.Media.Ocr.OcrWord nativeWord in nativeLine.Words)
                        {
                            line.Words.Add(new OcrWord
                            {
                                Text = nativeWord.Text,
                                Bounds = ToScreenRectangle(nativeWord.BoundingRect, offsetX, offsetY)
                            });
                        }
                        line.Bounds = UnionBounds(line.Words);
                        ocrResult.Lines.Add(line);
                    }
                    result = ocrResult;
                    message = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = "OCR recognition failed: " + ex.Message;
                return false;
            }
        }

        #endregion

        #region Win32 Interop

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        #endregion
    }
}
