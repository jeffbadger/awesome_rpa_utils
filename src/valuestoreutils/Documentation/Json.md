# Json

Serialize the whole store, or load one from a JSON object or an existing
.NET object's properties:

```csharp
store.TryGetJson(out string json, out string message);

var reloaded = new ValueStoreUtils();
reloaded.TryLoadFromJson(json, clearExisting: true, out int loadedCount, out message);

reloaded.TryLoadFromObject(myOrderObject, clearExisting: true, out loadedCount, out message);
```

`TryLoadFromJson` requires a JSON object at the root — a bare array or
scalar fails with a message. Both load methods are atomic: on a malformed
document, a non-object root, or an unexpected exception, nothing already in
the store is changed. `TryLoadFromObject` is shallow — a nested object
property is stored as-is, not recursively flattened into its own keys;
combine with [`SetPath`/path getters](Path.md) afterward if you need to
reach into it. `TryGetJson` can fail if a stored value is a type
`System.Text.Json` cannot serialize (for example a raw handle or stream).
