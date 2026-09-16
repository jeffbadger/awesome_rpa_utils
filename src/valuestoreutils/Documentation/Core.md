# Core

`SetValue`/`TryGetValue` work with the raw, unconverted value; everything
else in the component builds on top of them:

```csharp
store.SetValue("Order", myOrderObject, out string message);
store.TryGetValue("Order", out bool found, out object order, out message);

store.ContainsKey("Order", out bool exists, out message);
store.Remove("Order", out bool removed, out message);
store.GetCount(out int count, out message);

store.GetKeys(out string[] keys, out message);
foreach (string key in keys) { /* ... */ }

store.GetKeysJson(out string keysJson, out message);
```

A missing key is a normal result, not a failure: `TryGetValue`,
`ContainsKey`, and `Remove` all return `True` with `message == null`,
reporting the actual state through `found`/`exists`/`removed`. Use `GetKeys`
when the caller can iterate a plain array directly; use `GetKeysJson` when
the destination expects JSON text (e.g. logging, or handing keys to another
JSON-based component).
