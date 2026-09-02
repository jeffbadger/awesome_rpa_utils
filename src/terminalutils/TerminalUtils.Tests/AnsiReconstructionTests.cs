using System;
using TerminalAutomation;
using Xunit;

namespace TerminalAutomation.Tests
{
    /// <summary>
    /// Pure string/bit-manipulation tests for <see cref="AnsiReconstruction"/> - no
    /// P/Invoke, no live console, runs anywhere. This is the single most important part of
    /// TerminalUtils to unit-test thoroughly, since it's the one place this component's core
    /// logic can be verified without a real Windows console.
    /// </summary>
    public class AnsiReconstructionTests
    {
        private static TerminalCell Cell(char c, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor bg = ConsoleColor.Black)
        {
            return new TerminalCell(c, fg, bg);
        }

        [Fact]
        public void Render_NullCells_ReturnsEmptyStringInEitherMode()
        {
            Assert.Equal(string.Empty, AnsiReconstruction.Render(null, preserveAnsi: false));
            Assert.Equal(string.Empty, AnsiReconstruction.Render(null, preserveAnsi: true));
        }

        [Fact]
        public void Render_EmptyCells_ReturnsEmptyStringInEitherMode()
        {
            var cells = Array.Empty<TerminalCell>();

            Assert.Equal(string.Empty, AnsiReconstruction.Render(cells, preserveAnsi: false));
            Assert.Equal(string.Empty, AnsiReconstruction.Render(cells, preserveAnsi: true));
        }

        [Fact]
        public void Render_PlainMode_ReturnsCharactersOnly_NoEscapeCodes()
        {
            var cells = new[]
            {
                Cell('A', ConsoleColor.Red, ConsoleColor.Black),
                Cell('B', ConsoleColor.Green, ConsoleColor.White),
                Cell('C', ConsoleColor.Blue, ConsoleColor.Yellow)
            };

            string result = AnsiReconstruction.Render(cells, preserveAnsi: false);

            Assert.Equal("ABC", result);
            Assert.DoesNotContain('\x1b', result);
        }

        [Fact]
        public void Render_PreserveAnsi_SingleColorThroughout_EmitsOneColorCodeAndOneReset()
        {
            var cells = new[]
            {
                Cell('A', ConsoleColor.Red, ConsoleColor.Black),
                Cell('B', ConsoleColor.Red, ConsoleColor.Black),
                Cell('C', ConsoleColor.Red, ConsoleColor.Black)
            };

            string result = AnsiReconstruction.Render(cells, preserveAnsi: true);

            // Red=91 (bright), Black=30+10=40 background.
            Assert.Equal("\x1b[91;40mABC\x1b[0m", result);
        }

        [Fact]
        public void Render_PreserveAnsi_ColorChangeMidRow_EmitsNewCodeOnlyAtTheChange()
        {
            var cells = new[]
            {
                Cell('A', ConsoleColor.Red, ConsoleColor.Black),
                Cell('B', ConsoleColor.Red, ConsoleColor.Black),
                Cell('C', ConsoleColor.Green, ConsoleColor.Black)
            };

            string result = AnsiReconstruction.Render(cells, preserveAnsi: true);

            Assert.Equal("\x1b[91;40mAB\x1b[92;40mC\x1b[0m", result);
        }

        [Fact]
        public void Render_PreserveAnsi_BackgroundOnlyChange_EmitsNewCode()
        {
            var cells = new[]
            {
                Cell('A', ConsoleColor.White, ConsoleColor.Black),
                Cell('B', ConsoleColor.White, ConsoleColor.DarkBlue)
            };

            string result = AnsiReconstruction.Render(cells, preserveAnsi: true);

            Assert.Equal("\x1b[97;40mA\x1b[97;44mB\x1b[0m", result);
        }

        [Theory]
        [InlineData(ConsoleColor.Black, 30)]
        [InlineData(ConsoleColor.DarkBlue, 34)]
        [InlineData(ConsoleColor.DarkGreen, 32)]
        [InlineData(ConsoleColor.DarkCyan, 36)]
        [InlineData(ConsoleColor.DarkRed, 31)]
        [InlineData(ConsoleColor.DarkMagenta, 35)]
        [InlineData(ConsoleColor.DarkYellow, 33)]
        [InlineData(ConsoleColor.Gray, 37)]
        [InlineData(ConsoleColor.DarkGray, 90)]
        [InlineData(ConsoleColor.Blue, 94)]
        [InlineData(ConsoleColor.Green, 92)]
        [InlineData(ConsoleColor.Cyan, 96)]
        [InlineData(ConsoleColor.Red, 91)]
        [InlineData(ConsoleColor.Magenta, 95)]
        [InlineData(ConsoleColor.Yellow, 93)]
        [InlineData(ConsoleColor.White, 97)]
        public void Render_PreserveAnsi_EveryConsoleColor_MapsToTheDocumentedSgrForegroundCode(ConsoleColor color, int expectedFgCode)
        {
            var cells = new[] { Cell('X', color, ConsoleColor.Black) };

            string result = AnsiReconstruction.Render(cells, preserveAnsi: true);

            Assert.StartsWith($"\x1b[{expectedFgCode};40m", result);
        }

        [Fact]
        public void Render_PreserveAnsi_AlwaysEndsWithResetSequence()
        {
            var cells = new[] { Cell('Z') };

            string result = AnsiReconstruction.Render(cells, preserveAnsi: true);

            Assert.EndsWith("\x1b[0m", result);
        }
    }
}
