# ReconciliationUtils documentation

`ReconciliationUtils` compares two datasets (two JSON arrays of objects, for example the rows
of a spreadsheet export and of an application report) by business key, and tells an automation
exactly which records match, differ, are missing on one side, are ambiguous, or could not be
compared. Every result comes back through scalar ports, so it can drive a Robot Studio
`Switch`, a loop or a queue without any collection proxy.

| Page | What it covers |
|---|---|
| [QuickStart](QuickStart.md) | The smallest complete flow: define, run, read the summary, walk the exceptions and their differences. |
| [Configuration](Configuration.md) | Building a definition with methods or JSON, keys, comparisons, validation and the canonical form. |
| [ComparisonRules](ComparisonRules.md) | Exactly how Text and Decimal comparisons decide equal, different or invalid, with the reason codes. |
| [ResultsAndCounts](ResultsAndCounts.md) | The seven result kinds, the counts and the accounting equations, both cursors and `GetResultJson`. |
| [QueueHandoff](QueueHandoff.md) | Turning exceptions into work items (for example in `LocalQueueUtils`) for review or correction. |
| [Limits](Limits.md) | The four resource limits, their defaults and maximums, and what a run does when one is exceeded. |

The [component README](../README.md) holds the method reference. Conventions shared by every method:

- Every method returns `bool` and ends with `out string message`. `False` plus a message means the
  call could not be done (bad input, a limit, no results, disposed). It never throws.
- A reconciliation that finds mismatches is still a **success** (`True`, `message` null). Read the
  outcome from the outputs, not from `message`.
- On failure every output holds its sentinel: null strings, 0 counts, `False` flags, `-1` row indices.
- Messages never contain data from your rows. Values come only through the value outputs and `GetResultJson`.
- Use one instance per automation flow. Calls are serialized by an instance lock.
- The component is in-memory only: nothing is written to disk and nothing leaves the machine.
