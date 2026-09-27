# TextExtractUtils Pega usability review

This review covers the public surface as designed in `project-docs/plans/2026-09-26-textextractutils-design.md` and as implemented in phase 1 (fields) and phase 2 (tables).
The design was shaped for the Robot Studio design surface from the start: the alternative it replaces is Pega's regex service, which needs a
hand-written regular expression per field, manual OCR tolerance and manual conversion of the captured text.

## Summary

Every port is a scalar (`string`, `bool`, `int`) or one of four enums shown as drop-downs (`FieldType`, `ValuePosition`, `DecimalStyle`,
`Occurrence`). No collection, object or generic type crosses the public surface. Text goes in as a string (typically from an email, OCR or screen
step); the template goes in as method calls or one JSON text; results come out as scalars per field, a cursor, or JSON.

| Area | Rating | Notes |
|---|---|---|
| Template | Direct | `AddLabelFieldSimple` takes a name, label text and a type drop-down; `AddLabelField` adds a position, a decimal style and occurrence drop-downs, date formats as text and a required flag; a whole template can be one JSON asset checked with `ValidateTemplateJson`. |
| Extracting | Direct | One call, `ExtractFromText(text)`; `True` means it ran, even when fields were not found, so a step never parses `message`. |
| Reading one field | Direct | `GetField(name)` returns found, value, raw, reason and line as scalars, ready for a decision step. |
| Reading all fields | Direct | `TryReadNextField` follows the suite's cursor pattern: `hasItem` is the `While` condition and `reason` feeds a `StringSwitch`. |
| Detail | Direct | `GetResultJson` for what scalars do not carry (the matched label, OCR slips, currency, occurrences), ready for a queue payload. |
| Tables | Direct | `AddTableColumn(table, header, type drop-down, decimal style drop-down, date formats)` per column; `TryReadNextRow(table)` is the row cursor (`hasItem` as the `While` condition, `rowNumber` a scalar) and `GetRowValue(column)` returns one cell as scalars, so a row loop needs no JSON. |
| Limits | Direct | `ConfigureLimits` takes one `int`. |

## Findings applied

- **No regular expression in the common case.** A field is described by the words a person sees; case, spacing, separators, typography and a small,
  fixed set of OCR slips are handled by the component. Pattern fields remain as an escape hatch and cannot hang the robot.
- **The decimal style is a drop-down, one per field.** A value like `1,234` means different things in the two styles, so it is chosen, never
  guessed, and a number written in the other style is refused rather than half-read.
- **Not found is a normal outcome.** `False` plus `message` is reserved for calls that could not run; a missing or invalid field is `found = False`
  with a stable reason code, so an automation routes on `reason` rather than on error handling.
- **Failure sentinels are uniform**: null strings, 0 counts and line numbers, `False` flags, so a data link never carries a stale value.
- **`value` and `raw` are separate.** Downstream steps get a normalized, culture-independent value; a person reviewing an exception sees the text
  exactly as it appeared.
- **No overloads or optional parameters**: the Simple and full forms have distinct names.
- **Rows are read the way fields are.** A cell has the same found, value, raw and reason as a field, and the same type drop-downs decide what is
  valid, so a designer who knows fields already knows tables. Each table keeps its own cursor, and a header repeated on every page continues the
  table instead of starting a new one.
- **Messages never quote the input**, so logs and screenshots of error ports do not leak email or document contents.

## Remaining friction

- Date formats are typed as text (`dd/MM/yyyy|yyyy-MM-dd`); a typo is caught when the field is added, not while typing it.
- Changing a pattern field to `First`, `Last` or optional needs a JSON template; the builder method makes required, unique pattern fields.
- `Below` relies on character columns, so it suits screens and aligned reports; tab-separated text should use `SameLine` or `NextLine`.
- Tables must be fixed-width with headers on one line: a header split over two lines, or a table whose cells are separated by single spaces or
  tabs, is not read. A cell that wraps onto the next line is read as a second row.
- A table has no required flag; a design checks `found` in `GetResultJson`, or that `TryReadNextRow` gives at least one row.
