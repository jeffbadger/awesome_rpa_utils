# Control

## Start a service and handle a slow start

```csharp
bool started = svc.StartServiceSimple("MyBackgroundAgent", timeoutMs: 15000, out string msg);
if (!started)
{
    // Still starting after 15 seconds (or the start failed - msg says which).
}
```

## Restart a stuck service before continuing an automation

```csharp
// Safe to call regardless of current state: an already-stopped service skips
// its stop phase, an already-running one still stops and starts back up. If
// the stop phase fails, the start phase is skipped and msg says why.
if (!svc.RestartServiceSimple("MyBackgroundAgent", timeoutMs: 20000, out string msg))
{
    // Restart failed - msg names the failing phase and reason.
}
```

## Wait for a paused service to resume

```csharp
svc.ResumeServiceSimple("MyBackgroundAgent", out string msg);
svc.WaitForServiceStatus("MyBackgroundAgent", ServiceControllerStatus.Running, timeoutMs: 10000, out msg);
```

The `ServiceStatus` overload waits on the repository-owned enum instead, for a
Pega deployment without the `System.ServiceProcess` assembly:

```csharp
svc.ResumeServiceSimple("MyBackgroundAgent", out string msg);
svc.WaitForServiceStatus("MyBackgroundAgent", ServiceStatus.Running, timeoutMs: 10000, out msg);
```

## Handle a start attempt on an already-running service without a guard

```csharp
// Idempotent: this returns True with an "already running" note in msg instead
// of throwing, so retried automations don't need a status check up front.
svc.StartServiceSimple("MyBackgroundAgent", timeoutMs: 5000, out string msg);
```

## Keep `message` null on success, even for the idempotent "already there" case

If a project-wide convention treats any non-null `message` as failure, use the
`wasAlready...`-output overloads instead - `message` stays `null` on every
success path, and the idempotent case is reported through its own output:

```csharp
if (svc.StartService("MyBackgroundAgent", timeoutMs: 5000, out bool wasAlreadyRunning, out string msg))
{
    if (wasAlreadyRunning)
        Logger.Info("MyBackgroundAgent was already running.");
}
else
{
    Logger.Error($"Failed to start MyBackgroundAgent: {msg}");
}
```

The same pattern is available on `StopService` (`wasAlreadyStopped`),
`RestartService` (`wasAlreadyStopped`, from the stop phase), `PauseService`
(`wasAlreadyPaused`), and `ResumeService` (`wasAlreadyRunning`) - the
message-only originals (`StartServiceSimple`, `StopServiceSimple`,
`RestartServiceSimple`, `PauseServiceSimple`, `ResumeServiceSimple`) report
the idempotent case through `message` instead.