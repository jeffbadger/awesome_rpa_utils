# Context and guards

Guards cannot run your code - Robot Studio cannot pass code into a component -
so a guard is a **declarative test against the machine's context**: a bag of
text keys and values you fill in from the automation.

```csharp
machine.SetContext("amount", "2500", out string message);
machine.GetContext("amount", out bool exists, out string value, out message);
machine.RemoveContext("amount", out bool removed, out message);
machine.ClearContext(out message);
machine.GetContextJson(out string json, out message);      // {"amount":"2500"}
```

Keys are case-insensitive. A missing key on `GetContext` / `RemoveContext` is a
normal outcome (`exists` / `removed` is `False`), not an error.

## Guard operators

```json
{ "from": "Validated", "trigger": "post", "to": "Posted",
  "guards": [ { "key": "amount", "op": "lessThan", "value": "10000" },
              { "key": "region", "op": "in",       "value": "EU, UK" } ] }
```

All guards on a transition must pass (AND). For OR, write two transitions -
they are tried in order and the first that passes wins.

| Operator | Passes when | Value |
|---|---|---|
| `equals` | the key exists and its text equals the value (case-insensitive) | required |
| `notEquals` | the key exists and its text differs | required |
| `in` | the key exists and equals one item of a comma-separated list | required, e.g. `"EU, UK"` |
| `notIn` | the key exists and equals none of them | required |
| `greaterThan` | the key exists and both sides are numbers and it is larger | required, numeric |
| `lessThan` | the key exists and both sides are numbers and it is smaller | required, numeric |
| `exists` | the key is present (even with an empty value) | none |
| `notExists` | the key is absent | none |

A value on `exists` / `notExists`, or an empty item in an `in` / `notIn` list (`"EU,,UK"`, a trailing comma), is rejected when the definition is loaded rather than silently ignored.

Numbers are parsed with the invariant culture (`10.5`, `1e3`); a value that
is not a number fails a numeric guard rather than throwing. A guard `value`
written as a JSON number or boolean is accepted and treated as its text.

**A missing key fails every comparison** - only `notExists` passes - so a value
that was never set can never satisfy a guard by accident. That includes
`notEquals` and `notIn`: "the key is not X" is false when there is no key.

`equals` is a **text** comparison: `"007"` does not equal `"7"`, `"10"` does not
equal `"10.0"`. Use `greaterThan` / `lessThan` when you mean numbers.

## Rules of thumb

- Keep the context to **facts a guard needs** (an amount, a flag, a counter),
  not payloads. Everything is text and is written to disk when persistence is on.
- **Do not store secrets.** Persisted context is plain text.
- Limits: 1,000 keys, values up to 4,096 characters.
- Set the context **before** the `Fire` whose guards read it.
