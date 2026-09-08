# JsonUtils — Design

**Goal:** Add a new `JsonUtils` component (`src/jsonutils`) as a full
replacement for Pega Robotics' native `Json` component (`DeserializeObject`,
`GetValueFromJSON`, `SerializeObject`, `SetValueInJSON`), built on
Newtonsoft.Json so path-based get/set support real JSONPath (wildcards,
recursive descent, filter expressions) instead of dot-notation-only, plus a
set of scalar-getter and array/validation gap-fillers the native component
has no equivalent for. Not a review-cycle pass — this is a brand-new
component, following the precedent set by `EventLogUtils` (PR #63).

**Non-goals for this pass (phase 2):** `MergeJson`, `DiffJson`, JSON↔XML
conversion, and array filter/sort helpers (`FilterJsonArrayByField`,
`SortJsonArrayByField`). These are real candidates but out of scope here to
keep this PR reviewable; revisit as a follow-on component pass once this
lands. JSON **Schema** validation is deliberately excluded, not deferred:
Newtonsoft's own schema validator (`Newtonsoft.Json.Schema`) is a separate
commercially-licensed package, unlike Json.NET itself — adding it would mean
either paying for a license or pulling in a different (unvetted) free schema
library, neither of which is worth doing just for this method.

## Architecture

- New `src/jsonutils/JsonUtils.csproj`:
  - `TargetFrameworks net8.0-windows;net10.0-windows`, `LangVersion latest`,
    `ImplicitUsings disable`, `Nullable disable` — matches every other
    component in the suite.
  - `AssemblyName`/`RootNamespace` = `JsonAutomation` (the suite's
    `<Name>Automation` convention: `DialogAutomation`, `ServiceAutomation`,
    `EventLogAutomation`, etc.).
  - `EnableWindowsTargeting true`, `GenerateDocumentationFile true`,
    `Platforms AnyCPU;x64`.
  - `PackageReference Include="Newtonsoft.Json"` — the suite's **second**
    external NuGet dependency (after `ServiceUtils`'s
    `System.ServiceProcess.ServiceController`). README Notes & Caveats gets
    the same "why this dependency" callout `ServiceUtils` uses: Json.NET's
    `SelectToken`/`SelectTokens` are what make real JSONPath (as opposed to
    dot-notation-only) possible without hand-rolling a path parser.
  - Own `NeverThrowsGuard.cs` (per-component copy, matching every other
    component — no shared internal library between components in this
    repo).
  - `JsonUtils.Tests/` subfolder, `<Compile Remove="JsonUtils.Tests/**/*.cs" />`
    in the csproj, registered in `src/AwesomeRpaUtils.sln` (project + 12
    config lines + nesting).
  - `InternalsVisibleTo Include="JsonUtils.Tests"` if any private helper
    (e.g. a path-parsing or flatten-walk helper) ends up worth testing
    directly rather than only through the public surface.
- No Win32/Windows-only APIs anywhere in this component — it's pure
  JSON/string logic over Newtonsoft's `JToken`/`JObject`/`JArray`. Like
  `ServiceUtils`/`EventLogUtils`, `JsonUtils.Tests` should be fully
  Linux-runnable despite the Windows-flavored TFM (verify with `dotnet test`
  on this host, not just `dotnet build`).

## Method surface

Every method follows the suite's never-throws contract: `bool` return +
trailing `out string message`; invalid JSON, a path that doesn't resolve, or
a type mismatch at a resolved path all produce `false` + a descriptive
message, never an exception.

**Native parity** (same 4 capabilities as Pega's `Json` component, rebuilt
on Newtonsoft's JSONPath):

| Method | Purpose |
|---|---|
| `TryDeserializeObject<T>(string json, out T result, out string message)` | JSON string → typed object |
| `TrySerializeObject(object value, out string json, out string message)` | object → JSON string |
| `TryGetValueFromJson(string json, string path, out string value, out string message)` | single value at a JSONPath, as string |
| `TrySetValueInJson(string json, string path, string value, out string updatedJson, out string message)` | update a value at a JSONPath |

**Gap-fillers** (this pass):

| Method | Purpose |
|---|---|
| `IsValidJson(string json, out string message)` | never-throws parse check |
| `TryGetStringValue` / `TryGetIntValue` / `TryGetBoolValue` / `TryGetDoubleValue` / `TryGetDateTimeValue(string json, string path, out T value, out string message)` | typed scalar getters at a path — no caller-side string parsing |
| `TryGetValuesFromJson(string json, string path, string delimiter, out string delimitedValues, out string message)` | multi-match (wildcard/filter paths, via `SelectTokens`) joined into one delimited string — the suite's established `ListXNamesDelimited` pattern (see `ServiceUtils`) applied to JSONPath results |
| `TryRemoveValueFromJson(string json, string path, out string updatedJson, out string message)` | delete a node at a path |
| `TryGetArrayLength(string json, string path, out int length, out string message)` | element count at a path |
| `TryAppendToJsonArray(string json, string path, string valueJson, out string updatedJson, out string message)` | append a JSON-fragment element to an existing array |
| `TryGetValueType(string json, string path, out JsonValueKind kind, out string message)` | reports a **repo-owned** `JsonValueKind` enum (`String`, `Number`, `Boolean`, `Array`, `Object`, `Null`, `NotFound`) — mirrors `ServiceUtils`'s repo-owned `ServiceStatus` enum so Newtonsoft's own `JTokenType` doesn't leak into the public contract |
| `TryPrettyPrintJson(string json, out string formattedJson, out string message)` | indented formatting |
| `TryMinifyJson(string json, out string minifiedJson, out string message)` | whitespace-stripped formatting |

**On `SerializeObject`'s native `⚠@default=SingleOutput` annotation:** Pega's
own component collapses that method to a single visible output port in the
designer. `TrySerializeObject` keeps the standard bool+out signature for
consistency with every other method in this suite and every other component;
the README documents that Robot Studio's designer can still be configured to
show only the `json` output if a single-port surface is wanted, without that
being baked into the method signature itself.

## Testing

Input-guard tests for every method above: malformed JSON, a path that
doesn't resolve, and a type mismatch at a resolved path (e.g.
`TryGetIntValue` against a string node). Plus real round-trip tests:
serialize → deserialize, set → get, append → length, remove → get-returns-
not-found. Standard xunit project layout matching `ServiceUtils.Tests`.

## Documentation

- `src/jsonutils/README.md` with the standard 3-column method table
  (`Method | Signature | Description`) and a Notes & Caveats section
  covering: the Newtonsoft.Json dependency and why, a short JSONPath syntax
  primer (dot/bracket, wildcards, filters) with 3-4 worked examples, the
  `SerializeObject` single-output-port note above, and an explicit "Phase 2"
  callout listing `MergeJson`/`DiffJson`/XML conversion/array filter-sort as
  deferred, plus the JSON Schema exclusion and why.
- Root `README.md` gains the new component's one-line entry, matching the
  existing per-component list.
- `scripts/Package-Release.ps1`'s `$releaseAssemblies` array — hardcoded,
  not auto-discovered — needs `JsonAutomation` added manually (the gotcha
  caught post-merge on `EventLogUtils`, PR #63). `Package-Documentation.ps1`
  needs no change (auto-discovers via `Get-ChildItem` globs).

## Suite conventions followed

- Never-throws contract: `bool` + `out string message`.
- `<Name>Automation` assembly/namespace naming.
- Per-component `NeverThrowsGuard.cs`, no shared internal library between
  components.
- Test project in a subfolder with `<Compile Remove>`, registered in the
  `.sln`.
- New work happens in `.worktrees/jsonutils` on branch `add-jsonutils`,
  PR'd and the worktree removed afterward — the established new-component
  workflow (see `EventLogUtils`, PR #63).
