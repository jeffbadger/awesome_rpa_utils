# Configuration

## Ensure a service starts automatically on boot

```csharp
if (!svc.SetStartType("MyBackgroundAgent", ServiceStartType.Automatic, out string msg))
{
    // Not elevated, no such service, or the SCM refused - msg says which.
}
```

## Set delayed auto-start, so it doesn't compete with other services at boot

```csharp
svc.SetStartType("MyBackgroundAgent", ServiceStartType.AutomaticDelayedStart, out string msg);
```

## Disable a service entirely

```csharp
svc.SetStartType("SomeUnwantedService", ServiceStartType.Disabled, out string msg);
```