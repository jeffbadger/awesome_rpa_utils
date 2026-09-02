# Identity

## Am I running as a Windows service (Session 0)?

The single most common unattended-automation failure this component targets
- a bot running as a plain Windows service has zero desktop access, full
stop, regardless of anything else `SessionUtils` reports.

```csharp
if (session.IsRunningAsServiceSession(out string message))
{
    // This automation cannot interact with any desktop at all - fail fast
    // with a clear diagnosis instead of a confusing UI-automation timeout.
    throw new InvalidOperationException("Cannot run desktop automation from a Windows service (Session 0).");
}
```

## Am I on the console, or over RDP?

```csharp
if (session.GetCurrentSessionKind(out SessionKind kind, out string message))
{
    switch (kind)
    {
        case SessionKind.Console:
            // Physical console logon.
            break;
        case SessionKind.Rdp:
            // Remote Desktop - a disconnect (see State.md) can interrupt UI automation.
            break;
        case SessionKind.Service:
            // Session 0 - see IsRunningAsServiceSession above.
            break;
    }
}
```

## Is my session the one on the physical console?

```csharp
bool onConsole = session.IsCurrentSessionOnConsole(out string message);
// onConsole is false (with a message) for any RDP session, even the active one.
```

`GetCurrentSessionId` (via `ProcessIdToSessionId`) and
`GetActiveConsoleSessionId` (via `WTSGetActiveConsoleSessionId`) answer two
different questions - the session the calling process is actually in, versus
whichever session happens to be attached to the physical console right now.
Conflating them gives the wrong answer for any bot running over RDP.
