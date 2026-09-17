# Set

Use the scalar setter matching the source value, or `SetJson` for a
structured value:

```csharp
store.SetString("CustomerName", "Acme Corp", out string message);
store.SetInt32("OrderId", 4471, out message);
store.SetBoolean("IsActive", true, out message);
store.SetDateTime("DueDate", DateTime.UtcNow, out message);

store.SetJson("Address", "{\"City\":\"Columbus\",\"Zip\":\"43215\"}", out message);
```

Every setter creates the key if it is missing or overwrites it if present —
there is no separate "declare" step like `DataContractUtils`. `SetJson` accepts
any JSON value at the root (object, array, string, number, boolean, or
null); an object or array becomes a nested structure reachable through
[Path](Path.md) access. `SetNull` clears a value without removing the key,
which is different from `Remove`.
