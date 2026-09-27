# Limits

| Limit | Default | Maximum | Set with |
|---|---|---|---|
| Characters of text for `ExtractFromText` | 1,000,000 | 10,000,000 | `ConfigureLimits` (or `limits.maximumTextCharacters` in JSON) |
| Fields in a template | 200 | 200 | fixed |
| Labels per field | 20 | 20 | fixed |
| Characters in a label or a field name | 128 | 128 | fixed |
| Characters in a pattern | 1,024 | 1,024 | fixed |
| Date formats per field or column | 10 | 10 | fixed |
| Tables in a template | 20 | 20 | fixed |
| Columns in a table | 50 | 50 | fixed |
| Rows in a table | 10,000 | 10,000 | fixed |
| Characters of a template as `GetTemplateJson` writes it | 256,000 | 256,000 | fixed |
| One pattern match attempt | 100 ms | 100 ms | fixed |
| All of one pattern field's matches | 1 s | 1 s | fixed |
| Occurrences of one pattern field | 10,000 | 10,000 | fixed |

- Text over the limit: `ExtractFromText` returns `False` with a message naming the limit, and no results remain.
- A template change that would exceed a template limit is refused and changes nothing. The template size is checked whenever a template changes, so
  anything accepted can be saved with `GetTemplateJson` and loaded again.
- A table with more than 10,000 rows gets the reason `TooManyRows` and no rows; the fields and other tables are unaffected.
- The pattern limits are per field: they give that field `PatternTimeout` or `TooManyMatches`, and the rest of the extraction is unaffected.

## Measured cost

On a Linux development machine (Intel i9-14900KF, .NET 10, Release build, each workload in its own process). Times are one `ExtractFromText` call,
averaged over repeated runs after a first warm-up run; "allocated" is the memory allocated per call, most of it short-lived; "peak" is the whole
test process. The measurements are repeatable: set `TEXTEXTRACT_MEASURE=1` and run the `MeasurementTests` one at a time (see the class remarks).

| Workload | Characters | Fields | Time per call | Allocated per call | Peak process |
|---|---|---|---|---|---|
| An invoice email (10 fields, one of them a pattern) | 8,443 | 10 | 0.36 ms | 0.4 MB | 119 MB |
| A 10,000-line report, 50 fields with two labels each | 710,140 | 50 | 30 ms | 28 MB | 150 MB |
| The maximum: 200 fields with 20 labels each over 10,000,000 characters (the text limit) | 10,000,000 | 200 | 6.3 s | 432 MB | 286 MB |
| A statement with a 10,000-row table (the row limit) of 5 typed columns, and 2 fields | 680,108 | 2 | 61 ms | 65 MB | 173 MB |
| The maximum tables: 20 tables of 50 columns, all headers different, over 10,000,000 characters where every line is one header short of a header line (the worst case for header search) | 10,000,000 | 0 | 6.1 s | 491 MB | 257 MB |

Typical documents take well under a millisecond; the cost grows with the length of the text and the number of labels and headers. Only a template and a text at
the maximums take seconds. Timings on a Windows robot will differ; treat these as an order of magnitude.
