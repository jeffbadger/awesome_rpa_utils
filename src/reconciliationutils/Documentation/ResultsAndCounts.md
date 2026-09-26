# Results and counts

## Result kinds

Each input row lands in exactly one result. Results come in left-row order, then the remaining right rows, with IDs
`r000001`, `r000002`, ... (an ID is a reference within one run, not a permanent identity).

| Kind | Meaning | Exception? |
|---|---|---|
| `Matched` | One row each side under the same key; every comparison equal. | No |
| `Different` | A pair with at least one unequal comparison and none invalid. | Yes |
| `InvalidComparison` | A pair with at least one comparison that could not be made. | Yes |
| `OnlyLeft` / `OnlyRight` | A valid key present on one side only. | Yes |
| `DuplicateKey` | More than one row on either side for the same key: one result listing every member. Nothing is guessed or zipped, and a unique row on the other side belongs to the group. | Yes |
| `InvalidRecord` | A row that is not an object, or whose key is missing, null, empty or of a wrong type. | Yes |

## Counts

`GetSummary` returns the headline counts. `GetSummaryJson` returns all of them:

`leftRowCount`, `rightRowCount`, `matchedPairCount`, `differentPairCount`, `invalidPairCount`, `onlyLeftCount`,
`onlyRightCount`, `invalidLeftRowCount`, `invalidRightRowCount`, `ambiguousKeyCount`, `ambiguousLeftRowCount`,
`ambiguousRightRowCount`, `resultCount`, `exceptionCount`, `differenceCount`, `allMatched`, `bothInputsEmpty`.

The run checks these equations before it publishes anything (a failure fails the run):

- left rows = matched + different + invalid pairs + onlyLeft + invalid left rows + ambiguous left rows
- right rows = the same pairs + onlyRight + invalid right rows + ambiguous right rows
- results = pairs + onlyLeft + onlyRight + invalid rows + ambiguous keys
- exceptions = results - matched pairs

`allMatched` is true when there are no exceptions. Two empty inputs also give `allMatched` true, so check
`bothInputsEmpty` when "nothing to compare" should be treated differently.

## The two cursors

```text
TryReadNextException(...)        -> hasItem, resultId, kind, keyJson, leftRowIndex, rightRowIndex, reason, differenceCount
  TryReadNextDifference(...)     -> hasItem, ruleName, reasonCode, leftValueJson, rightValueJson, explanation
```

- Only exceptions are read, in result order. `reason` is the stable reason code (null when there is none).
- `TryReadNextDifference` reads the differences of the exception read most recently; reading the next exception
  restarts it. An exception with no field differences (a lone record, an invalid row, a duplicate group) has
  `differenceCount` 0 and an immediately exhausted inner cursor.
- A row index is the row's position in its input (zero-based), or `-1` when that side has no row or several.
- An exhausted cursor returns `True` with `hasItem` `False` and sentinel outputs, and stays exhausted until
  `ResetResultCursor` or a new successful run.

## GetResultJson

`GetResultJson("r000004")` returns everything about one result: kind, key, reason, explanation, every member with its
side, row and original key parts, and every kept difference with values as found, interpreted values and delta (a Money difference also carries `leftCurrency` and `rightCurrency`, as found). Use it
for duplicate groups, which the scalar cursor cannot fully describe. A bad or unknown ID is a failure that leaves the
results and cursors alone.

## Exporting everything

`ExportResultsJson(runLabel)` returns the definition, the summary and every result as one deterministic report; see [Export](Export.md).

## When there are no results

Before a completed run, after a failed run, after any accepted setup change, and after `ClearResults`, every reader
returns `False` with a "no results" message. `ClearResults` always succeeds.
