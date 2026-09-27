# Types

The type of a field decides what is **captured** (the first part of the text that looks like a value of that type), whether it is **valid**,
and the **normalized** form returned in `value`. `raw` is always the captured part exactly as it appeared. Nothing depends on the machine's culture.

## Examples

| Type | Style or formats | Text | value |
|---|---|---|---|
| `Text` | | `Net 30 days` | `Net 30 days` |
| `Code` | | `INV-2026-0042.` | `INV-2026-0042` |
| `Code` | | `PX/00_12 (reissued)` | `PX/00_12` |
| `Integer` | `DotDecimal` | `1,234 units` | `1234` |
| `Integer` | `CommaDecimal` | `1.234.567` | `1234567` |
| `Decimal` | `DotDecimal` | `1,234.5600` | `1234.5600` |
| `Decimal` | `CommaDecimal` | `0,5` | `0.5` |
| `Amount` | `DotDecimal` | `$1,234.50` | `1234.50` |
| `Amount` | `DotDecimal` | `(12.00)` | `-12.00` |
| `Amount` | `DotDecimal` | `12.00- USD` | `-12.00` |
| `Amount` | `CommaDecimal` | `1.234,50 EUR` | `1234.50` |
| `Percentage` | `DotDecimal` | `12.5 %` | `12.5` |
| `Date` | `dd/MM/yyyy` | `due 26/09/2026` | `2026-09-26` |
| `Date` | `dd/MM/yyyy\|yyyy-MM-dd` | `2026-09-26` | `2026-09-26` |
| `Email` | | `write to Jane.Doe@Example.COM` | `Jane.Doe@example.com` |
| `Iban` | | `DE89 3704 0044 0532 0130 00` | `DE89370400440532013000` |

## Refused (InvalidValue)

| Type | Style or formats | Text | Why |
|---|---|---|---|
| `Integer` | `DotDecimal` | `12.50` | an Integer has no fraction |
| `Decimal` | `DotDecimal` | `1.234,56` | written in the other style: refused, never read as 1.234 |
| `Integer` | `DotDecimal` | `1,23` | thousands must be groups of three |
| `Percentage` | `DotDecimal` | `12.5 percent` | a percentage needs `%` |
| `Date` | `yyyy-MM-dd` | `2023-02-29` | not a real date |
| `Date` | `yyyy-MM-dd` | `26/09/2026` | not in the field's formats |
| `Email` | | `jane@localhost` | no domain ending |
| `Iban` | | `DE89 3704 0044 0532 0130 01` | the check digits do not match |

## Rules

- **Text**: the whole text, trimmed. **Code**: the first token of letters, digits and `- / . _`; a full stop that ends a sentence is dropped.
- **Numbers** (`Integer`, `Decimal`, `Amount`, `Percentage`) are read only in the field's **decimal style**, one per field and never guessed:
  - `DotDecimal`: `1,234.56` (grouping `,`, a space or `'`); `CommaDecimal`: `1.234,56` (grouping `.`, a space or `'`).
  - Thousands must be groups of three. A number that only makes sense in the other style is `InvalidValue`. A space followed by something
    other than three digits separates two numbers (`5 12/03` gives 5).
  - The value keeps its decimals as written (`1234.50`, not `1234.5`) and is never rounded; it must fit .NET's `decimal` exactly (up to about
    29 significant digits and 28 decimals; leading and trailing zeros do not count).
- **Amounts** may carry a currency symbol or a three-letter code on either side (reported in the result JSON as `currency`) and are negative by a
  sign, a trailing minus or accounting parentheses.
- **OCR repair** inside numbers and dates: `O` is read as 0 and `I`, `l` or `|` as 1, but only right next to a real digit (`2O26` is 2026;
  `ORDER 12` and `O 12` are not changed). `raw` keeps what OCR produced.
- **Dates** are read with the field's formats (built from `yyyy`, `MM`, `dd`, separators and quoted literals; default `yyyy-MM-dd`), tried in
  order at word boundaries, and returned as `yyyy-MM-dd`.
- **Email**: the first address; the domain is lower-cased. **IBAN**: two letters, two check digits and the account, spaces allowed, checked with
  mod 97, returned in upper case without spaces; an IBAN-shaped word that fails the check does not stop a valid one later in the text.
