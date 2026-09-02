using System.Collections.Generic;
using System.Text.Json;
using SessionAutomation;
using Xunit;

namespace SessionAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for SessionUtils' input guards and the pure
    /// enum-mapping/filter-parsing logic - the paths that return before any live Windows
    /// Terminal Services or desktop P/Invoke call. This component has a much smaller
    /// Linux-testable surface than most others in this suite: nearly every method either
    /// takes no input to guard, or reaches a native call on its very first line with no
    /// input validation ahead of it (workstation lock/desktop/idle-time checks, current-
    /// session identity). Those are exercised manually against a real Windows session per
    /// the repo's TESTING.md, not here - deliberately, mirroring WindowUtils.Tests'
    /// documented avoidance of user32 P/Invoke paths on this suite's Linux-runnable tests.
    /// </summary>
    public class SessionUtilsTests
    {
        private readonly SessionUtils _session = new SessionUtils();

        // --- Guards that return before any native call ---

        [Fact]
        public void GetSessionKind_NegativeSessionId_ReturnsFalseWithMessage()
        {
            bool ok = _session.GetSessionKind(-1, out SessionKind kind, out string message);

            Assert.False(ok);
            Assert.Equal(default, kind);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetSessionConnectState_NegativeSessionId_ReturnsFalseWithMessage()
        {
            bool ok = _session.GetSessionConnectState(-1, out SessionConnectState state, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void IsSessionDisconnected_NegativeSessionId_ReturnsFalseWithMessage()
        {
            bool disconnected = _session.IsSessionDisconnected(-1, out string message);

            Assert.False(disconnected);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetSessionUser_NegativeSessionId_ReturnsFalseWithMessage()
        {
            bool ok = _session.GetSessionUser(-1, out string userName, out string domainName, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DisconnectSession_NegativeSessionId_ReturnsFalseWithMessage()
        {
            bool ok = _session.DisconnectSession(-1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void EnumerateSessionsJson_UnknownConnectStateToken_ReturnsFalseWithMessage()
        {
            bool ok = _session.EnumerateSessionsJson("NotARealState", out string json, out string message);

            Assert.False(ok);
            Assert.Null(json);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1, 100, 10)]   // negative sessionId
        [InlineData(1, -1, 10)]     // negative timeoutMs
        [InlineData(1, 100, 0)]     // non-positive pollIntervalMs
        [InlineData(1, 100, -5)]
        public void WaitForSessionConnectState_InvalidArgs_ReturnsFalseWithMessage(int sessionId, int timeoutMs, int pollIntervalMs)
        {
            bool ok = _session.WaitForSessionConnectState(sessionId, SessionConnectState.Active, timeoutMs, pollIntervalMs, out bool timedOut, out string message);

            Assert.False(ok);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1, 100, 10)]
        [InlineData(1, -1, 10)]
        [InlineData(1, 100, 0)]
        public void WaitForSessionConnectStateSimple_InvalidArgs_ReturnsFalseWithMessage(int sessionId, int timeoutMs, int pollIntervalMs)
        {
            bool ok = _session.WaitForSessionConnectStateSimple(sessionId, SessionConnectState.Active, timeoutMs, pollIntervalMs, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1, 10)]
        [InlineData(100, 0)]
        [InlineData(100, -5)]
        public void WaitForInputDesktopAvailable_InvalidArgs_ReturnsFalseWithMessage(int timeoutMs, int pollIntervalMs)
        {
            bool ok = _session.WaitForInputDesktopAvailable(timeoutMs, pollIntervalMs, out bool timedOut, out string message);

            Assert.False(ok);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1, 10)]
        [InlineData(100, 0)]
        public void WaitForInputDesktopAvailableSimple_InvalidArgs_ReturnsFalseWithMessage(int timeoutMs, int pollIntervalMs)
        {
            bool ok = _session.WaitForInputDesktopAvailableSimple(timeoutMs, pollIntervalMs, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1, 10)]
        [InlineData(100, 0)]
        [InlineData(100, -5)]
        public void WaitForWorkstationUnlocked_InvalidArgs_ReturnsFalseWithMessage(int timeoutMs, int pollIntervalMs)
        {
            bool ok = _session.WaitForWorkstationUnlocked(timeoutMs, pollIntervalMs, out bool timedOut, out string message);

            Assert.False(ok);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1, 10)]
        [InlineData(100, 0)]
        public void WaitForWorkstationUnlockedSimple_InvalidArgs_ReturnsFalseWithMessage(int timeoutMs, int pollIntervalMs)
        {
            bool ok = _session.WaitForWorkstationUnlockedSimple(timeoutMs, pollIntervalMs, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- IsSessionInteractive* is backed only by System.Environment.UserInteractive,
        //     with no P/Invoke at all, so it is genuinely portable and meaningful to test here
        //     (unlike the lock/desktop/idle-time checks, which reach a native call immediately). ---

        [Fact]
        public void IsSessionInteractiveSimple_NeverThrows()
        {
            bool interactive = _session.IsSessionInteractiveSimple(out string message);

            // The actual value depends on this process's window station, which varies by
            // test runner - only the never-throws contract and message shape are asserted.
            Assert.True(interactive ? message == null : !string.IsNullOrEmpty(message));
        }

        [Fact]
        public void IsSessionInteractive_QuerySucceededIsAlwaysTrue()
        {
            _session.IsSessionInteractive(out bool querySucceeded, out string message);

            // Environment.UserInteractive cannot fail in the way a native call can - this
            // overload's querySucceeded should always be true.
            Assert.True(querySucceeded);
        }

        // --- Pure internal mapping/parsing logic (SessionUtils.Tests has InternalsVisibleTo) ---

        [Theory]
        [InlineData(0, SessionConnectState.Active)]
        [InlineData(4, SessionConnectState.Disconnected)]
        [InlineData(9, SessionConnectState.Init)]
        public void TryToSessionConnectState_ValidRaw_MapsCorrectly(int raw, SessionConnectState expected)
        {
            bool ok = SessionUtils.TryToSessionConnectState(raw, out SessionConnectState state, out string message);

            Assert.True(ok);
            Assert.Equal(expected, state);
            Assert.Null(message);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10)]
        [InlineData(999)]
        public void TryToSessionConnectState_OutOfRangeRaw_ReturnsFalseWithMessage(int raw)
        {
            bool ok = SessionUtils.TryToSessionConnectState(raw, out SessionConnectState state, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryToSessionKind_SessionZero_IsServiceRegardlessOfProtocol()
        {
            bool ok = SessionUtils.TryToSessionKind(0, protocolType: 2, out SessionKind kind, out string message);

            Assert.True(ok);
            Assert.Equal(SessionKind.Service, kind);
            Assert.Null(message);
        }

        [Theory]
        [InlineData(0, SessionKind.Console)]
        [InlineData(2, SessionKind.Rdp)]
        [InlineData(1, SessionKind.Other)]
        [InlineData(99, SessionKind.Other)]
        public void TryToSessionKind_NonZeroSession_MapsProtocolType(int protocolType, SessionKind expected)
        {
            bool ok = SessionUtils.TryToSessionKind(1, protocolType, out SessionKind kind, out string message);

            Assert.True(ok);
            Assert.Equal(expected, kind);
            Assert.Null(message);
        }

        [Fact]
        public void TryToSessionKind_NegativeSessionId_ReturnsFalseWithMessage()
        {
            bool ok = SessionUtils.TryToSessionKind(-1, 0, out SessionKind kind, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryParseConnectStates_NullOrEmpty_MeansUnbounded()
        {
            bool ok1 = SessionUtils.TryParseConnectStates(null, out HashSet<SessionConnectState> states1, out string error1);
            bool ok2 = SessionUtils.TryParseConnectStates("   ", out HashSet<SessionConnectState> states2, out string error2);

            Assert.True(ok1);
            Assert.Null(states1);
            Assert.Null(error1);
            Assert.True(ok2);
            Assert.Null(states2);
            Assert.Null(error2);
        }

        [Fact]
        public void TryParseConnectStates_ValidCsv_ParsesCaseInsensitively()
        {
            bool ok = SessionUtils.TryParseConnectStates(" active,DISCONNECTED ", out HashSet<SessionConnectState> states, out string error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(2, states.Count);
            Assert.Contains(SessionConnectState.Active, states);
            Assert.Contains(SessionConnectState.Disconnected, states);
        }

        [Fact]
        public void TryParseConnectStates_UnknownToken_ReturnsFalseWithError()
        {
            bool ok = SessionUtils.TryParseConnectStates("Active,NotAState", out HashSet<SessionConnectState> states, out string error);

            Assert.False(ok);
            Assert.Null(states);
            Assert.False(string.IsNullOrEmpty(error));
        }

        // --- JSON shape (bypasses P/Invoke entirely) ---

        [Fact]
        public void SessionInfoData_SerializesExpectedShape()
        {
            var data = new SessionInfoData
            {
                SessionId = 2,
                WinStationName = "RDP-Tcp#0",
                ConnectState = SessionConnectState.Active,
                UserName = "jdoe",
                DomainName = "CONTOSO",
                Kind = SessionKind.Rdp
            };

            string json = JsonSerializer.Serialize(data, SessionJson.Options);

            Assert.Contains("\"SessionId\":2", json);
            Assert.Contains("\"WinStationName\":\"RDP-Tcp#0\"", json);
            Assert.Contains("\"UserName\":\"jdoe\"", json);
            Assert.Contains("\"DomainName\":\"CONTOSO\"", json);
        }
    }
}
