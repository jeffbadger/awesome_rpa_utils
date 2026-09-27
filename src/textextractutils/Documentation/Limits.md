# Limits

| Limit | Default | Maximum | Set with |
|---|---|---|---|
| Characters of text for `ExtractFromText` | 1,000,000 | 10,000,000 | `ConfigureLimits` (or `limits.maximumTextCharacters` in JSON) |
| Fields in a template | 200 | 200 | fixed |
| Labels per field | 20 | 20 | fixed |
| Characters in a label or a field name | 128 | 128 | fixed |
| Characters in a pattern | 1,024 | 1,024 | fixed |
| Date formats per field | 10 | 10 | fixed |
| Characters of a template as `GetTemplateJson` writes it | 256,000 | 256,000 | fixed |
| One pattern match attempt | 100 ms | 100 ms | fixed |
| All of one pattern field's matches | 1 s | 1 s | fixed |
| Occurrences of one pattern field | 10,000 | 10,000 | fixed |

- Text over the limit: `ExtractFromText` returns `False` with a message naming the limit, and no results remain.
- A template change that would exceed a template limit is refused and changes nothing. The template size is checked whenever a template changes, so
  anything accepted can be saved with `GetTemplateJson` and loaded again.
- The pattern limits are per field: they give that field `PatternTimeout` or `TooManyMatches`, and the rest of the extraction is unaffected.
