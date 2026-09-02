# State

## Detecting an RDP disconnect

```csharp
if (session.IsCurrentSessionDisconnected(out string message))
{
    // The RDP client disconnected but the session is still running - UI
    // automation against the visible desktop will not work in this state.
}
```

Or for a session other than the caller's own (found via `EnumerateSessionsJson`
below):

```csharp
bool disconnected = session.IsSessionDisconnected(sessionId, out string message);
```

## Who is logged on?

```csharp
if (session.GetCurrentSessionUser(out string userName, out string domainName, out string message))
{
    // userName/domainName are empty strings, not a failure, for a session
    // with no logged-on user (e.g. the Session 0 service session).
}
```

## Enumerating every session on the machine

```csharp
// Every session, unfiltered:
session.EnumerateSessionsJson(connectStateFilter: null, out string allSessionsJson, out string message);

// Only active and disconnected sessions:
session.EnumerateSessionsJson("Active,Disconnected", out string filteredJson, out string message);
```

Each entry in the returned JSON array has the shape:

```json
{
  "SessionId": 2,
  "WinStationName": "RDP-Tcp#0",
  "ConnectState": "Active",
  "UserName": "jdoe",
  "DomainName": "CONTOSO",
  "Kind": "Rdp"
}
```

Parse the array with Robot Studio's built-in JSON methods to loop over
sessions - for example, to find every disconnected RDP session across a
shared automation host before deciding whether to reconnect or clean one up.
