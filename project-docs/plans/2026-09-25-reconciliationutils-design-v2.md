# ReconciliationUtils — Design and Implementation Plan (revision 2)

**Status:** Approved revision of
[`2026-09-25-reconciliationutils-design.md`](2026-09-25-reconciliationutils-design.md);
implementation proceeds one work package (one pull request) at a time, starting with WP1. This revision changes scope and API shape after
a review of the first plan; the matching semantics, accounting, exactness and
failure rules of the first plan are kept unless stated here.

**Goal:** Add a standalone Pega Robot Studio component that reconciles two
datasets by business key, explains disagreements, and exposes exceptions through
scalar ports so an automation can route them to review or correction.

**Component:** `ReconciliationUtils` in namespace/assembly
`ReconciliationAutomation`, under `src/reconciliationutils/`.

## 0. What changed from revision 1, and why

| Finding on revision 1 | Resolution in this revision |
|---|---|
| One release was too large (5 rule kinds, 2 date modes, exact-decimal engine, bounded export, ~20 methods, 12 files, 7 tasks). Reviews cost roughly one round per unit of surface. | **Two releases** (section 3). Release 1 ships the reconciliation core; Release 2 adds the DataTable bridge, Boolean, Money, dates and the full export. Each work package is its own pull request. |
| Blocks with several bool ports (`allMatched`, `valid`, `leftPresent`/`rightPresent` next to the result bool). | `ReconcileJson` and `GetSummary` output the integer `exceptionCount` only (all matched = 0). `ValidateDefinitionJson` outputs `errorCount`. Presence is carried by a null value string. The only bool outputs left are the cursor `hasItem` flags, the suite's established cursor pattern. |
| Heavy inputs (6, 7 and 8 parameters) and no "Simple" variants. | Every `Add...` method has a `...Simple` form with only name and the two pointers (plus the pointers/formats a rule cannot work without). Defaults are documented. |
| `toleranceUnits` meant days or seconds depending on an enum. | Two methods, `AddCalendarDateComparison` (tolerance in days) and `AddInstantComparison` (tolerance in seconds); the mode enum is gone. |
| Keys had to be JSON strings; ERP APIs often send numeric IDs. | A key part may also be a JSON **integer** (converted to its exact invariant text). Details in section 4. |
| Getting data in was the biggest adoption risk (CSV/Excel/DataTable out of scope). | Release 2 begins with a `ReconcileDataTables` bridge, following the precedent of `DataContractUtils`' DataTable methods. Release 1 is built around a row-reader seam so the bridge adds no matching code. Section 2 states the data-in path per release. |
| Nested index loop for differences (`GetDifferenceCount` + `GetDifferenceAt(i)`). | A second cursor, `TryReadNextDifference`, reads the differences of the exception just read, mirroring `TryReadNextException`. |
| Result kind was an enum output (untested port shape in Robot Studio). | `kind` is a stable string code (`Matched`, `Different`, ...), like `Fire`'s message: one Switch on a string. The enum stays internal. |
| Testing and CI text was out of date. | Aligned with `CONTRIBUTING.md` and the CI test step (section 1, section 10). |
| Packaging steps said "inspect" for a list that is known to be hardcoded. | The `$releaseAssemblies` list in `scripts/Package-Release.ps1` and the rest of the registration checklist are named work items (work package 6). |

## 1. Repository constraints

Follow [`CONTRIBUTING.md`](../../CONTRIBUTING.md), the
[Never-Throws Standard](../coding-standards/never-throws-standard.md), the
[Signature Uniqueness Standard](../coding-standards/signature-uniqueness-standard.md),
and the [Pega usability methodology](../pega-usability-reviews/README.md).

- Derive the public component from `System.ComponentModel.Component`; provide
  both parameterless and `IContainer` constructors.
- Target `net8.0-windows;net10.0-windows`, with `AnyCPU;x64`, XML documentation,
  and the project shape of `DataContractUtils` (including
  `<Compile Remove="ReconciliationUtils.Tests/**/*.cs" />` and the README pack item).
- Use the BCL and `System.Text.Json` (and, in Release 2, `System.Data` for the
  DataTable bridge); no new runtime NuGet dependencies, no Windows desktop
  framework dependency, no references to sibling utilities.
- Provide a project-local `NeverThrowsGuard`. Recoverable exceptions become
  `false` plus an actionable message; fatal conditions are outside the contract.
  Sanitize exception text (section 9): the shared guard's raw `ex.Message` can
  echo source data.
- Use scalar inputs/outputs, enums, JSON strings and, in Release 2, `DataTable`
  (as `DataContractUtils` does). No public dictionaries, delegates,
  `JsonElement` or custom result objects.
- Every public operation has a unique name (no overloads), XML documentation,
  `[Category]` and `[Description]`. Success is `true` with a `null` message.
- **Tests are required** (see CONTRIBUTING): a `ReconciliationUtils.Tests` xunit
  project (`net10.0-windows`, like the other test projects) registered in
  `src/AwesomeRpaUtils.sln`. CI runs the tests (one project at a time, on Windows)
  once the CI test-step change (PR #138) is merged; until then the test result
  pasted into each PR is the verification. Registering the project is what makes
  that CI step pick it up. Keep the core platform-independent so the suite also runs
  on Linux/macOS. Every behavior rule gets a test that was seen to fail without
  the code (mutation check); a bug fix gets a regression test.
- Tests are deterministic: no wall-clock assertions, no sleeps, no shared static
  state, injected clocks if any time is ever read (none is planned).

## 2. Business workflow and boundaries

Example: ERP invoices are the left dataset; a payment export is the right.
Match `company` plus `invoiceNumber`, compare amount and currency, and optionally
compare date/status fields. Results distinguish a missing payment from a
duplicate identifier, invalid amount, or actual amount difference.

| Key | Left | Right | Expected result |
|---|---|---|---|
| A / INV-100 | USD 125.00 | USD 125.00 | Matched |
| A / INV-101 | USD 250.00 | USD 249.00 | Different |
| A / INV-102 | USD 80.00 | absent | OnlyLeft |
| A / INV-103 | absent | USD 45.00 | OnlyRight |
| A / INV-104 | two records | one record | DuplicateKey; all three unresolved |
| A / INV-105 | amount `"unknown"` | USD 10.00 | InvalidComparison |

(Release 1 has no Money rule; it compares the amount with a Decimal rule and the
currency with a Text rule. Release 2's Money rule gates the amount on the currency.)

The component compares supplied snapshots. The caller owns source extraction,
business cutoff times, pagination, and whether both snapshots cover the same
population. "No exceptions" means the supplied records agree under the configured
rules; it does not certify completeness of either source system.

### How data gets in

| Release | Path |
|---|---|
| 1 | Two JSON arrays of objects (an API response, `JsonUtils` output, or JSON the automation builds). |
| 2 | `ReconcileDataTables` takes two `DataTable`s directly, so Excel/CSV/database results already loaded into a Robot Studio DataTable need no JSON step. Column names are the field names. |

CSV/Excel *parsing* stays outside this component in both releases; Robot Studio
already provides connectors that produce a DataTable. Do not make either release
depend on a not-yet-existing `DelimitedFileUtils`.

Integration stays in the consuming automation: obtain the data; use
`FileWatchUtils` first when coordinating externally produced files; configure and
run this component; inspect the summary and iterate exceptions; optionally enqueue
result JSON with `LocalQueueUtils` using a caller-owned reconciliation/run
identifier; use `StateMachineUtils` for the review/correction workflow; archive
inputs and the exported report with `ArchiveUtils` when required.

### Explicitly outside both releases

- Fuzzy matching, inferred field mappings, nearest-match suggestions.
- One-to-many/many-to-many matching, partial payments, grouped totals.
- Currency conversion, percentage tolerances, or automatic rounding.
- Arbitrary expressions, scripts, full JSONPath, array expansion.
- Native CSV/Excel/PDF/database/API connectors and direct file export.
- Automatic data correction, notifications, queue ownership, persistence.
- Streaming/external-sort processing of unbounded datasets.
- Async events, background reconciliation, shared-instance parallel execution.

## 3. Releases

| | Release 1 (core) | Release 2 |
|---|---|---|
| Input | JSON arrays | + `ReconcileDataTables` |
| Keys | composite string/integer keys, per-part trim/ignore-case | same |
| Rules | none (presence-only), **Text**, **Decimal** | + **Boolean**, **Money**, **CalendarDate**, **Instant** |
| Results | all seven kinds, summary, exception and difference cursors, `GetResultJson`, definition JSON | + `ExportResultsJson` (versioned envelope), output limit |
| Limits | rows, input characters, results, difference details | + output characters |

Release 1 is useful on its own: an amount is a Decimal rule and a currency a Text
rule. Splitting keeps each review small and lets real use shape the second release.
Public members added in Release 2 are purely additive; no Release 1 signature
changes.

## 4. Input and field-selection contract

`ReconcileJson` accepts two JSON strings, each a top-level array. Empty arrays
are valid. Array order supplies stable, zero-based source row indices.

- Invalid JSON, a non-array root, duplicate property names anywhere in an input
  object, or a resource-limit breach fails the entire operation.
- A non-object array item is a data exception (`InvalidRecord`) at its row index,
  not a malformed-document failure. It cannot enter the key index.
- Field references use a documented restricted JSON Pointer syntax: a leading `/`,
  object-property segments, `~0` for `~`, and `~1` for `/`. Empty root pointers,
  invalid escapes, and array traversal are unsupported. Empty property segments
  are valid; `/` selects an empty-name property. (In `ReconcileDataTables`, a
  pointer must be exactly one segment naming a column.)
- Property lookup is ordinal and case-sensitive. Literal dots in property names
  do not have special meaning. Intermediate null/scalar values mean the requested
  path is missing; an array encountered during traversal is unsupported data.
- Unreferenced fields are ignored for matching and comparison, but still count
  toward input-size/depth limits and duplicate-property validation.
- Parse with bounded depth; disallow comments and trailing commas.
- **Row-reader seam (internal):** matching and comparison read fields through an
  internal row-reader that reports *present / null / string / integer / decimal
  text / boolean / unsupported* for a pointer. The JSON reader is Release 1; the
  DataTable reader is Release 2. No matching logic knows the source.

### Business keys

At least one key mapping is required; zero comparison rules is valid for a
presence-only reconciliation.

- A key part must resolve to a non-null JSON **string** or a JSON **integer**.
  An integer is a token matching `-?(0|[1-9][0-9]*)` (JSON grammar already
  forbids leading zeros and `+`); it is converted to that exact invariant text
  and then follows the string rules. Fractions, exponents (`1E2`), Booleans,
  objects and arrays are `InvalidKeyType`. **Caution, documented:** the number
  `7` and the string `"007"` are different keys (leading zeros in a string are
  preserved), while `123` and `"123"` are the same key.
- Each key mapping independently selects trimming and ordinal case sensitivity.
  Defaults are no trimming and case-sensitive matching. (Trimming an integer's
  text is a no-op.)
- Empty strings after configured normalization are invalid keys. Whitespace-only
  strings remain literal keys when trimming is disabled; document this choice.
- Missing/null/unsupported keys make the row `InvalidRecord`, with a stable
  reason code and pointer identifying the faulty key part.
- Composite keys are structured ordered tuples, never delimiter-concatenated
  strings. Equality and hashing must use identical per-part normalization.
- Expose a display key as a JSON array of normalized strings, e.g.
  `["A","INV-101"]`; retain original values in detailed results.
- Mapping names are required, unique across keys and comparison rules, and
  compared ordinal-ignore-case. Pointers and labels have explicit size limits.

### Duplicate-key policy

Index all valid-key rows on each side before comparing any fields. If a key has
more than one row on either side, emit one `DuplicateKey` group result containing
all left/right row indices. Do not choose a first row, zip duplicates by order,
or emit Cartesian pairs. A unique row on the opposite side belongs to that
ambiguous group; it is not also reported as missing or matched.

Duplicate detection happens after normalization. Include original key values so
users can see whether trimming/case rules created a collision. Duplicate groups
do not undergo field comparison.

## 5. Comparison rules

Each rule has a name, left pointer, right pointer, rule kind, and null policy.
Evaluate all rules for an unambiguous pair, in configuration order; never stop
at the first mismatch. Store original values separately from interpreted values.

### Null and type handling

`ComparisonNullPolicy` has `RequireValue` (default) and `AllowBothNull`.

- Missing fields are always invalid comparisons, even when missing on both sides.
- With `RequireValue`, either explicit null is invalid.
- With `AllowBothNull`, two explicit nulls are equal; null versus a valid non-null
  value is different. A non-null value with an invalid type still yields invalid.
- Empty text is a real string. Empty decimal/date text is invalid. Boolean rules
  require JSON Boolean values, not `"yes"`, `0`, or `1`.
- No implicit cross-type text conversion or null/empty/zero equivalence.
- Objects/arrays at comparison leaves are invalid for all rule kinds.

### Text (Release 1)

Require strings. Optional trim and ordinal-ignore-case comparison; no locale
collation, Unicode normalization, punctuation removal, or whitespace collapsing.
Defaults preserve the original text exactly.

### Decimal (Release 1)

Accept JSON numbers or invariant numeric strings with grammar
`[+-]?[0-9]+(\.[0-9]+)?` for strings (JSON number syntax remains standard JSON).
No grouping separators, currency symbols, or surrounding whitespace in strings.

Convert exactly to `decimal`. Reject overflow or precision loss rather than
silently rounding input. Handle JSON exponent notation only when its exact value
is representable. Build/test an exact conversion helper; ordinary parsing alone
must not be assumed to reject every rounded value. Bound token length before
conversion to avoid exponent/mantissa abuse.

Use absolute tolerance, inclusive: values are equal when their absolute
difference is at most the configured non-negative tolerance. No implicit
rounding. Avoid subtraction overflow for opposing extreme values; compare with
an exact scaled-integer implementation or equivalent proven-safe arithmetic.
Return an exact invariant delta string, defined as right minus left, even if the
delta itself exceeds `decimal` range.

Tolerance is invariant decimal **text**, default `"0"`, to avoid a binary
floating-point conversion through a Pega port.

### Boolean (Release 2)

Compare JSON `true`/`false` directly under the common null policy.

### Money (Release 2)

Same amount semantics as Decimal, plus required left/right currency pointers.
Require three ASCII letters; normalize currency with trim and uppercase invariant.
Do not claim validation against a current currency registry. Missing or malformed
currency is invalid; different currency codes are a mismatch and prohibit amount
comparison under the tolerance. Report a currency reason, not a misleading amount
delta. `AllowBothNull` applies to amounts only after currencies validate and agree.
Money stays its own method so a monetary rule cannot omit currency compatibility.

### Dates (Release 2)

Two separate methods, so a tolerance never changes meaning:

- **CalendarDate** (`AddCalendarDateComparison`): explicit left/right format
  strings; `DateOnly.TryParseExact`, invariant culture, an approved date-only
  custom-format token subset (`yyyy`, `MM`, `dd`, quoted literals and literal
  separators). Reject time/offset tokens and formats missing a date part.
  Tolerance is a non-negative integer number of **calendar days**.
- **Instant** (`AddInstantComparison`): accept only documented explicit-offset ISO
  shapes: `yyyy-MM-dd'T'HH:mm:sszzz`, `yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz`, or the
  same two shapes ending in literal `'Z'`. Parse Z explicitly as UTC; `zzz`
  requires a supplied offset. Compare UTC instants; tolerance is a non-negative
  integer number of **seconds**. No format arguments are needed.

Reject offset-free instant strings. Do not infer a machine culture, local zone,
DST resolution, or business calendar. Report original strings, normalized
date/UTC representations, and the measured difference and its unit.

## 6. Result model, counts, and ordering

Result `kind` is reported as a stable string code:

| Kind | Unit and meaning |
|---|---|
| `Matched` | One unique left/right pair; every configured comparison agrees. |
| `Different` | One unique pair; at least one mismatch, no invalid comparisons. |
| `InvalidComparison` | One unique pair; at least one invalid comparison. |
| `OnlyLeft` | One valid-key left record with no right counterpart. |
| `OnlyRight` | One valid-key right record with no left counterpart. |
| `DuplicateKey` | One ambiguous key group, including every member on both sides. |
| `InvalidRecord` | One row excluded from the key index, with its source side. |

Exhausted cursors and failures report an empty `kind`. `InvalidComparison`
takes precedence over `Different`, but retain both invalid and unequal field
details. A paired record with five unequal fields is one exception result with
five difference details, not five exception records.

Summary JSON includes: `leftRowCount`, `rightRowCount`, `matchedPairCount`,
`differentPairCount`, `invalidPairCount`, `onlyLeftCount`, `onlyRightCount`,
`invalidLeftRowCount`, `invalidRightRowCount`, `ambiguousKeyCount`,
`ambiguousLeftRowCount`, `ambiguousRightRowCount`, `resultCount`,
`exceptionCount`, `differenceCount`, `allMatched`, `bothInputsEmpty`. (The JSON
keeps `allMatched`; only the scalar port dropped it.)

Accounting invariants:

```text
leftRowCount = matchedPairCount + differentPairCount + invalidPairCount
             + onlyLeftCount + invalidLeftRowCount + ambiguousLeftRowCount
rightRowCount = matchedPairCount + differentPairCount + invalidPairCount
              + onlyRightCount + invalidRightRowCount + ambiguousRightRowCount
resultCount = matchedPairCount + differentPairCount + invalidPairCount
            + onlyLeftCount + onlyRightCount + invalidLeftRowCount
            + invalidRightRowCount + ambiguousKeyCount
exceptionCount = resultCount - matchedPairCount
allMatched = (exceptionCount == 0)
```

Both empty inputs succeed with `allMatched=true` and `bothInputsEmpty=true`.
The caller can reject an unexpectedly empty population using the row counts.

Ordering is deterministic: visit left rows in source order, emitting an invalid
row, a unique pair/left-only result, or an ambiguous group at its first member;
then visit right rows and emit all remaining unconsumed results in source order.
Order group members by source index and field differences by rule order.

Result IDs are `r000001`, `r000002`, ... within a result snapshot: local
references, not persistent deduplication identities. Never derive queue
deduplication keys solely from a result ID.

Each detailed result contains kind, normalized key parts, original key parts,
left/right index arrays, stable reason codes, and comparison differences. Do not
copy full source records; include only referenced fields. Differences preserve
whether each side was present and the original JSON scalar value so missing,
null, empty string and zero stay distinguishable: **a missing value is a null
string; a present JSON null is the text `null`.**

Stable reason codes include `MissingKey`, `InvalidKeyType`, `EmptyKey`,
`NonObjectRow`, `DuplicateNormalizedKey`, `MissingField`, `NullNotAllowed`,
`InvalidType`, `InvalidDecimal`, `TextMismatch`, `DecimalMismatch` and, in
Release 2, `InvalidDate`, `InvalidCurrency`, `CurrencyMismatch`, `DateMismatch`,
`BooleanMismatch`. Human-readable explanations supplement these codes.

### Versioned export envelope (Release 2)

```json
{
  "schemaVersion": 1,
  "runLabel": "invoice-close-2026-09-25",
  "definition": { "schemaVersion": 1, "keys": [], "comparisons": [] },
  "summary": {},
  "results": []
}
```

The arrays/objects show envelope shape only; a real definition needs at least one
key. Export the exact effective definition, including defaults/limits. Omit
implicit timestamps and random IDs; identical inputs, definition and run label
yield identical report bytes. The run label is supplied to the export method, not
to `ReconcileJson`.

## 7. Public API

All methods return `bool` and end in `out string message`. No overloads. Inputs
precede outputs. Do not depend on optional-argument support for the designer
workflow. A `...Simple` method has fewer inputs and documented defaults; the full
form exposes every option.

### Release 1

| Method | Inputs | Outputs beyond message |
|---|---|---|
| `ClearDefinition` | none | none |
| `AddKeyMappingSimple` | `string name, string leftPointer, string rightPointer` | none |
| `AddKeyMapping` | `string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase` | none |
| `AddTextComparisonSimple` | `string name, string leftPointer, string rightPointer` | none |
| `AddTextComparison` | `string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase, ComparisonNullPolicy nullPolicy` | none |
| `AddDecimalComparisonSimple` | `string name, string leftPointer, string rightPointer` | none |
| `AddDecimalComparison` | `string name, string leftPointer, string rightPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy` | none |
| `LoadDefinitionJson` | `string definitionJson` | none |
| `GetDefinitionJson` | none | `string definitionJson` |
| `ValidateDefinitionJson` | `string definitionJson` | `int errorCount, string reportJson` |
| `ConfigureLimits` | `int maximumRowsPerSide, int maximumInputCharactersPerSide, int maximumResults, int maximumDifferenceDetails` | none |
| `ReconcileJson` | `string leftJson, string rightJson` | `int exceptionCount` |
| `GetSummary` | none | `int leftRowCount, int rightRowCount, int matchedPairCount, int exceptionCount` |
| `GetSummaryJson` | none | `string summaryJson` |
| `ResetResultCursor` | none | none |
| `TryReadNextException` | none | `bool hasItem, string resultId, string kind, string keyJson, int leftRowIndex, int rightRowIndex, string reason, int differenceCount` |
| `TryReadNextDifference` | none | `bool hasItem, string ruleName, string reasonCode, string leftValueJson, string rightValueJson, string explanation` |
| `GetResultJson` | `string resultId` | `string resultJson` |
| `ClearResults` | none | none |

Defaults of the Simple forms: keys are not trimmed and case-sensitive; Text is
exact (no trim, case-sensitive, `RequireValue`); Decimal has tolerance `"0"` and
`RequireValue`.

`ValidateDefinitionJson` returns true when validation executed, even if the
definition is invalid; `errorCount` is 0 for a valid one and `reportJson` holds
bounded location/code/message findings (malformed JSON is a finding here). An
oversized input or operational failure returns false. It never modifies state.

`LoadDefinitionJson` returns false for any invalid definition and leaves the
previous definition and results unchanged. Reject unknown properties, unknown
enums, duplicate properties/names, unsupported versions, invalid pointers,
tolerances or limits. Loading replaces the complete definition, including limits;
omitted options use the documented defaults.

Successful setup changes invalidate all results and cursors. Rejected changes
leave existing state intact. `ClearDefinition` restores defaults and clears
results. An incomplete definition with no keys is allowed while building it;
`ReconcileJson` rejects it. `GetDefinitionJson` can serialize this incomplete state.

### Release 2 (additive)

| Method | Inputs | Outputs beyond message |
|---|---|---|
| `ReconcileDataTables` | `DataTable leftTable, DataTable rightTable` | `int exceptionCount` |
| `AddBooleanComparisonSimple` | `string name, string leftPointer, string rightPointer` | none |
| `AddBooleanComparison` | `string name, string leftPointer, string rightPointer, ComparisonNullPolicy nullPolicy` | none |
| `AddMoneyComparisonSimple` | `string name, string leftPointer, string rightPointer, string leftCurrencyPointer, string rightCurrencyPointer` | none |
| `AddMoneyComparison` | `string name, string leftPointer, string rightPointer, string leftCurrencyPointer, string rightCurrencyPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy` | none |
| `AddCalendarDateComparisonSimple` | `string name, string leftPointer, string rightPointer, string leftFormat, string rightFormat` | none |
| `AddCalendarDateComparison` | `string name, string leftPointer, string rightPointer, string leftFormat, string rightFormat, int toleranceDays, ComparisonNullPolicy nullPolicy` | none |
| `AddInstantComparisonSimple` | `string name, string leftPointer, string rightPointer` | none |
| `AddInstantComparison` | `string name, string leftPointer, string rightPointer, int toleranceSeconds, ComparisonNullPolicy nullPolicy` | none |
| `ConfigureTableLimits` | `int maximumColumns, int maximumCells, int maximumValueCharacters` | none |
| `ConfigureOutputLimit` | `int maximumOutputCharacters` | none |
| `ExportResultsJson` | `string runLabel` | `string reportJson` |

`ReconcileDataTables` reads DataRow values as: `DBNull` = null; string = string;
integral types = integer; `decimal`/`double`/`float`/`Single` = decimal text
(exactly converted, rejecting values that cannot be represented exactly);
`bool` = Boolean; `DateTime`/`DateTimeOffset` and other types = unsupported for
keys and rules in this release (dates arrive as text and are parsed by the date
rules). The tables are read, never modified.

### Consuming results: the two cursors

```text
ReconcileJson(...)                       -> exceptionCount
loop:  TryReadNextException(...)         -> hasItem, resultId, kind, keyJson, ..., differenceCount
   inner loop: TryReadNextDifference(...)  -> hasItem, ruleName, reasonCode, left/rightValueJson, explanation
```

`TryReadNextDifference` reads the differences of the exception most recently read;
reading the next exception restarts it. An exception with no field differences
(missing record, invalid-key row, duplicate group) simply has `differenceCount`
0 and an immediately exhausted inner cursor. Detailed group membership is in
`GetResultJson`. Scalar row indices are `-1` if absent or if that side has
multiple rows; otherwise the actual single index.

### Return values and failure sentinels

- A completed reconciliation returns true even with mismatches, duplicate keys,
  or invalid records/comparisons; `message` is null and the outputs explain the data.
- Every attempted reconciliation first invalidates prior results. Build a new
  snapshot privately and publish only after completion and limit checks. Failure
  leaves no results available to accidentally process from an old run.
- Before a completed run, all result getters, cursors and export return false with
  an actionable no-results message. `ClearResults` is idempotent and succeeds.
- Exhausted cursors return true with `hasItem=false` and sentinel outputs, and
  stay exhausted until reset or a new successful run.
- Bad result IDs are operational failures (false).
- Strings default to null, counts to 0, Boolean outputs to false, row indices to
  -1. A present JSON null is the text `null`; a missing value is a null string.
- Getter/cursor failures do not destroy an otherwise valid snapshot or advance a
  cursor. A successful reconciliation resets both cursors.
- Public calls after disposal fail with the standard message; disposal is
  idempotent and releases retained inputs/results. No claim of secure erasure.

Use one instance per automation flow. Serialize public operations with an
instance lock to prevent torn state; do not promise concurrent independent runs
or a cancellation capability. Long synchronous calls remain bounded by the input
limits, and callers should not run them on an interactive UI thread.

## 8. Definition JSON example

Scalar builder methods and JSON loading compile to the same internal model and
produce identical behavior. `GetDefinitionJson` emits all resolved options in
canonical order. Unknown fields are errors to catch misspellings.

```json
{
  "schemaVersion": 1,
  "keys": [
    { "name": "Company", "leftPointer": "/company", "rightPointer": "/entity",
      "trim": true, "ignoreCase": true },
    { "name": "Invoice", "leftPointer": "/invoiceNumber", "rightPointer": "/invoiceId",
      "trim": true, "ignoreCase": false }
  ],
  "comparisons": [
    { "name": "Amount", "kind": "Decimal", "leftPointer": "/amount",
      "rightPointer": "/paidAmount", "absoluteTolerance": "0.01",
      "nullPolicy": "RequireValue" },
    { "name": "Currency", "kind": "Text", "leftPointer": "/currency",
      "rightPointer": "/currencyCode", "trim": true, "ignoreCase": true,
      "nullPolicy": "RequireValue" }
  ]
}
```

Release 2 adds the kinds `Boolean`, `Money`, `CalendarDate` and `Instant`; a
Release 1 component rejects them as unknown kinds. The absence of `limits` uses
defaults; when present, its property names match the `ConfigureLimits` inputs.
Only options applicable to the rule kind are allowed. Store enum names as
strings; reject numeric enum representations.

## 9. Resource limits, diagnostics, and architecture

Initial defaults are design starting points to be validated by measurement:

| Limit | Default | Allowed maximum | Release |
|---|---:|---:|---|
| Rows per side | 50,000 | 250,000 | 1 |
| UTF-16 input characters per side (JSON input) | 8,000,000 | 32,000,000 | 1 |
| Result records | 100,000 | 500,000 | 1 |
| Difference details across the run | 100,000 | 500,000 | 1 |
| DataTable columns (per table) | 100 | 1,000 | 2 |
| DataTable cells (rows × columns, per table) | 2,000,000 | 10,000,000 | 2 |
| DataTable characters in one string value | 4,096 | 1,000,000 | 2 |
| UTF-16 output characters (export) | 16,000,000 | 64,000,000 | 2 |

All configurable limits must be positive. Exceeding any one fails the run
atomically. Never silently truncate, skip excess rows, or publish a
partial-success report.

`ReconcileDataTables` is bounded by concrete limits, checked in this order and
before any value is interpreted: the **row** limit; the **column** limit
(`maximumColumns`); the **cell** limit (`rows × columns` must not exceed
`maximumCells`, computed without overflow); then, while reading, the length of
each string value (`maximumValueCharacters`) and the running total of string
characters read per table, which is held to the same
`maximumInputCharactersPerSide` limit that bounds JSON input. A table with few
rows but very many columns, or with huge strings, therefore fails before it can
exhaust memory. Only the columns the definition actually references are read;
the column and cell limits still apply to the table as supplied. The limits are
set with `ConfigureTableLimits` (a separate method so no Release 1 signature
changes), and each failure names the table side and the limit, never the data.

Fixed bounds: 16 key mappings, 128 comparisons, JSON depth 64, pointer length
1,024 characters, mapping name 128 characters, numeric token 256 characters,
definition JSON 256,000 characters, run label 256 characters (Release 2).
Bound diagnostic reports too: record total findings and a `truncated` indicator
when only the first 100 validation errors are returned. This diagnostic
truncation does not truncate reconciliation results.

Release 2 uses bounded output writing/counting rather than serializing an
arbitrarily large report before checking its size; a successful reconciliation
proves the full export fits its limit even if the caller only iterates, and caches
that proven size rather than serializing unboundedly again. Report bodies and
source strings may contain sensitive business data: return values only through
result ports, never through automatic logging or generic failure messages.

Failure diagnostics identify operation, side, row index, pointer/rule name, and
stable error code without echoing raw payloads. Sanitize JSON/conversion
exception messages rather than passing the shared guard's raw text through.

### Algorithm

1. Acquire the instance lock, clear previous results, snapshot the definition.
2. Validate the definition and input limits.
3. Read rows through the row-reader (bounded parse for JSON; duplicate-property
   rejection).
4. Validate rows and extract structured keys; build left/right dictionaries of
   key to ordered row-index lists.
5. Classify invalid rows and duplicate groups; compare only unique pairs.
6. Produce deterministic records and counts; enforce every limit during
   construction, not just afterward.
7. Assert the accounting invariants and copy only the required values into the
   immutable published snapshot.
8. Guarantee cleanup of parse documents and publish on success only.

Expected cost is O(n + m) plus key extraction and field evaluation; comparison is
O(p × r) for p unique pairs and r rules. Avoid cloning complete rows, Cartesian
joins, repeated pointer parsing, or scans of all right rows.

### Proposed files under `src/reconciliationutils/`

| File | Responsibility | Release |
|---|---|---|
| `ReconciliationUtils.csproj` | Standalone multi-target component and README packaging. | 1 |
| `ReconciliationUtils.cs` | Lifecycle, guarded public methods, state and cursors. | 1 |
| `ComparisonNullPolicy.cs` | Public null-policy enum. | 1 |
| `ReconciliationDefinition.cs` | Internal immutable definition, JSON validation and builders. | 1 |
| `RowReader.cs`, `JsonRowReader.cs` | Row-reader seam, restricted pointers, presence/type tracking. | 1 |
| `ReconciliationKey.cs` | Structured key equality/hash and source grouping. | 1 |
| `ComparisonCore.cs` | Rule dispatch and text/null semantics (Boolean/Money/Date in 2). | 1, 2 |
| `ExactDecimalCore.cs` | Exact bounded numeric conversion, overflow-safe differences. | 1 |
| `ReconciliationCore.cs` | Matching, classification, ordering, counts. | 1 |
| `ReconciliationResults.cs` | Internal immutable result and summary models. | 1 |
| `ResultJsonWriter.cs` | Canonical JSON output (summary and per-result; bounded export in 2). | 1, 2 |
| `DataTableRowReader.cs` | DataTable row-reader. | 2 |
| `DateCore.cs` | Calendar-date and instant parsing/comparison. | 2 |
| `NeverThrowsGuard.cs` | Project-local recoverable failure translation. | 1 |
| `README.md`, `Documentation/*.md` | Method reference and worked designer workflows. | 1, 2 |
| `ReconciliationUtils.Tests/` | Core, public contract, scenario and performance-smoke tests. | 1, 2 |

Use internal seams only where needed for resource-failure injection. Do not build
a pluggable rule framework or shared suite infrastructure.

## 10. Implementation work packages

**One pull request per work package, merged before the next starts.** Each PR
carries its own tests and docs (CONTRIBUTING), includes the `dotnet test` result,
and waits for review and approval. Stop and report after each PR.

### Release 1

**WP1 — Scaffold and freeze public contracts**
- [ ] Component and test projects, local guard, both constructors, the null-policy
  enum; `<Compile Remove>` for the tests; README pack item.
- [ ] Register the component **and** its test project in `src/AwesomeRpaUtils.sln`
  (project, configurations, nesting) so CI runs the tests; verify no sibling
  project references.
- [ ] Draft every Release 1 public signature with XML docs, `[Category]`,
  `[Description]`; failing-first public-boundary tests: sentinels, disposal,
  signature uniqueness (a reflection test like `StateMachineUtils.Tests`'
  `ConventionTests`), designer-friendly parameter types.

**WP2 — Definition, input and keys**
- [ ] Builders, atomic JSON load, canonical export, validation-only report.
- [ ] Bounded parsing, duplicate-property detection, restricted pointers, the
  row-reader seam, normalization, structured composite keys including integer keys.
- [ ] Builder and JSON produce identical definitions; invalid definitions leave
  state untouched.

**WP3 — Text and Decimal comparisons**
- [ ] Text/null rules; exact decimal conversion, overflow-safe delta and
  tolerance; original value and presence retained.
- [ ] Tests: tolerance boundaries, precision rejection, culture independence
  (run under a non-invariant culture such as Turkish).

**WP4 — Matching and immutable snapshots**
- [ ] Key indexes, duplicate-group quarantine, exactly-once row classification,
  deterministic IDs/order/summary, accounting invariants, invalid-over-different
  precedence, limits enforced during construction, stale results cleared first.
- [ ] End-to-end fixtures with mixed missing, duplicate, malformed and unequal data.

**WP5 — Consuming results**
- [ ] `GetSummary`, `GetSummaryJson`, both cursors, `GetResultJson`, reset/clear.
- [ ] Tests: exhaustion, reset, failed getters, rerun failure, limit failure,
  disposal, and that no generic message exposes source field contents.

**WP6 — Documentation and repository integration**
- [ ] README tables for every public member, default, sentinel, row-index rule and
  limit; `Documentation/` (README, QuickStart, Configuration, ComparisonRules,
  ResultsAndCounts, QueueHandoff, Limits) with complete Pega-oriented setup →
  run → summary → exception → difference examples, compile-checked in tests where
  they are complete methods.
- [ ] Add the assembly to the hardcoded `$releaseAssemblies` list in
  `scripts/Package-Release.ps1` (not auto-discovered); root `README.md` row;
  `CrossReference.md`; `TESTING.md` for manual host checks; a Pega usability review
  and its index entry. Confirm `Pack-NuGet.ps1` and `Package-Documentation.ps1`
  discover it (they should) by running them.
- [ ] Check Component Browser discovery with the built assembly.

**WP7 — Verification and release 1**
- [ ] CI green (the new test project runs); local Linux run of the platform-independent
  tests.
- [ ] Measure the default-limit workload, worst-case differences and duplicate-heavy
  data (machine, runtime, row sizes, time, peak managed and process memory);
  lower documented defaults if needed.
- [ ] Windows/Pega checks: method visibility, enum drop-downs, scalar wiring,
  decimal tolerance text, both cursors, error branches; record unavailable checks
  honestly as pending.
- [ ] Release-package inspection and a NuGet pack dry run with the existing
  scripts; then the release per the repository convention (tag, workflow) once
  approved.

### Release 2

**WP8 — DataTable bridge:** `DataTableRowReader`, `ReconcileDataTables`, value
mapping, `ConfigureTableLimits` and the limit checks above, docs
(Excel/CSV → DataTable → reconcile), tests including `DBNull`, numeric column
types, unsupported types, and each limit at and beyond its boundary (many columns
few rows, few rows huge strings, cell-count overflow).

**WP9 — Boolean and Money:** the two rule kinds, their builders/Simple forms and
JSON kinds; currency gating tests.

**WP10 — Dates:** `AddCalendarDateComparison[Simple]`, `AddInstantComparison[Simple]`,
`DateCore`; format-token validation, leap days, offsets, `Z`, tolerance edges,
no dependence on the machine zone or culture.

**WP11 — Export:** `ExportResultsJson`, the output limit, bounded canonical
serialization with a cached proven size, deterministic bytes.

**WP12 — Docs, verification and release 2:** as WP6/WP7 for the new surface
(README, CrossReference, TESTING, usability-review update), packaging check, release.

## 11. Verification matrix

| Area | Required cases | Release |
|---|---|---|
| Keys | Leading zeros in strings, integer keys (`7` vs `"007"`, `123` vs `"123"`), rejected fractions/exponents/Booleans, composite delimiter-like content, escaped pointer names, case/trim collisions, missing/null keys, nested paths, empty names. | 1 |
| Input shape | Malformed JSON, non-array roots, non-object rows, duplicate properties, arrays in paths, depth and character limits. | 1 |
| Duplicates | Left-only/right-only/both-side duplication; opposite unique row included; no fabricated pair or missing result. | 1 |
| Text | Ordinal case behavior, Turkish-culture run, whitespace, empty/null/missing, incompatible types. | 1 |
| Decimals | Exact boundary equality, one unit beyond tolerance, negatives, large magnitudes, signed zero, excessive precision, exponent extremes, locale separators, overflow-safe delta. | 1 |
| Accounting | Multiple differences per pair, invalid plus unequal fields, duplicate group membership, both/one input empty, every row accounted for once. | 1 |
| Lifecycle | Atomic definition load, setup invalidation, failed setup preservation, failed-run stale-result removal, reset, clear, disposal. | 1 |
| Results | Stable ordering/IDs, absent vs null values, both cursors incl. exhaustion and restart, invalid IDs, index -1 conventions. | 1 |
| Limits/failures | Each cap at and beyond its boundary, recoverable injected failures, no partial publication. | 1 |
| DataTable | `DBNull`, each numeric type, bool, unsupported types, column-name pointers, row/column/cell/value-size/total-character limits at and beyond the boundary, table unchanged. | 2 |
| Boolean/null, Money | JSON Boolean only; both/one null; currency normalization and mismatch; no tolerance across currencies. | 2 |
| Dates | Exact formats, leap days, invalid dates, different offsets for one instant, `Z`, missing offsets, day/second tolerance edges, no local-time dependence. | 2 |
| Export | Deterministic bytes, absent vs null, bounded size, output-limit failure leaves no report. | 2 |
| Host/package | Framework builds, CI test run, Pega scalar flow, Component Browser, ZIP/NuGet contents, docs links. | 1, 2 |

Use deterministic generated fixtures for accounting and permutation properties:
reordering inputs can change source indices/result order but not the business
classification/counts for the same keyed multiset; swapping left and right swaps
the missing-side counts and delta signs while preserving match/ambiguity totals
(use asymmetric pointer mappings correctly when constructing swapped tests).
Performance numbers are recorded evidence, not unit-test assertions; the required
property is indexed matching rather than a quadratic search.

## 12. Acceptance criteria

**Release 1**
- [ ] A Pega automation can configure (Simple and full methods, or JSON), run,
  summarize, iterate exceptions and their field differences using only
  scalar/enum/JSON ports, with no more than one bool output on any method besides
  the result.
- [ ] Presence-only, exact-key, composite-key (string and integer) and Text/Decimal
  reconciliations work.
- [ ] Duplicate and invalid rows never silently match or disappear from accounting.
- [ ] Amounts retain exactness; no implicit culture behavior.
- [ ] Mismatches are successful executions; operational failures are distinct and
  leave no readable stale or partial snapshot.
- [ ] All public methods honor the never-throws, unique-signature and documentation
  standards; tests (including a proof that each rule test can fail) pass in CI.
- [ ] Packaging (solution, release list, NuGet, docs) verified, not assumed.

**Release 2**
- [ ] DataTables reconcile without a JSON step; Boolean, Money, calendar-date and
  instant rules behave as specified; currencies gate money; dates have no implicit
  machine-culture or time-zone behavior.
- [ ] Full exports reproduce definitions, reason codes, referenced values, row
  membership and totals with deterministic ordering and bounded size.

## 13. Decisions (confirmed by Jeff)

1. **Two releases**, as proposed.
2. **DataTable bridge** is the first item of Release 2 (WP8).
3. **Integer keys are accepted**, with the `7` vs `"007"` caution documented.
4. **Simple-variant defaults confirmed:** no trim, case-sensitive, tolerance `"0"`,
   `RequireValue`.
5. **Two cursors confirmed** (`TryReadNextException`, then `TryReadNextDifference`),
   not an indexed `GetDifferenceAt`.

## 14. Follow-on candidates

Consider explicit grouped-total reconciliation first: group by declared keys and
currency, sum exact amounts, and report original membership. Design it as a
separate operation with its own counting contract, not an implicit duplicate fix.

Later candidates are caller-supplied status/value mapping tables, richer
DataTable typing, and file/stream processing for larger inputs. Fuzzy suggestions
would need a separate review workflow and must never silently convert ambiguous
records into confirmed matches.
