using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using WindowAutomation;
using Xunit;

namespace WindowAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for WindowUtils' input guards, plus the
    /// never-throw contract on the paths that return before any Win32 call. These
    /// run anywhere (including non-Windows CI shells), so they deliberately avoid
    /// user32 P/Invoke paths — live window behavior (enumerate/move/close real
    /// windows) is covered by the Pega Unit Test plan in the repo's TESTING.md.
    /// </summary>
    public class WindowUtilsTests
    {
        private readonly WindowUtils _window = new WindowUtils();

        // --- FindWindowByTitle: null/empty title is "not found", never a wildcard match or exception ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void FindWindowByTitle_NullOrEmptyTitle_ReturnsZero(string title)
        {
            Assert.Equal(IntPtr.Zero, _window.FindWindowByTitle(title));
            Assert.Equal(IntPtr.Zero, _window.FindWindowByTitle(title, exactMatch: false));
        }

        // --- WaitForWindow: null/empty title refuses to poll (pointless — matches nothing) ---

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void WaitForWindowSimple_NullOrEmptyTitle_ReturnsFalseWithZeroHandle(string title, bool _)
        {
            bool found = _window.WaitForWindowSimple(title, timeoutMs: 5000, pollIntervalMs: 10, out IntPtr hWnd);

            Assert.False(found);
            Assert.Equal(IntPtr.Zero, hWnd);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForWindowWithMessage_NullOrEmptyTitle_ReturnsFalseWithMessage(string title)
        {
            bool found = _window.WaitForWindow(title, timeoutMs: 5000, pollIntervalMs: 10, out IntPtr hWnd, out string message);

            Assert.False(found);
            Assert.Equal(IntPtr.Zero, hWnd);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Try* getters: a zero handle is always invalid, so False + message on every
        //     platform - on Windows this is IsWindow(IntPtr.Zero) == false; on a non-Windows
        //     host without user32.dll, the P/Invoke itself fails and is caught by the
        //     never-throws contract, which also produces False + message. ---

        [Fact]
        public void TryGetWindowTitle_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_window.TryGetWindowTitle(IntPtr.Zero, out string title, out string message));
            Assert.Null(title);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetWindowClassName_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_window.TryGetWindowClassName(IntPtr.Zero, out string className, out string message));
            Assert.Null(className);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetWindowProcessId_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_window.TryGetWindowProcessId(IntPtr.Zero, out int processId, out string message));
            Assert.Equal(0, processId);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- SetWindowBounds: negative dimensions rejected before any Win32 call, with a message ---

        [Theory]
        [InlineData(-1, 100)]
        [InlineData(100, -1)]
        [InlineData(-1, -1)]
        public void SetWindowBounds_NegativeDimensions_ReturnsFalseWithMessage(int width, int height)
        {
            Assert.False(_window.SetWindowBounds(IntPtr.Zero, left: 10, top: 10, width, height, out string message));

            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Window state / enabled: a zero handle is always invalid ---

        [Fact]
        public void TryGetWindowState_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_window.TryGetWindowState(IntPtr.Zero, out WindowDisplayState state, out string message));
            Assert.Equal(WindowDisplayState.Normal, state);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetWindowEnabled_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_window.TryGetWindowEnabled(IntPtr.Zero, out bool enabled, out string message));
            Assert.False(enabled);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void PlainStateGetters_ZeroHandle_ReturnFalse()
        {
            Assert.False(_window.IsWindowEnabled(IntPtr.Zero));
            Assert.False(_window.IsWindowMinimized(IntPtr.Zero));
            Assert.False(_window.IsWindowMaximized(IntPtr.Zero));
        }

        [Theory]
        [InlineData(false, false, WindowDisplayState.Normal)]
        [InlineData(false, true, WindowDisplayState.Maximized)]
        [InlineData(true, false, WindowDisplayState.Minimized)]
        // A window minimized from maximized keeps the maximized flag; minimized must win.
        [InlineData(true, true, WindowDisplayState.Minimized)]
        public void ToDisplayState_MapsIconicAndZoomedFlags(bool isIconic, bool isZoomed, WindowDisplayState expected)
        {
            Assert.Equal(expected, WindowUtils.ToDisplayState(isIconic, isZoomed));
        }

        // --- Regex lookup: argument guards, before any window is enumerated ---

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("", null)]
        public void TryFindWindowByRegex_NoPattern_ReturnsFalseWithMessage(string titlePattern, string classNamePattern)
        {
            Assert.False(_window.TryFindWindowByRegex(titlePattern, classNamePattern, out IntPtr hWnd, out string message));

            Assert.Equal(IntPtr.Zero, hWnd);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData("(unclosed", null, "titlePattern")]
        [InlineData(null, "[bad", "classNamePattern")]
        [InlineData("fine", "(unclosed", "classNamePattern")]
        public void TryFindWindowByRegex_InvalidPattern_ReturnsFalseWithMessageNamingTheParameter(string titlePattern, string classNamePattern, string expectedParameter)
        {
            Assert.False(_window.TryFindWindowByRegex(titlePattern, classNamePattern, out IntPtr hWnd, out string message));

            Assert.Equal(IntPtr.Zero, hWnd);
            Assert.Contains(expectedParameter, message);
        }

        [Fact]
        public void TryFindChildWindowByRegex_ZeroParent_ReturnsFalseWithMessage()
        {
            Assert.False(_window.TryFindChildWindowByRegex(IntPtr.Zero, "anything", null, out IntPtr hWnd, out string message));

            Assert.Equal(IntPtr.Zero, hWnd);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryBuildRegexFilters_CapsEveryMatchAtOneSecond()
        {
            Assert.True(WindowUtils.TryBuildRegexFilters("a", "b", ignoreCase: true, out Regex title, out Regex cls, out string message));

            Assert.Null(message);
            Assert.Equal(TimeSpan.FromSeconds(1), title.MatchTimeout);
            Assert.Equal(TimeSpan.FromSeconds(1), cls.MatchTimeout);
        }

        [Fact]
        public void TryBuildRegexFilters_EmptyPatternLeavesThatAxisUnfiltered()
        {
            Assert.True(WindowUtils.TryBuildRegexFilters("a", "", ignoreCase: true, out Regex title, out Regex cls, out _));

            Assert.NotNull(title);
            Assert.Null(cls);
        }

        // --- Regex matching semantics, against a fake set of windows (no live desktop) ---

        private static readonly Dictionary<IntPtr, (string Title, string Class)> Fake = new Dictionary<IntPtr, (string, string)>
        {
            [new IntPtr(1)] = ("Invoice 4471 - Notepad", "Notepad"),
            [new IntPtr(2)] = ("Settings", "ApplicationFrameWindow"),
            [new IntPtr(3)] = ("", "WindowsForms10.Window.8.app.0.141b42a_r14_ad1"),
            [new IntPtr(4)] = ("Invoice 9 - Notepad", "WindowsForms10.Window.8.app.0.141b42a_r15_ad1"),
        };

        private static IntPtr Find(string titlePattern, string classNamePattern, bool ignoreCase = true)
        {
            Assert.True(WindowUtils.TryBuildRegexFilters(titlePattern, classNamePattern, ignoreCase, out Regex title, out Regex cls, out _));
            return WindowUtils.FindFirstMatching(Fake.Keys, title, cls, h => Fake[h].Title, h => Fake[h].Class);
        }

        [Fact]
        public void FindFirstMatching_TitleOnly_MatchesEmbeddedChangingValue()
        {
            Assert.Equal(new IntPtr(1), Find(@"^Invoice \d+ - Notepad$", null));
        }

        [Fact]
        public void FindFirstMatching_ClassOnly_MatchesPerRunSuffix()
        {
            Assert.Equal(new IntPtr(3), Find(null, @"^WindowsForms10\.Window\.8\.app\.0\.[0-9a-f]+_r\d+_ad1$"));
        }

        [Fact]
        public void FindFirstMatching_BothPatterns_RequireBothToMatch()
        {
            // Window 1 matches the title but not the class; window 4 matches both.
            Assert.Equal(new IntPtr(4), Find("Invoice", "WindowsForms10"));
        }

        [Fact]
        public void FindFirstMatching_ReturnsFirstInEnumerationOrder()
        {
            Assert.Equal(new IntPtr(1), Find("Invoice", null));
        }

        [Fact]
        public void FindFirstMatching_NoMatch_ReturnsZero()
        {
            Assert.Equal(IntPtr.Zero, Find("does not exist", null));
        }

        [Fact]
        public void FindFirstMatching_IgnoreCaseControlsCaseSensitivity()
        {
            Assert.Equal(new IntPtr(2), Find("^SETTINGS$", null, ignoreCase: true));
            Assert.Equal(IntPtr.Zero, Find("^SETTINGS$", null, ignoreCase: false));
        }

        [Fact]
        public void FindFirstMatching_UnfilteredAxisIsNeverRead()
        {
            Assert.True(WindowUtils.TryBuildRegexFilters(null, "Notepad", true, out Regex title, out Regex cls, out _));

            IntPtr found = WindowUtils.FindFirstMatching(Fake.Keys, title, cls,
                h => throw new InvalidOperationException("title must not be read for a class-only search"),
                h => Fake[h].Class);

            Assert.Equal(new IntPtr(1), found);
        }

        // --- EnumerateWindowsJson ---

        [Fact]
        public void EnumerateWindowsJson_NegativeProcessId_ReturnsFalseWithMessage()
        {
            Assert.False(_window.EnumerateWindowsJson(out string json, out string message, visibleOnly: true, processId: -1));

            Assert.Null(json);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WindowJson_EmptyList_IsEmptyArray()
        {
            Assert.Equal("[]", WindowJson.Serialize(new List<WindowInfoData>()));
        }

        [Fact]
        public void WindowJson_SerializesEveryPropertyWithReadableState()
        {
            var window = new WindowInfoData
            {
                Handle = 197066,
                Title = "Invoice \"4471\" - Notepad",
                ClassName = "Notepad",
                ProcessId = 4321,
                IsVisible = true,
                IsEnabled = false,
                State = WindowDisplayState.Minimized,
                Left = -32000,
                Top = -32000,
                Width = 160,
                Height = 28
            };

            using JsonDocument doc = JsonDocument.Parse(WindowJson.Serialize(new[] { window }));
            JsonElement entry = Assert.Single(doc.RootElement.EnumerateArray());

            Assert.Equal(197066, entry.GetProperty("Handle").GetInt64());
            Assert.Equal("Invoice \"4471\" - Notepad", entry.GetProperty("Title").GetString());
            Assert.Equal("Notepad", entry.GetProperty("ClassName").GetString());
            Assert.Equal(4321, entry.GetProperty("ProcessId").GetInt32());
            Assert.True(entry.GetProperty("IsVisible").GetBoolean());
            Assert.False(entry.GetProperty("IsEnabled").GetBoolean());
            Assert.Equal("Minimized", entry.GetProperty("State").GetString());
            Assert.Equal(-32000, entry.GetProperty("Left").GetInt32());
            Assert.Equal(-32000, entry.GetProperty("Top").GetInt32());
            Assert.Equal(160, entry.GetProperty("Width").GetInt32());
            Assert.Equal(28, entry.GetProperty("Height").GetInt32());
        }

        // --- WindowDisplayState / ShowWindowCommand ---

        [Fact]
        public void WindowDisplayState_HasTheDocumentedMembers()
        {
            Assert.Equal(new[] { "Normal", "Minimized", "Maximized" }, Enum.GetNames(typeof(WindowDisplayState)));
        }

        // --- ShowWindowCommand must keep its Win32 SW_* values (ShowWindow receives the raw int) ---

        [Fact]
        public void ShowWindowCommand_ValuesMatchWin32Constants()
        {
            Assert.Equal(0, (int)ShowWindowCommand.Hide);        // SW_HIDE
            Assert.Equal(1, (int)ShowWindowCommand.Normal);      // SW_SHOWNORMAL
            Assert.Equal(3, (int)ShowWindowCommand.Maximized);   // SW_SHOWMAXIMIZED
            Assert.Equal(6, (int)ShowWindowCommand.Minimized);   // SW_MINIMIZE
            Assert.Equal(9, (int)ShowWindowCommand.Restore);     // SW_RESTORE
        }
    }
}