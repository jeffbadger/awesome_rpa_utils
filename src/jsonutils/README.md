# JsonAutomation

A Pega Robot Studio-ready component (`JsonUtils`) for reading, updating,
validating, and transforming JSON via real JSONPath, as a full replacement
for the native `Json` component's dot-notation-only path support. Like every
component in this suite, its methods report recoverable failures as `False`
with a descriptive message instead of throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `JsonAutomation`
- Assembly: `JsonAutomation`

## Methods

| Method | Signature | Description |
|---|---|---|
| `TryDeserializeObject` | `(string json, string typeName, out object result, out string message) : bool` | Deserializes JSON into an instance of the named .NET type. |
| `TrySerializeObject` | `(object value, out string json, out string message) : bool` | Serializes an object to JSON. |
| `TryGetValueFromJson` | `(string json, string path, out string value, out string message) : bool` | Extracts a single value at a JSONPath. |
| `TrySetValueInJson` | `(string json, string path, string value, out string updatedJson, out string message) : bool` | Updates a value at an existing JSONPath. |
| `IsValidJson` | `(string json, out string message) : bool` | Checks whether text is well-formed JSON. |
| `TryGetStringValue` | `(string json, string path, out string value, out string message) : bool` | Extracts a value as a string. |
| `TryGetIntValue` | `(string json, string path, out int value, out string message) : bool` | Extracts a value as an int. |
| `TryGetBoolValue` | `(string json, string path, out bool value, out string message) : bool` | Extracts a value as a bool. |
| `TryGetDoubleValue` | `(string json, string path, out double value, out string message) : bool` | Extracts a value as a double. |
| `TryGetDateTimeValue` | `(string json, string path, out DateTime value, out string message) : bool` | Extracts a value as a DateTime. |
| `TryGetValuesFromJson` | `(string json, string path, string delimiter, out string delimitedValues, out string message) : bool` | Extracts every value matching a JSONPath, delimited. |
| `TryGetValueType` | `(string json, string path, out JsonValueKind kind, out string message) : bool` | Reports the kind of value at a JSONPath. |
| `TryRemoveValueFromJson` | `(string json, string path, out string updatedJson, out string message) : bool` | Removes a value at a JSONPath. |
| `TryGetArrayLength` | `(string json, string path, out int length, out string message) : bool` | Reports an array's element count. |
| `TryAppendToJsonArray` | `(string json, string path, string valueJson, out string updatedJson, out string message) : bool` | Appends an element to an array. |
| `TryPrettyPrintJson` | `(string json, out string formattedJson, out string message) : bool` | Reformats JSON with indentation. |
| `TryMinifyJson` | `(string json, out string minifiedJson, out string message) : bool` | Reformats JSON with whitespace removed. |

## JSONPath syntax

Path expressions use Newtonsoft.Json's JSONPath dialect:

- `order.status` — a property.
- `items[0].sku` — an array element's property.
- `items[*].sku` — every element's `sku` (use with `TryGetValuesFromJson`).
- `items[?(@.price > 10)].sku` — a filter expression (use with `TryGetValuesFromJson`).
- `$..sku` — recursive descent: every `sku` anywhere in the document.

## Typical workflow

Parse a JSON document, read a value via JSONPath, update it, and reformat
the result:

```csharp
var json = new JsonUtils();

// Read a value from a nested path
json.TryGetValueFromJson("{\"order\":{\"items\":[{\"sku\":\"ABC\"}]}}", "order.items[0].sku", out string sku, out string message);
// sku == "ABC"

// Update an existing value
json.TrySetValueInJson("{\"order\":{\"status\":\"open\"}}", "order.status", "closed", out string updatedJson, out message);
// updatedJson == "{\"order\":{\"status\":\"closed\"}}"

// Deserialize into a custom type - typeName is a string, not a generic parameter
json.TryDeserializeObject(updatedJson, typeof(MyOrderType).AssemblyQualifiedName, out object order, out message);
```

`TryDeserializeObject`'s date-preservation guarantee does not extend to its
own code path - see the caveat below.

## Notes & Caveats

- **Never throws.** Malformed JSON, a path that doesn't resolve, and a type
  mismatch at a resolved path (e.g. `TryGetIntValue` against a string) all
  return `False` with a descriptive message instead of throwing.
- **Newtonsoft.Json dependency.** This is the suite's second component (after
  `ServiceUtils`) with an external NuGet dependency. It's what makes real
  JSONPath - wildcards, recursive descent, filter expressions - possible
  without hand-rolling a path parser; the suite's other JSON handling
  elsewhere uses the BCL's `System.Text.Json`, which doesn't support JSONPath
  querying.
- **Date-like string values are read back exactly as written, never
  reformatted.** JSON is parsed with Newtonsoft's date auto-detection
  disabled (`DateParseHandling.None`), so a value like
  `"2026-02-20T08:30:00Z"` comes back from `TryGetValueFromJson`/
  `TryGetValuesFromJson` byte-for-byte identical to the source text instead
  of being silently reformatted into a culture-dependent .NET date string
  with the `Z`/timezone marker dropped (a real bug caught by code-quality
  review and fixed before this component shipped). Typed getters like
  `TryGetDateTimeValue` are unaffected - they parse the string into a
  `DateTime` on demand regardless of this setting.
- **This date-preservation guarantee does not extend to `TryDeserializeObject`.**
  It uses a separate `JsonConvert.DeserializeObject` code path without
  `DateParseHandling.None` set; deserializing into a target type with an
  `object`/`JToken`/`dynamic`-typed member could still reformat a date-like
  string. Safe for `string`-typed members, which is the common case and the
  only one this component's tests exercise.
- **`TrySetValueInJson` requires the path to already exist.** It replaces a
  value in place; it does not create new object properties or array elements
  along the way. Use `TryAppendToJsonArray` to add array elements.
- **`TrySetValueInJson`'s new value is always set as a JSON string scalar** -
  it does not accept a JSON fragment for nested objects/arrays. This matches
  the native `Json` component's string-typed `value` parameter.
- **`TryDeserializeObject` takes the target type as a string, not a generic
  parameter.** This matches the native `Json` component's own
  `DeserializeObject(string jsonString, string typeString, out object deserializedObject)`
  shape (see the `pega-robotic-automation` skill, ch11) and avoids being the
  only generic public method in this 17-component suite — Robot Studio's
  designer binds parameters/outputs via reflection over closed, concrete
  types. `typeName` is resolved via `Type.GetType(typeName)`: a simple name
  only resolves types in `mscorlib`/already-loaded assemblies, so a type
  defined elsewhere in the same Robot Studio project may need its
  assembly-qualified name (`Type.AssemblyQualifiedName`).
- **Security: `typeName` must come from a trusted, design-time-authored
  value, never from untrusted runtime or external data.** `TryDeserializeObject`
  instantiates the type you name and populates its properties from `json` -
  the same trust model as any other literal string parameter wired into a
  Robot Studio canvas (e.g. `CommandLineUtils`'s `allowedProgramsCsv`,
  `RestCodeGenerator`'s plain-string credential parameters), not a general
  sandbox for arbitrary caller-chosen types. `TypeNameHandling` is explicitly
  set to `None`, so JSON content itself cannot smuggle in a different type
  than the one you named.
- **On the native `Json` component's `SerializeObject` `⚠@default=SingleOutput`
  annotation:** `TrySerializeObject` keeps the standard bool+out signature
  for consistency with every other method in this suite. Robot Studio's
  designer can still be configured to show only the `json` output if a
  single-port surface is wanted.
- **Phase 2 (not yet implemented):** `MergeJson`, `DiffJson`, JSON↔XML
  conversion, and array filter/sort helpers (`FilterJsonArrayByField`,
  `SortJsonArrayByField`). JSON **Schema** validation is not planned at all -
  Newtonsoft's schema validator is a separate commercially-licensed package.
