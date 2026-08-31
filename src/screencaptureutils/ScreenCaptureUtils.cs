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
    /// the hosting process is DPI-aware (see MouseUtils.IsProcessDpiAware).
    /// File format is inferred from the extension; note that .gif saves are lossy
    /// (256 colors, no true alpha), so prefer .png for evidence screenshots.
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

        /// <summary>
        /// Releases the resources used by the component. ScreenCaptureUtils holds no
        /// unmanaged handles of its own (bitmaps and device contexts are released in
        /// using blocks on each call), so this override exists to give you a cleanup
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

        #endregion

        #region Core Capture

        /// <summary>
        /// Captures the entire virtual screen (all monitors) to an image file.
        /// </summary>
        /// <param name="filePath">Destination file path. The format is inferred from the extension (.png, .jpg/.jpeg, .bmp, .gif, .tif/.tiff); unrecognized extensions are saved as PNG.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="filePath"/> is null, empty, or whitespace. Never throws.</returns>
        [Category("Capture - Core")]
        [Description("Captures the entire virtual screen (all monitors) to an image file. Returns True on success; never throws.")]
        public bool CaptureScreenToFile(string filePath, out string message)
        {
            message = default;
            try
            {
                Rectangle bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
                if (!TryCaptureRegionToBitmap(bounds.Left, bounds.Top, bounds.Width, bounds.Height, out Bitmap bmp, out message))
                    return false;

                using (bmp)
                {
                    return TrySaveBitmap(bmp, filePath, out message);
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CaptureScreenToFile", ex);
                return false;
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
            message = default;
            try
            {
                if (!TryCaptureRegionToBitmap(left, top, width, height, out Bitmap bmp, out message))
                    return false;

                using (bmp)
                {
                    return TrySaveBitmap(bmp, filePath, out message);
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CaptureRegionToFile", ex);
                return false;
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
            message = default;
            try
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

                try
                {
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
                catch (Exception ex)
                {
                    // new Bitmap / Graphics.FromImage / GetHdc can throw (OOM for a huge
                    // window rect, GDI resource exhaustion) - surfaces as false + message
                    // instead of an exception (never-throws contract).
                    message = $"Window capture failed: {ex.Message}";
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CaptureWindowToFile", ex);
                return false;
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
            message = default;
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero)
                {
                    message = "No foreground window is currently available.";
                    return false;
                }

                return CaptureWindowToFile(hWnd, filePath, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CaptureActiveWindowToFile", ex);
                return false;
            }
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
            message = default;
            try
            {
                return CaptureRegionToFile(x - width / 2, y - height / 2, width, height, filePath, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CaptureAroundPointToFile", ex);
                return false;
            }
        }

        /// <summary>
        /// Captures the entire virtual screen and copies it to the Windows clipboard
        /// as an image, ready to paste into an email or ticket.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the capture or clipboard copy failed.</param>
        /// <returns><c>true</c> on success. Never throws.</returns>
        /// <remarks>
        /// The clipboard write runs on an internal STA thread regardless of the calling
        /// thread's apartment state, so the automation does not need to know or control it -
        /// unlike raw Windows Forms clipboard access, which requires an STA caller.
        /// A capture failure (locked/secure desktop) or clipboard contention with another
        /// process is reported via <paramref name="message"/> rather than thrown.
        /// </remarks>
        [Category("Capture - Core")]
        [Description("Captures the entire virtual screen and copies it to the clipboard as an image. Returns True on success; never throws.")]
        public bool CaptureToClipboard(out string message)
        {
            message = default;
            try
            {
                Rectangle bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
                if (!TryCaptureRegionToBitmap(bounds.Left, bounds.Top, bounds.Width, bounds.Height, out Bitmap bmp, out message))
                    return false;

                using (bmp)
                {
                    return TrySetClipboardImageOnStaThread(bmp, out message);
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CaptureToClipboard", ex);
                return false;
            }
        }

        /// <summary>
        /// Sets the clipboard image on a dedicated STA thread, since Windows Forms
        /// clipboard access requires an STA apartment and the calling thread's apartment
        /// state is not under this component's control (Pega Robot Studio automations
        /// commonly run MTA).
        /// </summary>
        private static bool TrySetClipboardImageOnStaThread(Bitmap bmp, out string message)
        {
            string staMessage = null;
            bool ok = false;
            var thread = new Thread(() =>
            {
                try
                {
                    System.Windows.Forms.Clipboard.SetImage(bmp);
                    ok = true;
                }
                catch (Exception ex)
                {
                    staMessage = "Could not copy to the clipboard: " + ex.Message +
                                 " (the clipboard may be held by another process.)";
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            message = staMessage;
            return ok;
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
            fullPath = default;
            message = default;
            try
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

                try
                {
                    Directory.CreateDirectory(folderPath);
                }
                catch (Exception ex)
                {
                    message = $"Could not create evidence folder '{folderPath}': {ex.Message}";
                    return false;
                }

                Interlocked.Increment(ref _evidenceCounter);

                string fileName = $"{_evidenceCounter:D3}_{SanitizeFileNameSegment(stepName)}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string candidatePath = Path.Combine(folderPath, fileName);
                if (!CaptureScreenToFile(candidatePath, out message))
                {
                    // Roll the counter back so a failed capture doesn't leave a gap in
                    // the evidence sequence (001, 003, ...).
                    Interlocked.Decrement(ref _evidenceCounter);
                    return false;
                }

                fullPath = candidatePath;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                fullPath = NeverThrowsGuard.Failure("CaptureStepEvidence", ex);
                return false;
            }
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
            hash = default;
            message = default;
            try
            {
                hash = null;
                if (!TryCaptureRegionToBitmap(left, top, width, height, out Bitmap region, out message))
                    return false;

                using (region)
                {
                    try
                    {
                        using (Bitmap small = new Bitmap(region, new Size(8, 8)))
                        {
                            long[] luminance = new long[64];
                            int i = 0;
                            for (int y = 0; y < 8; y++)
                            {
                                for (int x = 0; x < 8; x++)
                                {
                                    Color c = small.GetPixel(x, y);
                                    // Weighted luma (Rec. 601) rather than a plain average -
                                    // green dominates perceived brightness, so a plain average
                                    // under-weights it and over-weights blue.
                                    luminance[i++] = (long)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
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
                    catch (Exception ex)
                    {
                        hash = null;
                        message = "Region hash computation failed: " + ex.Message;
                        return false;
                    }
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                hash = NeverThrowsGuard.Failure("GetRegionHash", ex);
                return false;
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
        /// <remarks>
        /// The actual wait can exceed <paramref name="timeoutMs"/> by up to one poll
        /// interval plus the time of a single capture+hash pass, because the timeout
        /// is checked between passes rather than pre-empting a pass in progress.
        /// </remarks>
        [Category("Capture - Verification")]
        [Description("Polls a screen region until its appearance changes, or the timeout elapses. Returns True if it changed in time; never throws.")]
        public bool WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForRegionToChange(left, top, width, height, timeoutMs, pollIntervalMs, out _, out message);
        }

        /// <summary>
        /// Same as <see cref="WaitForRegionToChange(int, int, int, int, int, int, out string)"/>,
        /// but also reports whether the wait ended because the timeout elapsed, so the
        /// automation can branch on timeout vs. execution failure without a null-message test.
        /// </summary>
        /// <param name="left">Left edge of the region in screen pixels.</param>
        /// <param name="top">Top edge of the region in screen pixels.</param>
        /// <param name="width">Region width in pixels.</param>
        /// <param name="height">Region height in pixels.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="timedOut"><c>true</c> if this method returned <c>false</c> because the timeout elapsed; <c>false</c> on success or on a real failure (check <paramref name="message"/> for the latter).</param>
        /// <param name="message"><c>null</c> if the poll completed (changed or genuinely timed out); otherwise a human-readable reason a real failure (bad dimensions) aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the region changed before the timeout; <c>false</c> if it timed out, or if a real failure aborted the poll (check <paramref name="timedOut"/>/<paramref name="message"/> to tell them apart). Never throws.</returns>
        /// <remarks>
        /// The actual wait can exceed <paramref name="timeoutMs"/> by up to one poll
        /// interval plus the time of a single capture+hash pass, because the timeout
        /// is checked between passes rather than pre-empting a pass in progress.
        /// </remarks>
        [Category("Capture - Verification")]
        [Description("Polls a screen region until its appearance changes, or the timeout elapses; reports whether the wait timed out. Returns True if it changed in time; never throws.")]
        public bool WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (timeoutMs < 0)
                {
                    message = "Timeout must not be negative.";
                    return false;
                }
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
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForRegionToChange", ex);
                return false;
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
            return CompareRegionToBaseline(left, top, width, height, baselineImagePath, tolerancePercent, out actualDifferencePercent, out _, out message);
        }

        /// <summary>
        /// Same as <see cref="CompareRegionToBaseline(int, int, int, int, string, double, out double, out string)"/>,
        /// but also reports whether the comparison actually ran to completion, so the
        /// automation can branch on out-of-tolerance vs. execution failure without a
        /// null-message test.
        /// </summary>
        /// <param name="left">Left edge of the region in screen pixels.</param>
        /// <param name="top">Top edge of the region in screen pixels.</param>
        /// <param name="width">Region width in pixels; must match the baseline image's width.</param>
        /// <param name="height">Region height in pixels; must match the baseline image's height.</param>
        /// <param name="baselineImagePath">Path to the reference image to compare against.</param>
        /// <param name="tolerancePercent">Maximum percentage of differing pixels still considered a match (0-100).</param>
        /// <param name="actualDifferencePercent">Receives the actual percentage of differing pixels found, or <c>0</c> if this method returns <c>false</c> due to a real failure (check <paramref name="message"/>).</param>
        /// <param name="comparisonCompleted"><c>true</c> if the comparison ran and produced a real difference percentage (whether within tolerance or not); <c>false</c> if a real failure prevented it (check <paramref name="message"/>).</param>
        /// <param name="message"><c>null</c> if the comparison completed (within or outside tolerance); otherwise a human-readable reason a real failure (missing baseline, size mismatch) prevented the comparison (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the actual difference is within <paramref name="tolerancePercent"/>; <c>false</c> if it isn't, or if a real failure prevented the comparison (check <paramref name="comparisonCompleted"/>/<paramref name="message"/> to tell them apart). Never throws.</returns>
        /// <remarks>
        /// Per-pixel comparison uses a small per-channel tolerance internally to absorb
        /// anti-aliasing/font-rendering noise, so it is not thrown off by single-pixel
        /// rendering jitter the way an exact byte-for-byte comparison would be.
        /// </remarks>
        [Category("Capture - Verification")]
        [Description("Compares a screen region against a saved baseline image and reports whether the difference is within tolerance, plus whether the comparison completed. Never throws.")]
        public bool CompareRegionToBaseline(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent, out bool comparisonCompleted, out string message)
        {
            actualDifferencePercent = default;
            comparisonCompleted = default;
            message = default;
            try
            {
                actualDifferencePercent = 0;

                if (double.IsNaN(tolerancePercent) || tolerancePercent < 0 || tolerancePercent > 100)
                {
                    message = "Tolerance must be a percentage between 0 and 100.";
                    return false;
                }

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
                        try
                        {
                            // LockBits/Marshal.Copy can throw (OOM, GDI failure) - surfaces
                            // as false + message instead of an exception (never-throws contract).
                            actualDifferencePercent = ComputeDifferencePercent(baseline, current);
                        }
                        catch (Exception ex)
                        {
                            message = "Region comparison failed: " + ex.Message;
                            return false;
                        }
                        comparisonCompleted = true;
                        message = null;
                        return actualDifferencePercent <= tolerancePercent;
                    }
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CompareRegionToBaseline", ex);
                return false;
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
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DrawHighlightBox", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="DrawHighlightBox(string, int, int, int, int, int, out string, int)"/>,
        /// but takes the color as RGB components (0-255 each, clamped) instead of a packed
        /// <c>0x00BBGGRR</c> colorRef, for designers who find hexadecimal entry inconvenient.
        /// </summary>
        [Category("Capture - Annotation")]
        [Description("Draws a rectangular highlight box onto a saved screenshot using RGB color components. Returns True on success; never throws.")]
        public bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int red, int green, int blue, out string message, int lineWidth = 3)
        {
            return DrawHighlightBox(imagePath, left, top, right, bottom, PackColorRef(red, green, blue), out message, lineWidth);
        }

        /// <summary>
        /// Same as <see cref="DrawHighlightBox(string, int, int, int, int, int, out string, int)"/>,
        /// but takes the color as a <see cref="System.Drawing.Color"/>, e.g.
        /// <c>Color.Red</c> or a named/system color, for designers with a <c>Color</c> proxy.
        /// </summary>
        [Category("Capture - Annotation")]
        [Description("Draws a rectangular highlight box onto a saved screenshot using a System.Drawing.Color. Returns True on success; never throws.")]
        public bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, Color color, out string message, int lineWidth = 3)
        {
            return DrawHighlightBox(imagePath, left, top, right, bottom, PackColorRef(color.R, color.G, color.B), out message, lineWidth);
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
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DrawArrowToPoint", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="DrawArrowToPoint(string, int, int, int, out string, int, int)"/>,
        /// but takes the color as RGB components (0-255 each, clamped) instead of a packed
        /// <c>0x00BBGGRR</c> colorRef, for designers who find hexadecimal entry inconvenient.
        /// </summary>
        [Category("Capture - Annotation")]
        [Description("Draws an arrow pointing at the given coordinates using RGB color components. Returns True on success; never throws.")]
        public bool DrawArrowToPoint(string imagePath, int x, int y, int red, int green, int blue, out string message, int length = 40, int lineWidth = 3)
        {
            return DrawArrowToPoint(imagePath, x, y, PackColorRef(red, green, blue), out message, length, lineWidth);
        }

        /// <summary>
        /// Same as <see cref="DrawArrowToPoint(string, int, int, int, out string, int, int)"/>,
        /// but takes the color as a <see cref="System.Drawing.Color"/>, e.g.
        /// <c>Color.Red</c> or a named/system color, for designers with a <c>Color</c> proxy.
        /// </summary>
        [Category("Capture - Annotation")]
        [Description("Draws an arrow pointing at the given coordinates using a System.Drawing.Color. Returns True on success; never throws.")]
        public bool DrawArrowToPoint(string imagePath, int x, int y, Color color, out string message, int length = 40, int lineWidth = 3)
        {
            return DrawArrowToPoint(imagePath, x, y, PackColorRef(color.R, color.G, color.B), out message, length, lineWidth);
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
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("RedactRegion", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="RedactRegion(string, int, int, int, int, out string, int)"/>,
        /// but takes the fill color as RGB components (0-255 each, clamped) instead of a
        /// packed <c>0x00BBGGRR</c> colorRef, for designers who find hexadecimal entry
        /// inconvenient.
        /// </summary>
        [Category("Capture - Annotation")]
        [Description("Fills a rectangular region of a saved screenshot with a solid RGB color to redact PII, overwriting the file in place. Returns True on success; never throws.")]
        public bool RedactRegion(string imagePath, int left, int top, int width, int height, int red, int green, int blue, out string message)
        {
            return RedactRegion(imagePath, left, top, width, height, out message, PackColorRef(red, green, blue));
        }

        /// <summary>
        /// Same as <see cref="RedactRegion(string, int, int, int, int, out string, int)"/>,
        /// but takes the fill color as a <see cref="System.Drawing.Color"/>, e.g.
        /// <c>Color.Black</c> or a named/system color, for designers with a <c>Color</c> proxy.
        /// </summary>
        [Category("Capture - Annotation")]
        [Description("Fills a rectangular region of a saved screenshot with a solid System.Drawing.Color to redact PII, overwriting the file in place. Returns True on success; never throws.")]
        public bool RedactRegion(string imagePath, int left, int top, int width, int height, Color color, out string message)
        {
            return RedactRegion(imagePath, left, top, width, height, out message, PackColorRef(color.R, color.G, color.B));
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

        /// <summary>
        /// Rejects capture regions that lie entirely outside the virtual screen (all
        /// monitors). A partially overlapping region is allowed - Windows copies the
        /// on-screen part and leaves the off-screen part black - but a region entirely
        /// off-screen would capture a solid black rectangle, so it surfaces as
        /// false + message instead.
        /// </summary>
        private static bool TryEnsureRegionOnScreen(int left, int top, int width, int height, out string message)
        {
            Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            long right = (long)left + width;
            long bottom = (long)top + height;

            if (right <= vs.Left || left >= vs.Right || bottom <= vs.Top || top >= vs.Bottom)
            {
                message = $"Capture region ({left},{top}) {width}x{height} lies entirely outside the " +
                          $"virtual screen ({vs.Left},{vs.Top}) {vs.Width}x{vs.Height}; nothing can be captured there.";
                return false;
            }

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

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // bmp.Save throws on IO failures (unauthorized access, locked target,
                // disk full) - surfaces as false + message instead of an exception.
                bmp.Save(fullPath, GetImageFormatFromExtension(fullPath));
                message = null;
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to save capture to '{filePath}': {ex.Message}";
                return false;
            }
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
                case ".tif":
                case ".tiff":
                    return ImageFormat.Tiff;
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
            string sanitized = new string(chars);
            // Cap the length so a long step name can't push the evidence path past
            // the Windows MAX_PATH limit (the counter and timestamp add ~24 chars).
            return sanitized.Length > 60 ? sanitized.Substring(0, 60) : sanitized;
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

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    // new Bitmap(Stream) fully decodes raster formats (PNG/JPEG/BMP/GIF)
                    // into memory, so disposing the stream here is safe for the inputs
                    // this component supports - no second copy is needed. It can throw
                    // ArgumentException for a file that exists but is corrupt/not an
                    // image - surfaces as false + message (never-throws contract).
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

        private static Color ColorFromColorRef(int colorRef)
        {
            int r = colorRef & 0xFF;
            int g = (colorRef >> 8) & 0xFF;
            int b = (colorRef >> 16) & 0xFF;
            return Color.FromArgb(r, g, b);
        }

        /// <summary>Packs RGB components (each clamped to 0-255) into a 0x00BBGGRR colorRef value.</summary>
        private static int PackColorRef(int red, int green, int blue)
        {
            red = Math.Clamp(red, 0, 255);
            green = Math.Clamp(green, 0, 255);
            blue = Math.Clamp(blue, 0, 255);
            return red | (green << 8) | (blue << 16);
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
                int stride = Math.Abs(dataA.Stride);
                int byteCount = stride * a.Height;
                byte[] bytesA = new byte[byteCount];
                byte[] bytesB = new byte[byteCount];
                CopyPixels(dataA, bytesA, stride, a.Height);
                CopyPixels(dataB, bytesB, stride, b.Height);

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

        /// <summary>
        /// Copies a locked bitmap's pixels into <paramref name="buffer"/> in top-down
        /// row order, flipping bottom-up (negative-stride) bitmaps so both images are
        /// compared in the same orientation. Both bitmaps are locked as 32bpp with the
        /// same dimensions, so their strides are identical.
        /// </summary>
        private static void CopyPixels(BitmapData data, byte[] buffer, int stride, int height)
        {
            if (data.Stride > 0)
            {
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            }
            else
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(data.Scan0 + (height - 1 - y) * data.Stride, buffer, y * stride, stride);
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
