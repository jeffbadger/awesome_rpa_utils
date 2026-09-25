# ReconciliationUtils — Design and Implementation Plan

**Status:** Proposed; implementation has not started.

**Goal:** Add a standalone Pega Robot Studio component that reconciles two
datasets by business key, explains disagreements, and exposes exceptions through
scalar ports so an automation can route them to review or correction.

**Component:** `ReconciliationUtils` in namespace/assembly
`ReconciliationAutomation`, under `src/reconciliationutils/`.

**Release scope:** Deterministic one-to-one matching, composite string keys,
explicit text/decimal/money/date/Boolean comparisons, duplicate and invalid-row
reporting, scalar result iteration, and versioned JSON configuration/results.

This plan defines the implementation defaults. It does not authorize automatic
correction of source systems, deployment, or publication. Implement the checked
tasks in dependency order; no special agent framework or additional skill is
required.

## 1. Repository constraints

Follow the existing
[Never-Throws Standard](../coding-standards/never-throws-standard.md),
[Signature Uniqueness Standard](../coding-standards/signature-uniqueness-standard.md),
and [Pega usability methodology](../pega-usability-reviews/README.md).

- Derive the public component from `System.ComponentModel.Component`; provide
  both parameterless and `IContainer` constructors.
- Target `net8.0-windows;net10.0-windows`, with `AnyCPU;x64`, XML documentation,
  and the build/package conventions used by `DataContractUtils`.
- Use the BCL and `System.Text.Json`; no new runtime NuGet dependencies,
  Windows desktop framework dependency, or references to sibling utilities.
- Provide a project-local `NeverThrowsGuard`. Recoverable exceptions become
  `false` plus an actionable message; fatal conditions are outside the contract.
- Use scalar inputs/outputs, enums, and JSON strings. No public dictionaries,
  collection proxies, delegates, `JsonElement`, or custom result objects.
- Give every public operation a unique name, XML documentation, `[Category]`,
  and `[Description]`. Document success, negative outcomes, failure sentinels,
  and state changes consistently in code and user documentation.
- Keep the core platform-independent. Verify actual execution on Linux and
  Windows; a Windows-flavored target framework alone is not a functional test.

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

The component compares supplied snapshots. The caller owns source extraction,
business cutoff times, pagination, and whether both snapshots cover the same
population. `allMatched` means the supplied records agree under the configured
rules; it does not certify completeness of either source system.

Integration stays in the consuming automation:

1. Obtain JSON arrays from API responses or a separately supplied file parser.
2. Use `FileWatchUtils` first when coordinating externally produced files.
3. Configure and run this component.
4. Inspect summary, iterate exceptions, and optionally enqueue result JSON using
   `LocalQueueUtils` with an explicit caller-owned reconciliation/run identifier.
5. Use `StateMachineUtils` for the subsequent review/correction workflow.
6. Archive inputs and exported report with `ArchiveUtils` when required.

CSV parsing is not currently supplied by this proposed component. Do not make
its release depend on the previously suggested `DelimitedFileUtils`.

### Explicitly outside version 1

- Fuzzy matching, inferred field mappings, nearest-match suggestions.
- One-to-many/many-to-many matching, partial payments, grouped totals.
- Currency conversion, percentage tolerances, or automatic rounding.
- Arbitrary expressions, scripts, full JSONPath, array expansion.
- Native CSV/Excel/PDF/database/API connectors and direct file export.
- Automatic data correction, notifications, queue ownership, persistence.
- Streaming/external-sort processing of unbounded datasets.
- Async events, background reconciliation, shared-instance parallel execution.

These boundaries keep the initial algorithm and audit trail explainable.

## 3. Input and field-selection contract

`ReconcileJson` accepts two JSON strings, each a top-level array. Empty arrays
are valid. Array order supplies stable, zero-based source row indices.

- Invalid JSON, a non-array root, duplicate property names anywhere in an input
  object, or a resource-limit breach fails the entire operation.
- A non-object array item is a data exception (`InvalidRecord`) at its row index,
  not a malformed-document failure. It cannot enter the key index.
- Field references use an explicitly documented restricted JSON Pointer syntax:
  a leading `/`, object-property segments, `~0` for `~`, and `~1` for `/`.
  Empty root pointers, invalid escapes, and array traversal are unsupported.
  Empty property segments are valid; `/` selects an empty-name property.
- Property lookup is ordinal and case-sensitive. Literal dots in property names
  do not have special meaning. Intermediate null/scalar values mean the requested
  path is missing; an array encountered during traversal is unsupported data.
- Unreferenced fields are ignored for matching and comparison, but still count
  toward input-size/depth limits and duplicate-property validation.
- Parse with bounded depth; disallow comments and trailing commas.

### Business keys

At least one key mapping is required; zero comparison rules is valid for a
presence-only reconciliation.

- Version 1 keys must resolve to non-null JSON strings. Numeric identifiers
  must be converted by the caller. Preserve leading zeros.
- Each key mapping independently selects trimming and ordinal case sensitivity.
  Defaults are no trimming and case-sensitive matching.
- Empty strings after configured normalization are invalid keys. Whitespace-only
  strings remain literal keys when trimming is disabled; document this choice.
- Missing/null/non-string/unsupported keys make the row `InvalidRecord`, with a
  stable reason code and pointer identifying the faulty key part.
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
do not undergo field comparison in version 1.

## 4. Comparison rules

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
- Objects/arrays at comparison leaves are invalid for all version 1 rule kinds.

### Text

Require strings. Optional trim and ordinal-ignore-case comparison; no locale
collation, Unicode normalization, punctuation removal, or whitespace collapsing.
Defaults preserve the original text exactly.

### Decimal

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

Designer methods accept tolerance as invariant decimal **text**, default `"0"`,
to avoid introducing a binary floating-point conversion through a Pega port.

### Money

Same amount semantics as Decimal, plus required left/right currency pointers.
Require three ASCII letters; normalize currency with trim and uppercase invariant.
Do not claim validation against a current currency registry. Missing or malformed
currency is invalid; different currency codes are a mismatch and prohibit amount
comparison under the tolerance. Report a currency reason, not a misleading amount
delta. `AllowBothNull` applies to amounts only after currencies validate and agree.

Keep this an explicit `AddMoneyComparison` operation so a basic monetary rule
cannot accidentally omit currency compatibility.

### Date

Two designer-selectable modes, with explicit left/right format strings:

- `CalendarDate`: use `DateOnly.TryParseExact`, invariant culture, and an approved
  date-only custom-format token subset (`yyyy`, `MM`, `dd`, quoted literals and
  literal separators). Reject time/offset tokens and formats missing a date part.
  Tolerance is a non-negative integer number of calendar days.
- `UtcInstant`: accept only documented explicit-offset ISO shapes:
  `yyyy-MM-dd'T'HH:mm:sszzz`,
  `yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz`, or the same two shapes ending in literal
  `'Z'`. Parse Z explicitly as UTC; `zzz` requires a supplied offset. Compare UTC
  instants with tolerance in whole seconds.

Reject offset-free instant strings. Do not infer a machine culture, local zone,
DST resolution, or business calendar. Report original strings, normalized
date/UTC representations, and the measured difference/unit in detailed results.
Use a single non-negative `toleranceUnits` integer interpreted by mode; expose
the units in the designer description and documentation.

### Boolean

Compare JSON `true`/`false` directly under the common null policy.

## 5. Result model, counts, and ordering

Public `ReconciliationResultKind` values:

| Kind | Unit and meaning |
|---|---|
| `None` | Failure/exhaustion sentinel only; never stored as a result. |
| `Matched` | One unique left/right pair; every configured comparison agrees. |
| `Different` | One unique pair; at least one mismatch, no invalid comparisons. |
| `InvalidComparison` | One unique pair; at least one invalid comparison. |
| `OnlyLeft` | One valid-key left record with no right counterpart. |
| `OnlyRight` | One valid-key right record with no left counterpart. |
| `DuplicateKey` | One ambiguous key group, including every member on both sides. |
| `InvalidRecord` | One row excluded from the key index, with its source side. |

`InvalidComparison` takes precedence over `Different`, but retain both invalid
and unequal field details. A paired record with five unequal fields is one
exception result with five difference details, not five exception records.

Summary JSON must include:

- `leftRowCount`, `rightRowCount`, `matchedPairCount`, `differentPairCount`,
  `invalidPairCount`, `onlyLeftCount`, `onlyRightCount`.
- `invalidLeftRowCount`, `invalidRightRowCount`, `ambiguousKeyCount`,
  `ambiguousLeftRowCount`, `ambiguousRightRowCount`.
- `resultCount`, `exceptionCount`, `differenceCount`, `allMatched`,
  `bothInputsEmpty`.

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

Assign result IDs `r000001`, `r000002`, etc. within a result snapshot. IDs are
local references, not persistent deduplication identities. Include a caller-supplied
`runLabel` in reports; never derive queue deduplication keys solely from result ID.

Each detailed result contains kind, normalized key parts, original key parts,
left/right index arrays, stable reason codes, and comparison differences.
Do not copy full source records into reports; include only referenced fields.
Differences preserve `leftPresent`/`rightPresent` and original JSON scalar values
so missing, null, empty string, and zero remain distinguishable.

Suggested stable reason codes include `MissingKey`, `InvalidKeyType`, `EmptyKey`,
`NonObjectRow`, `DuplicateNormalizedKey`, `MissingField`, `NullNotAllowed`,
`InvalidType`, `InvalidDecimal`, `InvalidDate`, `InvalidCurrency`,
`CurrencyMismatch`, `TextMismatch`, `DecimalMismatch`, `DateMismatch`, and
`BooleanMismatch`. Human-readable explanations supplement these codes.

### Versioned export envelope

```json
{
  "schemaVersion": 1,
  "runLabel": "invoice-close-2026-09-25",
  "definition": { "schemaVersion": 1, "keys": [], "comparisons": [] },
  "summary": {},
  "results": []
}
```

The arrays/objects above show envelope shape only; a real definition needs at
least one key. Export the exact effective definition, including defaults/limits,
so the report explains the applied policy. Omit implicit timestamps and random
IDs; identical inputs, definition, and run label yield identical report bytes.

## 6. Public API

All methods below return `bool` and end in `out string message`. No overloads.
Inputs precede outputs; defaults are resolved by implementation when strings
are empty only where explicitly documented. Do not depend on optional argument
support for the core designer workflow.

### Setup

| Method | Inputs | Outputs beyond message |
|---|---|---|
| `ClearDefinition` | none | none |
| `AddKeyMapping` | `string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase` | none |
| `AddTextComparison` | `string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase, ComparisonNullPolicy nullPolicy` | none |
| `AddDecimalComparison` | `string name, string leftPointer, string rightPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy` | none |
| `AddMoneyComparison` | `string name, string leftPointer, string rightPointer, string leftCurrencyPointer, string rightCurrencyPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy` | none |
| `AddDateComparison` | `string name, string leftPointer, string rightPointer, string leftFormat, string rightFormat, DateComparisonMode mode, int toleranceUnits, ComparisonNullPolicy nullPolicy` | none |
| `AddBooleanComparison` | `string name, string leftPointer, string rightPointer, ComparisonNullPolicy nullPolicy` | none |
| `LoadDefinitionJson` | `string definitionJson` | none |
| `GetDefinitionJson` | none | `string definitionJson` |
| `ValidateDefinitionJson` | `string definitionJson` | `bool valid, string reportJson` |
| `ConfigureLimits` | `int maximumRowsPerSide, int maximumInputCharactersPerSide, int maximumResults, int maximumDifferenceDetails, int maximumOutputCharacters` | none |

`ValidateDefinitionJson` returns true when validation executes, even if `valid`
is false; the report contains bounded location/code/message errors. Malformed
JSON is a validation finding here. An oversized validation input or operational
failure returns false. Validation never modifies component state.

`LoadDefinitionJson` instead returns false for any invalid definition and leaves
the previous definition and results unchanged. Reject unknown properties,
unknown enums, duplicate properties/names, unsupported versions, invalid pointers,
formats, tolerances, or limits. Loading replaces the complete definition,
including limits; omitted options use the documented version-1 defaults.

Successful setup changes invalidate all results and cursors. Rejected changes
leave existing state intact. `ClearDefinition` restores defaults and clears
results. An incomplete definition with no keys is allowed while building it;
`ReconcileJson` rejects it. `GetDefinitionJson` can serialize this incomplete state.

### Execute and consume

| Method | Inputs | Outputs beyond message |
|---|---|---|
| `ReconcileJson` | `string leftJson, string rightJson, string runLabel` | `bool allMatched, int exceptionCount` |
| `GetSummary` | none | `int leftRowCount, int rightRowCount, int matchedPairCount, int exceptionCount, bool allMatched` |
| `GetSummaryJson` | none | `string summaryJson` |
| `ResetResultCursor` | none | none |
| `TryReadNextException` | none | `bool hasItem, string resultId, ReconciliationResultKind kind, string keyJson, int leftRowIndex, int rightRowIndex, string reason` |
| `GetResultJson` | `string resultId` | `string resultJson` |
| `GetDifferenceCount` | `string resultId` | `int count` |
| `GetDifferenceAt` | `string resultId, int index` | `string ruleName, string reasonCode, bool leftPresent, bool rightPresent, string leftValueJson, string rightValueJson, string explanation` |
| `ExportResultsJson` | none | `string reportJson` |
| `ClearResults` | none | none |

Detailed group membership is available via `GetResultJson`. Scalar row indices
are `-1` if absent or if that side has multiple rows; otherwise return its actual
single index. An empty difference list is valid for missing records, invalid-key
rows, and duplicate groups; their result kind/reason explains the issue.

Example full signature:

```csharp
public bool ReconcileJson(
    string leftJson, string rightJson, string runLabel,
    out bool allMatched, out int exceptionCount, out string message);
```

### Return values and failure sentinels

- Completed reconciliation returns true even with mismatches, duplicate keys,
  or invalid records/comparisons. `message` is null; report outputs explain data.
- Every attempted reconciliation first invalidates prior results. Build a new
  snapshot privately and publish only after completion and output-budget checks.
  Failure leaves no results available to accidentally process from an old run.
- Before a completed run, all result getters/iteration/export return false with
  an actionable no-results message. `ClearResults` is idempotent and succeeds.
- Exhausted iteration returns true, `hasItem=false`, and sentinel outputs. It
  remains exhausted until reset or a new successful run.
- Bad result IDs or out-of-range difference indices are operational failures.
- Strings default to null, counts to 0, Boolean outputs to false, row indices to
  -1, and result kind to `None`. A present JSON null is the string `"null"`,
  whereas a missing value has `present=false` and a null output string.
- Getter/export failures do not destroy an otherwise valid snapshot or advance
  a cursor. Successful reconciliation resets the cursor to the beginning.
- Reject public calls after disposal with standard failure results; disposal is
  idempotent and releases retained inputs/results. No claim of secure string erasure.

Use one instance per automation flow. Serialize public operations with an
instance lock to prevent torn state; do not promise concurrent independent runs
or a cancellation capability. Long synchronous calls remain bounded by input
limits, and callers should not run them on an interactive UI thread.

## 7. Definition JSON example

Scalar builder methods and JSON loading must compile to the same internal model
and produce identical comparison behavior. `GetDefinitionJson` emits all resolved
options in canonical order. Unknown fields are errors to catch misspellings.

```json
{
  "schemaVersion": 1,
  "keys": [
    {
      "name": "Company",
      "leftPointer": "/company",
      "rightPointer": "/entity",
      "trim": true,
      "ignoreCase": true
    },
    {
      "name": "Invoice",
      "leftPointer": "/invoiceNumber",
      "rightPointer": "/invoiceId",
      "trim": true,
      "ignoreCase": false
    }
  ],
  "comparisons": [
    {
      "name": "Amount",
      "kind": "Money",
      "leftPointer": "/amount",
      "rightPointer": "/paidAmount",
      "leftCurrencyPointer": "/currency",
      "rightCurrencyPointer": "/currencyCode",
      "absoluteTolerance": "0.01",
      "nullPolicy": "RequireValue"
    },
    {
      "name": "Status",
      "kind": "Text",
      "leftPointer": "/status",
      "rightPointer": "/status",
      "trim": true,
      "ignoreCase": true,
      "nullPolicy": "RequireValue"
    }
  ]
}
```

The absence of `limits` uses defaults. When present, its property names match the
five `ConfigureLimits` inputs. Only options applicable to the given rule kind
are allowed. Store enum names as strings; reject numeric enum representations.

## 8. Resource limits, diagnostics, and implementation architecture

Initial defaults are design starting points to be validated by measurement:

| Limit | Default | Allowed maximum |
|---|---:|---:|
| Rows per side | 50,000 | 250,000 |
| UTF-16 input characters per side | 8,000,000 | 32,000,000 |
| Result records | 100,000 | 500,000 |
| Difference details across the run | 100,000 | 500,000 |
| UTF-16 output characters | 16,000,000 | 64,000,000 |

All configurable limits must be positive. Independent limits need not guarantee
that every permitted input fits; exceeding any one fails the run atomically.
Never silently truncate, skip excess rows, or publish a partial-success report.

Fixed version-1 bounds: 16 key mappings, 128 comparisons, JSON depth 64, pointer
length 1,024 characters, mapping name 128 characters, run label 256 characters,
numeric token 256 characters, definition JSON 256,000 characters. Bound diagnostic
reports too; record total findings and a `truncated` indicator when only the first
100 validation errors are returned. This diagnostic truncation does not truncate
reconciliation results.

Use bounded output writing/counting rather than allocating an arbitrarily large
serialization before checking its size. Successful reconciliation must establish
that the full export envelope fits its limit, even if the caller only uses
scalar iteration. Cache that canonical export or its proven serialization size;
do not serialize it unboundedly again. Report bodies and source strings may
contain sensitive business data: return requested values only through result
ports, never through automatic logging or generic failure messages.

Failure diagnostics identify operation, side, row index, pointer/rule name, and
stable error code without echoing raw payloads. Sanitize JSON/conversion exception
messages rather than assuming the common guard's raw exception text is safe.

### Algorithm

1. Acquire instance lock, clear previous results, and snapshot the definition.
2. Validate definition, input character limits, and run label.
3. Parse bounded documents; reject malformed/duplicate-property input.
4. Validate rows and extract structured keys. Build left/right dictionaries of
   key to ordered row-index lists.
5. Classify invalid rows and duplicate groups; compare only unique pairs.
6. Produce deterministic records and counts; enforce every result/detail limit
   during construction, not just afterward.
7. Assert accounting invariants, create the bounded canonical export, and copy
   only required values into the immutable published snapshot.
8. Dispose parse documents in guaranteed cleanup and publish on success. If
   required cleanup fails, return failure and retain no new results.

Expected index/matching cost is O(n + m) plus key extraction and field evaluation;
comparison cost is O(p × r) for p unique pairs and r rules. String lengths and
parsing contribute their own costs. Retained memory is bounded but not constant;
release source parse trees after building the snapshot. Avoid cloning complete
rows, Cartesian joins, repeated pointer parsing, or scans of all right rows.

### Proposed files

| File under `src/reconciliationutils/` | Responsibility |
|---|---|
| `ReconciliationUtils.csproj` | Standalone multi-target component and README packaging. |
| `ReconciliationUtils.cs` | Component lifecycle, guarded public methods, state/cursor. |
| `ReconciliationEnums.cs` | Public result/null/date enums; private rule kinds as appropriate. |
| `ReconciliationDefinition.cs` | Internal immutable definitions, JSON validation/builders. |
| `JsonFieldReader.cs` | Restricted pointer parsing, presence/type tracking, input validation. |
| `ReconciliationKey.cs` | Structured key equality/hash and source grouping. |
| `ComparisonCore.cs` | Rule dispatch, text/null/Boolean/date/money semantics. |
| `ExactDecimalCore.cs` | Exact bounded numeric conversion and overflow-safe differences. |
| `ReconciliationCore.cs` | Matching, classification, deterministic ordering, counts. |
| `ReconciliationResults.cs` | Internal immutable result and summary models. |
| `ResultJsonWriter.cs` | Canonical bounded JSON output. |
| `NeverThrowsGuard.cs` | Project-local recoverable failure translation. |
| `README.md`, `Documentation/*.md` | Method reference and worked designer workflows. |
| `ReconciliationUtils.Tests/` | Core, public contract, scenario, and performance-smoke tests. |

Use internal seams only where needed for resource-failure injection. Do not build
a pluggable rule framework or shared suite infrastructure for this release.

## 9. Implementation work packages

### Task 1 — Scaffold and freeze public contracts

- [ ] Create the component/test projects, local guard, constructors, and enums.
- [ ] Match current repository project/package conventions; exclude nested test
  sources from the component build. Reuse the current test-package versions.
- [ ] Add projects and configurations to `src/AwesomeRpaUtils.sln` following its
  existing nesting pattern; verify no sibling utility project references.
- [ ] Draft all public signatures, XML contracts, categories, and descriptions.
- [ ] Add focused public-boundary tests for sentinels, disposal, and method-name
  uniqueness before implementing core behavior.

**Exit:** Both target frameworks build; public shape matches section 6.

### Task 2 — Definition validation and input/key handling

- [ ] Implement builders, atomic JSON load, canonical export, and validation-only
  reporting, including strict unknown-field and enum handling.
- [ ] Implement bounded parsing, duplicate-property detection, restricted pointers,
  presence/type distinctions, normalization, and structured composite keys.
- [ ] Verify equivalent builder/JSON configurations produce identical definitions.
- [ ] Test invalid definitions without state mutation and successful setup changes
  invalidating old results.

**Exit:** All supported definitions and pathological key cases have deterministic
behavior; no collisions from delimiter concatenation or normalization inconsistency.

### Task 3 — Exact field comparisons

- [ ] Implement text/null/Boolean rules and detailed outcomes.
- [ ] Implement exact decimal conversion and overflow-safe delta/tolerance logic.
- [ ] Implement money currency gating and the specified date modes/formats.
- [ ] Retain original value/presence and interpreted differences for explanations.
- [ ] Test tolerance boundaries, precision rejection, invalid input, and culture
  independence before integrating record matching.

**Exit:** Each rule has explicit equal/different/invalid results and cannot silently
round, infer culture, equate missing with null, or compare different currencies.

### Task 4 — Matching and immutable snapshots

- [ ] Build key indexes and implement duplicate-group quarantine.
- [ ] Classify every source row exactly once and evaluate every rule on unique pairs.
- [ ] Implement deterministic result IDs/order, summaries, accounting invariants,
  and invalid-comparison precedence without dropping mismatch details.
- [ ] Enforce row/result/detail limits during construction and clear stale results
  before every attempted run.
- [ ] Add end-to-end cases with mixed missing, duplicate, malformed, and unequal data.

**Exit:** The example dataset and mixed-edge fixtures reconcile exactly as specified;
accounting equations hold for generated datasets as well as hand-written examples.

### Task 5 — Designer consumption and bounded exports

- [ ] Implement scalar summary, exception cursor, indexed field details, JSON
  result lookup, reset/clear operations, and full report export.
- [ ] Implement bounded deterministic serialization and publish results only after
  the complete report fits its configured budget.
- [ ] Test end-of-iteration, reset, failed getters, rerun failure, limit failure,
  disposal, and injected internal recoverable failures.
- [ ] Verify that no generic error message exposes source field contents.

**Exit:** A caller can process every exception and field difference without custom
collection proxies; no incomplete or stale report can be mistaken for success.

### Task 6 — Documentation and repository integration

- [ ] Write README tables for every public method/property/enum and all defaults,
  failure sentinels, no-results behavior, row-index conventions, and limits.
- [ ] Add `Documentation/README.md`, `QuickStart.md`, `Configuration.md`,
  `ComparisonRules.md`, `ResultsAndCounts.md`, `QueueHandoff.md`, and `Limits.md`.
- [ ] Include complete Pega-oriented setup → run → summary → exception → field-loop
  examples, plus duplicate-key and failed-rerun examples. Compile-check any C#
  snippets that represent complete methods in the test project.
- [ ] Add the component to root `README.md` and `CrossReference.md`; update any
  component totals that actually enumerate shipped components.
- [ ] Add a Pega usability review and link it in the review index; update
  `TESTING.md` with the functional and manual host checks.
- [ ] Inspect `.github/workflows/build.yml`, release workflow, and packaging scripts
  for discovery behavior; change only explicit lists that need the new component.
- [ ] Verify release ZIP/NuGet/documentation packaging includes the new assembly,
  method docs, and README without test binaries or extra runtime dependencies.
- [ ] Check Component Browser parsing/discovery with the built assembly and README;
  add a targeted regression only if the new surface exposes a real parsing issue.

**Exit:** The component is discoverable and documented through the same routes as
the existing suite. Packaging discovery is verified rather than assumed.

### Task 7 — Final verification and handoff

- [ ] Run component tests on .NET 8 and .NET 10, with real Linux and Windows
  execution for the platform-independent logic.
- [ ] Build the solution in Release and run affected repository checks once.
- [ ] Measure the default-limit workload, worst-case differences, and duplicate-heavy
  datasets; record machine/runtime, row sizes, elapsed time, peak managed allocation,
  and process peak memory. Adjust documented defaults downward if needed.
- [ ] Verify Windows/Pega method visibility, enum selection, scalar wiring, decimal
  tolerance text, cursor iteration, field-detail iteration, and error branches.
- [ ] Perform release-package inspection and a NuGet pack dry run using the existing
  scripts, without publishing or creating a release.
- [ ] Record actual checks and any unavailable host checks honestly; do not mark a
  Pega runtime check complete merely because the assembly builds.

**Exit:** Acceptance criteria below are met, or outstanding environment-dependent
checks are explicitly recorded as pending in the implementation handoff.

## 10. Verification matrix

| Area | Required cases |
|---|---|
| Keys | Leading zeros, composite delimiter-like content, escaped pointer names, case/trim collisions, missing/null/numeric keys, nested paths, empty names. |
| Input shape | Malformed JSON, non-array roots, non-object rows, duplicate properties, arrays in paths, depth and character limits. |
| Duplicates | Left-only/right-only/both-side duplication; opposite unique row included; no fabricated pair or missing result. |
| Text | Ordinal case behavior, Turkish-culture run, whitespace, empty/null/missing, incompatible types. |
| Decimals | Exact boundary equality, one unit beyond tolerance, negatives, large magnitudes, signed zero, excessive precision, exponent extremes, locale separators, overflow-safe delta. |
| Money | Same currency after normalization, different currencies, missing/malformed codes, null amounts, no amount tolerance across currencies. |
| Dates | Exact formats, leap days, invalid dates, different offsets for one instant, Z handling, missing offsets, calendar-day boundaries, tolerance edges, no local-time dependence. |
| Boolean/null | JSON Boolean only, both null, one null, null versus wrong type, missing fields under each null policy. |
| Accounting | Multiple differences per pair, invalid plus unequal fields, duplicate group membership, both/one input empty, every row accounted for once. |
| Lifecycle | Atomic definition load, successful setup invalidation, failed setup preservation, failed-run stale-result removal, reset, clear, disposal. |
| Results | Stable ordering/IDs/bytes, absent versus null JSON values, cursor exhaustion, invalid IDs/indices, index -1 conventions. |
| Limits/failures | Each cap at/exceeding boundary, bounded serialization, recoverable injected failures, disposal/cleanup paths where fallible, no partial publication. |
| Host/package | Framework builds, Linux/Windows tests, Pega scalar flow, Component Browser, ZIP contents, NuGet metadata, documentation links. |

Use deterministic generated fixtures to test accounting and permutation properties:
reordering inputs can change source indices/result order, but not the business
classification/counts for the same keyed multiset. Swapping left/right swaps
missing-side counts and delta signs while preserving match/ambiguity totals.
Use asymmetric pointer/format mappings correctly when constructing swapped tests.

Avoid fragile wall-clock assertions in unit tests. Performance measurements are
recorded evidence; the required algorithmic property is indexed matching rather
than a quadratic search. Fault-injection tests should cover actual recoverable
boundaries, not manufacture file/native cleanup scenarios the component lacks.

## 11. Acceptance criteria

- [ ] A Pega automation can configure, run, summarize, iterate exceptions, and
  inspect field differences using only scalar/enum/JSON ports.
- [ ] Exact-key, presence-only, composite-key, and all five field-rule kinds work.
- [ ] Duplicate and invalid rows never silently match or disappear from accounting.
- [ ] Amounts retain exactness, currencies gate money comparisons, and dates have
  no implicit machine-culture/time-zone behavior.
- [ ] Mismatches are successful executions; operational failures are distinct and
  leave no readable stale/partial reconciliation snapshot.
- [ ] Full exports reproduce definitions, reason codes, original referenced values,
  row membership, and totals with deterministic ordering and bounded size.
- [ ] All public methods honor the repository error/signature/documentation standards.
- [ ] Tests, build, packaging, and documented Windows/Pega verification are complete.

## 12. Follow-on candidates after version 1

Consider explicit grouped-total reconciliation first: group by declared keys and
currency, sum exact amounts, and report original membership. Design it as a
separate operation with its own counting contract, not an implicit duplicate fix.

Later candidates are caller-supplied status/value mapping tables, a constrained
DataTable adapter, and file/stream processing for larger inputs. Fuzzy suggestions
would need a separate review workflow and must never silently convert ambiguous
records into confirmed matches.
