using System;
using SessionAutomation;

namespace Exerciser.Menus
{
    /// <summary>
    /// SessionUtils' menu. Most methods here genuinely need a live interactive
    /// Windows session (and several need a second concurrent session, an RDP
    /// connection, a lock, a UAC prompt, or a real Windows service) - see the
    /// "SessionUtils reminder" entry in the Setup/Cleanup menu before using this.
    /// Simple-suffixed wrapper overloads are omitted; their non-Simple siblings
    /// cover the same ground with more output.
    /// </summary>
    internal static class SessionMenu
    {
        internal static MenuItem[] Build(SessionUtils session)
        {
            return new[]
            {
                new MenuItem("GetCurrentSessionId", "No input.", () =>
                {
                    bool ok = session.GetCurrentSessionId(out int sessionId, out string message);
                    Report.Result(ok, message, ("sessionId", sessionId));
                }),
                new MenuItem("GetActiveConsoleSessionId", "No input.", () =>
                {
                    bool ok = session.GetActiveConsoleSessionId(out int sessionId, out string message);
                    Report.Result(ok, message, ("sessionId", sessionId));
                }),
                new MenuItem("IsCurrentSessionOnConsole", "No input.", () =>
                {
                    bool ok = session.IsCurrentSessionOnConsole(out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("GetCurrentSessionKind", "Rdp specifically needs an actual RDP session, not just the console.", () =>
                {
                    bool ok = session.GetCurrentSessionKind(out SessionKind kind, out string message);
                    Report.Result(ok, message, ("kind", kind));
                }),
                new MenuItem("IsRunningAsServiceSession", "Needs a real Windows service (Session 0) to exercise the true path.", () =>
                {
                    bool ok = session.IsRunningAsServiceSession(out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("GetSessionKind", "Needs a second concurrent session for a session other than your own; a made-up ID returns false + message.", () =>
                {
                    int sessionId = Prompt.Int("Session ID", 0);
                    bool ok = session.GetSessionKind(sessionId, out SessionKind kind, out string message);
                    Report.Result(ok, message, ("kind", kind));
                }),
                new MenuItem("GetCurrentSessionConnectState", "No input.", () =>
                {
                    bool ok = session.GetCurrentSessionConnectState(out SessionConnectState state, out string message);
                    Report.Result(ok, message, ("state", state));
                }),
                new MenuItem("GetSessionConnectState", "Needs a second concurrent session.", () =>
                {
                    int sessionId = Prompt.Int("Session ID", 0);
                    bool ok = session.GetSessionConnectState(sessionId, out SessionConnectState state, out string message);
                    Report.Result(ok, message, ("state", state));
                }),
                new MenuItem("IsCurrentSessionDisconnected", "No input.", () =>
                {
                    bool ok = session.IsCurrentSessionDisconnected(out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("IsSessionDisconnected", "Needs a second concurrent session; a made-up/negative session ID returns false + message, never an exception.", () =>
                {
                    int sessionId = Prompt.Int("Session ID", 0);
                    bool ok = session.IsSessionDisconnected(sessionId, out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("GetCurrentSessionUser", "No input.", () =>
                {
                    bool ok = session.GetCurrentSessionUser(out string userName, out string domainName, out string message);
                    Report.Result(ok, message, ("userName", userName), ("domainName", domainName));
                }),
                new MenuItem("GetSessionUser", "Needs a second concurrent session.", () =>
                {
                    int sessionId = Prompt.Int("Session ID", 0);
                    bool ok = session.GetSessionUser(sessionId, out string userName, out string domainName, out string message);
                    Report.Result(ok, message, ("userName", userName), ("domainName", domainName));
                }),
                new MenuItem("EnumerateSessionsJson", "connectStateFilter is a comma-separated SessionConnectState list; blank means every state.", () =>
                {
                    string filter = Prompt.String("Connect state filter (blank for all)", null);
                    bool ok = session.EnumerateSessionsJson(filter, out string json, out string message);
                    Report.Result(ok, message, ("json", json));
                }),
                new MenuItem("IsWorkstationLockedSimple", "Lock with Win+L during the test and confirm true.", () =>
                {
                    bool ok = session.IsWorkstationLockedSimple(out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("IsWorkstationLocked", "Lock with Win+L and confirm true; separately trigger a UAC prompt and confirm it stays false (per the component README's Notes & Caveats).", () =>
                {
                    bool ok = session.IsWorkstationLocked(out bool querySucceeded, out string message);
                    Report.Result(ok, message, ("querySucceeded", querySucceeded));
                }),
                new MenuItem("IsSessionInteractiveSimple", "Needs a real Windows service (Session 0) to exercise the false path; console/RDP sessions only exercise true.", () =>
                {
                    bool ok = session.IsSessionInteractiveSimple(out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("IsSessionInteractive", "Needs a real Windows service (Session 0) to exercise the false path.", () =>
                {
                    bool ok = session.IsSessionInteractive(out bool querySucceeded, out string message);
                    Report.Result(ok, message, ("querySucceeded", querySucceeded));
                }),
                new MenuItem("IsInputDesktopAvailableSimple", "Needs both a lock and a UAC/Ctrl+Alt+Del trigger to exercise both 'unavailable' paths.", () =>
                {
                    bool ok = session.IsInputDesktopAvailableSimple(out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("IsInputDesktopAvailable", "Needs both a lock and a UAC/Ctrl+Alt+Del trigger; a Session-0 service context exercises the 'no desktop at all' path.", () =>
                {
                    bool ok = session.IsInputDesktopAvailable(out bool querySucceeded, out string message);
                    Report.Result(ok, message, ("querySucceeded", querySucceeded));
                }),
                new MenuItem("GetIdleTimeMilliseconds", "Manual, timing-sensitive - idle the session for a known number of seconds first, then assert within a reasonable tolerance.", () =>
                {
                    bool ok = session.GetIdleTimeMilliseconds(out long idleMilliseconds, out string message);
                    Report.Result(ok, message, ("idleMilliseconds", idleMilliseconds));
                }),
                new MenuItem("GetCurrentSessionUptime", "Confirm against how long you've actually been logged on; call again a few seconds later and confirm it increased.", () =>
                {
                    bool ok = session.GetCurrentSessionUptime(out long milliseconds, out string message);
                    Report.Result(ok, message, ("milliseconds", milliseconds));
                }),
                new MenuItem("GetSystemUptime", "No live-session dependency. Confirm against systeminfo/net stats srv; call again later and confirm it increased; also worth checking across a sleep/hibernate cycle.", () =>
                {
                    bool ok = session.GetSystemUptime(out long milliseconds, out string message);
                    Report.Result(ok, message, ("milliseconds", milliseconds));
                }),
                new MenuItem("WaitForSessionConnectState", "Found-in-time case: trigger the state change from a second session/RDP client partway through the wait.", () =>
                {
                    int sessionId = Prompt.Int("Session ID", 0);
                    SessionConnectState expected = Prompt.Enum<SessionConnectState>("Expected state");
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    int pollIntervalMs = Prompt.Int("Poll interval ms", 500);
                    bool ok = session.WaitForSessionConnectState(sessionId, expected, timeoutMs, pollIntervalMs, out bool timedOut, out string message);
                    Report.Result(ok, message, ("timedOut", timedOut));
                }),
                new MenuItem("WaitForInputDesktopAvailable", "Found-in-time case: dismiss the lock/UAC trigger partway through the wait.", () =>
                {
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    int pollIntervalMs = Prompt.Int("Poll interval ms", 500);
                    bool ok = session.WaitForInputDesktopAvailable(timeoutMs, pollIntervalMs, out bool timedOut, out string message);
                    Report.Result(ok, message, ("timedOut", timedOut));
                }),
                new MenuItem("WaitForWorkstationUnlocked", "Lock with Win+L, then unlock partway through the wait for the found-in-time case.", () =>
                {
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    int pollIntervalMs = Prompt.Int("Poll interval ms", 500);
                    bool ok = session.WaitForWorkstationUnlocked(timeoutMs, pollIntervalMs, out bool timedOut, out string message);
                    Report.Result(ok, message, ("timedOut", timedOut));
                }),
                new MenuItem("LockWorkstation", "DISRUPTIVE - locks your desktop immediately. Run from a disposable/secondary RDP session, never your primary console.", () =>
                {
                    if (!Prompt.Bool("This locks your workstation right now. Continue?", false))
                    {
                        Console.WriteLine("Cancelled.");
                        return;
                    }
                    bool ok = session.LockWorkstation(out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("DisconnectSession", "DISRUPTIVE - never disconnect the primary session; needs a disposable second RDP session, and admin rights to disconnect someone else's.", () =>
                {
                    int sessionId = Prompt.Int("Session ID", 0);
                    if (!Prompt.Bool($"This disconnects session {sessionId} right now. Continue?", false))
                    {
                        Console.WriteLine("Cancelled.");
                        return;
                    }
                    bool ok = session.DisconnectSession(sessionId, out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("DisconnectCurrentSession", "DISRUPTIVE - disconnects the session you're running this from. Run from a disposable/secondary RDP session.", () =>
                {
                    if (!Prompt.Bool("This disconnects your CURRENT session right now. Continue?", false))
                    {
                        Console.WriteLine("Cancelled.");
                        return;
                    }
                    bool ok = session.DisconnectCurrentSession(out string message);
                    Report.Result(ok, message);
                })
            };
        }
    }
}
