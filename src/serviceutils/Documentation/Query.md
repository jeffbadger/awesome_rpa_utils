# Query

## Check a dependency service is installed and running before continuing

```csharp
if (!svc.IsServiceInstalledSimple("MSSQLSERVER", out string msg))
{
    // Not installed (or msg says why the check failed) - handle the
    // missing-dependency case.
}
else if (!svc.IsRunningSimple("MSSQLSERVER", out msg))
{
    if (msg == null)
    {
        // Installed but stopped - start it, with a 60-second budget.
        svc.StartServiceSimple("MSSQLSERVER", timeoutMs: 60000, out msg);
    }
}
```

## Check whether a service is running, without caring whether it's even installed

```csharp
// IsRunningSimple returns False (never an exception) for a nonexistent service too.
// A null message means the service exists and the answer is trustworthy;
// a non-null message explains why the check failed (e.g. not installed).
if (!svc.IsRunningSimple("MyBackgroundAgent", out string msg) && msg == null)
{
    // Running the check confirmed it: installed, but stopped.
}
```

## Check installed/running without interpreting `message`

```csharp
// querySucceeded separates "the query completed" from the state itself, so
// no null-message test is needed to tell a genuine negative apart from a failure.
if (!svc.IsServiceInstalled("MSSQLSERVER", out bool querySucceeded, out string msg))
{
    if (!querySucceeded)
        Logger.Error($"Could not check whether MSSQLSERVER is installed: {msg}");
    // else: querySucceeded is true and installed is false - genuinely not installed.
}
```

## Find a service by its display name

```csharp
List<string> matches = svc.FindServiceNamesByDisplayName("SQL Server", exactMatch: false);
```

For a single expected match, `TryFindFirstServiceNameByDisplayName` returns a
scalar service name instead of a list:

```csharp
if (svc.TryFindFirstServiceNameByDisplayName("SQL Server (MSSQLSERVER)", out string serviceName, out string msg))
{
    svc.StartServiceSimple(serviceName, timeoutMs: 60000, out msg);
}
```

For designers without a `List<string>` proxy, `ListServiceNamesDelimited` and
`FindServiceNamesByDisplayNameDelimited` return the same results as one
delimited string:

```csharp
string allNames = svc.ListServiceNamesDelimited(); // "AeLookupSvc,ALG,AppIDSvc,..."
string sqlNames = svc.FindServiceNamesByDisplayNameDelimited("SQL Server", exactMatch: false);
```

## Get a service's status without the `System.ServiceProcess` dependency

```csharp
// The ServiceStatus overload is a repository-owned enum, so a Pega deployment
// doesn't need System.ServiceProcess.ServiceController to select or read it.
if (svc.TryGetStatus("MSSQLSERVER", out ServiceStatus status, out string msg))
{
    bool isPausedOrPending = status == ServiceStatus.Paused || status == ServiceStatus.PausePending;
}
```

## Check whether a service will survive a reboot

```csharp
if (svc.TryGetStartType("MyBackgroundAgent", out ServiceStartType startType, out string msg))
{
    bool willAutoStart = startType == ServiceStartType.Automatic || startType == ServiceStartType.AutomaticDelayedStart;
}
```