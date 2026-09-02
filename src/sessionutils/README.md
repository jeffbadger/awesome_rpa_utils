# SessionAutomation

A Pega Robot Studio-ready component (`SessionUtils`) that reports on and acts
on Windows session/workstation state on the local machine: session identity
and kind (console/RDP/service), session enumeration and connect state,
logged-on user, workstation lock state, input-desktop availability, idle
time, and deliberate lock/disconnect actions. Like every component in this
suite, it honors the never-throws contract: invalid input and runtime
failures return `False` with a descriptive message instead of throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `SessionAutomation`
- Assembly: `SessionAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

**Not to be confused with `EventUtils`/`EventLogUtils`** - this component is
exclusively about Windows Terminal Services session state
(`WTSQuerySessionInformation`/`WTSEnumerateSessions`) and desktop/session
Win32 APIs, not window messages or the Event Log.

**Scope:** local machine only (no remote-server support), and does not
include any login, unlock, or credential-handling capability - that is
intentionally out of scope given its security implications.
[`WaitForWorkstationUnlocked`](#wait) only *observes* an externally-driven
unlock; it never performs one.

## Types

### `SessionConnectState`
A repository-owned mirror of the native `WTS_CONNECTSTATE_CLASS` enumeration
(same member order/values), so Pega does not need a reference to any Windows
Terminal Services assembly to select or read a connect state: `Active`,
`Connected`, `ConnectQuery`, `Shadow`, `Disconnected`, `Idle`, `Listen`,
`Reset`, `Down`, `Init`.

### `SessionKind`
How a session is connected: `Console`, `Rdp`, `Service` (Session 0 - an
isolated Windows service session with no interactive desktop), `Other`
(a protocol other than console/RDP, e.g. Citrix ICA - rare on stock Windows).

### `SessionInfoData`
The JSON shape produced by `EnumerateSessionsJson`: `SessionId`,
`WinStationName`, `ConnectState`, `UserName`, `DomainName`, `Kind`.

## Constructors

| Constructor | Description |
|---|---|
| `SessionUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `SessionUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Identity

| Method | Signature | Description |
|---|---|---|
| `GetCurrentSessionId` | `bool GetCurrentSessionId(out int sessionId, out string message)` | The session ID the calling process is actually running in. |
| `GetActiveConsoleSessionId` | `bool GetActiveConsoleSessionId(out int sessionId, out string message)` | The session attached to the machine's physical console - a different question from the current session; a bot over RDP is not on the console. |
| `IsCurrentSessionOnConsole` | `bool IsCurrentSessionOnConsole(out string message)` | Returns True if the calling process's session is the console session. |
| `GetCurrentSessionKind` | `bool GetCurrentSessionKind(out SessionKind kind, out string message)` | Console/RDP/Service, for the calling process's own session. |
| `IsRunningAsServiceSession` | `bool IsRunningAsServiceSession(out string message)` | Returns True if running in Session 0 - a dedicated check for the single most common unattended-automation failure mode this component targets. |

### State

| Method | Signature | Description |
|---|---|---|
| `GetSessionKind` | `bool GetSessionKind(int sessionId, out SessionKind kind, out string message)` | Same as `GetCurrentSessionKind`, for an arbitrary session ID. |
| `GetCurrentSessionConnectState` | `bool GetCurrentSessionConnectState(out SessionConnectState state, out string message)` | Connect state of the calling process's own session. |
| `GetSessionConnectState` | `bool GetSessionConnectState(int sessionId, out SessionConnectState state, out string message)` | Connect state of an arbitrary session. |
| `IsCurrentSessionDisconnected` | `bool IsCurrentSessionDisconnected(out string message)` | Returns True if the calling process's own session is disconnected. |
| `IsSessionDisconnected` | `bool IsSessionDisconnected(int sessionId, out string message)` | Returns True if an arbitrary session is disconnected - the literal "detect an RDP disconnect" check. |
| `GetCurrentSessionUser` | `bool GetCurrentSessionUser(out string userName, out string domainName, out string message)` | Logged-on user/domain for the calling process's own session. |
| `GetSessionUser` | `bool GetSessionUser(int sessionId, out string userName, out string domainName, out string message)` | Logged-on user/domain for an arbitrary session. |
| `EnumerateSessionsJson` | `bool EnumerateSessionsJson(string connectStateFilter, out string json, out string message)` | Enumerates every session on the machine as a JSON array of `SessionInfoData`, optionally filtered by a comma-separated list of connect state names. |

### Desktop

| Method | Signature | Description |
|---|---|---|
| `IsWorkstationLockedSimple` | `bool IsWorkstationLockedSimple(out string message)` | Returns True if the workstation is locked, via the authoritative session-flags check. |
| `IsWorkstationLocked` | `bool IsWorkstationLocked(out bool querySucceeded, out string message)` | Same, plus a `querySucceeded` output. |
| `IsSessionInteractiveSimple` | `bool IsSessionInteractiveSimple(out string message)` | Returns True if the calling process's session is interactive (backed by `Environment.UserInteractive`). |
| `IsSessionInteractive` | `bool IsSessionInteractive(out bool querySucceeded, out string message)` | Same, plus a `querySucceeded` output. |
| `IsInputDesktopAvailableSimple` | `bool IsInputDesktopAvailableSimple(out string message)` | Returns True if the default interactive desktop is currently receiving input - a different, narrower question from the lock check (see Notes & Caveats). |
| `IsInputDesktopAvailable` | `bool IsInputDesktopAvailable(out bool querySucceeded, out string message)` | Same, plus a `querySucceeded` output. |

### IdleTime

| Method | Signature | Description |
|---|---|---|
| `GetIdleTimeMilliseconds` | `bool GetIdleTimeMilliseconds(out long idleMilliseconds, out string message)` | Milliseconds since the last local keyboard/mouse input to the calling process's own session. |

### Wait

| Method | Signature | Description |
|---|---|---|
| `WaitForSessionConnectState` | `bool WaitForSessionConnectState(int sessionId, SessionConnectState expectedState, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until a session reaches the expected connect state, or the timeout elapses. |
| `WaitForSessionConnectStateSimple` | `bool WaitForSessionConnectStateSimple(int sessionId, SessionConnectState expectedState, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |
| `WaitForInputDesktopAvailable` | `bool WaitForInputDesktopAvailable(int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until the input desktop becomes available, or the timeout elapses. |
| `WaitForInputDesktopAvailableSimple` | `bool WaitForInputDesktopAvailableSimple(int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |
| `WaitForWorkstationUnlocked` | `bool WaitForWorkstationUnlocked(int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until the workstation is unlocked (by external action), or the timeout elapses. Never performs an unlock itself. |
| `WaitForWorkstationUnlockedSimple` | `bool WaitForWorkstationUnlockedSimple(int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut` output. |

### Actions

| Method | Signature | Description |
|---|---|---|
| `LockWorkstation` | `bool LockWorkstation(out string message)` | Locks the workstation. Only works from the calling process's own interactive session; ends that session's desktop interaction for the rest of the automation run. |
| `DisconnectSession` | `bool DisconnectSession(int sessionId, out string message)` | Disconnects an arbitrary session (not logoff). Disconnecting another session typically requires administrator rights. |
| `DisconnectCurrentSession` | `bool DisconnectCurrentSession(out string message)` | Disconnects the calling process's own session (not logoff). |

## Notes & Caveats

- **Never throws.** Invalid input (a negative session ID, a negative
  timeout, a non-positive poll interval, an unknown `connectStateFilter`
  token) and runtime failures (an inaccessible session, insufficient
  rights) return `False` with a descriptive message.
- **No login, unlock, or credential-handling capability anywhere in this
  component**, by deliberate, security-motivated design choice.
  `WaitForWorkstationUnlocked`/`WaitForWorkstationUnlockedSimple` only poll
  for an externally-driven unlock to happen (e.g. someone else unlocking the
  machine) - they never perform one.
- **Session 0 isolation is the central motivating failure mode.**
  `IsRunningAsServiceSession` gives it a dedicated, obvious check: a bot
  running as a plain Windows service has zero desktop access, full stop,
  regardless of anything else this component reports.
- **Three genuinely distinct concepts, three separate methods - do not
  conflate them:**
  - `IsWorkstationLocked(Simple)` is the authoritative lock-state check, via
    `WTSQuerySessionInformation(WTSSessionInfoEx)`'s `SessionFlags`. It is
    not affected by a transient secure-desktop switch.
  - `IsInputDesktopAvailable(Simple)` answers a narrower, different
    question - is the *default* desktop currently receiving input. A UAC
    consent prompt or the Ctrl+Alt+Del screen also switches the input
    desktop away from `"Default"` **without the workstation being locked**,
    so this method correctly reports "unavailable" during those states even
    though `IsWorkstationLocked` reports "not locked" at the same moment.
    That is expected behavior for what each method claims to answer, not a
    defect or redundancy between them.
  - `IsSessionInteractive(Simple)` answers whether the session has a visible
    window station at all (`Environment.UserInteractive`) - false for a
    plain Windows service (Session 0), independent of lock/desktop state.
- **`querySucceeded` means "the check itself ran without an unexpected
  failure," not "the answer was true."** A legitimate negative answer -
  including `IsInputDesktopAvailable` reporting unavailable because a
  service session has no desktop at all - still counts as
  `querySucceeded = true`. `querySucceeded = false` is reserved for a
  genuine unexpected failure (e.g. the underlying WTS call itself failed).
- **`GetCurrentSessionId` uses `ProcessIdToSessionId`, not
  `WTSGetActiveConsoleSessionId`.** The latter reports the session on the
  physical console, which is a different session entirely for a bot running
  over RDP - `GetActiveConsoleSessionId` is offered separately for that
  distinct question.
- **`GetIdleTimeMilliseconds` only reflects local input to the calling
  process's own session** - another session's idle time cannot be observed
  this way.
- **Elevation requirements.** `DisconnectSession` on a session other than
  your own typically requires administrator rights (or `SeTcbPrivilege`);
  disconnecting your own session does not. `LockWorkstation` only works from
  the calling, interactive session.
- **Naming convention: `Simple` suffix marks the less-disambiguated
  overload.** `IsWorkstationLocked(Simple)`, `IsSessionInteractive(Simple)`,
  and `IsInputDesktopAvailable(Simple)` are mandatory `Simple` splits (both
  overloads have zero Pega-visible input parameters, an exact
  signature-uniqueness collision) - see
  `project-docs/pega-usability-reviews/SessionUtils-pega-usability-review.md`.
- **Local machine only.** No `serverName`/remote-session parameters in this
  version, even though the underlying WTS APIs support remote servers
  natively.
- **Guard tests.** `SessionUtils.Tests` (in this folder) covers the
  guard-clause paths that return before any native call, plus the pure
  `TryToSessionConnectState`/`TryToSessionKind`/`TryParseConnectStates`
  mapping logic. It runs on Linux too - see `TESTING.md` at the repo root.
  This component's Linux-testable surface is smaller than most others in
  this suite: most methods either take no input to guard, or reach a native
  call immediately with nothing to validate first.
