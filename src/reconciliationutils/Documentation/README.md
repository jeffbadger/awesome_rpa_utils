# ReconciliationUtils documentation

`ReconciliationUtils` compares two datasets (two JSON arrays of objects or two DataTables, for example the rows
of a spreadsheet export and of an application report) by business key, and tells an automation
exactly which records match, differ, are missing on one side, are ambiguous, or could not be
compared. Every result comes back through scalar ports, so it can drive a Robot Studio
`Switch`, a loop or a queue without any collection proxy.

| Page | What it covers |
|---|---|
| [QuickStart](QuickStart.md) | The smallest complete flow: define, run, read the summary, walk the exceptions and their differences. |
| [DataTables](DataTables.md) | Reconciling DataTables directly (Excel, CSV, database results), how cell values are read, and the table limits. |
| [ExcelWorksheets](ExcelWorksheets.md) | Comparing two Excel worksheets: export each to a table, header-named columns, totals and blank rows, blank cells, numbers and dates. |
| [Configuration](Configuration.md) | Building a definition with methods or JSON, keys, comparisons, validation and the canonical form. |
| [ComparisonRules](ComparisonRules.md) | Exactly how Text, Decimal, Boolean, Money, calendar-date and instant comparisons decide equal, different or invalid, with the reason codes. |
| [ResultsAndCounts](ResultsAndCounts.md) | The seven result kinds, the counts and the accounting equations, both cursors and `GetResultJson`. |
| [Export](Export.md) | Exporting a deterministic JSON report of a run, the run label and the output limit. |
| [QueueHandoff](QueueHandoff.md) | Turning exceptions into work items (for example in `LocalQueueUtils`) for review or correction. |
| [Limits](Limits.md) | The resource limits (run, DataTable and export), their defaults and maximums, what a run does when one is exceeded, and measured cost. |

The [component README](../README.md) holds the method reference. Conventions shared by every method:

- Every method returns `bool` and ends with `out string message`. `False` plus a message means the
  call could not be done (bad input, a limit, no results, disposed). It never throws.
- A reconciliation that finds mismatches is still a **success** (`True`, `message` null). Read the
  outcome from the outputs, not from `message`.
- On failure every output holds its sentinel: null strings, 0 counts, `False` flags, `-1` row indices.
- Messages never contain data from your rows. Values come only through the value outputs, `GetResultJson` and `ExportResultsJson`.
- Use one instance per automation flow. Calls are serialized by an instance lock.
- The component is in-memory only: nothing is written to disk and nothing leaves the machine.
