# Actions

Every method here is a deliberate, explicitly-named action - none of them
trigger ambiently as a side effect of a query method.

## Locking the workstation

```csharp
if (session.LockWorkstation(out string message))
{
    // The workstation is now locked. This ends the CALLING session's own
    // desktop interaction immediately - any later step in this same
    // automation run that needs the desktop will fail until someone
    // unlocks it again. Only call this as a deliberate final step, or from
    // a session you don't need back right away.
}
```

Only works from the calling process's own interactive session - it cannot
lock a different session, and it fails outright from a non-interactive
service session (there is no desktop to lock).

## Disconnecting a session

```csharp
// Disconnect an arbitrary session (found via EnumerateSessionsJson) - not logoff:
session.DisconnectSession(sessionId, out string message);

// Disconnect the calling process's own session:
session.DisconnectCurrentSession(out string message);
```

Disconnecting your own session needs no special privilege. Disconnecting a
session other than your own typically requires administrator rights (or
`SeTcbPrivilege`) and will fail with an access-denied message otherwise.
This disconnects only - it does not log the session off, and there is no
`DeleteEventSource`-style destructive counterpart in this component.
