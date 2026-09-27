# Labels and positions

A label field is found by the words in front of its value. You give one or more **labels** (alternatives, `Invoice No|Invoice Number`) and a
**position**, and the component finds every place a label appears and reads the value from there.

## How a label is matched

- **Case does not matter**, and it never depends on the machine's culture (`INVOICE`, `invoice` and `Invoice` all match; Turkish settings change nothing).
- **Spacing is flexible**: a space in a label matches one or more spaces, tabs or non-breaking spaces in the text.
- **Whole words only**: a label that starts or ends with a letter or digit must start or end at a word boundary, so `Total` does not match
  `Subtotal` or `Totals`. A label ending in punctuation needs no boundary after it (`Invoice #` matches `Invoice #123`).
- **Separators are skipped**: spaces, `:` `#` `=`, dot leaders (`Total ........ 12.00`) and dashes between the label and the value. A `-` or `.`
  directly before a digit is kept, so the sign of `-12.00` and the point of `.50` stay part of the value.
- **Typography does not matter**: typographic quotes and dashes, ligatures (`ﬁ`), full-width letters and digits and invisible characters are
  normalized before matching. The value you get back in `raw` is always the original text.
- **The longest label wins** where two labels overlap: `Due Date` is not also `Date`, and `Invoice Number` beats `Invoice`. Two fields may use the
  same label (for example `Status` read as `First` by one field and `Last` by another); both see it.

## OCR slips

OCR output often misreads a character. A label still matches when it contains one of these look-alikes in place of the right character:

| In the label | May be read as |
|---|---|
| `I`, `L`, `1` | any of `I`, `L`, `1`, or a `|` in the text |
| `O`, `0` | either |
| `S`, `5` | either |
| `rn` | `m` (and `m` as `rn`) |

Only in labels of **four or more** letters and digits, and at most **one slip per six** of them, rounded up: `Total` (5) tolerates one
(`Tota1`), `Invoice No` (9) tolerates two (`lnvoice N0`), `Due` (3) must be exact. There is no other fuzzy matching. `GetResultJson` reports
`labelSlipped: true` when a label needed a slip, so an automation can send such values for review.

## Where the value is

`\n` in these examples is a line break.

| Labels | Position | Text | value |
|---|---|---|---|
| `Invoice No` | `SameLine` | `Invoice No: 88731` | `88731` |
| `Invoice No` | `SameLine` | `Invoice No.: 88731` | `88731` |
| `Total` | `SameLine` | `Total ........ 12.00` | `12.00` |
| `Invoice No` | `SameLine` | `Invoice No: 88731   Date: 2026-09-26` | `88731` |
| `Ship To` | `NextLine` | `Ship To:\n\n12 Main St, Springfield` | `12 Main St, Springfield` |
| `Policy No` | `Below` | `Policy No    Holder\nPX-000123    Jane Q Public` | `PX-000123` |
| `Holder` | `Below` | `Policy No    Holder\nPX-000123    Jane Q Public` | `Jane Q Public` |

- **`SameLine`** (the default): the rest of the line after the label, up to the next label of **any** field on that line. That is why the fourth
  example stops before `Date` (when `Date` is also a field of the template).
- **`NextLine`**: the next line that is not blank, up to the first label on it.
- **`Below`**: the cell under the label's columns on the next line that is not blank. Cells are separated by **two or more spaces**, so a single
  space inside a value (`Jane Q Public`) keeps it together. Blank lines are skipped because OCR often double-spaces a form; the value must still sit
  under the label. Columns count characters, and a tab counts as one, so `Below` suits screens and aligned reports rather than tab-separated text.

The type then takes the part of that text it needs (see [Types](Types.md)): `Code` takes the first token, `Amount` the amount with its currency,
`Date` the first date, and so on.
