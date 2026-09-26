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

## Measured cost

One run on a Linux development machine (Intel i9-14900KF, 32 logical cores, 64 GB, .NET 10.0.12, Release build, one
workload per process so the peak is meaningful). Rows have four to five short fields, about 60 to 100 characters each.
Times are the `ReconcileJson` call; reading every exception and difference (each read checked, and the count checked
against `exceptionCount`) took 1 to 75 ms more. "Heap at end of run" is the managed heap right after the call, so it
still includes garbage from parsing that has not been collected. "Held by results" is what remains after a full
collection, on top of the two input strings the caller still holds (the component keeps neither input). "Peak" is the
process peak working set including the test host.

| Workload (both sides) | Input characters (each side) | Exceptions | Time | Heap at end of run | Held by results | Peak process |
|---|---|---|---|---|---|---|
| 50,000 rows, all matching (text + decimal) | 4.9 M | 0 | 0.53 s | 127 MB | 42 MB | 220 MB |
| 50,000 rows, every row differs in two fields | 3.4 M | 50,000 | 0.61 s | 172 MB | 89 MB | 272 MB |
| 50,000 rows over 100 keys (100 duplicate groups) | 2.9 M | 100 | 0.16 s | 81 MB | 33 MB | 171 MB |
| 50,000 left keys and 50,000 different right keys | 2.8 M | 100,000 | 0.30 s | 113 MB | 63 MB | 221 MB |
| At the maximums: 250,000 rows, 500,000 differences | 15.5 M | 250,000 | 2.3 s | 772 MB | 400 MB | 907 MB |

The defaults comfortably fit a typical robot machine, and even the maximums stay under 1 GB for rows of this size.
Memory grows with input size, so wider rows or a 32-million-character input cost proportionally more. The
measurements are repeatable: set `RECON_MEASURE=1` and run the `MeasurementTests` one at a time (see the class remarks).
Timings on a Windows robot will differ; treat these as an order of magnitude, not a promise.
