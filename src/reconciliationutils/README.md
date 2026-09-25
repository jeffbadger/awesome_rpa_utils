# ReconciliationAutomation

A Pega Robot Studio-ready component (`ReconciliationUtils`) that reconciles two
datasets by business key and explains every disagreement, exposing exceptions
through scalar ports so an automation can route them to review or correction.

> **Status: under construction.** This folder currently contains the frozen public
> contract only (work package 1 of the
> [design plan](../../project-docs/plans/2026-09-25-reconciliationutils-design-v2.md)):
> every method exists with its final signature but reports that it is not
> implemented yet. Do not use it until the first release notes say otherwise.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `ReconciliationAutomation`
- Assembly: `ReconciliationAutomation`

## Method reference (frozen contract)

All 19 Release 1 methods, with their final signatures. **None of them is implemented
yet**: each returns `False` with a message saying so, and sets every output to its
failure value (null strings, 0 counts, `False` flags, -1 row indices). Behavior arrives
in the later work packages of the
[plan](../../project-docs/plans/2026-09-25-reconciliationutils-design-v2.md), and this
table gains defaults, worked examples and the full method reference then.

### Definition

| Method | Signature | Description |
|---|---|---|
| `ClearDefinition` | `bool ClearDefinition(out string message)` | Restores the default definition (no keys, no comparisons, default limits) and clears any results. |
| `AddKeyMappingSimple` | `bool AddKeyMappingSimple(string name, string leftPointer, string rightPointer, out string message)` | Adds a business-key part matched exactly (no trimming, case-sensitive). The pointers are restricted JSON Pointers such as /invoiceNumber. |
| `AddKeyMapping` | `bool AddKeyMapping(string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase, out string message)` | Adds a business-key part with a choice of trimming and case-insensitive matching. The pointers are restricted JSON Pointers such as /invoiceNumber. |
| `AddTextComparisonSimple` | `bool AddTextComparisonSimple(string name, string leftPointer, string rightPointer, out string message)` | Adds an exact text comparison (no trimming, case-sensitive, a value is required on both sides). |
| `AddTextComparison` | `bool AddTextComparison(string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase, ComparisonNullPolicy nullPolicy, out string message)` | Adds a text comparison with a choice of trimming, case-insensitive comparison and null policy. |
| `AddDecimalComparisonSimple` | `bool AddDecimalComparisonSimple(string name, string leftPointer, string rightPointer, out string message)` | Adds an exact decimal comparison (tolerance 0, a value is required on both sides). |
| `AddDecimalComparison` | `bool AddDecimalComparison(string name, string leftPointer, string rightPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy, out string message)` | Adds a decimal comparison with an absolute tolerance given as invariant decimal text (for example 0.01) and a null policy. |
| `LoadDefinitionJson` | `bool LoadDefinitionJson(string definitionJson, out string message)` | Replaces the whole definition from JSON. An invalid definition is rejected whole and the previous one stays in force. |
| `GetDefinitionJson` | `bool GetDefinitionJson(out string definitionJson, out string message)` | Returns the current definition, including limits, as canonical JSON. |
| `ValidateDefinitionJson` | `bool ValidateDefinitionJson(string definitionJson, out int errorCount, out string reportJson, out string message)` | Validates a JSON definition without loading it. Returns True when validation ran; errorCount is 0 for a valid definition and reportJson lists the findings. |
| `ConfigureLimits` | `bool ConfigureLimits(int maximumRowsPerSide, int maximumInputCharactersPerSide, int maximumResults, int maximumDifferenceDetails, out string message)` | Sets the resource limits: rows per side, input characters per side, result records and difference details. A run that exceeds a limit fails whole. |

### Run

| Method | Signature | Description |
|---|---|---|
| `ReconcileJson` | `bool ReconcileJson(string leftJson, string rightJson, out int exceptionCount, out string message)` | Reconciles two JSON arrays of objects. True means the run completed, even with mismatches; exceptionCount is 0 when everything matched. |

### Results

| Method | Signature | Description |
|---|---|---|
| `GetSummary` | `bool GetSummary(out int leftRowCount, out int rightRowCount, out int matchedPairCount, out int exceptionCount, out string message)` | Returns the headline counts of the last completed run. |
| `GetSummaryJson` | `bool GetSummaryJson(out string summaryJson, out string message)` | Returns every count of the last completed run as JSON. |
| `ResetResultCursor` | `bool ResetResultCursor(out string message)` | Restarts exception and difference reading from the first exception. |
| `TryReadNextException` | `bool TryReadNextException(out bool hasItem, out string resultId, out string kind, out string keyJson, out int leftRowIndex, out int rightRowIndex, out string reason, out int differenceCount, out string message)` | Reads the next exception of the last run. hasItem is False when there are no more. kind is a stable code such as Different or OnlyLeft; a row index is -1 when absent or ambiguous. |
| `TryReadNextDifference` | `bool TryReadNextDifference(out bool hasItem, out string ruleName, out string reasonCode, out string leftValueJson, out string rightValueJson, out string explanation, out string message)` | Reads the next field difference of the exception most recently read. hasItem is False when there are no more. A missing value is null; a JSON null is the text null. |
| `GetResultJson` | `bool GetResultJson(string resultId, out string resultJson, out string message)` | Returns the full detail of one result, including every member of a duplicate-key group. |
| `ClearResults` | `bool ClearResults(out string message)` | Discards the last run's results. Succeeds even when there are none. |

`ComparisonNullPolicy` is a drop-down: `RequireValue` (the default: a null on either
side is invalid) or `AllowBothNull` (two nulls are equal).

## Not released yet

This component is registered in the solution so it builds and its tests run in CI, but
it is **not** part of any release: no release will be cut while it is incomplete (the
project owner's decision). It is deliberately not yet listed in the root `README.md` or
`CrossReference.md`, and not in `scripts/Package-Release.ps1`; the plan's documentation
work package (WP6) does all of that when the component is finished.
