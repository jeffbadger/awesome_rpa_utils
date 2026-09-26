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
| `ConfigureLimits(...)` | See [Limits](Limits.md). |
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

## Pointers

A pointer selects one field: a leading `/` then property names: `/company`, `/customer/id`. Use `~1` for `/` and
`~0` for `~` inside a name. Lookup is case-sensitive; a dot has no special meaning; arrays cannot be walked into.
Left and right pointers are independent, so differently named fields are fine.

## Keys

A key value is a JSON string or a JSON integer (used as its exact text: `123` and `"123"` are the same key; `7` and
`"007"` are not). A fraction, exponent, Boolean, object, array, missing, null or empty key makes that row an
**InvalidRecord**. Trimming removes Unicode white space; case-insensitive matching is ordinal (culture-independent).
