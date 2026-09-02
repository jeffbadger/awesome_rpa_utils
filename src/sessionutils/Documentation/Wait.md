# Wait

## Waiting for a session to reach a connect state

```csharp
bool reached = session.WaitForSessionConnectStateSimple(
    sessionId, SessionConnectState.Active, timeoutMs: 30000, pollIntervalMs: 500, out string message);
```

## Waiting for the input desktop to become available

Useful after a scheduled UAC prompt or lock-screen event that you expect to
resolve shortly:

```csharp
bool available = session.WaitForInputDesktopAvailableSimple(
    timeoutMs: 60000, pollIntervalMs: 1000, out string message);
```

## Waiting for the workstation to be unlocked

This only *observes* an externally-driven unlock (someone else unlocking the
machine, or a scheduled unlock policy) - it never performs an unlock itself,
by design:

```csharp
bool unlocked = session.WaitForWorkstationUnlockedSimple(
    timeoutMs: 120000, pollIntervalMs: 2000, out string message);
if (!unlocked)
{
    // Still locked after the timeout - someone needs to unlock it manually
    // before this automation can proceed with desktop interaction.
}
```

Every `WaitForX` method also has a `timedOut`-output overload (without the
`Simple` suffix) if you need to distinguish "timed out" from "a genuine
failure occurred" without inspecting `message` text.
