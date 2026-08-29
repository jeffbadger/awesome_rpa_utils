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
    }
}
