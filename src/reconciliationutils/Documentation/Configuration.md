# Configuration

A **definition** has keys, comparisons and limits. It lives in the component instance; every successful
change discards existing results.

## With methods

| Method | Use |
|---|---|
| `AddKeyMappingSimple(name, leftPointer, rightPointer)` | A key part matched exactly (no trimming, case-sensitive). |
| `AddKeyMapping(name, leftPointer, rightPointer, trim, ignoreCase)` | A key part with trimming and/or case-insensitive matching. |
| `AddTextComparisonSimple(name, leftPointer, rightPointer)` | Exact text; a value is required on both sides. |
| `AddTextComparison(name, leftPointer, rightPointer, trim, ignoreCase, nullPolicy)` | Text with trim, case and null-policy choices. |
| `AddDecimalComparisonSimple(name, leftPointer, rightPointer)` | Exact decimals; tolerance `0`. |
| `AddDecimalComparison(name, leftPointer, rightPointer, absoluteTolerance, nullPolicy)` | Decimals within an absolute tolerance such as `"0.01"`. |
| `AddBooleanComparisonSimple(name, leftPointer, rightPointer)` | JSON `true`/`false` on both sides; a value is required. |
| `AddBooleanComparison(name, leftPointer, rightPointer, nullPolicy)` | Boolean with a null-policy choice. |
| `AddMoneyComparisonSimple(name, leftPointer, rightPointer, leftCurrencyPointer, rightCurrencyPointer)` | An exact amount gated on a currency per side; tolerance `0`. |
| `AddMoneyComparison(name, leftPointer, rightPointer, leftCurrencyPointer, rightCurrencyPointer, absoluteTolerance, nullPolicy)` | Money with a tolerance and a null-policy choice. |
| `AddCalendarDateComparisonSimple(name, leftPointer, rightPointer, leftFormat, rightFormat)` | Text dates in an explicit format per side, compared by day, tolerance 0. |
| `AddCalendarDateComparison(name, leftPointer, rightPointer, leftFormat, rightFormat, toleranceDays, nullPolicy)` | Calendar dates with a tolerance in days and a null-policy choice. |
| `AddInstantComparisonSimple(name, leftPointer, rightPointer)` | ISO instants with an explicit offset or `Z`, compared as UTC instants, tolerance 0. |
| `AddInstantComparison(name, leftPointer, rightPointer, toleranceSeconds, nullPolicy)` | Instants with a tolerance in seconds and a null-policy choice. |
| `ConfigureLimits(...)` | See [Limits](Limits.md). |
| `ConfigureTableLimits(...)` | The extra limits for DataTables; see [DataTables](DataTables.md). |
| `ConfigureOutputLimit(...)` | The size limit of the exported report; see [Export](Export.md). |
| `ClearDefinition()` | Back to the defaults; also clears results. |

Add at least one key before running. A key may have several parts (add several key mappings); parts are
matched as an ordered tuple, so `("a","bc")` never collides with `("ab","c")`. Names must be unique across keys
and comparisons, ignoring case. `nullPolicy` is a drop-down: `RequireValue` or `AllowBothNull`.

A definition with keys and no comparisons is legal: it reports presence only (matched, only-left, only-right,
duplicates, invalid rows).

## With JSON

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
  ],
  "limits": { "maximumRowsPerSide": 50000, "maximumInputCharactersPerSide": 8000000, "maximumResults": 100000, "maximumDifferenceDetails": 100000 }
}
```

- `LoadDefinitionJson` replaces the whole definition (including limits; omitted options take their defaults). Any
  problem rejects it whole and the previous definition stays.
- `ValidateDefinitionJson` reports every problem (path, code, message; up to 100) without changing anything, so a
  definition kept in a Robot Studio asset or file can be checked before use. `errorCount` 0 means valid.
- `GetDefinitionJson` returns the canonical form with every option spelled out. A definition built with methods
  and the same one loaded from JSON produce identical text, so it can be saved and reloaded.
- Unknown or repeated properties, wrong types and numeric enum values are errors.

In JSON the kinds are `Text`, `Decimal`, `Boolean`, `Money`, `CalendarDate` and `Instant`. Only the options that belong to a kind are allowed (anything else is
an error): `Boolean` has none beyond the common ones; `Money` requires `leftCurrencyPointer` and `rightCurrencyPointer` and takes
`absoluteTolerance` like `Decimal`:

```json
{ "name": "Total", "kind": "Money", "leftPointer": "/total", "rightPointer": "/paidAmount",
  "leftCurrencyPointer": "/currency", "rightCurrencyPointer": "/currencyCode", "absoluteTolerance": "0.01" }
```

`CalendarDate` takes `leftFormat` and `rightFormat` (default `yyyy-MM-dd`) and `toleranceDays` (default 0); `Instant` takes `toleranceSeconds`
(default 0). Formats and tolerances are checked when the definition is validated; see [ComparisonRules](ComparisonRules.md).

```json
{ "name": "Due", "kind": "CalendarDate", "leftPointer": "/due", "rightPointer": "/dueDate",
  "leftFormat": "dd/MM/yyyy", "rightFormat": "yyyy-MM-dd", "toleranceDays": 2 }
```

A definition using `Boolean`, `Money`, `CalendarDate` or `Instant` cannot be loaded by a component release that predates them (it reports an unknown kind).

## Which calls discard results

Results belong to the definition that produced them, so a call that changes what a run would do discards them (and the cursors), and the readers then
fail until the next run. A call that is refused changes nothing and discards nothing.

- **Discards results:** `ClearDefinition`, `AddKeyMappingSimple`, `AddKeyMapping`, `AddTextComparisonSimple`, `AddTextComparison`, `AddDecimalComparisonSimple`, `AddDecimalComparison`, `AddBooleanComparisonSimple`, `AddBooleanComparison`, `AddMoneyComparisonSimple`, `AddMoneyComparison`, `AddCalendarDateComparisonSimple`, `AddCalendarDateComparison`, `AddInstantComparisonSimple`, `AddInstantComparison`, `LoadDefinitionJson`, `ConfigureLimits`, `ConfigureTableLimits`
- **Keeps results:** `ConfigureOutputLimit`, `ValidateDefinitionJson`, `GetDefinitionJson`, `ExportResultsJson`, `GetSummary`, `GetSummaryJson`, `ResetResultCursor`, `TryReadNextException`, `TryReadNextDifference`, `GetResultJson`

`ConfigureOutputLimit` keeps results because the output limit bounds only the export: after a report is refused for size, raise the limit and export again
without re-running. `ClearResults` discards results on purpose, and a failed or successful `ReconcileJson`/`ReconcileDataTables` replaces them. The output
limit and the table limits are instance settings, not part of the definition JSON: `ClearDefinition` restores both to their defaults, `LoadDefinitionJson` leaves both alone.

## Pointers

A pointer selects one field: a leading `/` then property names: `/company`, `/customer/id`. Use `~1` for `/` and
`~0` for `~` inside a name. Lookup is case-sensitive; a dot has no special meaning; arrays cannot be walked into.
Left and right pointers are independent, so differently named fields are fine.

## Keys

A key value is a JSON string or a JSON integer (used as its exact text: `123` and `"123"` are the same key; `7` and
`"007"` are not). A fraction, exponent, Boolean, object, array, missing, null or empty key makes that row an
**InvalidRecord**. Trimming removes Unicode white space; case-insensitive matching is ordinal (culture-independent).
