# Merge

## Layer a partial update onto a base document

```csharp
json.TryMergeJson("{\"status\":\"open\",\"priority\":1}", "{\"status\":\"closed\"}", out string mergedJson, out string message);
// mergedJson == "{\"status\":\"closed\",\"priority\":1}"
```

The second document's values win on scalar conflicts; a key present only
in the base document is kept as-is.

## Know what happens to arrays and nested objects on conflict

```csharp
json.TryMergeJson("{\"items\":[1,2]}", "{\"items\":[3]}", out string mergedItems, out message);
// mergedItems == "{\"items\":[1,2,3]}" - concatenated, NOT replaced or merged by index

json.TryMergeJson("{\"address\":{\"city\":\"A\",\"zip\":\"1\"}}", "{\"address\":{\"city\":\"B\"}}", out string mergedNested, out message);
// mergedNested == "{\"address\":{\"city\":\"B\",\"zip\":\"1\"}}" - merged recursively, not replaced wholesale

json.TryMergeJson("{\"note\":\"original\"}", "{\"note\":null}", out string mergedNull, out message);
// mergedNull == "{\"note\":\"original\"}" - an explicit null in the second document does NOT clear the first
```

Both inputs must be JSON objects at the root — a bare array or scalar at
either root fails with a descriptive message, since merging only makes
sense between two objects' properties.
