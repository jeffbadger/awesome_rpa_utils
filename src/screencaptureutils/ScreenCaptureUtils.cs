using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenCaptureAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that captures the screen, a region, or a
    /// window to a file/clipboard, compares captures against a baseline for visual
    /// verification, and annotates or redacts saved screenshots for audit evidence.
    /// Designed to be used alongside <c>MouseUtils</c> (MouseAutomation), not to
    /// duplicate it - this component owns pixels-to-image concerns; MouseUtils owns
    /// cursor/input concerns.
    /// </summary>
    /// <remarks>
    /// All coordinates are absolute screen pixels, consistent with MouseUtils. Screen
    /// captures use <see cref="Graphics.CopyFromScreen(int, int, int, int, Size)"/> and
    /// therefore have the same requirements as any GDI screen read: an interactive,
    /// unlocked desktop, and results reflect logical (DPI-virtualized) pixels unless
    /// the hosting process is DPI-aware.
    /// </remarks>
    [Description("Captures the screen, a region, or a window to a file/clipboard; compares captures for " +
                 "visual verification; and annotates/redacts saved screenshots. Drag this component onto " +
                 "a Pega Robot Studio automation to use its methods.")]
    public class ScreenCaptureUtils : Component
    {
        #region Construction

        /// <summary>Sequential counter used to name files produced by <see cref="CaptureStepEvidence"/>.</summary>
        private int _evidenceCounter;

        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public ScreenCaptureUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public ScreenCaptureUtils(IContainer container)
        {
            container?.Add(this);
        }

        #endregion

        #region Core Capture

        /// <summary>
        /// Captures the entire virtual screen (all monitors) to an image file.
        /// </summary>
        /// <param name="filePath">Destination file path. The format is inferred from the extension (.png, .jpg/.jpeg, .bmp, .gif); unrecognized extensions are saved as PNG.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="filePath"/> is null, empty, or whitespace. Never throws.</returns>
        [Category("Capture - Core")]
        [Description("Captures the entire virtual screen (all monitors) to an image file. Returns True on success; never throws.")]
        public bool CaptureScreenToFile(string filePath, out string message)
        {
            Rectangle bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            if (!TryCaptureRegionToBitmap(bounds.Left, bounds.Top, bounds.Width, bounds.Height, out Bitmap bmp, out message))
                return false;

            using (bmp)
            {
                return TrySaveBitmap(bmp, filePath, out message);
            }
        }

        /// <summary>
        /// Captures a specific screen region to an image file.
        /// </summary>
        /// <param name="left">Left edge of the region in screen pixels.</param>
        /// <param name="top">Top edge of the region in screen pixels.</param>
        /// <param name="width">Region width in pixels.</param>
        /// <param name="height">Region height in pixels.</param>
        /// <param name="filePath">Destination file path. The format is inferred from the extension.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="width"/>/<paramref name="height"/> are not positive, or <paramref name="filePath"/> is invalid. Never throws.</returns>
        [Category("Capture - Core")]
        [Description("Captures a specific screen region to an image file. Returns True on success; never throws.")]
        public bool CaptureRegionToFile(int left, int top, int width, int height, string filePath, out string message)
        {
            if (!TryCaptureRegionToBitmap(left, top, width, height, out Bitmap bmp, out message))
                return false;

            using (bmp)
            {
                return TrySaveBitmap(bmp, filePath, out message);
            }
        }

        /// <summary>
        /// Captures a window to an image file using <c>PrintWindow</c>, which can
        /// succeed even when the window is covered by other windows.
        /// </summary>
        /// <param name="hWnd">Handle of the window to capture.</param>
        /// <param name="filePath">Destination file path. The format is inferred from the extension.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the window's bounding rectangle is empty, <paramref name="filePath"/> is invalid, or GetWindowRect/PrintWindow failed (e.g. an invalid handle). Never throws.</returns>
        /// <remarks>
        /// Uses <c>PW_RENDERFULLCONTENT</c> so modern (DirectComposition/DirectX-backed)
        /// windows render correctly; some exclusive-fullscreen or protected-content
        /// windows may still capture as black.
        /// </remarks>
        [Category("Capture - Core")]
        [Description("Captures a window to an image file via PrintWindow - works even if the window is covered by other windows. Returns True on success; never throws.")]
        public bool CaptureWindowToFile(IntPtr hWnd, string filePath, out string message)
        {
            if (!GetWindowRect(hWnd, out RECT rc))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.").Message;
                return false;
            }

            int width = rc.Right - rc.Left;
            int height = rc.Bottom - rc.Top;
            if (width <= 0 || height <= 0)
            {
                message = "Target window has an empty or invalid bounding rectangle.";
                return false;
            }

            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        if (!PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT))
                        {
                            message = new Win32Exception(Marshal.GetLastWin32Error(), "PrintWindow failed.").Message;
                            return false;
                        }
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                return TrySaveBitmap(bmp, filePath, out message);
            }
        }

        /// <summary>
        /// Captures the current foreground window to an image file.
        /// </summary>
        /// <param name="filePath">Destination file path. The format is inferred from the extension.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if no foreground window is currently available, or GetWindowRect/PrintWindow failed. Never throws.</returns>
        [Category("Capture - Core")]
        [Description("Captures the current foreground window to an image file. Returns True on success; never throws.")]
        public bool CaptureActiveWindowToFile(string filePath, out string message)
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                message = "No foreground window is currently available.";
                return false;
            }

            return CaptureWindowToFile(hWnd, filePath, out message);
        }

        /// <summary>
        /// Captures a square region centered on the given point to an image file -
        /// e.g. "screenshot of just what was clicked", pairing naturally with
        /// MouseUtils' cursor-position methods.
        /// </summary>
        /// <param name="x">Center X coordinate in screen pixels.</param>
        /// <param name="y">Center Y coordinate in screen pixels.</param>
        /// <param name="width">Capture width in pixels.</param>
        /// <param name="height">Capture height in pixels.</param>
        /// <param name="filePath">Destination file path. The format is inferred from the extension.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="width"/>/<paramref name="height"/> are not positive. Never throws.</returns>
        [Category("Capture - Core")]
        [Description("Captures a region centered on the given point (e.g. MouseUtils.GetX/GetY) to an image file. Returns True on success; never throws.")]
        public bool CaptureAroundPointToFile(int x, int y, int width, int height, string filePath, out string message)
        {
            return CaptureRegionToFile(x - width / 2, y - height / 2, width, height, filePath, out message);
        }

        /// <summary>
        /// Captures the entire virtual screen and copies it to the Windows clipboard
        /// as an image, ready to paste into an email or ticket.
        /// </summary>
        /// <remarks>Requires the calling thread to be STA, as with any Windows Forms clipboard access.</remarks>
        [Category("Capture - Core")]
        [Description("Captures the entire virtual screen and copies it to the clipboard as an image.")]
        public void CaptureToClipboard()
        {
            Rectangle bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            TryCaptureRegionToBitmap(bounds.Left, bounds.Top, bounds.Width, bounds.Height, out Bitmap bmp, out _);
            using (bmp)
            {
                System.Windows.Forms.Clipboard.SetImage(bmp);
            }
        }

        /// <summary>
        /// Captures the full screen to a sequentially- and time-stamped file in the
        /// given folder, following the naming convention common to RPA evidence
        /// trails: <c>{counter}_{stepName}_{timestamp}.png</c>.
        /// </summary>
        /// <param name="stepName">A short, human-readable name for the step being evidenced (invalid file-name characters are replaced with underscores).</param>
        /// <param name="folderPath">Folder to save the evidence file into; created if it doesn't exist.</param>
        /// <param name="fullPath">The full path of the file that was written, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="stepName"/>/<paramref name="folderPath"/> are null, empty, or whitespace. Never throws.</returns>
        /// <remarks>
        /// The sequence counter is per component instance and resets when the
        /// automation creates a new instance of this component (e.g. at the start of
        /// each run), so evidence from a single run sorts in step order by filename.
        /// </remarks>
        [Category("Capture - Core")]
        [Description("Captures the screen to an auto-named, sequentially-numbered evidence file: 001_StepName_20260826_143201.png. Returns True on success; never throws.")]
        public bool CaptureStepEvidence(string stepName, string folderPath, out string fullPath, out string message)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(stepName))
            {
                message = "A step name is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                message = "A folder path is required.";
                return false;
            }

            Directory.CreateDirectory(folderPath);
            _evidenceCounter++;

            string fileName = $"{_evidenceCounter:D3}_{SanitizeFileNameSegment(stepName)}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string candidatePath = Path.Combine(folderPath, fileName);
            if (!CaptureScreenToFile(candidatePath, out message))
                return false;

            fullPath = candidatePath;
            return true;
        }

        #endregion

        #region Verification & Comparison

        /// <summary>
        /// Computes a lightweight perceptual hash (an 8x8 average hash) of a screen
        /// region, for cheap "did this area visibly change" checks without storing
        /// full-resolution bitmaps.
        /// </summary>
        /// <param name="left">Left edge of the region in screen pixels.</param>
        /// <param name="top">Top edge of the region in screen pixels.</param>
        /// <param name="width">Region width in pixels.</param>
        /// <param name="height">Region height in pixels.</param>
        /// <param name="hash">A 16-character hex string identifying the region's coarse appearance, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="width"/>/<paramref name="height"/> are not positive. Never throws.</returns>
        /// <remarks>
        /// Two regions with the same hash almost certainly look the same at a glance;
        /// two regions with different hashes definitely differ. It is not a
        /// cryptographic hash and is intentionally tolerant of tiny rendering noise.
        /// </remarks>
        [Category("Capture - Verification")]
        [Description("Computes a lightweight perceptual hash of a screen region, for cheap 'did this change' checks. Returns True on success; never throws.")]
        public bool GetRegionHash(int left, int top, int width, int height, out string hash, out string message)
        {
            hash = null;
            if (!TryCaptureRegionToBitmap(left, top, width, height, out Bitmap region, out message))
                return false;

            using (region)
            using (Bitmap small = new Bitmap(region, new Size(8, 8)))
            {
                long[] luminance = new long[64];
                int i = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        luminance[i++] = (c.R + c.G + c.B) / 3;
                    }
                }

                long average = 0;
                for (int p = 0; p < luminance.Length; p++)
                    average += luminance[p];
                average /= luminance.Length;

                ulong h = 0;
                for (int b = 0; b < 64; b++)
                {
                    if (luminance[b] >= average)
                        h |= (1UL << b);
                }

                hash = h.ToString("X16");
                message = null;
                return true;
            }
        }

        /// <summary>
        /// Polls a screen region until its perceptual hash changes from what it was
        /// when this method was called, or the timeout elapses.
        /// </summary>
        /// <param name="left">Left edge of the region in screen pixels.</param>
        /// <param name="top">Top edge of the region in screen pixels.</param>
        /// <param name="width">Region width in pixels.</param>
        /// <param name="height">Region height in pixels.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="message"><c>null</c> if the poll completed (changed or genuinely timed out); otherwise a human-readable reason a real failure (bad dimensions) aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the region changed before the timeout; <c>false</c> if it timed out, or if a real failure aborted the poll (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("Capture - Verification")]
        [Description("Polls a screen region until its appearance changes, or the timeout elapses. Returns True if it changed in time; never throws.")]
        public bool WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out string message)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            if (!GetRegionHash(left, top, width, height, out string baseline, out message))
                return false;

            int start = Environment.TickCount;
            while (true)
            {
                if (!GetRegionHash(left, top, width, height, out string current, out message))
                    return false;
                if (current != baseline)
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

        /// <summary>
        /// Compares a screen region against a previously-saved baseline image,
        /// pixel-by-pixel, and reports whether the difference is within tolerance -
        /// a simple visual-regression check for "does this screen still look right."
        /// </summary>
        /// <param name="left">Left edge of the region in screen pixels.</param>
        /// <param name="top">Top edge of the region in screen pixels.</param>
        /// <param name="width">Region width in pixels; must match the baseline image's width.</param>
        /// <param name="height">Region height in pixels; must match the baseline image's height.</param>
        /// <param name="baselineImagePath">Path to the reference image to compare against.</param>
        /// <param name="tolerancePercent">Maximum percentage of differing pixels still considered a match (0-100).</param>
        /// <param name="actualDifferencePercent">Receives the actual percentage of differing pixels found, or <c>0</c> if this method returns <c>false</c> due to a real failure (check <paramref name="message"/>).</param>
        /// <param name="message"><c>null</c> if the comparison completed (within or outside tolerance); otherwise a human-readable reason a real failure (missing baseline, size mismatch) prevented the comparison (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the actual difference is within <paramref name="tolerancePercent"/>; <c>false</c> if it isn't, or if a real failure prevented the comparison (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        /// <remarks>
        /// Per-pixel comparison uses a small per-channel tolerance internally to absorb
        /// anti-aliasing/font-rendering noise, so it is not thrown off by single-pixel
        /// rendering jitter the way an exact byte-for-byte comparison would be.
        /// </remarks>
        [Category("Capture - Verification")]
        [Description("Compares a screen region against a saved baseline image and reports whether the difference is within tolerance. Never throws.")]
        public bool CompareRegionToBaseline(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent, out string message)
        {
            actualDifferencePercent = 0;

            if (!TryLoadBitmapWithoutLockingFile(baselineImagePath, out Bitmap baseline, out message))
                return false;

            using (baseline)
            {
                if (baseline.Width != width || baseline.Height != height)
                {
                    message = $"Baseline image size ({baseline.Width}x{baseline.Height}) does not match the requested region size ({width}x{height}).";
                    return false;
                }

                if (!TryCaptureRegionToBitmap(left, top, width, height, out Bitmap current, out message))
                    return false;

                using (current)
                {
                    actualDifferencePercent = ComputeDifferencePercent(baseline, current);
                    message = null;
                    return actualDifferencePercent <= tolerancePercent;
                }
            }
        }

        #endregion

        #region Annotation & Redaction

        /// <summary>
        /// Draws a rectangular highlight box onto a saved screenshot and overwrites it
        /// in place - the static, saved-evidence counterpart to MouseUtils'
        /// live <c>FlashCursorHighlight</c> ring.
        /// </summary>
        /// <param name="imagePath">Path of the image to annotate; overwritten with the result.</param>
        /// <param name="left">Left edge of the box in image pixels.</param>
        /// <param name="top">Top edge of the box in image pixels.</param>
        /// <param name="right">Right edge of the box in image pixels.</param>
        /// <param name="bottom">Bottom edge of the box in image pixels.</param>
        /// <param name="colorRef">Box color as a 0x00BBGGRR value (same format as MouseUtils' colorRef parameters).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the annotation failed.</param>
        /// <param name="lineWidth">Line thickness in pixels; values below 1 are treated as 1.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the rectangle is empty/inverted, or <paramref name="imagePath"/> does not exist. Never throws.</returns>
        [Category("Capture - Annotation")]
        [Description("Draws a rectangular highlight box onto a saved screenshot and overwrites it in place. Returns True on success; never throws.")]
        public bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int colorRef, out string message, int lineWidth = 3)
        {
            if (right <= left || bottom <= top)
            {
                message = "Rectangle must be non-empty: right > left and bottom > top.";
                return false;
            }
            if (lineWidth < 1) lineWidth = 1;

            if (!TryLoadBitmapWithoutLockingFile(imagePath, out Bitmap bmp, out message))
                return false;

            using (bmp)
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen pen = new Pen(ColorFromColorRef(colorRef), lineWidth))
                {
                    g.DrawRectangle(pen, left, top, right - left, bottom - top);
                }

                return TrySaveBitmap(bmp, imagePath, out message);
            }
        }

        /// <summary>
        /// Draws an arrow pointing at the given coordinates onto a saved screenshot
        /// and overwrites it in place.
        /// </summary>
        /// <param name="imagePath">Path of the image to annotate; overwritten with the result.</param>
        /// <param name="x">X coordinate the arrow points to, in image pixels.</param>
        /// <param name="y">Y coordinate the arrow points to, in image pixels.</param>
        /// <param name="colorRef">Arrow color as a 0x00BBGGRR value.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the annotation failed.</param>
        /// <param name="length">Arrow shaft length in pixels; values below 1 are treated as 1.</param>
        /// <param name="lineWidth">Shaft thickness in pixels; values below 1 are treated as 1.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="imagePath"/> does not exist. Never throws.</returns>
        /// <remarks>The arrow approaches from the upper-left at a 45-degree angle; its tip lands exactly on (x, y).</remarks>
        [Category("Capture - Annotation")]
        [Description("Draws an arrow pointing at the given coordinates onto a saved screenshot and overwrites it in place. Returns True on success; never throws.")]
        public bool DrawArrowToPoint(string imagePath, int x, int y, int colorRef, out string message, int length = 40, int lineWidth = 3)
        {
            if (length < 1) length = 1;
            if (lineWidth < 1) lineWidth = 1;

            if (!TryLoadBitmapWithoutLockingFile(imagePath, out Bitmap bmp, out message))
                return false;

            using (bmp)
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen pen = new Pen(ColorFromColorRef(colorRef), lineWidth))
                {
                    pen.CustomEndCap = new AdjustableArrowCap(6, 8);
                    g.DrawLine(pen, x - length, y - length, x, y);
                }

                return TrySaveBitmap(bmp, imagePath, out message);
            }
        }

        /// <summary>
        /// Fills a rectangular region of a saved screenshot with a solid color,
        /// permanently redacting it, and overwrites the file in place.
        /// </summary>
        /// <param name="imagePath">Path of the image to redact; overwritten with the result.</param>
        /// <param name="left">Left edge of the region to redact, in image pixels.</param>
        /// <param name="top">Top edge of the region to redact, in image pixels.</param>
        /// <param name="width">Width of the region to redact, in pixels.</param>
        /// <param name="height">Height of the region to redact, in pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the redaction failed.</param>
        /// <param name="colorRef">Fill color as a 0x00BBGGRR value; defaults to solid black.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="width"/>/<paramref name="height"/> are not positive, or <paramref name="imagePath"/> does not exist. Never throws.</returns>
        /// <remarks>
        /// Uses an opaque solid fill rather than a blur, deliberately: blurred text or
        /// numbers can sometimes be partially reconstructed, whereas a solid fill
        /// permanently discards the underlying pixels - the safer default for
        /// redacting PII (SSNs, account numbers) in retained evidence screenshots.
        /// </remarks>
        [Category("Capture - Annotation")]
        [Description("Fills a rectangular region of a saved screenshot with a solid color (default black) to redact PII, overwriting the file in place. Returns True on success; never throws.")]
        public bool RedactRegion(string imagePath, int left, int top, int width, int height, out string message, int colorRef = 0x000000)
        {
            if (width <= 0 || height <= 0)
            {
                message = "Redaction width and height must both be positive.";
                return false;
            }

            if (!TryLoadBitmapWithoutLockingFile(imagePath, out Bitmap bmp, out message))
                return false;

            using (bmp)
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (SolidBrush brush = new SolidBrush(ColorFromColorRef(colorRef)))
                {
                    g.FillRectangle(brush, left, top, width, height);
                }

                return TrySaveBitmap(bmp, imagePath, out message);
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

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }
            bitmap = bmp;
            message = null;
            return true;
        }

        private static bool TrySaveBitmap(Bitmap bmp, string filePath, out string message)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                message = "A file path is required.";
                return false;
            }

            string fullPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            bmp.Save(fullPath, GetImageFormatFromExtension(fullPath));
            message = null;
            return true;
        }

        private static ImageFormat GetImageFormatFromExtension(string filePath)
        {
            switch (Path.GetExtension(filePath).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    return ImageFormat.Jpeg;
                case ".bmp":
                    return ImageFormat.Bmp;
                case ".gif":
                    return ImageFormat.Gif;
                case ".png":
                default:
                    return ImageFormat.Png;
            }
        }

        private static string SanitizeFileNameSegment(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ' || Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars);
        }

        /// <summary>
        /// Loads a bitmap from disk as a fully-decoded, independent copy, so the
        /// source file is not left locked and can be immediately overwritten
        /// (annotation/redaction methods load, modify, then save back in place).
        /// </summary>
        private static bool TryLoadBitmapWithoutLockingFile(string filePath, out Bitmap bitmap, out string message)
        {
            bitmap = null;
            if (!File.Exists(filePath))
            {
                message = $"Image file not found: '{filePath}'.";
                return false;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Bitmap decoded = new Bitmap(ms))
            {
                bitmap = new Bitmap(decoded);
            }
            message = null;
            return true;
        }

        private static Color ColorFromColorRef(int colorRef)
        {
            int r = colorRef & 0xFF;
            int g = (colorRef >> 8) & 0xFF;
            int b = (colorRef >> 16) & 0xFF;
            return Color.FromArgb(r, g, b);
        }

        private static double ComputeDifferencePercent(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height)
                throw new ArgumentException("Images must be the same size to compare.");

            const int channelThreshold = 12; // absorbs anti-aliasing/compression noise

            Rectangle rect = new Rectangle(0, 0, a.Width, a.Height);
            BitmapData dataA = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dataB = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int byteCount = Math.Abs(dataA.Stride) * a.Height;
                byte[] bytesA = new byte[byteCount];
                byte[] bytesB = new byte[byteCount];
                Marshal.Copy(dataA.Scan0, bytesA, 0, byteCount);
                Marshal.Copy(dataB.Scan0, bytesB, 0, byteCount);

                long differingPixels = 0;
                long totalPixels = (long)a.Width * a.Height;

                for (int p = 0; p + 3 < byteCount; p += 4)
                {
                    int diffBlue = Math.Abs(bytesA[p] - bytesB[p]);
                    int diffGreen = Math.Abs(bytesA[p + 1] - bytesB[p + 1]);
                    int diffRed = Math.Abs(bytesA[p + 2] - bytesB[p + 2]);
                    if (diffBlue > channelThreshold || diffGreen > channelThreshold || diffRed > channelThreshold)
                        differingPixels++;
                }

                return totalPixels == 0 ? 0.0 : (differingPixels / (double)totalPixels) * 100.0;
            }
            finally
            {
                a.UnlockBits(dataA);
                b.UnlockBits(dataB);
            }
        }

        #endregion

        #region Win32 Interop

        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        #endregion
    }
}
