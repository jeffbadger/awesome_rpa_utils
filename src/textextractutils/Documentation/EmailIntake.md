# Worked example: invoice email intake

An automation reads invoice emails from a mailbox, extracts the fields, and adds a work item to a `LocalQueueUtils` queue for each email: complete
invoices for automatic posting, incomplete ones for a person to check.

## The template

Kept as a Robot Studio asset or a file, loaded once with `LoadTemplateJson`:

```json
{
  "schemaVersion": 1,
  "fields": [
    { "name": "InvoiceNumber", "kind": "Label", "labels": ["Invoice Number", "Invoice No", "Invoice #"], "type": "Code" },
    { "name": "Supplier", "kind": "Label", "labels": ["From", "Supplier"], "type": "Text" },
    { "name": "Due", "kind": "Label", "labels": ["Due Date", "Payment Due"], "type": "Date", "dateFormats": ["dd/MM/yyyy", "yyyy-MM-dd"] },
    { "name": "Total", "kind": "Label", "labels": ["Amount Due", "Total"], "type": "Amount", "decimalStyle": "CommaDecimal" },
    { "name": "Iban", "kind": "Label", "labels": ["IBAN"], "type": "Iban" },
    { "name": "PurchaseOrder", "kind": "Label", "labels": ["PO Number", "Order No"], "type": "Code", "required": false }
  ]
}
```

## An email

```text
From: ACME Supplies GmbH
Invoice Number: INV-2026-0042
Due Date: 26/10/2026
Amount Due: EUR 1.234,50
IBAN: DE89 3704 0044 0532 0130 00
```

`ExtractFromText` gives `foundCount` 5 and `missingRequiredCount` 0: every required field is found, and the optional `PurchaseOrder` is simply
missing.

## Queue the result

```csharp
// For each email body:
if (!extract.ExtractFromText(body, out int foundCount, out int missingRequired, out message))
{
    // Could not run (for example, the email is over the size limit): queue it for a person with the message.
}
extract.GetResultJson(out string resultJson, out message);
extract.GetField("InvoiceNumber", out bool hasNumber, out string invoiceNumber, out _, out _, out _, out message);

// Complete invoices go to the posting queue with the invoice number as business key (a re-sent email becomes a duplicate, not a second item);
// incomplete ones go to the review queue, where a person sees each field's reason.
string queue = missingRequired == 0 ? postingQueue : reviewQueue;
queueUtils.AddJson(queue, resultJson, out string itemId, out bool duplicate, out message, hasNumber ? invoiceNumber : null);
```

Notes:

- The payload is the result JSON, so the worker has every value, its `raw` text, and a reason for anything missing, without the email.
- The payload contains data from the email; handle the queue like the mailbox.
- Values that needed an OCR slip (`labelSlipped`) or that came from a text where a label appeared more than once can be routed to review too:
  both are in the result JSON.
