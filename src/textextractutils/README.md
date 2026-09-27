# TextExtractAutomation

A Pega Robot Studio component (`TextExtractUtils`) that pulls labelled, typed business fields (an invoice number, a total, a due date, an IBAN)
out of text such as email bodies, OCR output and terminal screens, **without writing regular expressions**. Describe each field the way a person
sees it (the labels in front of it, where the value sits and what type it is) and read back a validated, normalized value or a stable reason code.

> **Status: under construction.** Work packages 1 to 3 of the [design plan](../../project-docs/plans/2026-09-26-textextractutils-design.md) are
> in: the **template** operations work (building fields with methods, JSON load/validate/export, limits), and the internal text normalization,
> label matching and typed value reading exist and are tested. `ExtractFromText` and the result readers report that they are not implemented yet. Do not use the component until a release says otherwise. It is registered in the solution so it builds and its
> tests run in CI, but it is deliberately not in any release, the root README or `CrossReference.md` yet.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `TextExtractAutomation`
- Assembly: `TextExtractAutomation`
- No NuGet dependencies. Everything is in memory; messages never contain the text being read.

## Templates

A template is a list of fields. A **label field** is found by the words in front of its value; a **pattern field** (an escape hatch for text with no
stable label) is found by a regular expression with a named group `value`.

```json
{
  "schemaVersion": 1,
  "fields": [
    { "name": "InvoiceNumber", "kind": "Label", "labels": ["Invoice No", "Invoice Number", "Invoice #"], "type": "Code" },
    { "name": "Total", "kind": "Label", "labels": ["Total", "Amount Due"], "type": "Amount", "decimalStyle": "CommaDecimal" },
    { "name": "DueDate", "kind": "Label", "labels": ["Due Date"], "position": "NextLine", "type": "Date", "dateFormats": ["dd/MM/yyyy", "yyyy-MM-dd"] }
  ]
}
```

- **Labels** are alternatives (`Invoice No|Invoice Number` in the builder methods). Each has a letter or digit, no line break, at most 128 characters; up to 20 per field.
- **Position**: `SameLine` (default), `NextLine` or `Below`. **Occurrence** when a label appears more than once: `RequireUnique` (default, reported as ambiguous), `First` or `Last`.
- **Type** decides what a valid value is: `Text`, `Code`, `Integer`, `Decimal`, `Amount`, `Date`, `Email`, `Iban`, `Percentage`.
- **Decimal style** (a drop-down) for `Decimal`, `Amount` and `Percentage` fields: `DotDecimal` (1,234.56, the default) or `CommaDecimal` (1.234,56).
  One style per field: `1,234` means a thousand and more in one style and just over one in the other, so it is never guessed. Text that mixes both
  styles needs two templates (or two fields with different labels). Other types ignore it.
- **Date formats** for `Date` fields: one or more formats, tried in order (`dd/MM/yyyy|yyyy-MM-dd` in the builder methods), built from `yyyy`, `MM`,
  `dd` and separators; `yyyy-MM-dd` when none are given. Every other type must be given none. Never the machine culture.
- Field names are unique ignoring case. A template has at most 200 fields and, as saved by `GetTemplateJson`, at most 256,000 characters, so
  anything accepted can be saved and loaded again. Unknown or repeated properties, wrong types and numeric enum values are errors.

## Method reference

All 14 phase 1 methods and their signatures. On any failure a method sets every output to its failure value (null strings, 0 counts and line
numbers, `False` flags) and returns a message naming the operation; success has no message.

### Template

| Method | Signature | Description |
|---|---|---|
| `ClearTemplate` | `bool ClearTemplate(out string message)` | Removes every field and restores the default limits. Also clears any results. |
| `AddLabelFieldSimple` | `bool AddLabelFieldSimple(string name, string labels, FieldType type, out string message)` | Adds a required field whose value follows one of its labels on the same line. Labels are alternatives separated by \|, for example Invoice No\|Invoice Number. The type decides what a valid value is; numbers use DotDecimal (1,234.56) and dates yyyy-MM-dd. |
| `AddLabelField` | `bool AddLabelField(string name, string labels, ValuePosition position, FieldType type, DecimalStyle decimalStyle, string dateFormats, bool required, Occurrence occurrence, out string message)` | Adds a field with every option: where the value sits relative to the label, how numbers are written (DecimalStyle, used by Decimal, Amount and Percentage fields), the date formats for a Date field (separated by \|; empty for yyyy-MM-dd, and empty for every other type), whether it is required, and what to do when the label appears more than once. |
| `AddPatternField` | `bool AddPatternField(string name, string pattern, FieldType type, DecimalStyle decimalStyle, string dateFormats, out string message)` | Adds a required field found by a regular expression with a named group called value, for text that has no stable label (an escape hatch; label fields need no pattern). DecimalStyle and date formats work as in AddLabelField. The match times out rather than hanging the robot. |
| `LoadTemplateJson` | `bool LoadTemplateJson(string templateJson, out string message)` | Replaces the whole template from JSON. An invalid template is rejected whole and the previous one stays in force. |
| `GetTemplateJson` | `bool GetTemplateJson(out string templateJson, out string message)` | Returns the current template, including limits, as canonical JSON that LoadTemplateJson accepts. |
| `ValidateTemplateJson` | `bool ValidateTemplateJson(string templateJson, out int errorCount, out string reportJson, out string message)` | Checks a JSON template without loading it. Returns True when the check ran; errorCount is 0 for a valid template and reportJson lists every problem with its path. |
| `ConfigureLimits` | `bool ConfigureLimits(int maximumTextCharacters, out string message)` | Sets the longest text ExtractFromText accepts, in characters (default 1,000,000; maximum 10,000,000). Longer text fails whole. |

### Run

| Method | Signature | Description |
|---|---|---|
| `ExtractFromText` | `bool ExtractFromText(string text, out int foundCount, out int missingRequiredCount, out string message)` | Extracts every field of the template from the text. True means the extraction ran, even when fields were not found; foundCount and missingRequiredCount summarize it, and GetField reads each field. |

### Results

| Method | Signature | Description |
|---|---|---|
| `GetField` | `bool GetField(string name, out bool found, out string value, out string raw, out string reason, out int lineNumber, out string message)` | Reads one field of the last extraction: found, the normalized value, the text as it appeared, a reason code when it was not found or not valid, and the 1-based line number (0 when none). |
| `GetResultJson` | `bool GetResultJson(out string resultJson, out string message)` | Returns every field of the last extraction as JSON, with values, text as found, reasons and line numbers. |
| `ResetFieldCursor` | `bool ResetFieldCursor(out string message)` | Restarts TryReadNextField from the first field. |
| `TryReadNextField` | `bool TryReadNextField(out bool hasItem, out string name, out string value, out string raw, out string reason, out int lineNumber, out string message)` | Reads the next field of the last extraction, in template order. hasItem is False when there are no more; reason is null when the field was found and valid. |
| `ClearResults` | `bool ClearResults(out string message)` | Discards the last extraction's results. Succeeds even when there are none. |
