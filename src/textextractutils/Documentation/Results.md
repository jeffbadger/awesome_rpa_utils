# Results

## Reading

| Method | Returns |
|---|---|
| `ExtractFromText(text)` | `foundCount`, `missingRequiredCount` |
| `GetField(name)` | `found`, `value`, `raw`, `reason`, `lineNumber` (names are matched ignoring case) |
| `TryReadNextField()` | `hasItem`, `name`, `value`, `raw`, `reason`, `lineNumber`, for every field in template order |
| `ResetFieldCursor()` | starts `TryReadNextField` again from the first field |
| `GetResultJson()` | everything, as JSON |
| `TryReadNextRow(table)`, `GetRowValue(column)` | the rows of a table, one cell at a time (see [Tables](Tables.md)) |
| `ClearResults()` | discards the results (always succeeds) |

- `value`: normalized, culture-independent; null unless found.
- `raw`: exactly the text that was read (`EUR 1.234,50` from `Amount Due: EUR 1.234,50 incl. VAT`). Also given for an `InvalidValue`, so a person can
  see what was there.
- `lineNumber`: 1-based; the value's line, or the label's line when the value is missing, or 0.

## Reason codes

`reason` is null when the field was found. Otherwise (a table's header line not found is `MissingLabel`, and a table cell can be `MissingValue`
or `InvalidValue`):

| Reason | Meaning |
|---|---|
| `MissingLabel` | None of the field's labels was found (for a pattern field: no match). |
| `MissingValue` | The label is there, but there is no text where the value should be. |
| `InvalidValue` | Text is there, but it is not a valid value of the field's type; `raw` shows it. |
| `AmbiguousValue` | The label appears more than once with different valid values, and the field is `RequireUnique`. |
| `PatternTimeout` | A pattern field took too long and was stopped (see [Patterns](Patterns.md)). |
| `TooManyMatches` | A pattern field matched more than 10,000 times. |
| `TooManyRows` | A table has more than 10,000 rows; it has no rows (tables only, see [Tables](Tables.md)). |

`missingRequiredCount` counts required fields that were not found, whatever the reason; optional fields never count.

## Repeated labels (occurrence)

- `RequireUnique` (the default): a label found more than once is fine when every valid occurrence has the **same** value (a total in a header and a
  footer); two **different** valid values are `AmbiguousValue`, never a guess. Occurrences with an invalid value do not count. When none is valid,
  the first occurrence's reason is reported.
- `First` / `Last`: that occurrence, as it is (if it is invalid, the field is invalid).

## The result JSON

```json
{
  "foundCount": 1,
  "missingRequiredCount": 0,
  "fields": [
    { "name": "Total", "found": true, "required": true, "value": "1234.50", "raw": "EUR 1.234,50", "reason": null, "explanation": null,
      "lineNumber": 3, "label": "Amount Due", "labelSlipped": false, "currency": "EUR", "occurrences": 1 }
  ]
}
```

`explanation` says in words why a field was not found; like every message, it never quotes the text.

## What discards results

Results belong to the template and text that produced them:

- **Discards results** (and the cursors): every accepted template change (`AddLabelFieldSimple`, `AddLabelField`, `AddPatternField`, `AddTableColumn`,
  `ClearTemplate`, `LoadTemplateJson`, `ConfigureLimits`), `ClearResults`, and every call to `ExtractFromText`, even one that fails.
- **Keeps results**: a refused template change, `GetTemplateJson`, `ValidateTemplateJson` and the read methods.

Before the first extraction (or after results are discarded), every reader returns `False` with a message saying to run `ExtractFromText` first.
