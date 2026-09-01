# EventLogAutomation

A Pega Robot Studio-ready component (`EventLogUtils`) that reads, queries,
waits for, writes, and exports/imports Windows Event Log entries on the
local machine. Like every component in this suite, it honors the
never-throws contract: invalid input and runtime failures return `False`
with a descriptive message instead of throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `EventLogAutomation`
- Assembly: `EventLogAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

**Not to be confused with `EventUtils`** (`EventAutomation`) - that component
watches window messages/WinEvent hooks/global input events. This component
is exclusively about the Windows Event Log subsystem
(`System.Diagnostics.EventLog`/`EventLogReader`).

**Scope:** local machine only (no remote log support), and no
`DeleteEventSource` - source/log deletion is intentionally out of scope.

## Types

### `EventLogLevel`
A repository-owned severity/level, unifying the classic write-side
`System.Diagnostics.EventLogEntryType` with the modern read-side
`System.Diagnostics.Eventing.Reader.StandardEventLevel` and the
`SuccessAudit`/`FailureAudit` keywords neither expresses alone:
`LogAlways`, `Critical`, `Error`, `Warning`, `Informational`, `Verbose`,
`SuccessAudit`, `FailureAudit`.

### `EventLogEntryData`
The JSON shape produced by every `*Json`/JSON-returning method: `TimeCreatedIso8601`,
`RecordId`, `EventId`, `Level`, `LevelDisplayName`, `ProviderName`, `LogName`,
`MachineName`, `Message`, `TaskDisplayName`.

## Constructors

| Constructor | Description |
|---|---|
| `EventLogUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `EventLogUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Discovery

| Method | Signature | Description |
|---|---|---|
| `ListLogNames` | `List<string> ListLogNames()` | Gets the names of every event log on the local machine. Empty list on failure. |
| `ListLogNamesDelimited` | `string ListLogNamesDelimited(string delimiter = ",")` | Same, joined into a single delimited string, for designers without a `List<string>` proxy. |
| `DoesLogExistSimple` | `bool DoesLogExistSimple(string logName, out string message)` | Returns True if a log with the given name exists. |
| `DoesLogExist` | `bool DoesLogExist(string logName, out bool querySucceeded, out string message)` | Same, plus a `querySucceeded` output. |
| `DoesSourceExistSimple` | `bool DoesSourceExistSimple(string sourceName, out string message)` | Returns True if an event source with the given name is registered. |
| `DoesSourceExist` | `bool DoesSourceExist(string sourceName, out bool querySucceeded, out string message)` | Same, plus a `querySucceeded` output. |
| `TryGetLogNameForSource` | `bool TryGetLogNameForSource(string sourceName, out string logName, out string message)` | Finds which log a registered source currently writes to. |
| `CreateEventSource` | `bool CreateEventSource(string sourceName, string logName, out bool alreadyExisted, out string message)` | Registers a new event source, creating the log if needed. Requires administrator rights. |
| `CreateEventSourceSimple` | `bool CreateEventSourceSimple(string sourceName, string logName, out string message)` | Same, without the `alreadyExisted` output. |

### Query

| Method | Signature | Description |
|---|---|---|
| `TryGetMostRecentEntry` | `bool TryGetMostRecentEntry(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601, out string timeCreatedIso8601, out int eventId, out string level, out string source, out string entryMessage, out string message)` | Finds the single most recent entry matching the given filters. False + null message means no match; False + a message means the query failed. |
| `CountMatchingEntries` | `bool CountMatchingEntries(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601, out int count, out string message)` | Counts entries matching the given filters. |
| `QueryByXPath` | `bool QueryByXPath(string logName, string xpath, int maxCount, out string json, out string message)` | Queries a live log with a raw `EventLogQuery` XPath expression, for filters the scalar helpers can't express. Returns a JSON array, newest first. |
| `QueryRecentEntriesJson` | `bool QueryRecentEntriesJson(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601, int maxCount, out string json, out string message)` | The main bulk-read method - scalar filters, JSON array of matches, newest first. |
| `DumpRecentEntriesJson` | `bool DumpRecentEntriesJson(string logName, int count, out string json, out string message)` | Unfiltered "last N entries" convenience. |

### Write

| Method | Signature | Description |
|---|---|---|
| `WriteEntry` | `bool WriteEntry(string sourceName, string message, EventLogLevel level, int eventId, out string errorMessage)` | Writes an entry via the given source. Does not create the source - call `CreateEventSource` first. |
| `WriteEntrySimple` | `bool WriteEntrySimple(string sourceName, string message, out string errorMessage)` | Same, defaulting to `Informational` level and event id 0. |

### Wait

| Method | Signature | Description |
|---|---|---|
| `WaitForEntry` | `bool WaitForEntry(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, int timeoutMs, int pollIntervalMs, out bool timedOut, out string timeCreatedIso8601, out int eventId, out string entryMessage, out string message)` | Polls until a new entry matching the given filters appears, or the timeout elapses. Only entries created after the call started are eligible. |
| `WaitForEntrySimple` | `bool WaitForEntrySimple(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, int timeoutMs, int pollIntervalMs, out string message)` | Same, without the `timedOut`/entry outputs. |

### Export

| Method | Signature | Description |
|---|---|---|
| `ExportFilteredLog` | `bool ExportFilteredLog(string logName, string xpath, string exportFilePath, out string message)` | Exports entries matching an XPath filter from a live log to a `.evtx` file. |
| `QueryExportedLog` | `bool QueryExportedLog(string evtxFilePath, string xpath, int maxCount, out string json, out string message)` | Queries a previously exported `.evtx` file with a raw XPath filter. |

## Notes & Caveats

- **Never throws.** Invalid input (null/empty log/source name, a negative
  timeout, an unknown filter value, an undefined `EventLogLevel`) and
  runtime failures (a missing log, insufficient rights, a malformed exported
  file) return `False` with a descriptive message.
- **Elevation requirements.** `CreateEventSource`/`CreateEventSourceSimple`
  require administrator rights (a one-time, machine-wide registry
  registration). Reading the **Security** log, and sometimes writing/
  checking sources depending on registry ACLs, requires elevation or
  membership in the "Event Log Readers" group - `UnauthorizedAccessException`/
  `SecurityException` are translated into a specific, actionable message
  rather than a generic failure.
- **`CreateEventSource` cannot reassign a source to a different log.**
  Windows only allows a source to be registered to one log for its
  lifetime. Calling it again with a different `logName` for an existing
  source is a hard failure (not an error you can retry your way out of);
  the only fix is deleting and recreating the source outside this
  component - `DeleteEventSource` is intentionally not provided here.
- **`SuccessAudit`/`FailureAudit` are inferred from `Keywords`, not
  `Level`.** The modern read API (`EventRecord`) does not expose these as a
  `Level` value - `EventLogLevel` unifies both worlds, and the internal
  filter builder uses an XPath `band(Keywords, ...)` condition (against the
  real `StandardEventKeywords.AuditSuccess`/`AuditFailure` values) for these
  two levels instead of a `Level=` condition.
- **`WriteEntry`/`WriteEntrySimple` truncate messages** longer than the
  practical ~32K character limit of the classic write API, reporting the
  truncation via `errorMessage` even though the write still succeeds
  (`errorMessage` is `null` only on a clean, untruncated success).
- **`messageContains` is applied after reading each candidate record**, not
  via XPath - the rendered message text isn't part of a record's queryable
  XML structure. `QueryByXPath`'s raw `xpath` parameter can filter on
  structural fields (`EventID`, `Provider`, `TimeCreated`, `Level`,
  `EventData` payload fields) but not on message text.
- **`CountMatchingEntries` materializes every match before counting** - for
  a very large log (Security can have hundreds of thousands of records),
  narrow the filter (especially `sinceIso8601`) rather than counting
  unfiltered.
- **Disposal.** Every query method opens and disposes its own
  `EventLogReader`/`EventLogQuery`/`EventRecord` instances internally, even
  when a filter or read error aborts partway through - callers never
  receive a live, disposable object.
- **`Query*`/`WaitForEntry*` all query newest-first** (`EventLogQuery.ReverseDirection = true`)
  so "most recent"/"just happened" lookups on a large log don't scan
  forward from the oldest record.
- **Naming convention: `Simple` suffix marks the less-disambiguated
  overload.** Where two overloads would otherwise share an identical
  Pega-visible (non-`out`) parameter list, the overload with the extra
  disambiguating output keeps the plain name, and its sibling gets a
  `Simple` suffix. See `project-docs/pega-usability-reviews/EventLogUtils-pega-usability-review.md`.
- **Local machine only.** No `machineName`/remote-log parameters in this
  version.
- **Guard tests.** `EventLogUtils.Tests` (in this folder) covers the
  null/empty-argument, negative-timeout, undefined-enum, and filter-parsing/
  XPath-building guards. It runs on Linux too - see `TESTING.md` at the
  repo root.
