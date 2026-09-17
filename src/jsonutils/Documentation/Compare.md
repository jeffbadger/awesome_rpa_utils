# Compare

## Diff two JSON documents

```csharp
json.TryDiffJson("{\"status\":\"open\",\"priority\":1}", "{\"status\":\"closed\",\"priority\":1}", ",", out bool areEqual, out string differingPaths, out string message);
// areEqual == false, differingPaths == "status"
```

**`True`/`False` here means "the comparison completed," not "the documents
are equal."** Unlike every other method in this component, check the
separate `areEqual` output for that — `TryDiffJson` only returns `False`
when one of the inputs is malformed JSON:

```csharp
json.TryDiffJson("{not json", "{\"a\":1}", ",", out areEqual, out differingPaths, out message);
// False - message describes the parse failure; areEqual/differingPaths are not meaningful
```

Path ordering is a deterministic pre-order walk of the first document's
structure — not alphabetical: keys/indices are visited in the first
document's own order, with any keys/indices that exist only in the second
document appended after. A whole-document-level difference (e.g. mismatched
root types) is reported as `"$"`.
