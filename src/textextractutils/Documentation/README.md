# TextExtractUtils documentation

`TextExtractUtils` pulls labelled, typed business fields out of text: email bodies, OCR output, terminal screens, text copied from PDFs.
You describe each field the way a person sees it (the words in front of it, where its value sits, what kind of value it is) and read back a
validated, normalized value, or a reason code saying why there is none. No regular expressions are needed; they remain available as an escape hatch.

| Page | What it covers |
|---|---|
| [QuickStart](QuickStart.md) | The smallest complete flow: define fields, extract, read each value, walk every field. |
| [Labels](Labels.md) | How labels are matched (case, spacing, separators, word boundaries, OCR slips) and where the value is taken from (`SameLine`, `NextLine`, `Below`). |
| [Types](Types.md) | What each type accepts and returns: text, codes, numbers, amounts, percentages, dates, emails, IBANs. |
| [Results](Results.md) | Found values, `raw`, reason codes, line numbers, occurrence policies, the result JSON and what discards results. |
| [Patterns](Patterns.md) | Pattern fields (a regular expression with a `value` group) for text with no stable label, and their limits. |
| [Limits](Limits.md) | Every limit, and what happens when one is reached. |
| [EmailIntake](EmailIntake.md) | A worked example: read an invoice email and queue it for processing with `LocalQueueUtils`. |

The [component README](../README.md) holds the method reference. Conventions shared by every method:

- Every method returns `bool` and ends with `out string message`. `False` plus a message means the call could not be done (bad input, a limit,
  no results, disposed). It never throws.
- An extraction that finds nothing is still a **success** (`True`, `message` null): read the outcome from `foundCount`, `missingRequiredCount`
  and each field's `reason`, not from `message`.
- On failure every output holds its sentinel: null strings, 0 counts and line numbers, `False` flags.
- Messages and explanations never contain the text being read. Values come only through the value outputs and the result JSON.
- Nothing depends on the machine's culture or time zone, and nothing is written to disk or sent anywhere.
- Use one instance per automation flow. Calls are serialized by an instance lock.
