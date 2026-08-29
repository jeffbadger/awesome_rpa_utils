# Control

## Start a service and handle a slow start

```csharp
bool started = svc.StartService("MyBackgroundAgent", timeoutMs: 15000, out string msg);
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
if (!svc.RestartService("MyBackgroundAgent", timeoutMs: 20000, out string msg))
{
    // Restart failed - msg names the failing phase and reason.
}
```

## Wait for a paused service to resume

```csharp
svc.ResumeService("MyBackgroundAgent", out string msg);
svc.WaitForServiceStatus("MyBackgroundAgent", ServiceControllerStatus.Running, timeoutMs: 10000, out msg);
```

## Handle a start attempt on an already-running service without a guard

```csharp
// Idempotent: this returns True with an "already running" note in msg instead
// of throwing, so retried automations don't need a status check up front.
svc.StartService("MyBackgroundAgent", timeoutMs: 5000, out string msg);
```