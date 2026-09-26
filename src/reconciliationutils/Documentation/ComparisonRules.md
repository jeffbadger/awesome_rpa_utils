# Comparison rules

Every rule is evaluated for every matched pair; one mismatch never stops the others. Each rule gives one of:

- **Equal**
- **Different** (both sides usable, values unequal)
- **Invalid** (a side cannot be compared under the rule)

The pair's result is **InvalidComparison** if any rule is invalid (this outranks Different), **Different** if any is
different, otherwise **Matched**. The details of every unequal or invalid rule are kept, in rule order.

## Reason codes

| Code | Meaning |
|---|---|
| `MissingField` | The pointer finds nothing on a side. Always invalid, even when both sides miss it. |
| `NullNotAllowed` | A null where the null policy requires a value. |
| `InvalidType` | The value's JSON type does not fit the rule (for example a number given to a text rule, or `"yes"`, `0` or `1` given to a Boolean rule). |
| `InvalidDecimal` | Not a usable decimal (bad text, too many digits or places, out of range). |
| `TextMismatch` | Text rule, both usable, unequal. |
| `DecimalMismatch` | Decimal or Money rule, both usable, difference larger than the tolerance (or one null against a value under `AllowBothNull`). |
| `BooleanMismatch` | Boolean rule, both usable, `true` against `false` (or a null against a value under `AllowBothNull`). |
| `InvalidDate` | Calendar-date or instant rule: the text is not a valid date or instant in the required shape (or is empty). |
| `DateMismatch` | Calendar-date or instant rule, both usable, difference larger than the tolerance (or one null against a value under `AllowBothNull`). |
| `InvalidCurrency` | Money rule: a currency field is null, not a string, or not a three-letter code. |
| `CurrencyMismatch` | Money rule: both currencies are valid and differ; the amounts are not compared. |

When both sides have problems the reported reason is the most basic one: missing, then unusable value, then null.

## Null policy

`RequireValue` (default): a null on either side is invalid. `AllowBothNull`: two nulls are equal, and a null against a
value is a difference. Missing, null, empty text and zero are never treated as the same thing.

## Text

Both sides must be JSON strings. Optional trimming (Unicode white space) and ordinal case-insensitive comparison. No
locale collation, Unicode normalization or whitespace collapsing, and the outcome does not depend on the machine's
culture (`I` and `i` match under Turkish settings; `İ` and `i` do not).

## Decimal

Each side is a JSON number, or a string of plain invariant numeric text (`-12.50`; no exponent, separators, symbols
or spaces in text; JSON numbers may use an exponent). The value is held exactly: more than 28 decimal places, more
than about 29 significant digits or a magnitude above `79228162514264337593543950335` is `InvalidDecimal`, never
rounded. Two values are equal when `|right - left|` is at most the absolute tolerance (inclusive, exact: `0.1` and
`0.3` differ by exactly `0.2`). The delta (right minus left) is reported as exact text, normalized (`1.5`, not `1.50`).

## Boolean

Both sides must be JSON `true` or `false`. Nothing converts implicitly: `"true"`, `"yes"`, `1` and `0` are `InvalidType`. Equal when
both are the same; otherwise `BooleanMismatch`. The null policy applies as usual (a missing field is always invalid).

## Money

A Decimal comparison of the amount, gated on a currency on each side. Each currency is read from its own pointer and must be a
string of exactly three ASCII letters once trimmed; it is normalized to upper case, so `" usd"` and `"USD"` are the same. No
registry lookup is claimed: `XXX` and `ZZZ` are as valid as `USD`.

The rule decides in this order, and the first that applies wins:

1. **A field is missing** (an amount or a currency on either side): `MissingField`.
2. **A currency is unusable** (null, not a string, wrong shape): `InvalidCurrency`. A null currency is never allowed, even with `AllowBothNull`.
3. **An amount is unusable** (wrong type, not a decimal, or null under `RequireValue`): the same reasons as a Decimal rule.
4. **The currencies differ**: `Different` with `CurrencyMismatch`. The amounts are never compared, so there is no delta and no tolerance can hide a currency difference. The explanation does not quote the codes; the detail in `GetResultJson` shows both currencies as found (`leftCurrency`, `rightCurrency`) and each amount with its normalized currency (`"10 USD"`).
5. **The currencies agree**: the null policy applies to the amounts (two nulls are equal under `AllowBothNull`), then the amounts are compared exactly within the absolute tolerance, as for Decimal.

Invalid always outranks different, so a bad amount reports its own reason even when the currencies also differ.

## Calendar dates

`AddCalendarDateComparison[Simple]` compares dates given as **text** in an explicit format per side, so left and right can
differ (`29/02/2024` against `2024-02-29`). A date is read as a plain day; no time, zone, culture or calendar is involved.

- **Formats** are built only from `yyyy`, `MM` and `dd`, each exactly once (a date part is never missing), the separators
  `- / . ,` and space, and quoted literals such as `'day'`. Time tokens (`HH`, `mm`, `ss`, `f`), offset tokens (`z`, `K`), era,
  month and day names, one- or two-digit years, `:` and escape characters are refused when the rule is added. Formats are
  checked when the rule is added, never when a row is read. The Simple form's formats are required too; `yyyy-MM-dd` is the usual one.
- **Strict reading:** four-digit year, two-digit month and day, ASCII digits only, no surrounding white space. `2023-02-29` and
  `2100-02-29` are `InvalidDate`; `2024-02-29` and `2000-02-29` are valid. Years 0001 to 9999.
- **Tolerance** is a whole number of **calendar days** (0 or more), inclusive. The delta is right minus left in days (`-3`, `2`).
- Empty text is `InvalidDate`; a non-string value is `InvalidType`.

## Instants

`AddInstantComparison[Simple]` compares points in time given as ISO text with an **explicit offset or `Z`**. Only these shapes
are accepted, in ASCII with an upper-case `T` and `Z`:

- `yyyy-MM-ddTHH:mm:ss` followed by `Z`, or by an offset `+hh:mm` / `-hh:mm`
- the same with a fraction of one to seven digits after the seconds (`2024-03-01T12:00:00.5Z`)

Any value with no offset (`2024-03-01T12:00:00`) is `InvalidDate`: it is never read in the machine's time zone, and no daylight-saving
rule is applied. Offsets up to +/-14:00; no leap seconds (`23:59:60` is invalid); no `+0530`. Values are converted to UTC and compared
exactly, so `12:00:00Z` and `08:00:00-04:00` are the same instant and `12:00:00Z` and `12:00:00-04:00` (same wall clock) are four hours apart.

**Tolerance** is a whole number of **seconds** (0 or more), inclusive, compared exactly to the tick (a difference of 1.0000001 s is beyond a
tolerance of 1). The delta is right minus left in seconds, exact and without trailing zeros (`14400`, `-1.5`).

The two are separate methods so a tolerance never changes meaning: days for dates, seconds for instants. For both, the detail shows the
values as found and normalized (`2024-02-29`, `2024-03-01T16:00:00Z`).

## Original values

Each difference keeps the original values as JSON fragments next to the interpreted ones: a missing field is a null
string, a JSON null is the text `null`, an empty string is `""` and a zero is `0`, so they stay distinguishable. An
object or array is described (`"(an object)"`), not copied.
