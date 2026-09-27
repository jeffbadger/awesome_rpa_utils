# Quick start

An invoice arrives by email:

```text
Invoice Number: INV-2026-0042
Invoice Date: 26/09/2026    Due Date: 26/10/2026
Amount Due: EUR 1.234,50
IBAN: DE89 3704 0044 0532 0130 00
```

## 1. Describe the fields

```csharp
var extract = new TextExtractUtils();

// Labels are alternatives separated by |. The Simple form takes the value on the same line, requires it, and wants one occurrence.
extract.AddLabelFieldSimple("InvoiceNumber", "Invoice Number|Invoice No", FieldType.Code, out string message);
extract.AddLabelFieldSimple("Iban", "IBAN", FieldType.Iban, out message);

// The full form chooses the position, the decimal style (a drop-down), date formats, whether the field is required and what to do with repeats.
extract.AddLabelField("Due", "Due Date|Payment Due", ValuePosition.SameLine, FieldType.Date, DecimalStyle.DotDecimal, "dd/MM/yyyy", true, Occurrence.RequireUnique, out message);
extract.AddLabelField("Total", "Amount Due|Total", ValuePosition.SameLine, FieldType.Amount, DecimalStyle.CommaDecimal, "", true, Occurrence.RequireUnique, out message);
```

Each `Add...` call returns `False` with a message if the field is refused (a repeated name, a bad date format), and then changes nothing.
The whole template can also be loaded from JSON with `LoadTemplateJson`; `GetTemplateJson` gives the JSON of what you built.

## 2. Extract

```csharp
if (!extract.ExtractFromText(emailBody, out int foundCount, out int missingRequiredCount, out message))
{
    // Not run: no text, no fields, or the text is over the size limit. `message` says which.
}
```

For the email above, `foundCount` is 4 and `missingRequiredCount` is 0.

## 3. Read the values

```csharp
extract.GetField("Total", out bool found, out string value, out string raw, out string reason, out int lineNumber, out message);
// found = True, value = "1234.50", raw = "EUR 1.234,50", reason = null, lineNumber = 3
```

| Field | value | raw | line |
|---|---|---|---|
| InvoiceNumber | `INV-2026-0042` | `INV-2026-0042` | 1 |
| Due | `2026-10-26` | `26/10/2026` | 2 |
| Total | `1234.50` | `EUR 1.234,50` | 3 |
| Iban | `DE89370400440532013000` | `DE89 3704 0044 0532 0130 00` | 4 |

`value` is normalized and culture-independent; `raw` is exactly the text it was read from. When `found` is `False`, `reason` says why
(`MissingLabel`, `MissingValue`, `InvalidValue`, `AmbiguousValue`, ...); see [Results](Results.md).

## 4. Or walk every field

```csharp
while (extract.TryReadNextField(out bool hasItem, out string name, out string value, out string raw, out string reason, out int line, out message) && hasItem)
{
    // In Robot Studio: a While loop on hasItem, and a Switch on reason (empty when the field was found).
}
```

`GetResultJson` returns everything at once, including which label matched and the currency of an amount.
