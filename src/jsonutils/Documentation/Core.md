# Core

## Read a single value out of a JSON document

```csharp
json.TryGetValueFromJson("{\"order\":{\"items\":[{\"sku\":\"ABC\"}]}}", "order.items[0].sku", out string sku, out string message);
// sku == "ABC"
```

A JSON `null` at the resolved path comes back as `null`, not the literal
text `"null"` — the same shape as a value that couldn't be read at all, so
check `message` (or `TryGetValueType`, see [Query](Query.md)) if the two
need to be told apart.

## Update an existing value in place

```csharp
json.TrySetValueInJson("{\"order\":{\"status\":\"open\"}}", "order.status", "closed", out string updatedJson, out message);
// updatedJson == "{\"order\":{\"status\":\"closed\"}}"
```

The path must already resolve to an existing value — this replaces in
place, it does not create new object properties or array elements. Use
`TryAppendToJsonArray` (see [Array](Array.md)) to grow an array instead. The
new value is always set as a JSON string scalar; it does not accept a JSON
fragment for a nested object or array.

## Deserialize into a custom .NET type

```csharp
json.TryDeserializeObject(updatedJson, typeof(MyOrderType).AssemblyQualifiedName, out object order, out message);
```

`typeName` is a string, not a generic parameter — matching the native
`Json` component's own shape, since Robot Studio's designer can't offer a
generic-parameter picker. It must be a design-time-authored literal, never
a value built from untrusted runtime data (see the component
[README](../README.md#notes--caveats)'s security note). A simple type name
only resolves types already loaded/in `mscorlib`; a type defined elsewhere
in the same Robot Studio project may need its assembly-qualified name.

## Serialize a .NET object back to JSON

```csharp
json.TrySerializeObject(order, out string orderJson, out message);
```
