# Uptime

## How long has this session been logged on?

```csharp
if (session.GetCurrentSessionUptime(out long milliseconds, out string message))
{
    TimeSpan uptime = TimeSpan.FromMilliseconds(milliseconds);
    // e.g. log "session up for 2h 14m" alongside other automation health metrics
}
```

## How long has the machine been running since it last booted?

```csharp
if (session.GetSystemUptime(out long milliseconds, out string message))
{
    if (TimeSpan.FromMilliseconds(milliseconds) > TimeSpan.FromDays(30))
    {
        // Flag the machine for a scheduled reboot before the next run.
    }
}
```

These read two unrelated clocks - do not use one to infer the other. A
machine reboot ends every session on it, so a session can never outlive one -
but its client can disconnect and reconnect over RDP any number of times
without resetting its logon time, while a freshly logged-on session on a
machine that has been running for weeks will report a much smaller uptime
than the machine's own.
