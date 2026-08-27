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
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is null, empty, or whitespace.</exception>
        [Category("Capture - Core")]
        [Description("Captures the entire virtual screen (all monitors) to an image file.")]
        public void CaptureScreenToFile(string filePath)
        {
            Rectangle bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            using (Bitmap bmp = CaptureRegionToBitmap(bounds.Left, bounds.Top, bounds.Width, bounds.Height))
            {
                SaveBitmap(bmp, filePath);
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
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is not positive, or <paramref name="filePath"/> is invalid.</exception>
        [Category("Capture - Core")]
        [Description("Captures a specific screen region to an image file.")]
        public void CaptureRegionToFile(int left, int top, int width, int height, string filePath)
        {
            using (Bitmap bmp = CaptureRegionToBitmap(left, top, width, height))
            {
                SaveBitmap(bmp, filePath);
            }
        }

        /// <summary>
        /// Captures a window to an image file using <c>PrintWindow</c>, which can
        /// succeed even when the window is covered by other windows.
        /// </summary>
        /// <param name="hWnd">Handle of the window to capture.</param>
        /// <param name="filePath">Destination file path. The format is inferred from the extension.</param>
        /// <exception cref="ArgumentException">The window's bounding rectangle is empty, or <paramref name="filePath"/> is invalid.</exception>
        /// <exception cref="Win32Exception">GetWindowRect or PrintWindow failed (e.g. an invalid handle).</exception>
        /// <remarks>
        /// Uses <c>PW_RENDERFULLCONTENT</c> so modern (DirectComposition/DirectX-backed)
        /// windows render correctly; some exclusive-fullscreen or protected-content
        /// windows may still capture as black.
        /// </remarks>
        [Category("Capture - Core")]
        [Description("Captures a window to an image file via PrintWindow - works even if the window is covered by other windows.")]
        public void CaptureWindowToFile(IntPtr hWnd, string filePath)
        {
            if (!GetWindowRect(hWnd, out RECT rc))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.");

            int width = rc.Right - rc.Left;
            int height = rc.Bottom - rc.Top;
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Target window has an empty or invalid bounding rectangle.", nameof(hWnd));

            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        if (!PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "PrintWindow failed.");
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                SaveBitmap(bmp, filePath);
            }
        }

        /// <summary>
        /// Captures the current foreground window to an image file.
        /// </summary>
        /// <param name="filePath">Destination file path. The format is inferred from the extension.</param>
        /// <exception cref="InvalidOperationException">No foreground window is currently available.</exception>
        /// <exception cref="Win32Exception">GetWindowRect or PrintWindow failed.</exception>
        [Category("Capture - Core")]
        [Description("Captures the current foreground window to an image file.")]
        public void CaptureActiveWindowToFile(string filePath)
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
                throw new InvalidOperationException("No foreground window is currently available.");

            CaptureWindowToFile(hWnd, filePath);
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
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
        [Category("Capture - Core")]
        [Description("Captures a region centered on the given point (e.g. MouseUtils.GetX/GetY) to an image file.")]
        public void CaptureAroundPointToFile(int x, int y, int width, int height, string filePath)
        {
            CaptureRegionToFile(x - width / 2, y - height / 2, width, height, filePath);
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
            using (Bitmap bmp = CaptureRegionToBitmap(bounds.Left, bounds.Top, bounds.Width, bounds.Height))
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
        /// <returns>The full path of the file that was written.</returns>
        /// <exception cref="ArgumentException"><paramref name="stepName"/> or <paramref name="folderPath"/> is null, empty, or whitespace.</exception>
        /// <remarks>
        /// The sequence counter is per component instance and resets when the
        /// automation creates a new instance of this component (e.g. at the start of
        /// each run), so evidence from a single run sorts in step order by filename.
        /// </remarks>
        [Category("Capture - Core")]
        [Description("Captures the screen to an auto-named, sequentially-numbered evidence file: 001_StepName_20260826_143201.png.")]
        public string CaptureStepEvidence(string stepName, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(stepName))
                throw new ArgumentException("A step name is required.", nameof(stepName));
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("A folder path is required.", nameof(folderPath));

            Directory.CreateDirectory(folderPath);
            _evidenceCounter++;

            string fileName = $"{_evidenceCounter:D3}_{SanitizeFileNameSegment(stepName)}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string fullPath = Path.Combine(folderPath, fileName);
            CaptureScreenToFile(fullPath);
            return fullPath;
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
        /// <returns>A 16-character hex string identifying the region's coarse appearance.</returns>
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
        /// <remarks>
        /// Two regions with the same hash almost certainly look the same at a glance;
        /// two regions with different hashes definitely differ. It is not a
        /// cryptographic hash and is intentionally tolerant of tiny rendering noise.
        /// </remarks>
        [Category("Capture - Verification")]
        [Description("Computes a lightweight perceptual hash of a screen region, for cheap 'did this change' checks.")]
        public string GetRegionHash(int left, int top, int width, int height)
        {
            using (Bitmap region = CaptureRegionToBitmap(left, top, width, height))
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

                ulong hash = 0;
                for (int b = 0; b < 64; b++)
                {
                    if (luminance[b] >= average)
                        hash |= (1UL << b);
                }

                return hash.ToString("X16");
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
        /// <returns>True if the region changed before the timeout; false if it timed out.</returns>
        [Category("Capture - Verification")]
        [Description("Polls a screen region until its appearance changes, or the timeout elapses. Returns True if it changed in time.")]
        public bool WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            string baseline = GetRegionHash(left, top, width, height);
            int start = Environment.TickCount;
            while (GetRegionHash(left, top, width, height) == baseline)
            {
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    return false;
                Thread.Sleep(pollIntervalMs);
            }
            return true;
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
        /// <param name="actualDifferencePercent">Receives the actual percentage of differing pixels found.</param>
        /// <returns>True if the actual difference is within <paramref name="tolerancePercent"/>.</returns>
        /// <exception cref="FileNotFoundException"><paramref name="baselineImagePath"/> does not exist.</exception>
        /// <exception cref="ArgumentException">The baseline image's dimensions do not match <paramref name="width"/>/<paramref name="height"/>.</exception>
        /// <remarks>
        /// Per-pixel comparison uses a small per-channel tolerance internally to absorb
        /// anti-aliasing/font-rendering noise, so it is not thrown off by single-pixel
        /// rendering jitter the way an exact byte-for-byte comparison would be.
        /// </remarks>
        [Category("Capture - Verification")]
        [Description("Compares a screen region against a saved baseline image and reports whether the difference is within tolerance.")]
        public bool CompareRegionToBaseline(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent)
        {
            if (!File.Exists(baselineImagePath))
                throw new FileNotFoundException("Baseline image not found.", baselineImagePath);

            using (Bitmap baseline = LoadBitmapWithoutLockingFile(baselineImagePath))
            {
                if (baseline.Width != width || baseline.Height != height)
                    throw new ArgumentException(
                        $"Baseline image size ({baseline.Width}x{baseline.Height}) does not match the requested region size ({width}x{height}).");

                using (Bitmap current = CaptureRegionToBitmap(left, top, width, height))
                {
                    actualDifferencePercent = ComputeDifferencePercent(baseline, current);
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
        /// <param name="lineWidth">Line thickness in pixels; values below 1 are treated as 1.</param>
        /// <exception cref="ArgumentException">The rectangle is empty or inverted.</exception>
        /// <exception cref="FileNotFoundException"><paramref name="imagePath"/> does not exist.</exception>
        [Category("Capture - Annotation")]
        [Description("Draws a rectangular highlight box onto a saved screenshot and overwrites it in place.")]
        public void DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int colorRef, int lineWidth = 3)
        {
            if (right <= left || bottom <= top)
                throw new ArgumentException("Rectangle must be non-empty: right > left and bottom > top.");
            if (lineWidth < 1) lineWidth = 1;

            using (Bitmap bmp = LoadBitmapWithoutLockingFile(imagePath))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen pen = new Pen(ColorFromColorRef(colorRef), lineWidth))
                {
                    g.DrawRectangle(pen, left, top, right - left, bottom - top);
                }

                SaveBitmap(bmp, imagePath);
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
        /// <param name="length">Arrow shaft length in pixels; values below 1 are treated as 1.</param>
        /// <param name="lineWidth">Shaft thickness in pixels; values below 1 are treated as 1.</param>
        /// <exception cref="FileNotFoundException"><paramref name="imagePath"/> does not exist.</exception>
        /// <remarks>The arrow approaches from the upper-left at a 45-degree angle; its tip lands exactly on (x, y).</remarks>
        [Category("Capture - Annotation")]
        [Description("Draws an arrow pointing at the given coordinates onto a saved screenshot and overwrites it in place.")]
        public void DrawArrowToPoint(string imagePath, int x, int y, int colorRef, int length = 40, int lineWidth = 3)
        {
            if (length < 1) length = 1;
            if (lineWidth < 1) lineWidth = 1;

            using (Bitmap bmp = LoadBitmapWithoutLockingFile(imagePath))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen pen = new Pen(ColorFromColorRef(colorRef), lineWidth))
                {
                    pen.CustomEndCap = new AdjustableArrowCap(6, 8);
                    g.DrawLine(pen, x - length, y - length, x, y);
                }

                SaveBitmap(bmp, imagePath);
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
        /// <param name="colorRef">Fill color as a 0x00BBGGRR value; defaults to solid black.</param>
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
        /// <exception cref="FileNotFoundException"><paramref name="imagePath"/> does not exist.</exception>
        /// <remarks>
        /// Uses an opaque solid fill rather than a blur, deliberately: blurred text or
        /// numbers can sometimes be partially reconstructed, whereas a solid fill
        /// permanently discards the underlying pixels - the safer default for
        /// redacting PII (SSNs, account numbers) in retained evidence screenshots.
        /// </remarks>
        [Category("Capture - Annotation")]
        [Description("Fills a rectangular region of a saved screenshot with a solid color (default black) to redact PII, overwriting the file in place.")]
        public void RedactRegion(string imagePath, int left, int top, int width, int height, int colorRef = 0x000000)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Redaction width and height must both be positive.");

            using (Bitmap bmp = LoadBitmapWithoutLockingFile(imagePath))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (SolidBrush brush = new SolidBrush(ColorFromColorRef(colorRef)))
                {
                    g.FillRectangle(brush, left, top, width, height);
                }

                SaveBitmap(bmp, imagePath);
            }
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

        private static void SaveBitmap(Bitmap bmp, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A file path is required.", nameof(filePath));

            string fullPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            bmp.Save(fullPath, GetImageFormatFromExtension(fullPath));
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
