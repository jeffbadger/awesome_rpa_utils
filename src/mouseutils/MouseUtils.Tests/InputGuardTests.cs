using System;
using MouseAutomation;
using Xunit;

namespace MouseAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for MouseUtils' input guards, plus the
    /// never-throw contract on the paths that return before any user32/gdi32
    /// call. These run anywhere (including non-Windows CI shells), so they
    /// deliberately stay behind the null/empty-path, inverted-rect, out-of-range
    /// fraction, and out-of-range double-click-time guards - live cursor/input
    /// injection against a real desktop is covered by the Pega Unit Test plan in
    /// the repo's TESTING.md.
    /// </summary>
    public class MouseUtilsTests
    {
        private readonly MouseUtils _mouse = new MouseUtils();

        // --- SetCursorFromFile: null/empty/whitespace path: false + message, never an exception ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SetCursorFromFile_NullOrEmptyPath_ReturnsFalseWithMessage(string filePath)
        {
            bool ok = _mouse.SetCursorFromFile(SystemCursorType.Arrow, filePath, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SetCursorFromFile_NonExistentPath_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.SetCursorFromFile(SystemCursorType.Arrow, "Z:\\definitely\\not\\a\\cursor.cur", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- ClipCursor: inverted/empty rectangle: false + message, never an exception ---

        [Theory]
        [InlineData(100, 100, 100, 200)] // right == left
        [InlineData(100, 100, 50, 200)]  // right < left
        public void ClipCursor_RightNotGreaterThanLeft_ReturnsFalseWithMessage(int left, int top, int right, int bottom)
        {
            bool ok = _mouse.ClipCursor(left, top, right, bottom, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(100, 100, 200, 100)] // bottom == top
        [InlineData(100, 100, 200, 50)]  // bottom < top
        public void ClipCursor_BottomNotGreaterThanTop_ReturnsFalseWithMessage(int left, int top, int right, int bottom)
        {
            bool ok = _mouse.ClipCursor(left, top, right, bottom, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- ClickAtRelativePosition: fraction outside [0.0, 1.0]: false + message, never an exception ---

        [Theory]
        [InlineData(-0.1, 0.5)]
        [InlineData(1.1, 0.5)]
        public void ClickAtRelativePosition_OutOfRangeXFraction_ReturnsFalseWithMessage(double xFraction, double yFraction)
        {
            bool ok = _mouse.ClickAtRelativePosition(IntPtr.Zero, xFraction, yFraction, MouseButton.Left, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0.5, -0.1)]
        [InlineData(0.5, 1.1)]
        public void ClickAtRelativePosition_OutOfRangeYFraction_ReturnsFalseWithMessage(double xFraction, double yFraction)
        {
            bool ok = _mouse.ClickAtRelativePosition(IntPtr.Zero, xFraction, yFraction, MouseButton.Left, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- ClickAtRelativePosition: non-finite fraction: false + message, never an exception ---
        // Regression test: every comparison against NaN is false in C#, so the
        // [0.0, 1.0] range check above previously let NaN sail straight through.

        [Theory]
        [InlineData(double.NaN, 0.5)]
        [InlineData(double.PositiveInfinity, 0.5)]
        [InlineData(double.NegativeInfinity, 0.5)]
        public void ClickAtRelativePosition_NonFiniteXFraction_ReturnsFalseWithMessage(double xFraction, double yFraction)
        {
            bool ok = _mouse.ClickAtRelativePosition(IntPtr.Zero, xFraction, yFraction, MouseButton.Left, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0.5, double.NaN)]
        [InlineData(0.5, double.PositiveInfinity)]
        [InlineData(0.5, double.NegativeInfinity)]
        public void ClickAtRelativePosition_NonFiniteYFraction_ReturnsFalseWithMessage(double xFraction, double yFraction)
        {
            bool ok = _mouse.ClickAtRelativePosition(IntPtr.Zero, xFraction, yFraction, MouseButton.Left, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- SafeClickAt: zero expected handle must never authorize a click ---
        // Regression test: without this guard, a point over empty desktop (where
        // GetWindowAtPoint also returns IntPtr.Zero) would make actual ==
        // expectedWindowHandle trivially true, defeating the misclick guard entirely.

        [Fact]
        public void SafeClickAt_ZeroExpectedWindowHandle_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.SafeClickAt(0, 0, MouseButton.Left, IntPtr.Zero, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- SetDoubleClickTimeMs: out-of-range value: false + message, never an exception ---

        [Theory]
        [InlineData(-1)]
        [InlineData(5001)]
        public void SetDoubleClickTimeMs_OutOfRange_ReturnsFalseWithMessage(int milliseconds)
        {
            bool ok = _mouse.SetDoubleClickTimeMs(milliseconds, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Validation tightening: reject invalid numeric input instead of silently
        // coercing it. Every check below returns before any native call, so these are
        // safe to test the same way as every other guard test in this file. ---

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void SmoothMoveTo_StepsBelowOne_ReturnsFalseWithMessage(int steps)
        {
            bool ok = _mouse.SmoothMoveTo(0, 0, steps, 5, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SmoothMoveTo_NegativeDelay_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.SmoothMoveTo(0, 0, 25, -1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void JiggleMouse_PixelsBelowOne_ReturnsFalseWithMessage(int pixels)
        {
            bool ok = _mouse.JiggleMouse(out string message, pixels);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ClickAndHold_NegativeHoldMilliseconds_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.ClickAndHold(MouseButton.Left, -1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DragAndHold_NegativeHoldMilliseconds_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.DragAndHold(0, 0, 10, 10, -1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MoveMouseBezier_DurationBelowOne_ReturnsFalseWithMessage(int durationMs)
        {
            bool ok = _mouse.MoveMouseBezier(0, 0, out string message, durationMs);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ScrollUp_NegativeNotches_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.ScrollUp(-1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ScrollDown_NegativeNotches_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.ScrollDown(-1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ScrollRight_NegativeNotches_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.ScrollRight(-1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ScrollLeft_NegativeNotches_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.ScrollLeft(-1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ClickWithRetry_MaxAttemptsBelowOne_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.ClickWithRetry(0, 0, MouseButton.Left, 0, 10, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ClickWithRetry_NegativeRetryDelay_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.ClickWithRetry(0, 0, MouseButton.Left, 3, -1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(0, 3, 200, 3)]  // radius
        [InlineData(30, 0, 200, 3)] // flashes
        [InlineData(30, 3, 0, 3)]   // flashMs
        [InlineData(30, 3, 200, 0)] // ringWidth
        public void FlashCursorHighlight_ParameterBelowOne_ReturnsFalseWithMessage(int radius, int flashes, int flashMs, int ringWidth)
        {
            bool ok = _mouse.FlashCursorHighlight(out string message, radius, flashes, flashMs, ringWidth);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ClickWithModifiers_UndefinedModifierBits_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.ClickWithModifiers(MouseButton.Left, (ModifierKeys)0x8, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void RubberBandSelect_UndefinedModifierBits_ReturnsFalseWithMessage()
        {
            bool ok = _mouse.RubberBandSelect(0, 0, 10, 10, (ModifierKeys)0x8, 30, 10, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
