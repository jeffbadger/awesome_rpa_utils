# Query

## Check a dependency service is installed and running before continuing

```csharp
if (!svc.IsServiceInstalled("MSSQLSERVER", out string msg))
{
    // Not installed (or msg says why the check failed) - handle the
    // missing-dependency case.
}
else if (!svc.IsRunning("MSSQLSERVER", out msg))
{
    if (msg == null)
    {
        // Installed but stopped - start it, with a 60-second budget.
        svc.StartService("MSSQLSERVER", timeoutMs: 60000, out msg);
    }
}
```

## Check whether a service is running, without caring whether it's even installed

```csharp
// IsRunning returns False (never an exception) for a nonexistent service too.
// A null message means the service exists and the answer is trustworthy;
// a non-null message explains why the check failed (e.g. not installed).
if (!svc.IsRunning("MyBackgroundAgent", out string msg) && msg == null)
{
    // Running the check confirmed it: installed, but stopped.
}
```

## Find a service by its display name

```csharp
List<string> matches = svc.FindServiceNamesByDisplayName("SQL Server", exactMatch: false);
```

## Check whether a service will survive a reboot

```csharp
if (svc.TryGetStartType("MyBackgroundAgent", out ServiceStartType startType, out string msg))
{
    bool willAutoStart = startType == ServiceStartType.Automatic || startType == ServiceStartType.AutomaticDelayedStart;
}
```