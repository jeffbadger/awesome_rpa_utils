# JsonUtils Phase 2 — Design

**Goal:** Implement the four capability groups explicitly deferred as
"Phase 2" in the original `JsonUtils` design
([2026-09-08-jsonutils-design.md](2026-09-08-jsonutils-design.md)):
`MergeJson`, `DiffJson`, JSON↔XML conversion, and array filter/sort helpers.
This is an extension of the existing `JsonUtils` component (`src/jsonutils/`,
assembly `JsonAutomation`) — no new component, no new `.csproj`, no `.sln`
changes. PR #77 (Phase 1) is already merged to `main`.

**Non-goals:** JSON Schema validation remains excluded (not deferred) for
the same reason as Phase 1 — Newtonsoft's schema validator is a separate
commercially-licensed package. A JSON Patch (RFC 6902) diff format was
considered and rejected in favor of a delimited differing-paths list,
matching this suite's existing convention (see Design Decisions below).

## Design decisions (resolved via brainstorming before this doc was written)

- **`TryMergeJson`'s conflict rule**: the second document (`overrideJson`)
  wins on scalar conflicts — the common "apply overrides on top of a base"
  use case. Arrays present in both documents concatenate (base's elements
  then override's), matching Newtonsoft's own `MergeArrayHandling.Concat`
  default rather than replacing or merging by index (replace-by-index is
  surprising for simple value arrays and mismatched lengths).
- **`TryDiffJson`'s output shape**: a delimited list of differing JSONPath
  strings, not a JSON Patch document. Matches this suite's existing
  delimited-list convention (`TryGetValuesFromJson`) and is easier for a
  Robot Studio developer to consume than a new patch format this suite has
  never used elsewhere. The outer `bool` return follows the suite's
  established "did the operation itself succeed" convention (`false` only
  on malformed JSON input) — NOT "are the documents equal." The actual
  comparison result lives in the `areEqual`/`differingPaths` out parameters,
  the same separation-of-concerns `TryGetArrayLength` and similar methods
  already use (operation success vs. domain answer).
- **`TryConvertJsonToXml`'s root element name**: a required parameter, not
  optional/auto-detected. Newtonsoft's `JsonConvert.DeserializeXmlNode`
  needs an explicit root name whenever the JSON's top level isn't already a
  single-rooted object (a bare array, or an object with multiple top-level
  keys); making it always-required avoids two different code paths to
  document and get right.

## Method surface

All six new methods follow the established never-throws contract (`bool`
return + trailing `out string message`) and use the shared `ParseJson`
helper (never raw `JToken.Parse`) for any JSON text they read.

**Merge** (new category `Json - Merge`):

| Method | Purpose |
|---|---|
| `TryMergeJson(string baseJson, string overrideJson, out string mergedJson, out string message)` | Merges two JSON objects; `overrideJson` wins on scalar conflicts, arrays concatenate. Fails if either input isn't a JSON object at the root. |

**Compare** (new category `Json - Compare`):

| Method | Purpose |
|---|---|
| `TryDiffJson(string json1, string json2, string delimiter, out bool areEqual, out string differingPaths, out string message)` | Compares two JSON documents; `areEqual` is the comparison result, `differingPaths` is a delimited list of paths where values differ (empty when equal). Returns `false` only if either input is malformed JSON. |

**Convert** (new category `Json - Convert`):

| Method | Purpose |
|---|---|
| `TryConvertJsonToXml(string json, string rootElementName, out string xml, out string message)` | Converts JSON to XML text via Newtonsoft's `JsonConvert.DeserializeXmlNode`, wrapping the result under `rootElementName`. |
| `TryConvertXmlToJson(string xml, out string json, out string message)` | Converts XML text to JSON via `JsonConvert.SerializeXmlNode`. No root name needed — XML always has exactly one root. |

**Array filter/sort** (extends existing category `Json - Array`):

| Method | Purpose |
|---|---|
| `TryFilterJsonArrayByField(string json, string path, string fieldName, JsonComparisonOperator comparisonOperator, string value, out string filteredJson, out string message)` | Filters an array at `path` to elements whose `fieldName` matches `value` under `comparisonOperator`. |
| `TrySortJsonArrayByField(string json, string path, string fieldName, bool ascending, out string sortedJson, out string message)` | Sorts an array at `path` by `fieldName`'s value. |

**New repo-owned enum**, `JsonComparisonOperator` (mirrors the
`JsonValueKind` pattern from Phase 1 — a small, purpose-built enum rather
than exposing a library type):

```csharp
public enum JsonComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains
}
```

`Contains` applies to string fields (substring match); the four ordering
operators use `JValue`'s own `IComparable` semantics (numeric-aware when
both sides are numeric, ordinal string comparison otherwise) via
`JToken.CompareTo`; `Equals`/`NotEquals` use `JToken.DeepEquals` against the
field value parsed from the caller-supplied `value` string through the
field's own token type (so `"30"` compares equal to a JSON number `30`, not
just to the JSON string `"30"`).

## Architecture

- No new `.csproj`/`.sln` changes — this extends the existing
  `src/jsonutils/JsonUtils.cs` with a 6th, 7th, and 8th-ish `#region` block
  (actually: `Merge`, `Compare`, `Convert` as three new regions; filter/sort
  are added to the existing `Array and removal` region, renamed in
  documentation-only prose to reflect the broader scope — the region
  marker itself can stay `Array and removal` since C# region names aren't
  part of the public contract and renaming it would be a no-op diff noise
  generator for zero benefit).
- New file `src/jsonutils/JsonComparisonOperator.cs` for the new enum,
  matching the precedent of `JsonValueKind.cs` living in its own file.
- `TryDiffJson`'s path-walking comparison is genuinely new logic (not a
  thin wrapper over a Newtonsoft one-liner like nearly everything in
  Phase 1) — implemented as a private recursive helper,
  `CollectDifferingPaths(JToken left, JToken right, string currentPath, List<string> differingPaths)`,
  walking `JObject` keys (union of both sides' property names) and `JArray`
  indices (up to the longer side's length; a missing index on the shorter
  side counts as a difference), and using `JToken.DeepEquals` at the leaves.
  This is the one method in this pass warranting closer implementation-plan
  detail and more thorough test coverage than a typical "wrap one
  Newtonsoft call" method.
- `TryConvertJsonToXml`/`TryConvertXmlToJson` use `System.Xml.XmlDocument`
  internally but never expose it publicly — both methods are string-in,
  string-out, keeping the component's string-based JSON-processing
  convention consistent even where the intermediate representation is XML.
  Needs `using System.Xml;` added to `JsonUtils.cs`.

## Testing

Same TDD-per-method-group approach as Phase 1: input-guard tests
(malformed JSON, non-object root for `TryMergeJson`, malformed XML) plus
real behavioral tests for each method. `TryDiffJson` gets the most test
cases given it's genuinely new logic: equal documents, a changed scalar, an
added/removed key, a changed array element, an added/removed array element,
and nested-path differences. `TryFilterJsonArrayByField` gets one test per
`JsonComparisonOperator` value.

## Documentation

- `src/jsonutils/README.md`'s method table gains 6 new rows; the JSONPath
  syntax / Typical workflow / Notes & Caveats sections get one addition
  each (a worked merge/diff/convert/filter-sort example; a caveat on
  `TryConvertJsonToXml`'s root-name requirement; a caveat documenting
  `TryDiffJson`'s path ordering — a deterministic pre-order walk of
  `json1`'s structure, with any keys/indices that exist only in `json2`
  appended after all of `json1`'s keys at that level. Not alphabetical, not
  guaranteed stable across a Newtonsoft version bump if its own property
  enumeration order ever changed, but deterministic for a given input pair
  on a given Newtonsoft version).
- No root `README.md`/`Package-Release.ps1` changes needed — the component
  and assembly already exist and are already registered.

## Suite conventions followed

- Never-throws contract, `ParseJson` for all reads, `[Category]`/
  `[Description]` on every new public method, repo-owned enum for
  `JsonComparisonOperator` (not exposing a library-specific type).
- New work happens in `.worktrees/jsonutils-phase2` on branch
  `jsonutils-phase2`, PR'd and the worktree removed afterward — same
  workflow as Phase 1, forked from the now-current `main` (which already
  contains the merged Phase 1 component).
