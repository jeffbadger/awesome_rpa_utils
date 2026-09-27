# Pattern fields

Some values have no stable label in front of them: a reference buried in a sentence, a code with a fixed shape. For those, a **pattern field**
finds the value with a .NET regular expression containing a named group `value`:

```csharp
extract.AddPatternField("Ref", @"Ref\s*[:#]?\s*(?<value>[A-Z]{3}-\d+)", FieldType.Code, DecimalStyle.DotDecimal, "", out string message);
```

The captured `value` group is read as the field's type, exactly like a label field's value, so `Amount`, `Date` and the rest work the same way.

- The pattern must compile and must have a group named `value`; otherwise the field is refused when it is added.
- Patterns run on the **original** text, with no hidden options. Use inline options: `(?i)` for case-insensitive, `(?m)` for `^` and `$` at each
  line. Windows text ends its lines in `\r\n`, so with `(?m)` write `\r?$`.
- A match whose `value` group is empty is not an occurrence, so `(?<value>\d*)` finds numbers rather than matching everywhere.
- `AddPatternField` makes a required, `RequireUnique` field. To make a pattern field `First`, `Last` or optional, set `occurrence` or `required` in a
  JSON template (`LoadTemplateJson`).

## Limits that protect the robot

- Each match attempt stops after **100 ms**, and all of one field's matches after **1 s** in total: `PatternTimeout`.
- At most **10,000** occurrences per field: `TooManyMatches`. A `First` field stops at its first match.
- A slow or flooding pattern affects only its own field; every other field is still extracted.

Patterns such as `(a+)+` or `(\w+\s?)*` can take exponential time on some text. If a field reports `PatternTimeout`, simplify the pattern, or
describe the field with labels instead.
