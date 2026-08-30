# ServiceUtils Pega API Usability Review

## Summary

Most service operations use a service-name string, integers, Booleans, messages,
and enums, making them directly usable in Pega. The collection-returning discovery
methods require proxies. `ServiceControllerStatus` is technically an enum but
leaks a NuGet dependency type into the Pega-facing contract.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `IsServiceInstalled` | Direct, ambiguous false | String input and Boolean/message output are easy to use. `false` can mean not installed, invalid input, or query failure, so branching requires interpreting `message`. |
| `IsRunning` | Direct, ambiguous false | All ports are scalar. A false state and a failed query are distinguished only by whether `message` is null. Separate success and state outputs would be clearer. |
| `TryGetStatus` | Direct enum, dependency concern | The status is an enum and should be selectable/consumable, but `ServiceControllerStatus` comes from an external package assembly. A repository-owned status enum would give Pega a smaller and more stable public dependency surface. |
| `TryGetStartType` | Direct | Uses the repository-owned `ServiceStartType` enum and follows the Boolean/message pattern. |
| `ListServiceNames` | Proxy friction | Returns `List<string>`, requiring a collection proxy and loop. Empty means either no results or enumeration failure. Add JSON/delimited output or count/index accessors. |
| `FindServiceNamesByDisplayName` | Proxy friction | Scalar search inputs produce `List<string>`. Add a `TryFindFirstServiceNameByDisplayName` scalar method for the common case and retain the list for ambiguous matches. |
| `StartService`, `StopService`, `RestartService` | Direct | Every port is scalar. These methods block while waiting and usually require administrative rights. `RestartService` can consume roughly twice the supplied timeout because each phase gets a full budget. |
| `PauseService`, `ResumeService` | Direct | Scalar ports, but success only means the request was accepted; use `WaitForServiceStatus` when the reached state matters. |
| `WaitForServiceStatus` | Direct enum, dependency concern | Inputs are scalar/enum. The external `ServiceControllerStatus` type should be replaced or wrapped by a repository enum. Timeout and operational failure both return false with a message. |
| `SetStartType` | Direct | String plus repository enum and Boolean/message outputs are Pega-friendly. It normally requires an elevated robot process. |

## Operational concerns

- Starting, stopping, pausing, and reconfiguring services often requires the Pega
  runtime itself to have sufficient rights. An unattended robot generally cannot
  solve this by automating a UAC secure-desktop prompt.
- Several idempotent operations can return `true` with a non-null informational
  message such as “already running.” If project-wide convention treats any message
  as failure, this will produce confusing Pega branches; use a separate note output
  or keep `message` null on success.
- Deploying this component also requires its `System.ServiceProcess.ServiceController`
  dependency to be available to the Robot Studio/runtime loader.

## Recommended changes

1. Add a scalar first-match display-name lookup.
2. Add a scalar representation or indexed access for service-name collections.
3. Wrap `ServiceControllerStatus` in a repository-owned Pega-facing enum.
4. Separate operation success from Boolean state for installed/running queries.
5. Standardize whether `message` may contain informational text on successful
   calls.

## Verdict

Service control and configuration methods are directly usable. Discovery lists
need adapters, while the externally defined status enum should be wrapped to
reduce designer and deployment coupling.
