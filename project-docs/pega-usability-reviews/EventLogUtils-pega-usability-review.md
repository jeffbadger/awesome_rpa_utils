# EventLogUtils Pega API Usability Review

## Summary

Unlike every other component's review doc in this folder, this one is
written **up front**, alongside the initial implementation, rather than as a
post-hoc pass over an already-shipped surface - there is no legacy API to
retrofit here. It records the signature-uniqueness decisions made before any
code was written, per the
[Signature Uniqueness Standard](../coding-standards/signature-uniqueness-standard.md).

All method ports are scalars, strings, ints, Booleans, or JSON strings -
directly usable in Pega. The repository-owned `EventLogLevel` enum avoids
requiring Pega to reference `System.Diagnostics`/`System.Diagnostics.Eventing.Reader`
types just to select or read a severity.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `DoesLogExistSimple`/`DoesLogExist` | Direct, disambiguated | String input, Boolean/message output. The `querySucceeded`-output overload separates a genuine "doesn't exist" answer from a query failure. |
| `DoesSourceExistSimple`/`DoesSourceExist` | Direct, disambiguated | Same pattern as above, for sources. |
| `TryGetLogNameForSource` | Direct | Scalar string in, scalar string out. |
| `CreateEventSource`/`CreateEventSourceSimple` | Direct, disambiguated | The `alreadyExisted`-output overload distinguishes a no-op success from a newly-created source without requiring `message` interpretation. Requires administrator rights - an unattended, non-elevated robot cannot use this. |
| `TryGetMostRecentEntry` | Direct | All scalar filters/outputs. The largest parameter list in the component, but every parameter is a primitive. |
| `CountMatchingEntries` | Direct | Same filter shape as `TryGetMostRecentEntry`, scalar `count` output. |
| `QueryByXPath` | Direct, power-user escape hatch | Requires the caller to know XPath 1.0 syntax against the Windows Event Log XML schema - not beginner-friendly, but intentionally so; the scalar-filtered methods cover the common cases. |
| `QueryRecentEntriesJson`/`DumpRecentEntriesJson` | Direct, JSON | Returns a JSON array string rather than a typed collection, consistent with this suite's preference for JSON over complex objects on the Pega boundary. |
| `WriteEntry`/`WriteEntrySimple` | Direct, disambiguated | `errorMessage` (not `message`) avoids colliding with the input `message` parameter name. `WriteEntrySimple` has a genuinely shorter parameter list (arity difference, not just a missing `out`), so it does not need `Simple` for uniqueness - the suffix is used anyway for family-naming consistency with the rest of this component. |
| `WaitForEntry`/`WaitForEntrySimple` | Direct, disambiguated | Same filter/timeout parameter list as `TryGetMostRecentEntry` plus `timeoutMs`/`pollIntervalMs` - the `Simple` sibling is a genuine signature-uniqueness necessity (identical non-`out` parameter list). |
| `ExportFilteredLog`/`QueryExportedLog` | Direct | Kept as two separate methods (not a single method with an "is this a file" flag) since live-log vs. exported-file querying use different `EventLogQuery` constructor overloads and fail differently. |

## Signature-uniqueness decisions made up front

- `DoesLogExistSimple(string, out string)` vs. `DoesLogExist(string, out bool, out string)` - `Simple` suffix on the overload missing the disambiguating `querySucceeded` output, per the standard's convention.
- `DoesSourceExistSimple`/`DoesSourceExist` - same pattern.
- `CreateEventSourceSimple(string, string, out string)` vs. `CreateEventSource(string, string, out bool, out string)` - `Simple` suffix on the overload missing `alreadyExisted`.
- `WaitForEntrySimple` vs. `WaitForEntry` - both share the identical `(string, string, string, int, string, int, int)` non-`out` parameter list, so `Simple` is required, not optional.
- `WriteEntrySimple` vs. `WriteEntry` - `(string, string)` vs. `(string, string, EventLogLevel, int)` already differ in arity, so this pair was never actually ambiguous; `Simple` is applied anyway purely for naming consistency with the rest of the component's `Simple`/plain pairs.
- No `As<Type>`-suffixed overloads exist in this component - there is no case here (unlike `ServiceUtils`'s `ServiceControllerStatus`/`ServiceStatus` pair) where two overloads return the same logical value via a different type.

## Operational concerns

- `CreateEventSource`/`CreateEventSourceSimple` require administrator rights
  and are a one-time, machine-wide registry registration - an unattended,
  non-elevated robot cannot provision a new source for itself. Provision
  sources ahead of time (e.g. via deployment scripting) rather than relying
  on a bot to self-register at runtime.
- Reading the **Security** log requires elevation and often "Event Log
  Readers" group membership, independent of whether the process is an
  administrator.
- `CreateEventSource` cannot reassign an existing source to a different log
  - this is a Windows platform limitation, not something this component can
    work around; the failure message says so explicitly.
- `CountMatchingEntries` materializes every matching record before counting;
  a very broad filter against a huge log (Security in particular) can be
  slow.

## Recommended changes

None outstanding - this is the initial design pass, not a retrofit. Future
recommendations (e.g. remote-machine support, `DeleteEventSource`, a
narrower/faster `CountMatchingEntries`) are tracked as open questions in the
implementation plan rather than here, since there is no existing rating to
revise.
