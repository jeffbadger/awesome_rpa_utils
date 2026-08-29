# ServiceAutomation

A Pega Robot Studio-ready component (`ServiceUtils`) that queries, starts,
stops, restarts, pauses/resumes, and configures the startup type of Windows
services. Like every component in this suite, it honors the never-throws
contract: invalid input and runtime failures return `False` with a
descriptive message instead of throwing.

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

| Method | Signature | Description |
|---|---|---|
| `IsServiceInstalled` | `bool IsServiceInstalled(string serviceName, out string message)` | Returns True if a service with the given name is installed. Never throws. |
| `IsRunning` | `bool IsRunning(string serviceName, out string message)` | Returns True if a service with the given name is installed and currently running. False with a null message means installed-but-stopped; False with a message means the check failed (invalid name or missing service). |
| `TryGetStatus` | `bool TryGetStatus(string serviceName, out ServiceControllerStatus status, out string message)` | Gets a service's current status. Returns False with a message on failure. |
| `TryGetStartType` | `bool TryGetStartType(string serviceName, out ServiceStartType startType, out string message)` | Gets a service's configured startup type. Returns False with a message on failure. |
| `ListServiceNames` | `List<string> ListServiceNames()` | Gets the service names of every installed service. Empty list on failure. |
| `FindServiceNamesByDisplayName` | `List<string> FindServiceNamesByDisplayName(string displayName, bool exactMatch = true)` | Finds the service name(s) of installed services matching a display name. Null/empty filter or failed enumeration returns an empty list. |

### Control

| Method | Signature | Description |
|---|---|---|
| `StartService` | `bool StartService(string serviceName, int timeoutMs, out string message)` | Starts a service and waits for it to reach Running. Idempotent: an already-running service returns True with an "already running" note. False (not an exception) on failure or timeout. |
| `StopService` | `bool StopService(string serviceName, int timeoutMs, out string message)` | Stops a service and waits for it to reach Stopped. Idempotent: an already-stopped service returns True with an "already stopped" note. False (not an exception) on failure or timeout. |
| `RestartService` | `bool RestartService(string serviceName, int timeoutMs, out string message)` | Stops then starts a service, skipping the start phase if the stop phase fails. Each phase gets its own timeoutMs budget. |
| `PauseService` | `bool PauseService(string serviceName, out string message)` | Pauses a running service. Idempotent. Does not wait for Paused - pair with `WaitForServiceStatus`. |
| `ResumeService` | `bool ResumeService(string serviceName, out string message)` | Resumes a paused service. Idempotent. Does not wait for Running - pair with `WaitForServiceStatus`. |
| `WaitForServiceStatus` | `bool WaitForServiceStatus(string serviceName, ServiceControllerStatus expectedStatus, int timeoutMs, out string message)` | Polls for a service to reach the given status until it does, or the timeout elapses. |

### Configuration

| Method | Signature | Description |
|---|---|---|
| `SetStartType` | `bool SetStartType(string serviceName, ServiceStartType startType, out string message)` | Sets a service's startup type, including delayed-auto-start. False with a message on failure. |

## Notes & Caveats

- **Never throws.** Invalid input (null/empty name, negative `timeoutMs`,
  undefined `ServiceStartType`) and runtime failures (missing service,
  non-elevated runtime, request the service rejects) return `False` with a
  descriptive message. A missing service reads as "No Windows service named
  'X' is installed." rather than `ServiceController`'s generic wrapper text.
- **Idempotent control methods.** `StartService` on an already-running
  service, `StopService` on an already-stopped one, `PauseService` on an
  already-paused, and `ResumeService` on an already-running all return `True`
  with an "already ..." note in the message - the normal automation retry
  case isn't an error. A `timeoutMs` of 0 means "don't wait" (success only if
  the service is already in the target state); a negative timeout returns
  `False` + message.
- **`RestartService` skips the start phase when the stop phase fails.**
  Starting a service that never stopped would throw, so the failure from the
  stop phase is reported and the start is not attempted.
- **`ServiceController` cannot change a service's startup type at all** -
  it can query it (`TryGetStartType`), start/stop/pause it, but has no
  setter. `SetStartType` is implemented with direct `advapi32.dll` P/Invoke
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
  slow-starting service is a normal, checkable outcome.
- **`WaitForServiceStatus` has no `pollIntervalMs` parameter**, unlike this
  repo's other `WaitForX` methods - `ServiceController.WaitForStatus` blocks
  natively against the Service Control Manager rather than needing a
  hand-rolled poll loop.
- **Starting/stopping/reconfiguring most services requires administrator
  rights.** A non-elevated caller gets `False` + a message ("Access is
  denied") from the underlying Win32 call. Combine with
  `CommandLineUtils.RunElevated` (a separate component) if you need to
  elevate just for this - there's no built-in elevation here.
- **Guard tests.** `ServiceUtils.Tests` (in this folder) covers the
  null/empty-name, negative-timeout, undefined-enum, and enum-to-Win32
  mapping guards. It runs on Linux too - see `TESTING.md` at the repo root.