# Types

Every field (and every table column) has a **type**, chosen from the `FieldType` drop-down. The type decides three things:

1. **What is captured.** The label says where the value is; the type picks the value out of the text there. It takes the first part that looks
   like a value of that type, so `Invoice No: INV-7 (reissued)` gives the code `INV-7`, and `Total: EUR 1.234,50 incl. VAT` gives the amount
   `1234.50`.
2. **Whether it is valid.** Text is there, but it is not a value of the type (`n/a` for an amount, `31/02/2026` for a date): the field is
   `InvalidValue`, and `raw` shows the text.
3. **The normalized form returned in `value`.** Numbers use a dot for decimals and no grouping, dates are `yyyy-MM-dd`, and IBANs have no
   spaces. Downstream steps get the same form whatever the document looked like, and nothing depends on the machine's culture.

`raw` is always the captured part exactly as it appeared in the text.

## Choosing a type

| Type | Use it for | `value` looks like | Also set |
|---|---|---|---|
| `Text` | Names, addresses, descriptions, anything free-form | the text, trimmed | |
| `Code` | Invoice, order, policy, customer and reference numbers | `INV-2026-0042` | |
| `Integer` | Counts and quantities | `1234` | decimal style |
| `Decimal` | Measurements, rates, unit prices without a currency | `1234.5600` | decimal style |
| `Amount` | Money: totals, balances, amounts due | `1234.50` (currency in the result JSON) | decimal style |
| `Percentage` | VAT rates, discounts, interest rates written with `%` | `12.5` | decimal style |
| `Date` | Invoice dates, due dates, dates of birth | `2026-09-26` | date formats |
| `Email` | Contact and reply-to addresses | `Jane.Doe@example.com` | |
| `Iban` | Bank account numbers | `DE89370400440532013000` | |

When in doubt between `Text` and `Code`: use `Code` for an identifier that is one word (it stops at the first space, so trailing words are left
out), and `Text` for anything that may contain spaces. Between `Decimal` and `Amount`: use `Amount` for money, since it also accepts a currency
and accounting negatives.

**Decimal style** (`DotDecimal` or `CommaDecimal`) matters only for `Integer`, `Decimal`, `Amount` and `Percentage`; other types ignore it.
**Date formats** apply only to `Date`, and must be left empty for every other type.

## The types

### `Text`

Any text. `value` is the whole text where the value sits, trimmed: for `SameLine`, the rest of the line up to the next label of another field.
A `Text` field is never `InvalidValue`; it is `MissingValue` only when there is nothing there. Use it for supplier names, subjects,
descriptions and payment terms (`Net 30 days`).

### `Code`

One identifier: letters, digits and `-` `/` `.` `_`, with no spaces. Capture starts at the first letter or digit (a leading quote or bracket is
skipped) and stops at the first other character, so `INV-2026-0042 (reissued)` gives `INV-2026-0042`. A full stop that ends a sentence is
dropped (`INV-2026-0042.` gives `INV-2026-0042`). Letter case is kept. The value is `InvalidValue` when the text does not start with a code.

### `Integer`

A whole number, optionally with a sign, read in the field's decimal style: `1,234 units` gives `1234` with `DotDecimal`, and `1.234.567`
gives `1234567` with `CommaDecimal`. A number with a fraction (`12.50`) is `InvalidValue`, never rounded or cut.

### `Decimal`

A number that may have decimals, read in the field's decimal style. `value` keeps the decimals exactly as written (`1,234.5600` gives
`1234.5600`), with a dot before the decimals and no grouping. Nothing is rounded.

### `Amount`

A money amount: a number in the field's decimal style, optionally with a currency and a negative sign.

- The currency may be a symbol (`$`, `€`, `£`) or a three-letter code in capitals (`EUR`, `USD`), before or after the number, at most a space
  away. It is not part of `value`; the result JSON reports it as `currency` (null when there is none). An amount without a currency is fine.
- An amount is negative by a leading minus, a trailing minus (`12.00-`) or accounting parentheses (`(12.00)`); `value` then starts with `-`.
- `value` keeps the decimals as written: `$1,234.50` gives `1234.50`, and `1.234,50 EUR` gives `1234.50` with `CommaDecimal`.

### `Percentage`

A number followed by `%` (a space before it is allowed), read in the field's decimal style. `value` is the number without the `%`: `12.5 %`
gives `12.5`. A number without `%` (`12.5 percent`) is `InvalidValue`, so a percentage is never confused with a plain number next to it.

### `Date`

A calendar date, read with the field's **date formats** and returned as `yyyy-MM-dd`.

- Formats are built from `yyyy`, `MM`, `dd`, separators and quoted literals. Give one or more, separated by `|` in the builder methods
  (`dd/MM/yyyy|yyyy-MM-dd`) or as an array in JSON. The earliest date in the text wins, whichever of the formats it is written in. With no
  formats the field reads `yyyy-MM-dd`.
- The date must exist: `2023-02-29` is `InvalidValue`. So is a date written in a format the field was not given.
- Formats are checked when the field is added, so a typo in a format is refused there, not discovered during extraction. The machine's
  regional settings never change how a date is read.

### `Email`

The first email address in the text. The domain is returned in lower case and the part before `@` as written:
`write to Jane.Doe@Example.COM` gives `Jane.Doe@example.com`. An address without a domain ending (`jane@localhost`) is `InvalidValue`.

### `Iban`

An international bank account number: two letters, two check digits and the account, 15 to 34 characters, with spaces allowed between groups.
It is checked with the IBAN checksum (mod 97), so a mistyped or misread digit is caught as `InvalidValue` instead of being passed on. `value`
is in upper case without spaces: `DE89 3704 0044 0532 0130 00` gives `DE89370400440532013000`. An IBAN-shaped word that fails the check does
not stop a valid IBAN later in the text.

## Rules shared by the number types

These apply to `Integer`, `Decimal`, `Amount` and `Percentage`.

- Numbers are read only in the field's **decimal style**, one per field and never guessed:
  - `DotDecimal` (the default): `1,234.56`, grouping with `,`, a space or `'`.
  - `CommaDecimal`: `1.234,56`, grouping with `.`, a space or `'`.
- Thousands must be groups of three. A number that only makes sense in the other style (`1.234,56` under `DotDecimal`) is `InvalidValue`,
  never read as part of itself. A space followed by something other than three digits separates two numbers (`5 12/03` gives 5).
- The value must fit .NET's `decimal` exactly (up to about 29 significant digits and 28 decimals; leading and trailing zeros do not count).
- **OCR repair** in numbers and dates: `O` is read as 0 and `I`, `l` or `|` as 1, but only right next to a real digit (`2O26` is 2026;
  `ORDER 12` and `O 12` are not changed). `raw` keeps what OCR produced.

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
