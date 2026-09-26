# ReconciliationUtils Pega usability review

This review covers the public surface as designed in
`project-docs/plans/2026-09-25-reconciliationutils-design-v2.md` and as implemented. The
design was shaped for the Robot Studio component tray and design surface from the start:
the alternatives it replaced were a collection proxy per dataset and a nested index loop
over differences.

## Summary

Every port is a scalar (`string`, `bool`, `int`) or the `ComparisonNullPolicy` enum. No
collection, object or generic type crosses the public surface. Datasets go in as JSON text
(typically the output of an existing JSON/Excel/SQL step); the definition goes in as
method calls or one JSON text; results come out as counts, two scalar cursors, or JSON.

| Area | Rating | Notes |
|---|---|---|
| Definition | Direct | `Add...Simple` methods take three strings; the full forms add `bool` trim/ignore-case and a drop-down null policy; the whole definition can be one JSON asset validated with `ValidateDefinitionJson`. |
| Running | Direct | One call, `ReconcileJson(left, right)`; `True` means the run completed even with mismatches, so a step never depends on parsing `message`. |
| Summary | Direct | `GetSummary` returns four `int` ports; `GetSummaryJson` returns the rest. |
| Exceptions | Direct | `TryReadNextException` follows the `TryTakeNext` idea: `hasItem` is the `While` condition, `kind` feeds a `StringSwitch`, row indices are `int` with a documented `-1`. |
| Differences | Direct | `TryReadNextDifference` is an inner cursor over the exception just read, replacing a `GetDifferenceCount`/`GetDifferenceAt(i)` index loop. |
| Detail | Direct | `GetResultJson(resultId)` for what scalars cannot carry (every member of a duplicate group). |

## Findings applied

- **Two cursors instead of an index API.** Robot Studio loops cleanly on a `hasItem` flag;
  a nested counted loop needs extra counter variables and easy off-by-one mistakes. Exhausted
  cursors return `True` with `hasItem` false, so end-of-data is not an error path.
- **"Completed with mismatches" is a success.** `False` plus `message` is reserved for
  operational failures (bad JSON, a limit, no keys, no results, disposal), matching
  `Fire`/`TryTakeNext` in the sibling components.
- **Stable codes, not prose, for routing.** `kind` and `reason` are fixed identifiers; the
  human explanation is separate and never quotes data.
- **Failure sentinels are uniform.** Null strings, 0 counts, `False` flags, `-1` indices, so a
  data link never carries a stale value from a previous successful call.
- **Missing versus null is preserved.** A missing value is a null string port; a JSON null is
  the text `null`, so a `Switch` can tell them apart.
- **No overloads or optional parameters.** `Simple` and full forms have distinct names, so the
  design surface never has to choose a signature.
- **Bad input never throws and never echoes data.** Messages name the operation, side, reason
  and location, not values, so logs and screenshots of error ports do not leak row contents.
- **Results are immutable and replaced whole.** A failed run leaves nothing to process by
  accident; a setup change discards results, so a stale exception list cannot be read.

## Remaining friction

- The exception cursor is stateful (one per component instance). Two independent readers of
  the same results need two instances or a `ResetResultCursor` between passes; this is
  documented in ResultsAndCounts and is the price of scalar ports.
- Datasets must arrive as JSON text. Until the DataTable bridge (planned, Release 2), a
  DataTable must be serialized first.
- Pointers are typed strings; a typo is caught only when a run reports the field missing
  (`MissingField`), not at design time. `ValidateDefinitionJson` catches malformed pointers.
