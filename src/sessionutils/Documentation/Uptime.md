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
session's logon time and the machine's boot time have no fixed relationship:
a session reconnected over RDP can easily outlive several reboots of a
machine that stays running, or be far younger than a machine that has been up
for weeks.
