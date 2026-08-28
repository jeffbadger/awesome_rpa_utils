# Query

## Check a dependency service is installed and running before continuing

```csharp
if (!svc.IsServiceInstalled("MSSQLSERVER"))
{
    // Handle the missing-dependency case.
}
else if (!svc.IsRunning("MSSQLSERVER"))
{
    svc.StartService("MSSQLSERVER", timeoutMs: 60000);
}
```

## Check whether a service is running, without caring whether it's even installed

```csharp
// IsRunning returns False (not an exception) for a nonexistent service too,
// so this is safe to call without an IsServiceInstalled check first.
if (!svc.IsRunning("MyBackgroundAgent"))
{
    // Not running - either it's stopped, or it isn't installed at all.
}
```

## Find a service by its display name

```csharp
List<string> matches = svc.FindServiceNamesByDisplayName("SQL Server", exactMatch: false);
```

## Check whether a service will survive a reboot

```csharp
ServiceStartType startType = svc.GetStartType("MyBackgroundAgent");
bool willAutoStart = startType == ServiceStartType.Automatic || startType == ServiceStartType.AutomaticDelayedStart;
```
