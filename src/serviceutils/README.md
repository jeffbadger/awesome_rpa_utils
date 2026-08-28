# ServiceAutomation

A Pega Robot Studio-ready component (`ServiceUtils`) that queries, starts,
stops, restarts, pauses/resumes, and configures the startup type of Windows
services.

- Target framework: `net10.0-windows`
- Namespace: `ServiceAutomation`
- Assembly: `ServiceAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

**Dependency note:** this is the first component in this repo with a NuGet
dependency (`System.ServiceProcess.ServiceController`). Every other
component uses either raw P/Invoke or a free platform-SDK reference
(`UseWindowsForms`/`UseWPF`/`EnableWindowsTargeting`) with zero NuGet
packages. `ServiceController` is Microsoft's official managed wrapper around
the Service Control Manager and is far simpler than the raw Win32
alternative for most of what this component does - but it has one real gap
(see below), which is why `SetStartType` still uses direct P/Invoke.

## Types

### `ServiceStartType`
A Windows service startup type, unifying `System.ServiceProcess.ServiceStartMode`
with the separate delayed-auto-start flag that mode doesn't expose:
`Boot`, `System`, `Automatic`, `AutomaticDelayedStart`, `Manual`, `Disabled`.

## Constructors

| Constructor | Description |
|---|---|
| `ServiceUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `ServiceUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Query

| Method | Description |
|---|---|
| `bool IsServiceInstalled(string serviceName)` | Returns True if a service with the given name is installed. |
| `ServiceControllerStatus GetStatus(string serviceName)` | Gets a service's current status. |
| `ServiceStartType GetStartType(string serviceName)` | Gets a service's configured startup type. |
| `List<string> ListServiceNames()` | Gets the service names of every installed service. |
| `List<string> FindServiceNamesByDisplayName(string displayName, bool exactMatch = true)` | Finds the service name(s) of installed services matching a display name. |

### Control

| Method | Description |
|---|---|
| `bool StartService(string serviceName, int timeoutMs = 30000)` | Starts a service and waits for it to reach Running. Returns False (not an exception) if it times out. |
| `bool StopService(string serviceName, int timeoutMs = 30000)` | Stops a service and waits for it to reach Stopped. Returns False (not an exception) if it times out. |
| `bool RestartService(string serviceName, int timeoutMs = 30000)` | Stops then starts a service. Each phase gets its own timeoutMs budget. |
| `void PauseService(string serviceName)` | Pauses a running service. |
| `void ResumeService(string serviceName)` | Resumes a paused service. |
| `bool WaitForServiceStatus(string serviceName, ServiceControllerStatus expectedStatus, int timeoutMs)` | Polls for a service to reach the given status until it does, or the timeout elapses. |

### Configuration

| Method | Description |
|---|---|
| `void SetStartType(string serviceName, ServiceStartType startType)` | Sets a service's startup type, including delayed-auto-start. |

## Notes & Caveats

- **`ServiceController` cannot change a service's startup type at all** -
  it can query it (`GetStartType`), start/stop/pause it, but has no setter.
  `SetStartType` is implemented with direct `advapi32.dll` P/Invoke
  (`ChangeServiceConfig`/`ChangeServiceConfig2`) specifically because of this
  gap - it's the only method in this component that doesn't use
  `ServiceController`.
- **Enumeration methods return service name strings, not live
  `ServiceController` objects.** `ServiceController` is `IDisposable`;
  returning live instances would push disposal responsibility onto the
  caller. Every method here opens and disposes its own `ServiceController`
  instance(s) internally.
- **Timeouts are `false` returns, not exceptions**, for
  `StartService`/`StopService`/`RestartService`/`WaitForServiceStatus` - a
  slow-starting service is a normal, checkable outcome. A genuinely broken
  case (no such service, access denied, unsupported control) still throws
  natively (`InvalidOperationException`/`Win32Exception`).
- **`WaitForServiceStatus` has no `pollIntervalMs` parameter**, unlike this
  repo's other `WaitForX` methods - `ServiceController.WaitForStatus` blocks
  natively against the Service Control Manager rather than needing a
  hand-rolled poll loop.
- **Starting/stopping/reconfiguring most services requires administrator
  rights.** A non-elevated caller will get a `Win32Exception`
  ("Access is denied") from the underlying Win32 call. Combine with
  `CommandLineUtils.RunElevated` (a separate component) if you need to
  elevate just for this - there's no built-in elevation here.
