# Comparing two Excel worksheets

Robot Studio's Excel components can export a worksheet to a `DataTable`, and `ReconcileDataTables` reads two `DataTable`s directly, so two worksheets
(from one workbook or two) can be compared with no JSON step:

```text
ExcelConnector (or ExcelRange)   ExportToTable  -> leftTable     (header row -> column names)
ExcelConnector (or ExcelRange)   ExportToTable  -> rightTable
ReconciliationUtils              AddKeyMapping..., Add...Comparison...
ReconciliationUtils              ReconcileDataTables(leftTable, rightTable)
ReconciliationUtils              GetSummary, then the two cursors
```

```csharp
var recon = new ReconciliationUtils();

// Pointers name columns by their header text, exactly: /Invoice Number (spaces are fine).
recon.AddKeyMappingSimple("Invoice", "/Invoice Number", "/Invoice No", out string message);
recon.AddDecimalComparison("Amount", "/Amount", "/Paid", "0.01", ComparisonNullPolicy.RequireValue, out message);
recon.AddTextComparison("Status", "/Status", "/Status", true, true, ComparisonNullPolicy.AllowBothNull, out message);

// erpTable and bankTable come from the Excel components' export to a table (see below).
if (!recon.ReconcileDataTables(erpTable, bankTable, out int exceptionCount, out message))
{
    // Not run: a limit, no keys, a pointer that is not a single column ... `message` says which.
}
```

Then read the summary and walk the exceptions exactly as in the [QuickStart](QuickStart.md).

## Exporting a worksheet

The Excel connector's worksheet and range components both have an `ExportToTable` method (in several overloads), and the connector also has
`ExportToTableWithFilters`, which takes row filters made with `CreateRowFilter` (`Exact`, `RegEx`, `StartsWith`, `EndsWith`, `Contains`,
`NotContains`). The `ExcelHeader` option decides whether the export names the DataTable's columns from the header row: choose **`Header`**,
because the pointers below name columns. The exact parameters differ by overload; use the Pega Excel connector reference for the one you pick.

Things this guide **cannot** promise, because they depend on what the export puts in the cells, and the component only sees the `DataTable`: which .NET type
each cell has (text, `double`, `decimal`, `DateTime`) and what an empty cell becomes. Check once with your export: put the table in a
grid or call `GetType()` on a few cells. The sections below say what the component does with each possibility.

## Column names are the header text

- A pointer is the header text after a `/`: header `Invoice Number` is `/Invoice Number`. Left and right can use different headers, as above.
- Matching is exact, **including case and spaces**. A header `Invoice number` does not match `/Invoice Number`.
- A referenced column that is not in the table is **not a failure**: every row reads it as missing. A misspelled key header therefore shows up as
  a large `invalidLeftRowCount` or `invalidRightRowCount`, and the other side's rows as `OnlyLeft`/`OnlyRight`. **Check the summary after the first run.**
- A header containing `/` is written `~1` in the pointer (`Ref/No` is `/Ref~1No`).

## Totals rows and blank rows

A totals row is data to the component. Left in the export it becomes an exception:

- If its key cell is blank, the row is an `InvalidRecord` with reason `MissingKey` (counted in `invalidLeftRowCount`/`invalidRightRowCount`).
- If it has a label in the key column (`Total`), it is an ordinary unmatched record (`OnlyLeft` or `OnlyRight` with key `["Total"]`).

Either way, remove it before comparing: export a range that stops above the totals row, or use `ExportToTableWithFilters` with a `NotContains` filter on the label
column, or delete the row from the table. Entirely blank rows have blank keys too and are reported the same way as a blank-key totals row.

## Blank cells

A blank cell arrives as `DBNull`, which the component reads as **null**. The null policy of each rule decides what that means:

- `RequireValue` (the default): a null on either side is invalid (`NullNotAllowed`). Use it for columns that must be filled in (amounts).
- `AllowBothNull`: two blanks are equal, and a blank against a value is a difference. Use it for optional columns (status, notes).

An empty cell, an empty string, a zero and a missing column are four different things and are never treated as the same.

## Numbers

- A numeric cell (`double`, `decimal`, integer types) is read as a number. A whole number stored as a `double` (even `10012345`) is an
  integer, so it is a valid **key** and equals the same number written as text; a leading zero in text is different (`007` is not `7`).
- A number stored as **text** is read as a decimal only when it is plain: `1234.50` works. `1,234.50`, `$10.00`, `10 %` and a value with a leading or trailing space are
  `InvalidDecimal` (never guessed at). If a sheet holds formatted text, convert the column to numbers in Excel or clean it before exporting.
- What the cell *shows* is not what it *is*: a cell displayed as `1,234.50` with a number format holds the number 1234.5, which compares fine.
- Comparison is exact within the tolerance you give (`"0.01"`); binary floating-point noise in a `double` (for example `0.1 + 0.2`) can exceed a tolerance of `0`.

## Dates

A date cell that the export returns as a `DateTime` is an **unsupported** value: a date rule reports it as `InvalidType`. Put dates in a **text** column in a format you
choose (in Excel, `=TEXT(A2,"yyyy-mm-dd")`, or format the column as text) and use `AddCalendarDateComparison` with that format, or `AddInstantComparison` for ISO
timestamps with an offset or `Z`. See [ComparisonRules](ComparisonRules.md).

## Duplicates and limits

- Two rows with the same key are never guessed at: one `DuplicateKey` result lists every row (use `GetResultJson`). Rows are numbered from 0 in the order the export gives them.
- The worksheets are only read, never changed. The default limits are 50,000 rows, 100 columns, 2,000,000 cells and 4,096 characters in one text value; see [Limits](Limits.md)
  and [DataTables](DataTables.md). Export only the columns and rows you need.
