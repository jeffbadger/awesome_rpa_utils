# Limits

`ConfigureLimits(maximumRowsPerSide, maximumInputCharactersPerSide, maximumResults, maximumDifferenceDetails)` (or the
`limits` object of a definition) bounds what one run may consume. Every value must be at least 1 and at most its maximum.

| Limit | Default | Maximum | Applies to |
|---|---|---|---|
| `maximumRowsPerSide` | 50,000 | 250,000 | Rows in each input array. |
| `maximumInputCharactersPerSide` | 8,000,000 | 32,000,000 | Characters in each input JSON text. |
| `maximumResults` | 100,000 | 500,000 | Results a run may build (matched pairs count too). |
| `maximumDifferenceDetails` | 100,000 | 500,000 | Field difference details kept across the run. |

Also fixed: JSON depth 64, 256 characters per number, and at most 100 findings in a validation report.

## What happens when a limit is hit

The run **fails whole**: `ReconcileJson` returns `False` with a message naming the limit, and no results remain (they
are cleared when a run starts). Nothing is truncated, because a silently partial reconciliation is worse than none.
Raise the limit, split the input, or reduce it before comparing.

Limits are checked while the run is built, so an oversized run stops early instead of exhausting memory. An
empty or whitespace-only input is not an empty dataset: use `[]`.

Everything is held in memory, so choose limits to fit the robot machine. The result count is at most one per key plus
one per invalid row, so `maximumResults` should be at least the total row count for a run that expects mostly
matches; the difference detail limit only matters when many rows differ in many fields.
