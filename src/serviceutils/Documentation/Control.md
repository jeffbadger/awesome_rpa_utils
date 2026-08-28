# Control

## Start a service and handle a slow start

```csharp
bool started = svc.StartService("MyBackgroundAgent", timeoutMs: 15000);
if (!started)
{
    // Still starting after 15 seconds - decide whether to wait longer or bail out.
}
```

## Restart a stuck service before continuing an automation

```csharp
bool restarted = svc.RestartService("MyBackgroundAgent", timeoutMs: 20000);
```

## Wait for a paused service to resume

```csharp
svc.ResumeService("MyBackgroundAgent");
bool resumed = svc.WaitForServiceStatus("MyBackgroundAgent", ServiceControllerStatus.Running, timeoutMs: 10000);
```
