# Configuration

## Ensure a service starts automatically on boot

```csharp
svc.SetStartType("MyBackgroundAgent", ServiceStartType.Automatic);
```

## Set delayed auto-start, so it doesn't compete with other services at boot

```csharp
svc.SetStartType("MyBackgroundAgent", ServiceStartType.AutomaticDelayedStart);
```

## Disable a service entirely

```csharp
svc.SetStartType("SomeUnwantedService", ServiceStartType.Disabled);
```
