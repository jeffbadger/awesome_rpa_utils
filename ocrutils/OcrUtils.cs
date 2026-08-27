using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

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

        #region Region/Image to Plain Text

        /// <summary>Captures a screen region and returns its recognized text.</summary>
        /// <param name="left">The X-coordinate of the top-left corner of the region to capture.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to capture.</param>
        /// <param name="width">The width of the region to capture, in pixels.</param>
        /// <param name="height">The height of the region to capture, in pixels.</param>
        /// <param name="languageTag">A BCP-47 language tag (e.g. <c>"en-US"</c>), or <c>null</c> to use the user's profile languages.</param>
        /// <exception cref="ArgumentException">Width or height is not positive.</exception>
        /// <exception cref="InvalidOperationException">No matching OCR language pack is installed.</exception>
        public string GetTextFromRegion(int left, int top, int width, int height, string languageTag = null)
        {
            using (Bitmap bitmap = CaptureRegionToBitmap(left, top, width, height))
            {
                return RecognizeText(bitmap, languageTag, left, top).Text;
            }
        }

        /// <summary>Loads an image file and returns its recognized text.</summary>
        /// <param name="filePath">Path to the image file to recognize.</param>
        /// <param name="languageTag">A BCP-47 language tag (e.g. <c>"en-US"</c>), or <c>null</c> to use the user's profile languages.</param>
        /// <exception cref="FileNotFoundException"><paramref name="filePath"/> does not exist.</exception>
        /// <exception cref="InvalidOperationException">No matching OCR language pack is installed.</exception>
        public string GetTextFromImageFile(string filePath, string languageTag = null)
        {
            using (Bitmap bitmap = LoadBitmapWithoutLockingFile(filePath))
            {
                return RecognizeText(bitmap, languageTag).Text;
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
        /// <exception cref="ArgumentException">Width or height is not positive.</exception>
        /// <exception cref="InvalidOperationException">No matching OCR language pack is installed.</exception>
        public OcrResult GetStructuredTextFromRegion(int left, int top, int width, int height, string languageTag = null)
        {
            using (Bitmap bitmap = CaptureRegionToBitmap(left, top, width, height))
            {
                return RecognizeText(bitmap, languageTag, left, top);
            }
        }

        /// <summary>
        /// Searches a screen region for text matching <paramref name="searchText"/> (a
        /// case-insensitive substring match — e.g. searching for "OK" also matches inside
        /// "BOOK" — a line match is preferred; falls back to a single-word match) and
        /// returns its bounding rectangle in screen coordinates, or
        /// <see cref="Rectangle.Empty"/> if not found (not found is a normal, checkable
        /// outcome, not an error).
        /// </summary>
        /// <param name="searchText">The text to search for (case-insensitive substring match).</param>
        /// <param name="left">The X-coordinate of the top-left corner of the region to search.</param>
        /// <param name="top">The Y-coordinate of the top-left corner of the region to search.</param>
        /// <param name="width">The width of the region to search, in pixels.</param>
        /// <param name="height">The height of the region to search, in pixels.</param>
        /// <exception cref="ArgumentException">Width or height is not positive.</exception>
        /// <exception cref="InvalidOperationException">No matching OCR language pack is installed.</exception>
        public Rectangle FindTextLocation(string searchText, int left, int top, int width, int height)
        {
            OcrResult result = GetStructuredTextFromRegion(left, top, width, height);
            foreach (var line in result.Lines)
            {
                if (line.Text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    return line.Bounds;

                foreach (var word in line.Words)
                {
                    if (word.Text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        return word.Bounds;
                }
            }
            return Rectangle.Empty;
        }

        #endregion

        #region Language

        /// <summary>Gets the BCP-47 language tags of every OCR language pack currently installed.</summary>
        public List<string> GetAvailableLanguages()
        {
            var tags = new List<string>();
            foreach (Language language in OcrEngine.AvailableRecognizerLanguages)
                tags.Add(language.LanguageTag);
            return tags;
        }

        #endregion

        #region Internal Helpers

        private static Bitmap CaptureRegionToBitmap(int left, int top, int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Capture width and height must both be positive.");

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        private static Bitmap LoadBitmapWithoutLockingFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Image file not found.", filePath);

            byte[] bytes = File.ReadAllBytes(filePath);
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Bitmap decoded = new Bitmap(ms))
            {
                return new Bitmap(decoded);
            }
        }

        /// <summary>
        /// Converts a GDI+ <see cref="Bitmap"/> to a WinRT <see cref="SoftwareBitmap"/> in
        /// Bgra8/Premultiplied format, the format <see cref="OcrEngine.RecognizeAsync"/>
        /// requires, via a PNG-encode round trip (the standard .NET/WinRT image interop path).
        /// </summary>
        private static SoftwareBitmap BitmapToSoftwareBitmap(Bitmap bitmap)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                using (var writeStream = stream.AsStreamForWrite())
                {
                    bitmap.Save(writeStream, ImageFormat.Png);
                }
                stream.Seek(0);

                BitmapDecoder decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
                SoftwareBitmap decoded = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
                using (decoded)
                {
                    return SoftwareBitmap.Convert(decoded, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }
            }
        }

        /// <exception cref="InvalidOperationException">
        /// No matching OCR language pack is installed. Install one via Windows Settings >
        /// Time &amp; Language > Language &amp; region.
        /// </exception>
        private static OcrEngine GetOcrEngine(string languageTag)
        {
            OcrEngine engine;
            if (string.IsNullOrEmpty(languageTag))
            {
                engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
            else
            {
                var language = new Language(languageTag);
                if (!OcrEngine.IsLanguageSupported(language))
                    throw new InvalidOperationException(
                        $"OCR language pack for '{languageTag}' is not installed. Install it via Windows Settings > Time & Language > Language & region.");
                engine = OcrEngine.TryCreateFromLanguage(language);
            }

            if (engine == null)
                throw new InvalidOperationException(
                    "No OCR language pack is installed. Install one via Windows Settings > Time & Language > Language & region.");

            return engine;
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
        /// Runs OCR on a bitmap and returns our structured <see cref="OcrResult"/>, with
        /// bounding rectangles offset by (<paramref name="offsetX"/>, <paramref name="offsetY"/>)
        /// so region-capture results come back in screen coordinates.
        /// </summary>
        private static OcrResult RecognizeText(Bitmap bitmap, string languageTag, int offsetX = 0, int offsetY = 0)
        {
            OcrEngine engine = GetOcrEngine(languageTag);
            using (SoftwareBitmap softwareBitmap = BitmapToSoftwareBitmap(bitmap))
            {
                Windows.Media.Ocr.OcrResult native = engine.RecognizeAsync(softwareBitmap).AsTask().GetAwaiter().GetResult();

                var result = new OcrResult { Text = native.Text };
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
                    result.Lines.Add(line);
                }
                return result;
            }
        }

        #endregion
    }
}
