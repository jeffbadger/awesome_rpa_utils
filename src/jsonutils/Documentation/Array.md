# Array

## Remove a value

```csharp
json.TryRemoveValueFromJson("{\"order\":{\"status\":\"open\",\"draft\":true}}", "order.draft", out string updatedJson, out string message);
// updatedJson == "{\"order\":{\"status\":\"open\"}}"
```

Works for an object property or an array element identified by the path;
the path must resolve to an existing value.

## Count and grow an array

```csharp
string cart = "{\"items\":[\"sku-1\",\"sku-2\"]}";

json.TryGetArrayLength(cart, "items", out int count, out string message);
// count == 2

json.TryAppendToJsonArray(cart, "items", "\"sku-3\"", out string updatedCart, out message);
// updatedCart == "{\"items\":[\"sku-1\",\"sku-2\",\"sku-3\"]}"
```

`TryAppendToJsonArray`'s new element is a JSON *fragment*, not a plain
string — a string element needs its own quotes in the argument
(`"\"sku-3\""`), and an object element is a full JSON fragment
(`"{\"sku\":\"C\",\"price\":25}"`). Both methods fail if the path doesn't
resolve to an array at all.

## Filter an array by a field comparison

```csharp
string catalog = "{\"items\":[{\"sku\":\"A\",\"price\":5},{\"sku\":\"B\",\"price\":15},{\"sku\":\"C\",\"price\":25}]}";

json.TryFilterJsonArrayByField(catalog, "items", "price", JsonComparisonOperator.GreaterThan, "10", out string filteredJson, out string message);
// filteredJson == "[{\"sku\":\"B\",\"price\":15},{\"sku\":\"C\",\"price\":25}]"
```

Comparison values are always passed as plain text (`"10"`, not `10`); the
comparison is numeric when both sides look like numbers, and falls back to
case-insensitive ordinal string comparison otherwise (so a boolean field's
`"True"`/`"False"` rendering matches a caller's lowercase
`"true"`/`"false"`). An element missing the compared field entirely is
excluded under every operator, **including** `NotEquals` — it's never
treated as a match just because the field isn't there to compare.

## Sort an array by a field's value

```csharp
json.TrySortJsonArrayByField(catalog, "items", "price", ascending: false, out string sortedJson, out message);
// sortedJson == "[{\"sku\":\"C\",\"price\":25},{\"sku\":\"B\",\"price\":15},{\"sku\":\"A\",\"price\":5}]"
```

Unlike the filter method above, an element **missing** the sort field is
not excluded — it's retained and sorted using an empty comparison value
(so it sorts before any non-empty value ascending, after descending). This
is a deliberate asymmetry between the two methods, not an oversight.
