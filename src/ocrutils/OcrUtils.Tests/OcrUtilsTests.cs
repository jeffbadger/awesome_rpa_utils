using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OcrAutomation;
using Xunit;

namespace OcrAutomation.Tests
{
    /// <summary>
    /// Unit tests for OcrUtils' input guards and the never-throw contract on the
    /// paths that return before any GDI+ capture, user32 call, or WinRT OCR engine
    /// handoff. Runs on Windows (the component's UseWindowsForms pins the runtime
    /// to WindowsDesktop), so the guards here deliberately stop short of the
    /// recognition pipeline itself - live-region capture and real OCR coverage is
    /// the Pega Unit Test plan in the repo's TESTING.md.
    /// </summary>
    public class OcrUtilsTests
    {
        private readonly OcrUtils _ocr = new OcrUtils();

        // --- FindTextLocation: null/empty search text is rejected, never a wildcard match ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void FindTextLocation_NullOrEmptySearchText_ReturnsFalseWithMessage(string searchText)
        {
            bool found = _ocr.FindTextLocation(searchText, 0, 0, 100, 100, out var location, out string message);

            Assert.False(found);
            Assert.Equal(System.Drawing.Rectangle.Empty, location);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Region guards: non-positive dimensions rejected before any capture/Win32 call ---

        [Theory]
        [InlineData(0, 100)]   // zero width
        [InlineData(100, 0)]   // zero height
        [InlineData(-5, 100)]  // negative width
        [InlineData(100, -5)]  // negative height
        public void GetTextFromRegion_NonPositiveDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            bool ok = _ocr.GetTextFromRegion(0, 0, width, height, out string text, out string message);

            Assert.False(ok);
            Assert.Null(text);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        [InlineData(-5, 100)]
        [InlineData(100, -5)]
        public void GetStructuredTextFromRegion_NonPositiveDimensions_ReturnsFalseWithNullResult(int width, int height)
        {
            bool ok = _ocr.GetStructuredTextFromRegion(0, 0, width, height, out OcrResult result, out string message);

            Assert.False(ok);
            Assert.Null(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        public void FindTextLocation_NonPositiveDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            bool found = _ocr.FindTextLocation("OK", 0, 0, width, height, out var location, out string message);

            Assert.False(found);
            Assert.Equal(System.Drawing.Rectangle.Empty, location);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- WaitForTextToAppear: null/empty text refuses to poll; bad dimensions abort on the first pass ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForTextToAppear_NullOrEmptyExpectedText_ReturnsFalseWithoutPolling(string expectedText)
        {
            var sw = Stopwatch.StartNew();
            // A generous timeout proves the guard returns before the poll loop could ever stall.
            bool found = _ocr.WaitForTextToAppear(0, 0, 100, 100, expectedText, timeoutMs: 30000, pollIntervalMs: 10, out string message);
            sw.Stop();

            Assert.False(found);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Guard should return immediately, took {sw.ElapsedMilliseconds} ms");
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        public void WaitForTextToAppear_NonPositiveDimensions_AbendsWithFailureNotTimeout(int width, int height)
        {
            bool found = _ocr.WaitForTextToAppear(0, 0, width, height, "any text", timeoutMs: 30000, pollIntervalMs: 10, out string message);

            // A real failure (bad dimensions) must abort the poll early with a message -
            // distinct from a clean timeout, which reports a null message.
            Assert.False(found);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForTextToAppear_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool found = _ocr.WaitForTextToAppear(0, 0, 100, 100, "any text", timeoutMs: -1, pollIntervalMs: 10, out string message);

            // A negative timeout is invalid input, not a clean timeout - it must abort
            // with a message rather than silently returning false with message == null.
            Assert.False(found);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- GetTextFromImageFile: missing/corrupt files are false + message, never an exception ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void GetTextFromImageFile_NullOrEmptyPath_ReturnsFalseWithMessage(string filePath)
        {
            bool ok = _ocr.GetTextFromImageFile(filePath, out string text, out string message);

            Assert.False(ok);
            Assert.Null(text);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetTextFromImageFile_NonexistentPath_ReturnsFalseWithMessage()
        {
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            bool ok = _ocr.GetTextFromImageFile(missing, out string text, out string message);

            Assert.False(ok);
            Assert.Null(text);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetTextFromImageFile_CorruptFile_ReturnsFalseWithMessage()
        {
            // A file that exists but isn't an image must surface as false + message
            // (the exact invalid-input case TESTING.md asserts), never an exception.
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
            try
            {
                File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

                bool ok = _ocr.GetTextFromImageFile(path, out string text, out string message);

                Assert.False(ok);
                Assert.Null(text);
                Assert.False(string.IsNullOrEmpty(message));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // --- Language: malformed tags and the language list are never-throw paths ---

        [Fact]
        public void GetTextFromImageFile_InvalidLanguageTag_ReturnsFalseWithMessage()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
            try
            {
                using (var bmp = new System.Drawing.Bitmap(1, 1))
                {
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }

                // "en_US" (underscore) is not valid BCP-47; the never-throws contract
                // requires false + message rather than the ArgumentException the WinRT
                // Language constructor would throw for it.
                bool ok = _ocr.GetTextFromImageFile(path, out string text, out string message, languageTag: "en_US");

                Assert.False(ok);
                Assert.Null(text);
                Assert.False(string.IsNullOrEmpty(message));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryGetAvailableLanguages_NeverThrows()
        {
            bool ok = _ocr.TryGetAvailableLanguages(out List<string> tags, out string message);

            // Either the list is returned (ok=true, tags non-null) or a real failure is
            // reported (ok=false, message set) - both are valid; it must never throw.
            if (ok)
            {
                Assert.NotNull(tags);
                Assert.Null(message);
            }
            else
            {
                Assert.Null(tags);
                Assert.False(string.IsNullOrEmpty(message));
            }
        }

        [Fact]
        public void GetAvailableLanguages_NeverThrows()
        {
            List<string> tags = _ocr.GetAvailableLanguages();

            Assert.NotNull(tags);
        }

        // --- Component lifecycle: construct + dispose is safe and side-effect free ---

        [Fact]
        public void Dispose_Smoke_CompletesWithoutThrowing()
        {
            using (var ocr = new OcrUtils())
            {
            }
        }
    }
}