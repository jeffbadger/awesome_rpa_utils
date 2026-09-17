# Query

## Extract every value matching a wildcard or filter path

```csharp
string order = "{\"items\":[{\"sku\":\"A\",\"price\":5},{\"sku\":\"B\",\"price\":15},{\"sku\":\"C\",\"price\":25}]}";

json.TryGetValuesFromJson(order, "items[*].sku", ",", out string allSkus, out string message);
// allSkus == "A,B,C"

json.TryGetValuesFromJson(order, "items[?(@.price > 10)].sku", ",", out string expensiveSkus, out message);
// expensiveSkus == "B,C"
```

Unlike `TryGetValueFromJson` (see [Core](Core.md)), which resolves exactly
one value, this matches zero or more and joins them with `delimiter`. It
fails (returns `False`) only when the path matches nothing at all — a
matched JSON `null` contributes an empty string to the joined result rather
than being skipped, so it's indistinguishable from a genuinely empty string
value at that position.

## Check what kind of value is at a path before deciding how to handle it

```csharp
json.TryGetValueType(order, "items[0].price", out JsonValueKind kind, out message);
// kind == JsonValueKind.Number

json.TryGetValueType(order, "items[0].missingField", out kind, out message);
// False - the path didn't resolve to anything; kind == JsonValueKind.NotFound
```

Useful when a field's shape can vary between documents (e.g. a value that's
sometimes a string, sometimes `null`, sometimes absent entirely) and the
automation needs to branch on which case it's looking at before calling one
of the [Get](Get.md) methods or `TryGetValueFromJson`.

## Find where a field lives when you don't already know its path

```csharp
string document = "{\"sku\":\"root-level\",\"order\":{\"sku\":\"nested\",\"items\":[{\"sku\":\"a\"},{\"sku\":\"b\"}]}}";

json.TryFindPathsByName(document, "sku", ",", out string paths, out message);
// paths == "sku,order.sku,order.items[0].sku,order.items[1].sku"
```

Every returned path is a real path you can pass straight into
`TryGetValueFromJson`, `TrySetValueInJson`, or any other path-based method
on this component — useful for a document whose overall shape is known but
whose exact structure (how deep a field is nested, whether it's inside an
array) varies between sources. The match is on the exact property name only
(no wildcard/partial matching), but works correctly even for a name
containing characters like `.` or a space that you'd otherwise need to
escape by hand in a JSONPath expression.

