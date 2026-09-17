# Format

## Pretty-print JSON for a log or a review screen

```csharp
json.TryPrettyPrintJson("{\"status\":\"open\",\"priority\":1}", out string formattedJson, out string message);
// formattedJson ==
// {
//   "status": "open",
//   "priority": 1
// }
```

## Minify JSON before storing or transmitting it

```csharp
json.TryMinifyJson(formattedJson, out string minifiedJson, out message);
// minifiedJson == "{\"status\":\"open\",\"priority\":1}"
```

Both methods only reformat whitespace — they don't reorder properties,
change value types, or otherwise alter the document's content.
