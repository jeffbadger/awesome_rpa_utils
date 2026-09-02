# IdleTime

## How long has the machine been idle?

```csharp
if (session.GetIdleTimeMilliseconds(out long idleMilliseconds, out string message))
{
    if (idleMilliseconds > 5 * 60 * 1000)
    {
        // No keyboard/mouse input for 5+ minutes - safe to assume no one is
        // actively using this session right now.
    }
}
```

This only reflects local input to the calling process's own session -
another session's idle time cannot be observed this way. There is no
per-session variant of this method, unlike most of the rest of this
component.
