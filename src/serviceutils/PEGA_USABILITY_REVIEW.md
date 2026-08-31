# ServiceUtils Pega API Usability Review

## Summary

Most service operations use a service-name string, integers, Booleans, messages,
and enums, making them directly usable in Pega. The collection-returning discovery
methods now also have scalar/delimited alternatives, and the repository-owned
`ServiceStatus` enum gives Pega a dependency-free alternative to
`ServiceControllerStatus`.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `IsServiceInstalled` | Direct, disambiguated | String input and Boolean/message output are easy to use. A `querySucceeded`-output overload now separates a genuine "not installed" answer from an invalid name or query failure, instead of requiring `message` interpretation. |
| `IsRunning` | Direct, disambiguated | All ports are scalar. A `querySucceeded`-output overload now makes the existing null-message distinction explicit as a Boolean. |
| `TryGetStatus` | Direct enum; dependency-free overload added | A `ServiceStatus`-typed overload (repository-owned) now sits alongside the original `ServiceControllerStatus` overload, giving Pega a smaller and more stable dependency surface without removing the .NET-facing option. |
| `TryGetStartType` | Direct | Uses the repository-owned `ServiceStartType` enum and follows the Boolean/message pattern. |
| `ListServiceNames` | Direct scalar alternative added | Returns `List<string>` as before; `ListServiceNamesDelimited` now returns the same names as one delimited string for designers without a collection proxy. |
| `FindServiceNamesByDisplayName` | Direct scalar alternatives added | `TryFindFirstServiceNameByDisplayName` returns the first match as a scalar string for the common case; `FindServiceNamesByDisplayNameDelimited` returns all matches as one delimited string; the original list method remains for ambiguous matches. |
| `StartService`, `StopService`, `RestartService` | Direct, standardized | Every port is scalar. These methods block while waiting and usually require administrative rights. `RestartService` can consume roughly twice the supplied timeout because each phase gets a full budget. Each now has a `wasAlready...`-output overload that keeps `message` `null` on every success path. |
| `PauseService`, `ResumeService` | Direct, standardized | Scalar ports, but success only means the request was accepted; use `WaitForServiceStatus` when the reached state matters. Each now has a `wasAlready...`-output overload with the same standardized `message` behavior. |
| `WaitForServiceStatus` | Direct enum; dependency-free overload added | Inputs are scalar/enum. A `ServiceStatus`-typed overload now sits alongside the original `ServiceControllerStatus` overload. Timeout and operational failure both return false with a message (unchanged - see Operational concerns). |
| `SetStartType` | Direct | String plus repository enum and Boolean/message outputs are Pega-friendly. It normally requires an elevated robot process. |

## Operational concerns

- Starting, stopping, pausing, and reconfiguring services often requires the Pega
  runtime itself to have sufficient rights. An unattended robot generally cannot
  solve this by automating a UAC secure-desktop prompt.
- Several idempotent operations can return `true` with a non-null informational
  message such as “already running.” The original overloads still do this, for
  backward compatibility; the new `wasAlready...`-output overloads keep `message`
  `null` on success instead and report the idempotent case through their own
  output - use those where a project-wide convention treats any message as failure.
- Deploying this component also requires its `System.ServiceProcess.ServiceController`
  dependency to be available to the Robot Studio/runtime loader, for methods that
  still use the `ServiceControllerStatus`-typed overloads.

## Recommended changes

1. **Done.** Added `TryFindFirstServiceNameByDisplayName`, a scalar first-match
   display-name lookup, alongside the existing list-returning method.
2. **Done.** Added `ListServiceNamesDelimited` and
   `FindServiceNamesByDisplayNameDelimited`, joining the existing
   `List<string>` results into a single delimited string (default comma).
3. **Done.** Added the repository-owned `ServiceStatus` enum and overloads of
   `TryGetStatus`/`WaitForServiceStatus` that use it instead of
   `ServiceControllerStatus`; the original overloads remain for .NET
   consumers.
4. **Done.** Added `IsServiceInstalled`/`IsRunning` overloads with a
   `querySucceeded` output, separating operation success from the Boolean
   state without requiring `message` interpretation.
5. **Done.** Added `wasAlready...`-output overloads of
   `StartService`/`StopService`/`RestartService`/`PauseService`/`ResumeService`
   that keep `message` `null` on every success path, reporting the idempotent
   "already in that state" case through a dedicated Boolean output instead;
   the original overloads (which set an informational `message` on that
   success path) remain for backward compatibility.

## Verdict

Service control and configuration methods are directly usable. Discovery
methods now have scalar/delimited alternatives to their `List<string>`
originals, and `ServiceStatus` gives Pega a dependency-free alternative to
`ServiceControllerStatus` for the two enum-typed methods. All five
recommended changes are implemented additively - every original overload
remains available for .NET consumers or backward compatibility.

## Addendum: naming ambiguity fix

The additive overloads above solved usability, but several of them introduced
a new problem: two same-named methods whose Pega-visible (non-`out`)
parameter lists became identical, differing only in an `out` parameter's
type or presence. C# overload resolution handles this fine, but Pega Robot
Studio's designer surface cannot disambiguate two overloads by `out`
parameter type/shape alone - both entries in the designer's method picker
would look the same.

Eight method groups in this component had this problem:
`IsServiceInstalled`, `IsRunning`, `TryGetStatus`, `StartService`,
`StopService`, `RestartService`, `PauseService`, `ResumeService`. In each
case, one overload takes just `(string serviceName, ..., out string
message)` and the other adds one extra `out` parameter (`querySucceeded`,
`wasAlready...`) or returns a different `out` type (`ServiceControllerStatus`
vs. `ServiceStatus`) - identical from Pega's point of view.

Fixed by renaming the less-disambiguated overload (the one without the
extra output) rather than the newer, more Pega-usable one, following this
repository's convention of breaking changes over compatibility shims:

| Old name | New name | Reason |
|---|---|---|
| `IsServiceInstalled(string, out string)` | `IsServiceInstalledSimple` | Missing the `querySucceeded` disambiguator |
| `IsRunning(string, out string)` | `IsRunningSimple` | Missing the `querySucceeded` disambiguator |
| `TryGetStatus(string, out ServiceControllerStatus, out string)` | `TryGetStatusAsServiceControllerStatus` | Return type, not an extra output, was the only difference |
| `StartService(string, int, out string)` | `StartServiceSimple` | Missing the `wasAlreadyRunning` disambiguator |
| `StopService(string, int, out string)` | `StopServiceSimple` | Missing the `wasAlreadyStopped` disambiguator |
| `RestartService(string, int, out string)` | `RestartServiceSimple` | Missing the `wasAlreadyStopped` disambiguator |
| `PauseService(string, out string)` | `PauseServiceSimple` | Missing the `wasAlreadyPaused` disambiguator |
| `ResumeService(string, out string)` | `ResumeServiceSimple` | Missing the `wasAlreadyRunning` disambiguator |

The plain (un-suffixed) name in each pair now belongs to the overload with
the extra disambiguating output - the one most useful to a Pega automation -
per the naming convention documented in the component `README.md`.
`TryGetStatus(string, out ServiceStatus, out string)` (the repository-owned
enum overload) was unaffected: it keeps the plain name since it was never
part of a colliding pair.
