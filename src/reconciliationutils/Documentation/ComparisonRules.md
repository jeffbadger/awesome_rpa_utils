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

## Original values

Each difference keeps the original values as JSON fragments next to the interpreted ones: a missing field is a null
string, a JSON null is the text `null`, an empty string is `""` and a zero is `0`, so they stay distinguishable. An
object or array is described (`"(an object)"`), not copied.
