# Quick start

Two invoice lists: what the ERP says (left) and what the bank paid (right).

```json
// left
[ { "invoice": "INV-100", "amount": "10.00", "status": "Open" },
  { "invoice": "INV-101", "amount": "20.00", "status": "Open" },
  { "invoice": "INV-102", "amount": "5.00",  "status": "Open" } ]

// right
[ { "invoiceId": "INV-100", "paid": "10.00", "status": "open" },
  { "invoiceId": "INV-101", "paid": "21.50", "status": "Open" },
  { "invoiceId": "INV-103", "paid": "9.00",  "status": "Open" } ]
```

## 1. Define

Left and right may name fields differently. Keys identify a record; comparisons say what must agree.

```csharp
var recon = new ReconciliationUtils();

recon.AddKeyMappingSimple("Invoice", "/invoice", "/invoiceId", out string message);
recon.AddDecimalComparisonSimple("Amount", "/amount", "/paid", out message);
recon.AddTextComparison("Status", "/status", "/status", trim: true, ignoreCase: true,
                        ComparisonNullPolicy.RequireValue, out message);
```

Every `Add...` call returns `False` and a message if it is refused (for example a duplicate name or a
bad pointer), and then changes nothing. You can also load the whole definition from JSON; see
[Configuration](Configuration.md).

## 2. Run

```csharp
string leftJson = /* the left array above; in Robot Studio, a String variable */ "[ ... ]";
string rightJson = /* the right array above */ "[ ... ]";

if (!recon.ReconcileJson(leftJson, rightJson, out int exceptionCount, out message))
{
    // The run could not complete (bad JSON, a limit, no keys ...): log `message` and stop the flow.
    // No results exist from this or any earlier run.
}
```

`True` means the run completed. `exceptionCount` is how many results need attention (0 = everything matched).

## 3. Read the summary

```csharp
recon.GetSummary(out int leftRows, out int rightRows, out int matchedPairs, out int exceptions, out message);
// 3, 3, 1 matched pair, 3 exceptions
```

`GetSummaryJson` returns every count. See [ResultsAndCounts](ResultsAndCounts.md).

## 4. Walk the exceptions and their differences

```csharp
while (true)
{
    recon.TryReadNextException(out bool hasItem, out string id, out string kind, out string keyJson,
        out int leftRow, out int rightRow, out string reason, out int differenceCount, out message);
    if (!hasItem) break;

    // Different  ["INV-101"]  rows 1/1, 1 difference
    // OnlyLeft   ["INV-102"]  rows 2/-1
    // OnlyRight  ["INV-103"]  rows -1/2

    while (true)
    {
        recon.TryReadNextDifference(out bool more, out string rule, out string code,
            out string leftValue, out string rightValue, out string explanation, out message);
        if (!more) break;
        // Amount  DecimalMismatch  "20.00"  "21.50"
    }
}
```

In Robot Studio this is two `While` loops over the same two methods; `hasItem` is the loop condition.
`kind` feeds a `Switch` (`Different`, `OnlyLeft`, `OnlyRight`, `DuplicateKey`, `InvalidComparison`,
`InvalidRecord`). `INV-100` matched (the status differs only by case, which this comparison ignores), so
it appears in the counts but is not an exception.

Run the same again with different data and the previous results are replaced. If a run fails,
nothing from the old run remains to be read by accident.
