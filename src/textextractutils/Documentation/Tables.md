# Tables

A **table** reads fixed-width rows under a header line: the line items of an invoice, the transactions of a statement, the rows of a report or
a terminal screen. Describe it by its column headers and their types; each row comes back as typed cells, read the same way as fields.

## Defining a table

`AddTableColumn(table, header, type, decimalStyle, dateFormats)` adds one column, creating the table when its name is new. Add the columns in the
order you want them reported (usually left to right). A template can hold fields and tables together, or tables alone.

```csharp
extract.AddLabelFieldSimple("InvoiceNumber", "Invoice Number", FieldType.Code, out message);
extract.AddLabelFieldSimple("Total", "Total", FieldType.Amount, out message);
extract.AddTableColumn("Lines", "Item", FieldType.Code, DecimalStyle.DotDecimal, "", out message);
extract.AddTableColumn("Lines", "Description", FieldType.Text, DecimalStyle.DotDecimal, "", out message);
extract.AddTableColumn("Lines", "Qty", FieldType.Integer, DecimalStyle.DotDecimal, "", out message);
extract.AddTableColumn("Lines", "Unit Price", FieldType.Amount, DecimalStyle.DotDecimal, "", out message);
extract.AddTableColumn("Lines", "Amount", FieldType.Amount, DecimalStyle.DotDecimal, "", out message);
```

In a JSON template the same table is:

```json
{
  "schemaVersion": 1,
  "tables": [
    { "name": "Lines", "columns": [
      { "header": "Item", "type": "Code" },
      { "header": "Description", "type": "Text" },
      { "header": "Qty", "type": "Integer" },
      { "header": "Unit Price", "type": "Amount", "decimalStyle": "DotDecimal" },
      { "header": "Amount", "type": "Amount", "decimalStyle": "DotDecimal" }
    ] }
  ]
}
```

- A table name shares the field names' namespace: it must differ, ignoring case, from every field and every other table. Later columns must spell
  the table name exactly as the first did.
- A header follows the rules of a single label (a letter or digit, no line break, at most 128 characters) and may not contain `|`. Headers are
  unique within a table, ignoring case. At most 50 columns per table and 20 tables per template.
- The type, decimal style and date formats of a column work exactly as for a field (see [Types](Types.md)).

## How a table is found

1. **The header line** is a line on which every column's header is found. Headers are matched like labels (see [Labels](Labels.md)): ignoring
   case and extra spaces, at word boundaries, with the same limited OCR slips. The header's position on that line is the column's position.
2. **The rows** are the lines after the header line, up to the first of: a blank line, a line holding a label of a label field (such as a `Total`
   line after the items), another header line, or the end of the text. Lines made only of `-`, `=`, `_`, `+`, `|` and spaces (the rule under
   a header) are skipped.
3. **A repeated header line continues the table**, so a report whose header is printed again on every page is read as one table, and row numbers
   carry on. Text between the pages (a page footer, a blank line) is not read as rows.
4. **Cells**: a row is split into cells at runs of two or more spaces (one space keeps `Red widget` together). Each column takes, left to right,
   the first unused cell that overlaps its header, or else the first unused cell that lies between the previous column's header and the next one
   without touching either. Right-aligned numbers and values a little out of line therefore still land in their column, and a value under the
   next column's header is never taken by an empty column before it. A column with nothing under it is `MissingValue` for that row.

For this text:

```text
Invoice Number: INV-7

Item        Description              Qty      Unit Price       Amount
----------  -----------------------  ---  -------------  -----------
A-100       Blue widget              2           12.50        25.00
B-200       Red widget, large        10           1.25        12.50
C-300       Service fee              1           99.00        99.00
Total: 136.50
```

the rows are:

| Row | Item | Description | Qty | Unit Price | Amount |
|---|---|---|---|---|---|
| 1 | A-100 | Blue widget | 2 | 12.50 | 25.00 |
| 2 | B-200 | Red widget, large | 10 | 1.25 | 12.50 |
| 3 | C-300 | Service fee | 1 | 99.00 | 99.00 |

and the `Total` line both ends the table and is read as the `Total` field (136.50).

## Reading rows

```text
TryReadNextRow(table)      -> hasItem, rowNumber, rowJson        (the next row; hasItem False when there are no more)
GetRowValue(column)        -> found, value, raw, reason          (one cell of the row TryReadNextRow read last)
```

- In Robot Studio, loop with `hasItem` as the `While` condition and read each column with `GetRowValue`. Each table has its own cursor; a new
  extraction starts every table from its first row again.
- `rowNumber` is 1-based within the table (0 when there is no row). Table names and column headers are matched ignoring case.
- A cell's `value`, `raw` and `reason` mean what they mean for a field: `reason` is null when the cell was read, `MissingValue` when there is
  nothing under the column, or `InvalidValue` when the text is not a valid value of the column's type (`raw` shows it).
- `rowJson` holds the whole row, with every cell in template column order (shortened here to two cells):

```json
{ "rowNumber": 1, "lineNumber": 5, "cells": [
  { "column": "Item", "found": true, "value": "A-100", "raw": "A-100", "reason": null },
  { "column": "Qty", "found": true, "value": "2", "raw": "2", "reason": null }
] }
```

- `GetResultJson` adds a `tables` array (only when the template has tables): each table's `name`, `found`, `reason`, `explanation`,
  `headerLine` (the first header line, or 0), `rowCount` and its `rows`.
- A table whose header line is not found has no rows; `TryReadNextRow` returns `True` with `hasItem` False, and the result JSON gives the reason
  `MissingLabel`. A table with more than 10,000 rows has no rows and the reason `TooManyRows`.
- `TryReadNextRow` fails (`False`) for a table name the template does not have; `GetRowValue` fails before a row has been read, after the rows
  are exhausted, and for a column the table does not have.
- Tables do not change `foundCount` or `missingRequiredCount`, which count fields only.
