# Query

## Check a dependency service is installed and running before continuing

```csharp
if (!svc.IsServiceInstalled("MSSQLSERVER"))
{
    // Handle the missing-dependency case.
}
else if (svc.GetStatus("MSSQLSERVER") != ServiceControllerStatus.Running)
{
    svc.StartService("MSSQLSERVER", timeoutMs: 60000);
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
