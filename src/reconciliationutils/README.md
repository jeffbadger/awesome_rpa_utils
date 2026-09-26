# ReconciliationAutomation

A Pega Robot Studio-ready component (`ReconciliationUtils`) that reconciles two
datasets by business key and explains every disagreement, exposing exceptions
through scalar ports so an automation can route them to review or correction.

> **Status: under construction.** Work package 2 of the
> [design plan](../../project-docs/plans/2026-09-25-reconciliationutils-design-v2.md)
> is in: the **definition** operations work (setup methods, JSON load/validate/export,
> limits). `ReconcileJson` and the result-reading methods still report that they are not
> implemented yet. Do not use the component until the first release notes say otherwise.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `ReconciliationAutomation`
- Assembly: `ReconciliationAutomation`

## Method reference (frozen contract)

All 19 Release 1 methods, with their final signatures. The **Definition** methods are
implemented. The **Run** and **Results** methods are not yet: each returns `False` with a
message saying so. On any failure a method sets every output to its failure value (null
strings, 0 counts, `False` flags, -1 row indices) and returns a message naming the
operation; success has no message. The
[plan](../../project-docs/plans/2026-09-25-reconciliationutils-design-v2.md) adds the rest.

## Defining a reconciliation

A definition has **keys** (which fields identify a record), **comparisons** (which fields
to compare once two records share a key) and **limits**. Build it with the `Add...`
methods, or load it as JSON (`LoadDefinitionJson`, validated whole: a definition with any
problem is rejected and the current one stays). `GetDefinitionJson` returns the canonical
form, with every option spelled out; a definition built with methods and the same one
loaded from JSON produce identical text.

```json
{
  "schemaVersion": 1,
  "keys": [
    { "name": "Company", "leftPointer": "/company", "rightPointer": "/entity", "trim": true, "ignoreCase": true },
    { "name": "Invoice", "leftPointer": "/invoiceNumber", "rightPointer": "/invoiceId" }
  ],
  "comparisons": [
    { "name": "Amount", "kind": "Decimal", "leftPointer": "/amount", "rightPointer": "/paidAmount", "absoluteTolerance": "0.01" },
    { "name": "Status", "kind": "Text", "leftPointer": "/status", "rightPointer": "/status", "trim": true, "ignoreCase": true }
  ]
}
```

- **Pointers** select a field: a leading `/` and property names, `/company` or `/customer/id`.
  Use `~1` for a `/` and `~0` for a `~` inside a name. Lookup is case-sensitive and a dot has
  no special meaning; arrays cannot be walked into.
- **Keys** may be JSON strings or JSON **integers** (an integer is used as its exact text, so
  `123` and `"123"` are the same key, but `7` and `"007"` are not). A fraction, an exponent,
  a Boolean, an object or an array as a key makes that row invalid, as does a missing, null or
  empty key. Keys with several parts are matched as ordered tuples, never joined into one string.
- **Simple** methods use the safe defaults: no trimming, case-sensitive, tolerance `0`, and a
  value required on both sides. Names must be unique across keys and comparisons (ignoring case).
- **Tolerance** is non-negative decimal *text* such as `0` or `0.01`.
- Unknown properties, repeated properties, wrong types and numeric enum values are errors. A definition is read with the same depth bound as input (64 levels; a real one is 3 deep), reported as `DepthLimit`.
  `ValidateDefinitionJson` reports every problem (path, code, message; up to 100) without
  changing anything: `errorCount` is 0 for a valid definition.
- Text must be valid Unicode: an unpaired surrogate (a lone half of an emoji-style pair, whether written as a raw character or as a JSON escape like `\uD800`) is refused in a definition, a name or a pointer (`InvalidText`), and in input it rejects the document (raw character or property name) or makes just that value unsupported data (escaped in a value), so a bad string is never silently turned into a different one. Trimming removes Unicode white space.
- Input JSON is read with bounds (characters, depth 64, rows, and 256 characters per number), rejects repeated property names
  anywhere, and never echoes the data in an error message.

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
