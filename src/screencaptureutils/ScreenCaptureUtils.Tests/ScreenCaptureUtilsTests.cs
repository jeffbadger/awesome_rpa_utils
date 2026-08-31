using System;
using System.IO;
using ScreenCaptureAutomation;
using Xunit;

namespace ScreenCaptureAutomation.Tests
{
    /// <summary>
    /// Unit tests for ScreenCaptureUtils' input guards and the never-throw contract on
    /// the paths that return before any GDI+ capture, Win32 call, or file write. Runs on
    /// Windows only (the component's UseWindowsForms pins the runtime to WindowsDesktop),
    /// so the guards here deliberately stop short of the live capture pipeline - real
    /// screen-capture and comparison coverage is the Pega Unit Test plan in the repo's
    /// TESTING.md.
    /// </summary>
    public class ScreenCaptureUtilsTests
    {
        private readonly ScreenCaptureUtils _capture = new ScreenCaptureUtils();

        // --- Region guards: non-positive dimensions rejected before any capture ---

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        [InlineData(-5, 100)]
        [InlineData(100, -5)]
        public void CaptureRegionToFile_NonPositiveDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            bool ok = _capture.CaptureRegionToFile(0, 0, width, height, "out.png", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        [InlineData(-5, 100)]
        [InlineData(100, -5)]
        public void CaptureAroundPointToFile_NonPositiveDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            bool ok = _capture.CaptureAroundPointToFile(0, 0, width, height, "out.png", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Off-screen guard: a region entirely outside the virtual screen is rejected ---

        [Fact]
        public void CaptureRegionToFile_EntirelyOffScreen_ReturnsFalseWithMessage()
        {
            // int.MaxValue is always outside the virtual screen regardless of monitor layout.
            bool ok = _capture.CaptureRegionToFile(int.MaxValue, int.MaxValue, 100, 100, "out.png", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- CaptureStepEvidence: null/empty step name or folder rejected before any capture ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void CaptureStepEvidence_NullOrEmptyStepName_ReturnsFalseWithMessage(string stepName)
        {
            bool ok = _capture.CaptureStepEvidence(stepName, Path.GetTempPath(), out string fullPath, out string message);

            Assert.False(ok);
            Assert.Null(fullPath);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void CaptureStepEvidence_NullOrEmptyFolder_ReturnsFalseWithMessage(string folderPath)
        {
            bool ok = _capture.CaptureStepEvidence("Step 1", folderPath, out string fullPath, out string message);

            Assert.False(ok);
            Assert.Null(fullPath);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- WaitForRegionToChange: negative timeout rejected before any capture ---

        [Fact]
        public void WaitForRegionToChange_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool changed = _capture.WaitForRegionToChange(0, 0, 100, 100, timeoutMs: -1, pollIntervalMs: 10, out string message);

            Assert.False(changed);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForRegionToChangeWithTimedOut_NegativeTimeout_ReturnsFalseWithoutTimeout()
        {
            bool changed = _capture.WaitForRegionToChange(0, 0, 100, 100, timeoutMs: -1, pollIntervalMs: 10, out bool timedOut, out string message);

            // A negative timeout is invalid input, not a clean timeout.
            Assert.False(changed);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Annotation/redaction: empty/inverted rect and missing image rejected ---

        [Fact]
        public void DrawHighlightBox_EmptyOrInvertedRect_ReturnsFalseWithMessage()
        {
            bool ok = _capture.DrawHighlightBox("any.png", 10, 10, 10, 20, 0x0000FF, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DrawHighlightBoxRgb_EmptyOrInvertedRect_ReturnsFalseWithMessage()
        {
            bool ok = _capture.DrawHighlightBox("any.png", 10, 10, 10, 20, red: 0, green: 0, blue: 255, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DrawHighlightBoxColor_EmptyOrInvertedRect_ReturnsFalseWithMessage()
        {
            bool ok = _capture.DrawHighlightBox("any.png", 10, 10, 10, 20, System.Drawing.Color.Blue, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        [InlineData(-5, 100)]
        [InlineData(100, -5)]
        public void RedactRegion_NonPositiveDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            bool ok = _capture.RedactRegion("any.png", 0, 0, width, height, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        public void RedactRegionRgb_NonPositiveDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            bool ok = _capture.RedactRegion("any.png", 0, 0, width, height, red: 0, green: 0, blue: 0, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        public void RedactRegionColor_NonPositiveDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            bool ok = _capture.RedactRegion("any.png", 0, 0, width, height, System.Drawing.Color.Black, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DrawHighlightBox_MissingImage_ReturnsFalseWithMessage()
        {
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            bool ok = _capture.DrawHighlightBox(missing, 10, 10, 50, 50, 0x0000FF, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DrawArrowToPoint_MissingImage_ReturnsFalseWithMessage()
        {
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            bool ok = _capture.DrawArrowToPoint(missing, 10, 10, 0x0000FF, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DrawArrowToPointRgb_MissingImage_ReturnsFalseWithMessage()
        {
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            bool ok = _capture.DrawArrowToPoint(missing, 10, 10, red: 0, green: 0, blue: 255, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DrawArrowToPointColor_MissingImage_ReturnsFalseWithMessage()
        {
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            bool ok = _capture.DrawArrowToPoint(missing, 10, 10, System.Drawing.Color.Blue, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void RedactRegion_MissingImage_ReturnsFalseWithMessage()
        {
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            bool ok = _capture.RedactRegion(missing, 0, 0, 10, 10, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- CompareRegionToBaseline: bad tolerance and missing baseline rejected ---

        [Theory]
        [InlineData(-1.0)]
        [InlineData(101.0)]
        [InlineData(double.NaN)]
        public void CompareRegionToBaseline_OutOfRangeTolerance_ReturnsFalseWithMessage(double tolerancePercent)
        {
            bool ok = _capture.CompareRegionToBaseline(0, 0, 100, 100, "baseline.png", tolerancePercent, out double actual, out string message);

            Assert.False(ok);
            Assert.Equal(0.0, actual);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void CompareRegionToBaseline_MissingBaseline_ReturnsFalseWithMessage()
        {
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            bool ok = _capture.CompareRegionToBaseline(0, 0, 100, 100, missing, 5.0, out double actual, out string message);

            Assert.False(ok);
            Assert.Equal(0.0, actual);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(101.0)]
        public void CompareRegionToBaselineWithCompleted_OutOfRangeTolerance_ReturnsFalseWithoutCompleting(double tolerancePercent)
        {
            bool ok = _capture.CompareRegionToBaseline(0, 0, 100, 100, "baseline.png", tolerancePercent, out double actual, out bool comparisonCompleted, out string message);

            Assert.False(ok);
            Assert.False(comparisonCompleted);
            Assert.Equal(0.0, actual);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void CompareRegionToBaselineWithCompleted_MissingBaseline_ReturnsFalseWithoutCompleting()
        {
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            bool ok = _capture.CompareRegionToBaseline(0, 0, 100, 100, missing, 5.0, out double actual, out bool comparisonCompleted, out string message);

            Assert.False(ok);
            Assert.False(comparisonCompleted);
            Assert.Equal(0.0, actual);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- CaptureWindowToFile: an invalid handle is false + message, never an exception ---

        [Fact]
        public void CaptureWindowToFile_InvalidHandle_ReturnsFalseWithMessage()
        {
            bool ok = _capture.CaptureWindowToFile(IntPtr.Zero, "out.png", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Component lifecycle: construct + dispose is safe and side-effect free ---

        [Fact]
        public void Dispose_Smoke_CompletesWithoutThrowing()
        {
            using (var capture = new ScreenCaptureUtils())
            {
            }
        }
    }
}
