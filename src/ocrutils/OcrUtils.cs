using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
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
            result = null;
            if (!TryCaptureRegionToBitmap(left, top, width, height, out Bitmap bitmap, out message))
                return false;

            using (bitmap)
            {
                return TryRecognizeText(bitmap, languageTag, out result, out message, left, top);
            }
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

        #endregion

        #region Language

        /// <summary>Gets the BCP-47 language tags of every OCR language pack currently installed.</summary>
        [Category("OCR - Language")]
        [Description("Gets the language tags of every OCR language pack currently installed.")]
        public List<string> GetAvailableLanguages()
        {
            var tags = new List<string>();
            foreach (Language language in OcrEngine.AvailableRecognizerLanguages)
                tags.Add(language.LanguageTag);
            return tags;
        }

        #endregion

        #region Wait-for-Text Polling

        /// <summary>Polls a screen region until it contains text matching <paramref name="expectedText"/> (case-insensitive substring), or the timeout elapses.</summary>
        /// <param name="left">The X-coordinate of the top-left corner of the region to poll.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to poll.</param>
        /// <param name="width">The width of the region to poll, in pixels.</param>
        /// <param name="height">The height of the region to poll, in pixels.</param>
        /// <param name="expectedText">The text to wait for (case-insensitive substring match).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="message"><c>null</c> if the poll completed (found or genuinely timed out); otherwise a human-readable reason a real failure (bad dimensions, missing language pack) aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the expected text appeared before the timeout; <c>false</c> if it timed out, or if a real failure aborted the poll (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("OCR - Wait for Text")]
        [Description("Polls a screen region until it contains the expected text, or the timeout elapses. Returns True if found in time; never throws.")]
        public bool WaitForTextToAppear(int left, int top, int width, int height, string expectedText, int timeoutMs, int pollIntervalMs, out string message)
        {
            if (string.IsNullOrEmpty(expectedText))
            {
                message = "Expected text must not be null or empty.";
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
                    message = null;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
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
                byte[] bytes = File.ReadAllBytes(filePath);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Bitmap decoded = new Bitmap(ms))
                {
                    // new Bitmap can throw ArgumentException for a file that exists
                    // but is not a valid/corrupt image - surfaces as false + message
                    // instead of an exception (never-throws contract).
                    bitmap = new Bitmap(decoded);
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
                var language = new Language(languageTag);
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
            return new Rectangle(
                offsetX + (int)Math.Round(rect.X),
                offsetY + (int)Math.Round(rect.Y),
                (int)Math.Round(rect.Width),
                (int)Math.Round(rect.Height));
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

            if (left + width <= vsLeft || left >= vsRight || top + height <= vsTop || top >= vsBottom)
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

            using (SoftwareBitmap softwareBitmap = BitmapToSoftwareBitmap(bitmap))
            {
                try
                {
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
                catch (Exception ex)
                {
                    message = "OCR recognition failed: " + ex.Message;
                    return false;
                }
            }
        }

        #endregion

        #region Win32 Interop

        private const int SM_XVIRTUALSCREEN  = 76;
        private const int SM_YVIRTUALSCREEN  = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        #endregion
    }
}
