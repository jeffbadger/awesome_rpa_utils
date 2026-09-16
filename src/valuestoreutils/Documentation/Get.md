# Get

Use the getter matching the type you need; pass `defaultValue` for what a
missing or unconvertible value should look like:

```csharp
int quantity = store.GetInt32("Quantity", defaultValue: 0);
bool active = store.GetBoolean("IsActive");
DateTime hostDate = store.GetDateTime("HOST_DATE", format: "yyyyMMdd");
```

A missing key, a null value, and an unconvertible value are all
indistinguishable "normal negative" outcomes here — they all fall back to
`defaultValue` rather than reporting an operational failure. `GetBoolean` is
intentionally permissive: it accepts a native bool, `"true"`/`"false"`,
`1`/`0`, and `yes`/`no`/`y`/`n` (case-insensitive), which covers most of
what shows up in scraped checkbox/flag fields. Use [TryGet](TryGet.md)
instead when the caller needs to tell "missing/unconvertible" apart from "a
genuine value."
