# Reconciling DataTables

`ReconcileDataTables(leftTable, rightTable)` does what `ReconcileJson` does, reading two `DataTable`s directly. Use it when
the data is already in a Robot Studio DataTable: an Excel range, a CSV, a database query. No JSON step is needed. For Excel worksheets specifically, see [ExcelWorksheets](ExcelWorksheets.md). Everything
after the run (summary, cursors, `GetResultJson`, the limits on results) is identical.

```csharp
var recon = new ReconciliationUtils();

// A pointer names exactly one column: /Invoice, /Amount. Column names are the field names.
recon.AddKeyMappingSimple("Invoice", "/Invoice", "/InvoiceId", out string message);
recon.AddDecimalComparisonSimple("Amount", "/Amount", "/Paid", out message);

if (!recon.ReconcileDataTables(erpTable, bankTable, out int exceptionCount, out message))
{
    // Not run: a limit, no keys, a pointer that names more than one column ... `message` names the side and the reason, never a value.
}
```

## Pointers and columns

- A pointer must be exactly one segment, the column name: `/Amount`. This includes the currency pointers of a Money rule. Deeper pointers such as `/a/b` are refused with a
  message naming the mapping. For a column whose name contains a slash, write `~1`: the column `Ref/No` is `/Ref~1No`.
- Column names are matched exactly, including case. Left and right may use different names, as above.
- A referenced column that a table does not have is read as **missing on every row**, exactly like a missing JSON property.
  For a comparison column, every pair reports `MissingField` (an `InvalidComparison`). For a key column, every row of that
  table is an `InvalidRecord` (`MissingKey`); such rows are never reported as `OnlyLeft` or `OnlyRight`, because a row that has no
  key cannot be said to have, or lack, a counterpart. The run does not fail, so a mistyped column name shows up as a large
  `invalidLeftRowCount`/`invalidRightRowCount` (or `MissingField` differences) in the summary; check the counts after a run.
- Only the columns the definition references are read. Other columns are ignored (they still count toward the column and cell limits).

## How values are read

| Cell value | Read as |
|---|---|
| `DBNull` | null |
| `string` | text |
| `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong` | an integer (its exact digits) |
| `decimal` | a number, exactly as the decimal prints (`10.00` keeps its scale) |
| `double`, `float` | a whole value up to 2^53 (9,007,199,254,740,992) is an **integer**, written as its digits (so a `float` that would print as `3E+09` reads as `3000000000`, and negative zero is `0`, not `-0`); any other value is a number, as the shortest text that reads back as the same value (`0.1` is `0.1`, not `0.1000000000000000055...`); `NaN` and infinity are unsupported |
| `bool` | a Boolean |
| anything else (`DateTime`, `DateTimeOffset`, `Guid`, `TimeSpan`, `char`, `byte[]`, objects) | unsupported |

Read through the usual rules: a number given to a text rule is `InvalidType`, a Boolean given to a decimal rule is
`InvalidType`, an unsupported value used as a key makes the row an `InvalidRecord`, and an integer key matches its text form
(`123` and `"123"` are the same key). Dates are not read as dates: a `DateTime` or `DateTimeOffset` cell is unsupported, so put dates in a text column and use a calendar-date or
instant rule, which parse the text with an explicit format (a date rule given a `DateTime` cell is `InvalidType`).

`double` and `float` carry binary rounding, so a value such as `0.1` computed in floating point may differ from a decimal
`0.1` by a hair. If exactness matters, load the column as `decimal` or as text.

## Rows

- A row's index in results is its position among the table's rows that are not deleted. A row with `RowState` `Deleted`
  is not part of the data and is skipped.
- The tables are read, never modified, and are read once at the start of the run: changing a table afterwards does not
  change published results.

## Limits

The row limit and the total text limit are the ones from `ConfigureLimits` (`maximumRowsPerSide`,
`maximumInputCharactersPerSide`). `ConfigureTableLimits(maximumColumns, maximumCells, maximumValueCharacters)` adds three
that only apply to tables:

| Limit | Default | Maximum | Applies to |
|---|---|---|---|
| `maximumColumns` | 100 | 1,000 | Columns in each table as supplied. |
| `maximumCells` | 2,000,000 | 10,000,000 | Rows times columns in each table. |
| `maximumValueCharacters` | 4,096 | 1,000,000 | Characters in one text value in a referenced column. |

They are checked in this order, before any value is interpreted: rows, columns, cells; then, while reading, each text value
and the running total of text characters (against `maximumInputCharactersPerSide`). A table with few rows and very many
columns, or with huge strings, therefore fails before it can exhaust memory. A failure names the side and the limit, and for
a text value its row and column, never the value.

The table limits are not part of the definition JSON. `ClearDefinition` restores their defaults; `LoadDefinitionJson` leaves
them alone. Like any setup change, `ConfigureTableLimits` discards existing results.
