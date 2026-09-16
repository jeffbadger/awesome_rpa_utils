# Path

Once a value has been set via [`SetJson`](Set.md) or `SetPath` itself, walk
or write into it with a dotted path instead of resolving each level by hand:

```csharp
store.SetPath("Customer.Address.City", "Columbus", out string message);
store.SetPath("Customer.Address.Zip", "43215", out message);

string city = store.GetPathString("Customer.Address.City"); // "Columbus"
int age = store.GetPathInt32("Customer.Age", defaultValue: 0);
bool vip = store.GetPathBoolean("Customer.IsVip");

store.TryGetPathValue("Customer.Address", out bool found, out object value, out message);
```

`SetPath` creates intermediate nested structures automatically — there is no
need to pre-create `Customer` or `Customer.Address` before setting
`Customer.Address.City`. `GetPath*` methods return `defaultValue` (and
`TryGetPathValue` returns `found == false` with `message == null`) if any
segment along the path is missing, rather than failing. Nested structures
created this way are always case-insensitive, regardless of the outer
store's [`CaseSensitiveKeys`](Configuration.md) setting.
