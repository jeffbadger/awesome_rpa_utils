# Exporting a report

`ExportResultsJson(runLabel)` turns the last completed run into one JSON text you can keep, attach to a case, archive (for example
with `ArchiveUtils`) or hand to another system. It contains everything needed to understand the run without the component:
the exact definition that produced it, the summary, and every result.

```json
{
  "schemaVersion": 1,
  "runLabel": "invoice-close-2026-09-25",
  "definition": { "schemaVersion": 1, "keys": [ "..." ], "comparisons": [ "..." ], "limits": { "...": 0 } },
  "summary": { "leftRowCount": 0, "rightRowCount": 0, "...": 0 },
  "results": [ { "id": "r000001", "kind": "Matched", "...": 0 } ]
}
```

(The `...` entries only shorten the example; a real report has every property.)

```csharp
if (recon.ExportResultsJson("invoice-close-2026-09-25", out string reportJson, out string message))
{
    // reportJson is the whole report. Save it, attach it, or pass it on.
}
else
{
    // No report: there are no results, the label is not usable, or the report is over the output limit.
    // `message` says which; the results themselves are still there unless there were none.
}
```

## What is in it

- **`definition`** is exactly what `GetDefinitionJson` returns: every option spelled out, including the four limits.
- **`summary`** is exactly what `GetSummaryJson` returns.
- **`results`** holds every result in order, matched pairs included: each item is exactly what `GetResultJson` returns for that ID
  (kind, key, reason, every member with its side, row and original key parts, and every kept difference with values as found,
  interpreted values, delta and, for money, the currencies).
- Missing, null, empty and zero stay distinguishable, as everywhere else: a missing value and a JSON `null` both read `null` in the value
  properties, and the `leftPresent`/`rightPresent` flags tell them apart.

The report contains **your data** (key parts and the values of the compared fields). Nothing is logged and nothing is written to disk by the
component: the text goes only to the output port, so decide where it may be stored.

## Deterministic

The same inputs, the same definition and the same label always give the same text, byte for byte. Nothing is added that varies: no timestamp,
no machine name, no random or generated identifier. To record when a report was made, put the date in the label. Result IDs (`r000001`, ...) are local
to one run, so do not use them as a permanent identity across runs; use the key parts.

## The run label

A free-text name for the report, at most 256 characters, copied into the report as given (escaped as JSON text). Null is treated as empty. A label
with an unpaired surrogate character is refused. The label is supplied here and not to `ReconcileJson`, so one run can be exported under different labels.

## The output limit

`ConfigureOutputLimit(maximumOutputCharacters)` bounds the report, in characters:

| Limit | Default | Maximum |
|---|---|---|
| `maximumOutputCharacters` | 16,000,000 | 64,000,000 |

A report over the limit is **refused whole**: `False`, a message naming the limit, and no report. Nothing is truncated. The results stay intact, and
changing this limit does **not** discard them (unlike a change to the definition), so the usual response is to raise the limit and export again, or to
read the results with the cursors and `GetResultJson` instead. The check is made while the report is written, which stops early rather than building an
oversized text first: a value that cannot fit is refused before it is written, so the report buffer never holds more than the limit (at most three bytes per allowed
character, because a character can take up to three bytes in UTF-8) plus the encoded size of one value.

A run with many results carrying many differences can exceed the default. Measured: a result with two field differences is about 875 characters, so 50,000 of
them (a full-size default run where every row differs) produce a report of about 43.8 million characters and are refused under the default limit; a matched
pair with a short key is about 230 characters. If you expect many exceptions, raise the limit before exporting (up to 64,000,000). `ClearDefinition` restores the default limit;
`LoadDefinitionJson` leaves it alone.

Export works the same after `ReconcileDataTables` as after `ReconcileJson`.
