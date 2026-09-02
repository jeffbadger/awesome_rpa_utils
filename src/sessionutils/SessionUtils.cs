using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace SessionAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that reports on and acts on Windows session/
    /// workstation state on the local machine: session identity and kind (console/RDP/
    /// service), session enumeration and connect state, logged-on user, workstation lock
    /// state, input-desktop availability, idle time, and deliberate lock/disconnect actions.
    /// <para>
    /// Like every component in this suite, all methods honor the never-throws contract:
    /// invalid input (a negative session ID, a negative timeout) and runtime failures
    /// (an inaccessible session, insufficient rights) return <c>false</c> with a
    /// descriptive message instead of throwing.
    /// </para>
    /// <para>
    /// Scope: local machine only (no remote-session support), and does not include any
    /// login, unlock, or credential-handling capability - that is intentionally out of
    /// scope for this component given its security implications. <see cref="WaitForWorkstationUnlocked"/>
    /// only observes an externally-driven unlock; it never performs one.
    /// </para>
    /// </summary>
    [Description("Reports on and acts on Windows session/workstation state: session identity/kind, " +
                 "enumeration, connect state, lock/desktop availability, idle time, and deliberate " +
                 "lock/disconnect actions. All methods return True/False with a failure message instead " +
                 "of throwing. Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class SessionUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public SessionUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public SessionUtils(IContainer container)
        {
            container?.Add(this);
        }

        #region Session Identity

        /// <summary>Gets the session ID the calling process is actually running in. Never throws.</summary>
        /// <param name="sessionId">The session ID, or 0 (a valid session ID) or unset on failure.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - Identity")]
        [Description("Gets the session ID the calling process is actually running in. Never throws.")]
        public bool GetCurrentSessionId(out int sessionId, out string message)
        {
            sessionId = default;
            message = default;
            try
            {
                if (!ProcessIdToSessionId(GetCurrentProcessId(), out uint sid))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "ProcessIdToSessionId failed.").Message;
                    return false;
                }
                sessionId = (int)sid;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetCurrentSessionId", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the session ID attached to the machine's physical console. This is a
        /// different question from <see cref="GetCurrentSessionId"/> - a bot running over
        /// RDP is not in the console session, and conflating the two gives the wrong answer.
        /// Never throws.
        /// </summary>
        /// <param name="sessionId">The console session ID, or unset if none.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - Identity")]
        [Description("Gets the session ID attached to the machine's physical console. Never throws.")]
        public bool GetActiveConsoleSessionId(out int sessionId, out string message)
        {
            sessionId = default;
            message = default;
            try
            {
                uint sid = WTSGetActiveConsoleSessionId();
                if (sid == 0xFFFFFFFF)
                {
                    message = "No session is currently attached to the physical console.";
                    return false;
                }
                sessionId = (int)sid;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetActiveConsoleSessionId", ex);
                return false;
            }
        }

        /// <summary>Returns <c>true</c> if the calling process's session is the one attached to the physical console. Never throws.</summary>
        /// <param name="message"><c>null</c> when on the console; otherwise a human-readable reason (not on console, or the check failed).</param>
        [Category("Session - Identity")]
        [Description("Returns True if the calling process's session is the one attached to the physical console. Never throws.")]
        public bool IsCurrentSessionOnConsole(out string message)
        {
            try
            {
                if (!GetCurrentSessionId(out int currentId, out message))
                    return false;
                if (!GetActiveConsoleSessionId(out int consoleId, out message))
                    return false;
                bool onConsole = currentId == consoleId;
                message = onConsole ? null : $"Current session {currentId} is not the console session ({consoleId}).";
                return onConsole;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsCurrentSessionOnConsole", ex);
                return false;
            }
        }

        /// <summary>Gets the calling process's own session kind (Console/RDP/Service). Never throws.</summary>
        /// <param name="kind">The session kind on success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - Identity")]
        [Description("Gets the calling process's own session kind: Console, Rdp, Service, or Other. Never throws.")]
        public bool GetCurrentSessionKind(out SessionKind kind, out string message)
        {
            kind = default;
            try
            {
                if (!GetCurrentSessionId(out int sessionId, out message))
                    return false;
                return GetSessionKind(sessionId, out kind, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetCurrentSessionKind", ex);
                kind = default;
                return false;
            }
        }

        /// <summary>
        /// Returns <c>true</c> if the calling process is running in Session 0 - the isolated,
        /// non-interactive session every Windows service runs in since Vista. This is the
        /// single most common unattended-automation failure this component targets: a bot
        /// running as a plain Windows service has zero desktop access, full stop, regardless
        /// of anything else this component reports. Never throws.
        /// </summary>
        /// <param name="message"><c>null</c> when running as a service session; otherwise a human-readable reason it isn't (or the check failed).</param>
        [Category("Session - Identity")]
        [Description("Returns True if the calling process is running in Session 0 (an isolated Windows service session with no desktop access). Never throws.")]
        public bool IsRunningAsServiceSession(out string message)
        {
            try
            {
                if (!GetCurrentSessionId(out int sessionId, out message))
                    return false;
                bool isService = sessionId == 0;
                message = isService ? null : $"Session {sessionId} is not Session 0 - not running as an isolated service session.";
                return isService;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsRunningAsServiceSession", ex);
                return false;
            }
        }

        #endregion

        #region Session State & Enumeration

        /// <summary>Gets an arbitrary session's kind (Console/RDP/Service). Never throws.</summary>
        /// <param name="sessionId">The session ID to check.</param>
        /// <param name="kind">The session kind on success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - State")]
        [Description("Gets an arbitrary session's kind: Console, Rdp, Service, or Other. Never throws.")]
        public bool GetSessionKind(int sessionId, out SessionKind kind, out string message)
        {
            kind = default;
            message = default;
            try
            {
                if (sessionId < 0)
                {
                    message = "sessionId must be zero or a positive session ID.";
                    return false;
                }

                int protocolType = 0;
                if (sessionId != 0)
                {
                    if (!WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, sessionId, WTS_INFO_CLASS.WTSClientProtocolType, out IntPtr buffer, out _))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), $"WTSQuerySessionInformationW(WTSClientProtocolType) failed for session {sessionId}.").Message;
                        return false;
                    }
                    try
                    {
                        protocolType = Marshal.ReadInt16(buffer);
                    }
                    finally
                    {
                        WTSFreeMemory(buffer);
                    }
                }

                return TryToSessionKind(sessionId, protocolType, out kind, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetSessionKind", ex);
                return false;
            }
        }

        /// <summary>Gets the connect state of the calling process's own session. Never throws.</summary>
        /// <param name="state">The connect state on success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - State")]
        [Description("Gets the connect state of the calling process's own session. Never throws.")]
        public bool GetCurrentSessionConnectState(out SessionConnectState state, out string message)
        {
            state = default;
            try
            {
                if (!GetCurrentSessionId(out int sessionId, out message))
                    return false;
                return GetSessionConnectState(sessionId, out state, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetCurrentSessionConnectState", ex);
                return false;
            }
        }

        /// <summary>Gets an arbitrary session's connect state. Never throws.</summary>
        /// <param name="sessionId">The session ID to check.</param>
        /// <param name="state">The connect state on success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - State")]
        [Description("Gets an arbitrary session's connect state. Never throws.")]
        public bool GetSessionConnectState(int sessionId, out SessionConnectState state, out string message)
        {
            state = default;
            message = default;
            try
            {
                if (sessionId < 0)
                {
                    message = "sessionId must be zero or a positive session ID.";
                    return false;
                }

                if (!WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, sessionId, WTS_INFO_CLASS.WTSConnectState, out IntPtr buffer, out _))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), $"WTSQuerySessionInformationW(WTSConnectState) failed for session {sessionId}.").Message;
                    return false;
                }

                int raw;
                try
                {
                    raw = Marshal.ReadInt32(buffer);
                }
                finally
                {
                    WTSFreeMemory(buffer);
                }

                return TryToSessionConnectState(raw, out state, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetSessionConnectState", ex);
                return false;
            }
        }

        /// <summary>Returns <c>true</c> if the calling process's own session is disconnected (the literal "RDP disconnect" state). Never throws.</summary>
        /// <param name="message"><c>null</c> when disconnected; otherwise a human-readable reason it isn't (or the check failed).</param>
        [Category("Session - State")]
        [Description("Returns True if the calling process's own session is disconnected. Never throws.")]
        public bool IsCurrentSessionDisconnected(out string message)
        {
            try
            {
                if (!GetCurrentSessionId(out int sessionId, out message))
                    return false;
                return IsSessionDisconnected(sessionId, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsCurrentSessionDisconnected", ex);
                return false;
            }
        }

        /// <summary>Returns <c>true</c> if an arbitrary session is disconnected (the literal "RDP disconnect" state). Never throws.</summary>
        /// <param name="sessionId">The session ID to check.</param>
        /// <param name="message"><c>null</c> when disconnected; otherwise a human-readable reason it isn't (or the check failed).</param>
        [Category("Session - State")]
        [Description("Returns True if an arbitrary session is disconnected. Never throws.")]
        public bool IsSessionDisconnected(int sessionId, out string message)
        {
            try
            {
                if (!GetSessionConnectState(sessionId, out SessionConnectState state, out message))
                    return false;
                bool disconnected = state == SessionConnectState.Disconnected;
                message = disconnected ? null : $"Session {sessionId} is {state}, not Disconnected.";
                return disconnected;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsSessionDisconnected", ex);
                return false;
            }
        }

        /// <summary>Gets the logged-on user/domain for the calling process's own session. Never throws.</summary>
        /// <param name="userName">The user name on success (empty string if none, e.g. Session 0).</param>
        /// <param name="domainName">The domain name on success (empty string if none).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - State")]
        [Description("Gets the logged-on user/domain for the calling process's own session. Never throws.")]
        public bool GetCurrentSessionUser(out string userName, out string domainName, out string message)
        {
            userName = default;
            domainName = default;
            try
            {
                if (!GetCurrentSessionId(out int sessionId, out message))
                    return false;
                return GetSessionUser(sessionId, out userName, out domainName, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetCurrentSessionUser", ex);
                return false;
            }
        }

        /// <summary>Gets the logged-on user/domain for an arbitrary session. Never throws.</summary>
        /// <param name="sessionId">The session ID to check.</param>
        /// <param name="userName">The user name on success (empty string if none, e.g. Session 0).</param>
        /// <param name="domainName">The domain name on success (empty string if none).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - State")]
        [Description("Gets the logged-on user/domain for an arbitrary session. Never throws.")]
        public bool GetSessionUser(int sessionId, out string userName, out string domainName, out string message)
        {
            userName = default;
            domainName = default;
            message = default;
            try
            {
                if (sessionId < 0)
                {
                    message = "sessionId must be zero or a positive session ID.";
                    return false;
                }

                if (!TryQuerySessionString(sessionId, WTS_INFO_CLASS.WTSUserName, out userName, out message))
                    return false;
                if (!TryQuerySessionString(sessionId, WTS_INFO_CLASS.WTSDomainName, out domainName, out message))
                    return false;

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetSessionUser", ex);
                return false;
            }
        }

        /// <summary>
        /// Enumerates every session on the local machine, as a JSON array of session
        /// summaries, newest information wins per query. Never throws.
        /// </summary>
        /// <param name="connectStateFilter">Comma-separated <see cref="SessionConnectState"/> names to include (e.g. "Active,Disconnected"). Null/empty means every state.</param>
        /// <param name="json">A JSON array of session summaries on success; unset otherwise.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason (including an unrecognized <paramref name="connectStateFilter"/> token).</param>
        [Category("Session - State")]
        [Description("Enumerates every session on the local machine as a JSON array, optionally filtered by connect state. Never throws.")]
        public bool EnumerateSessionsJson(string connectStateFilter, out string json, out string message)
        {
            json = default;
            message = default;
            IntPtr sessionInfoPtr = IntPtr.Zero;
            try
            {
                if (!TryParseConnectStates(connectStateFilter, out HashSet<SessionConnectState> filter, out message))
                    return false;

                if (!WTSEnumerateSessionsW(WTS_CURRENT_SERVER_HANDLE, 0, 1, out sessionInfoPtr, out int count))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "WTSEnumerateSessionsW failed.").Message;
                    return false;
                }

                var results = new List<SessionInfoData>();
                int elementSize = Marshal.SizeOf<WTS_SESSION_INFO>();
                for (int i = 0; i < count; i++)
                {
                    IntPtr elementPtr = IntPtr.Add(sessionInfoPtr, i * elementSize);
                    WTS_SESSION_INFO raw = Marshal.PtrToStructure<WTS_SESSION_INFO>(elementPtr);

                    // Skip an entry with an unrecognized native state rather than fail the whole enumeration.
                    if (!TryToSessionConnectState(raw.State, out SessionConnectState state, out _))
                        continue;
                    if (filter != null && !filter.Contains(state))
                        continue;

                    // Per-session enrichment failures are non-fatal for the enumeration as a
                    // whole - a session that vanished mid-enumeration just gets blank fields.
                    GetSessionKind(raw.SessionId, out SessionKind kind, out _);
                    GetSessionUser(raw.SessionId, out string userName, out string domainName, out _);

                    results.Add(new SessionInfoData
                    {
                        SessionId = raw.SessionId,
                        WinStationName = raw.pWinStationName,
                        ConnectState = state,
                        UserName = userName,
                        DomainName = domainName,
                        Kind = kind
                    });
                }

                json = JsonSerializer.Serialize(results, SessionJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("EnumerateSessionsJson", ex);
                return false;
            }
            finally
            {
                // Freed exactly once for the whole buffer, even if the loop above threw partway through.
                if (sessionInfoPtr != IntPtr.Zero)
                    WTSFreeMemory(sessionInfoPtr);
            }
        }

        #endregion

        #region Desktop & Lock State

        /// <summary>Returns <c>true</c> if the workstation is locked, via the authoritative session-flags check. Never throws.</summary>
        /// <param name="message"><c>null</c> when locked; otherwise a human-readable reason it isn't (or the check failed).</param>
        [Category("Session - Desktop")]
        [Description("Returns True if the workstation is locked. Never throws.")]
        public bool IsWorkstationLockedSimple(out string message)
        {
            return TryIsWorkstationLocked(out _, out message);
        }

        /// <summary>Same as <see cref="IsWorkstationLockedSimple"/>, plus a <c>querySucceeded</c> output separating "the query completed" from "the workstation is locked". Never throws.</summary>
        /// <param name="querySucceeded"><c>true</c> if the underlying check ran without an unexpected failure (whether or not the workstation turned out to be locked); <c>false</c> only for a genuine unexpected failure.</param>
        /// <param name="message"><c>null</c> when locked; otherwise a human-readable reason it isn't (or the check failed).</param>
        [Category("Session - Desktop")]
        [Description("Returns True if the workstation is locked, plus whether the query itself succeeded. Never throws.")]
        public bool IsWorkstationLocked(out bool querySucceeded, out string message)
        {
            return TryIsWorkstationLocked(out querySucceeded, out message);
        }

        /// <summary>Returns <c>true</c> if the calling process's session is interactive (has a visible window station). Never throws.</summary>
        /// <param name="message"><c>null</c> when interactive; otherwise a human-readable reason it isn't (typically Session 0).</param>
        [Category("Session - Desktop")]
        [Description("Returns True if the calling process's session is interactive. Never throws.")]
        public bool IsSessionInteractiveSimple(out string message)
        {
            return TryIsSessionInteractive(out _, out message);
        }

        /// <summary>Same as <see cref="IsSessionInteractiveSimple"/>, plus a <c>querySucceeded</c> output. Never throws.</summary>
        /// <param name="querySucceeded"><c>true</c> if the underlying check ran without an unexpected failure; <c>false</c> only for a genuine unexpected failure.</param>
        /// <param name="message"><c>null</c> when interactive; otherwise a human-readable reason it isn't.</param>
        [Category("Session - Desktop")]
        [Description("Returns True if the calling process's session is interactive, plus whether the query itself succeeded. Never throws.")]
        public bool IsSessionInteractive(out bool querySucceeded, out string message)
        {
            return TryIsSessionInteractive(out querySucceeded, out message);
        }

        /// <summary>
        /// Returns <c>true</c> if the default interactive desktop is currently receiving
        /// input. This is a narrower, different question from <see cref="IsWorkstationLockedSimple"/>:
        /// a UAC consent prompt or the Ctrl+Alt+Del screen also switches the input desktop
        /// away from "Default" without the workstation being locked, so "unavailable" here
        /// is the correct answer during those states, not a defect. Never throws.
        /// </summary>
        /// <param name="message"><c>null</c> when available; otherwise a human-readable reason it isn't (locked, a secure-desktop prompt, or no desktop at all in a service session).</param>
        [Category("Session - Desktop")]
        [Description("Returns True if the default interactive desktop is currently receiving input. Never throws.")]
        public bool IsInputDesktopAvailableSimple(out string message)
        {
            return TryIsInputDesktopAvailable(out _, out message);
        }

        /// <summary>Same as <see cref="IsInputDesktopAvailableSimple"/>, plus a <c>querySucceeded</c> output. Never throws.</summary>
        /// <param name="querySucceeded"><c>true</c> if the underlying check ran without an unexpected failure (an inaccessible desktop, e.g. from a service session, still counts as a succeeded query); <c>false</c> only for a genuine unexpected failure.</param>
        /// <param name="message"><c>null</c> when available; otherwise a human-readable reason it isn't.</param>
        [Category("Session - Desktop")]
        [Description("Returns True if the default interactive desktop is currently receiving input, plus whether the query itself succeeded. Never throws.")]
        public bool IsInputDesktopAvailable(out bool querySucceeded, out string message)
        {
            return TryIsInputDesktopAvailable(out querySucceeded, out message);
        }

        #endregion

        #region Idle Time

        /// <summary>
        /// Gets the number of milliseconds since the last keyboard/mouse input to the
        /// calling process's own session. This only reflects local input to that session -
        /// another session's idle time cannot be observed this way. Never throws.
        /// </summary>
        /// <param name="idleMilliseconds">Milliseconds since the last input, on success.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - IdleTime")]
        [Description("Gets milliseconds since the last local keyboard/mouse input. Never throws.")]
        public bool GetIdleTimeMilliseconds(out long idleMilliseconds, out string message)
        {
            idleMilliseconds = default;
            message = default;
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "GetLastInputInfo failed.").Message;
                    return false;
                }

                // GetLastInputInfo's dwTime is a 32-bit tick count (the GetTickCount domain,
                // not GetTickCount64), so it wraps roughly every 49.7 days while GetTickCount64
                // never does. Truncating the 64-bit tick count to 32 bits before subtracting
                // lets unsigned wraparound produce the right answer as long as the true idle
                // time is under that ~49.7-day window - subtracting the raw 64-bit value
                // directly would silently be wrong once uptime exceeds it.
                uint currentTick32 = unchecked((uint)GetTickCount64());
                idleMilliseconds = unchecked(currentTick32 - info.dwTime);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetIdleTimeMilliseconds", ex);
                return false;
            }
        }

        #endregion

        #region Wait

        /// <summary>Polls until an arbitrary session reaches the expected connect state, or the timeout elapses. Never throws.</summary>
        /// <param name="sessionId">The session ID to poll.</param>
        /// <param name="expectedState">The connect state to wait for.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds. Zero means check once, immediately.</param>
        /// <param name="pollIntervalMs">Delay between polls, in milliseconds. Must be positive.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed before the expected state was reached.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure or timeout reason.</param>
        [Category("Session - Wait")]
        [Description("Polls until a session reaches the expected connect state, or the timeout elapses. Never throws.")]
        public bool WaitForSessionConnectState(int sessionId, SessionConnectState expectedState, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (sessionId < 0)
                {
                    message = "sessionId must be zero or a positive session ID.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must be zero or positive.";
                    return false;
                }
                if (pollIntervalMs <= 0)
                {
                    message = "pollIntervalMs must be positive.";
                    return false;
                }

                var stopwatch = Stopwatch.StartNew();
                while (true)
                {
                    if (!GetSessionConnectState(sessionId, out SessionConnectState state, out message))
                        return false;
                    if (state == expectedState)
                    {
                        message = null;
                        timedOut = false;
                        return true;
                    }
                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        timedOut = true;
                        message = $"Timed out after {timeoutMs}ms waiting for session {sessionId} to reach {expectedState} (last observed: {state}).";
                        return false;
                    }
                    Thread.Sleep(RemainingSleep(pollIntervalMs, timeoutMs, stopwatch));
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForSessionConnectState", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForSessionConnectState"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("Session - Wait")]
        [Description("Polls until a session reaches the expected connect state, or the timeout elapses. Never throws.")]
        public bool WaitForSessionConnectStateSimple(int sessionId, SessionConnectState expectedState, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForSessionConnectState(sessionId, expectedState, timeoutMs, pollIntervalMs, out _, out message);
        }

        /// <summary>Polls until the default interactive desktop becomes available, or the timeout elapses. Never throws.</summary>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds. Zero means check once, immediately.</param>
        /// <param name="pollIntervalMs">Delay between polls, in milliseconds. Must be positive.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed before the desktop became available.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure or timeout reason.</param>
        [Category("Session - Wait")]
        [Description("Polls until the default interactive desktop becomes available, or the timeout elapses. Never throws.")]
        public bool WaitForInputDesktopAvailable(int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must be zero or positive.";
                    return false;
                }
                if (pollIntervalMs <= 0)
                {
                    message = "pollIntervalMs must be positive.";
                    return false;
                }

                var stopwatch = Stopwatch.StartNew();
                while (true)
                {
                    bool available = TryIsInputDesktopAvailable(out bool querySucceeded, out string checkMessage);
                    if (!querySucceeded)
                    {
                        message = checkMessage;
                        return false;
                    }
                    if (available)
                    {
                        message = null;
                        timedOut = false;
                        return true;
                    }
                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        timedOut = true;
                        message = $"Timed out after {timeoutMs}ms waiting for the input desktop to become available.";
                        return false;
                    }
                    Thread.Sleep(RemainingSleep(pollIntervalMs, timeoutMs, stopwatch));
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForInputDesktopAvailable", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForInputDesktopAvailable"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("Session - Wait")]
        [Description("Polls until the default interactive desktop becomes available, or the timeout elapses. Never throws.")]
        public bool WaitForInputDesktopAvailableSimple(int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForInputDesktopAvailable(timeoutMs, pollIntervalMs, out _, out message);
        }

        /// <summary>
        /// Polls until the workstation is unlocked, or the timeout elapses. This only
        /// observes an externally-driven unlock (someone else unlocking it) - it never
        /// performs an unlock itself. Never throws.
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds. Zero means check once, immediately.</param>
        /// <param name="pollIntervalMs">Delay between polls, in milliseconds. Must be positive.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed before the workstation was unlocked.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure or timeout reason.</param>
        [Category("Session - Wait")]
        [Description("Polls until the workstation is unlocked (by external action), or the timeout elapses. Never throws.")]
        public bool WaitForWorkstationUnlocked(int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must be zero or positive.";
                    return false;
                }
                if (pollIntervalMs <= 0)
                {
                    message = "pollIntervalMs must be positive.";
                    return false;
                }

                var stopwatch = Stopwatch.StartNew();
                while (true)
                {
                    bool locked = TryIsWorkstationLocked(out bool querySucceeded, out string checkMessage);
                    if (!querySucceeded)
                    {
                        message = checkMessage;
                        return false;
                    }
                    if (!locked)
                    {
                        message = null;
                        timedOut = false;
                        return true;
                    }
                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        timedOut = true;
                        message = $"Timed out after {timeoutMs}ms waiting for the workstation to be unlocked.";
                        return false;
                    }
                    Thread.Sleep(RemainingSleep(pollIntervalMs, timeoutMs, stopwatch));
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForWorkstationUnlocked", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForWorkstationUnlocked"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("Session - Wait")]
        [Description("Polls until the workstation is unlocked (by external action), or the timeout elapses. Never throws.")]
        public bool WaitForWorkstationUnlockedSimple(int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForWorkstationUnlocked(timeoutMs, pollIntervalMs, out _, out message);
        }

        #endregion

        #region Actions

        /// <summary>
        /// Locks the workstation. This only works from the calling process's own
        /// interactive session, and immediately ends that session's desktop interaction -
        /// any later steps in the same automation run that need the desktop will fail until
        /// someone unlocks it again. Never throws.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - Actions")]
        [Description("Locks the workstation. Only works from the calling process's own interactive session. Ends desktop interaction for the rest of the automation run. Never throws.")]
        public bool LockWorkstation(out string message)
        {
            message = default;
            try
            {
                if (!LockWorkStationNative())
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "LockWorkStation failed - this only works from the calling process's own interactive session.").Message;
                    return false;
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("LockWorkstation", ex);
                return false;
            }
        }

        /// <summary>
        /// Disconnects an arbitrary session (not logoff). Disconnecting your own session
        /// needs no special privilege; disconnecting another session typically requires
        /// administrator rights. Never throws.
        /// </summary>
        /// <param name="sessionId">The session ID to disconnect.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - Actions")]
        [Description("Disconnects an arbitrary session (not logoff). Disconnecting another session typically requires administrator rights. Never throws.")]
        public bool DisconnectSession(int sessionId, out string message)
        {
            message = default;
            try
            {
                if (sessionId < 0)
                {
                    message = "sessionId must be zero or a positive session ID.";
                    return false;
                }
                if (!WTSDisconnectSession(WTS_CURRENT_SERVER_HANDLE, sessionId, false))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), $"WTSDisconnectSession failed for session {sessionId} - disconnecting a session other than your own typically requires administrator rights.").Message;
                    return false;
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DisconnectSession", ex);
                return false;
            }
        }

        /// <summary>Disconnects the calling process's own session (not logoff). Never throws.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable failure reason.</param>
        [Category("Session - Actions")]
        [Description("Disconnects the calling process's own session (not logoff). Never throws.")]
        public bool DisconnectCurrentSession(out string message)
        {
            try
            {
                if (!GetCurrentSessionId(out int sessionId, out message))
                    return false;
                return DisconnectSession(sessionId, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DisconnectCurrentSession", ex);
                return false;
            }
        }

        #endregion

        #region Internal Helpers

        private const int UOI_NAME = 2;
        private const uint DESKTOP_READOBJECTS = 0x0001;
        private const int DesktopNameBufferBytes = 256;

        private const int WTS_SESSIONSTATE_LOCK = 0;
        private const int WTS_SESSIONSTATE_UNLOCK = 1;

        private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;
        private const int WTS_CURRENT_SESSION = -1;

        private enum WTS_INFO_CLASS
        {
            WTSInitialProgram = 0,
            WTSApplicationName = 1,
            WTSWorkingDirectory = 2,
            WTSOEMId = 3,
            WTSSessionId = 4,
            WTSUserName = 5,
            WTSWinStationName = 6,
            WTSDomainName = 7,
            WTSConnectState = 8,
            WTSClientBuildNumber = 9,
            WTSClientName = 10,
            WTSClientDirectory = 11,
            WTSClientProductId = 12,
            WTSClientHardwareId = 13,
            WTSClientAddress = 14,
            WTSClientDisplay = 15,
            WTSClientProtocolType = 16,
            WTSIdleTime = 17,
            WTSLogonTime = 18,
            WTSIncomingBytes = 19,
            WTSOutgoingBytes = 20,
            WTSIncomingFrames = 21,
            WTSOutgoingFrames = 22,
            WTSClientInfo = 23,
            WTSSessionInfo = 24,
            WTSSessionInfoEx = 25
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public int SessionId;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pWinStationName;
            public int State;
        }

        /// <summary>
        /// Only the leading DWORD-sized fields of the native WTSINFOEX/WTSINFOEX_LEVEL1
        /// union are modeled here (Level, SessionId, SessionState, SessionFlags) - the
        /// fields after them are fixed-size character arrays whose exact lengths are
        /// inconsistently documented across sources, and this component only ever needs
        /// SessionFlags. Marshal.PtrToStructure only reads sizeof(WTSINFOEX_HEADER) bytes
        /// from the returned buffer, so the real (larger) native struct's tail is simply
        /// never touched - safe regardless of its true layout.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct WTSINFOEX_HEADER
        {
            public int Level;
            public int SessionId;
            public int SessionState;
            public int SessionFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformationW(IntPtr hObj, int nIndex, IntPtr pvInfo, uint nLength, out uint lpnLengthNeeded);

        [DllImport("user32.dll", EntryPoint = "LockWorkStation", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LockWorkStationNative();

        [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSEnumerateSessionsW(IntPtr hServer, int Reserved, int Version, out IntPtr ppSessionInfo, out int pCount);

        [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQuerySessionInformationW(IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("wtsapi32.dll", EntryPoint = "WTSDisconnectSession", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSDisconnectSession(IntPtr hServer, int sessionId, [MarshalAs(UnmanagedType.Bool)] bool bWait);

        private static bool TryQuerySessionString(int sessionId, WTS_INFO_CLASS infoClass, out string value, out string message)
        {
            value = default;
            message = default;
            if (!WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, sessionId, infoClass, out IntPtr buffer, out _))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), $"WTSQuerySessionInformationW({infoClass}) failed for session {sessionId}.").Message;
                return false;
            }
            try
            {
                value = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                return true;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        private bool TryIsWorkstationLocked(out bool querySucceeded, out string message)
        {
            querySucceeded = false;
            message = default;
            try
            {
                if (!WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, WTS_CURRENT_SESSION, WTS_INFO_CLASS.WTSSessionInfoEx, out IntPtr buffer, out _))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "WTSQuerySessionInformationW(WTSSessionInfoEx) failed.").Message;
                    return false;
                }

                WTSINFOEX_HEADER header;
                try
                {
                    header = Marshal.PtrToStructure<WTSINFOEX_HEADER>(buffer);
                }
                finally
                {
                    WTSFreeMemory(buffer);
                }

                querySucceeded = true;
                switch (header.SessionFlags)
                {
                    case WTS_SESSIONSTATE_LOCK:
                        message = null;
                        return true;
                    case WTS_SESSIONSTATE_UNLOCK:
                        message = "The workstation is not locked.";
                        return false;
                    default:
                        // WTS_SESSIONSTATE_UNKNOWN (-1) or an unrecognized value - Windows itself
                        // has no definitive opinion right now. Report the conservative default
                        // (not locked) rather than treating this as a query failure.
                        message = $"The workstation lock state could not be determined (raw flag {header.SessionFlags}); reporting not locked.";
                        return false;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsWorkstationLocked", ex);
                querySucceeded = false;
                return false;
            }
        }

        private bool TryIsSessionInteractive(out bool querySucceeded, out string message)
        {
            querySucceeded = false;
            message = default;
            try
            {
                bool interactive = Environment.UserInteractive;
                querySucceeded = true;
                message = interactive ? null : "The current session is not interactive (no visible window station - typically a Windows service running in Session 0).";
                return interactive;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsSessionInteractive", ex);
                querySucceeded = false;
                return false;
            }
        }

        private bool TryIsInputDesktopAvailable(out bool querySucceeded, out string message)
        {
            querySucceeded = false;
            message = default;
            IntPtr hDesktop = IntPtr.Zero;
            try
            {
                hDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
                if (hDesktop == IntPtr.Zero)
                {
                    // Failing to open the input desktop at all IS the answer for a
                    // service/Session-0 caller (or a secure-desktop state that denies access) -
                    // that's a succeeded query, not a failure of this component.
                    querySucceeded = true;
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "OpenInputDesktop failed - the input desktop is not accessible from this session right now (a service session, a locked workstation, or a secure-desktop prompt such as UAC).").Message;
                    return false;
                }

                IntPtr nameBuffer = Marshal.AllocHGlobal(DesktopNameBufferBytes);
                try
                {
                    if (!GetUserObjectInformationW(hDesktop, UOI_NAME, nameBuffer, DesktopNameBufferBytes, out _))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), "GetUserObjectInformationW failed.").Message;
                        return false;
                    }

                    string name = Marshal.PtrToStringUni(nameBuffer) ?? string.Empty;
                    querySucceeded = true;
                    bool available = string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase);
                    message = available ? null : $"The input desktop is currently \"{name}\", not \"Default\" - a lock screen or a secure-desktop prompt (e.g. UAC) is likely showing.";
                    return available;
                }
                finally
                {
                    Marshal.FreeHGlobal(nameBuffer);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsInputDesktopAvailable", ex);
                querySucceeded = false;
                return false;
            }
            finally
            {
                if (hDesktop != IntPtr.Zero)
                    CloseDesktop(hDesktop);
            }
        }

        private static int RemainingSleep(int pollIntervalMs, int timeoutMs, Stopwatch stopwatch)
        {
            long remaining = timeoutMs - stopwatch.ElapsedMilliseconds;
            return (int)Math.Max(0, Math.Min(pollIntervalMs, remaining));
        }

        /// <summary>
        /// Maps a raw <c>WTS_CONNECTSTATE_CLASS</c> integer to <see cref="SessionConnectState"/>.
        /// Pure mapping logic, exposed internally so it can be unit-tested without any live
        /// Windows Terminal Services call.
        /// </summary>
        internal static bool TryToSessionConnectState(int raw, out SessionConnectState state, out string message)
        {
            message = null;
            if (raw < (int)SessionConnectState.Active || raw > (int)SessionConnectState.Init)
            {
                state = default;
                message = $"Unrecognized session connect state value {raw}.";
                return false;
            }
            state = (SessionConnectState)raw;
            return true;
        }

        /// <summary>
        /// Maps a session ID and a raw <c>WTSClientProtocolType</c> value to <see cref="SessionKind"/>
        /// (0=Console, 2=RDP, anything else=Other; session ID 0 always short-circuits to
        /// Service regardless of protocol type, since Session 0 has no client protocol at
        /// all). Pure mapping logic, exposed internally so it can be unit-tested without any
        /// live Windows Terminal Services call.
        /// </summary>
        internal static bool TryToSessionKind(int sessionId, int protocolType, out SessionKind kind, out string message)
        {
            message = null;
            if (sessionId < 0)
            {
                kind = default;
                message = "sessionId must be zero or a positive session ID.";
                return false;
            }
            if (sessionId == 0)
            {
                kind = SessionKind.Service;
                return true;
            }
            switch (protocolType)
            {
                case 0:
                    kind = SessionKind.Console;
                    return true;
                case 2:
                    kind = SessionKind.Rdp;
                    return true;
                default:
                    kind = SessionKind.Other;
                    return true;
            }
        }

        /// <summary>
        /// Parses a comma-separated list of <see cref="SessionConnectState"/> names. Null/
        /// empty/whitespace-only input means "no filter" (<paramref name="states"/> is set to
        /// <c>null</c>, not an empty set). Returns <c>false</c> with an error for any
        /// unrecognized token.
        /// </summary>
        internal static bool TryParseConnectStates(string connectStateFilterCsv, out HashSet<SessionConnectState> states, out string error)
        {
            states = null;
            error = null;
            if (string.IsNullOrWhiteSpace(connectStateFilterCsv))
                return true;

            var parsed = new HashSet<SessionConnectState>();
            foreach (string token in connectStateFilterCsv.Split(','))
            {
                string trimmed = token.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (!Enum.TryParse(trimmed, true, out SessionConnectState state))
                {
                    error = $"Unknown session connect state '{trimmed}'. Valid values: {string.Join(", ", Enum.GetNames(typeof(SessionConnectState)))}.";
                    return false;
                }
                parsed.Add(state);
            }

            states = parsed.Count == 0 ? null : parsed;
            return true;
        }

        #endregion
    }
}
