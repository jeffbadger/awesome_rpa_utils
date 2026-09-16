# Helpers

```csharp
store.IsEmpty("MiddleName", out bool isEmpty, out string message); // missing, null, or blank/whitespace

store.AddIfMissing("Priority", "Normal", out bool added, out message); // only sets if not already present

store.RemoveAndGetValue("TempFlag", out bool found, out object value, out message); // consume-and-remove

store.Merge("{\"Status\":\"Closed\",\"Priority\":1}", overwrite: false, out int mergedCount, out message);

store.FindKeysJson("Cust_*", out string keysJson, out message);
```

`Merge` only merges the source JSON object's top-level properties — a
conflicting nested object is replaced wholesale under `overwrite: true`, not
merged key-by-key. Use `JsonUtils.TryMergeJson` first if a recursive merge is
needed, then feed the result to [`TryLoadFromJson`](Json.md). `FindKeysJson`
returns an empty JSON array (`"[]"`) rather than failing when the pattern is
null, empty, or matches nothing.
