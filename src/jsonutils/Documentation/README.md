# JsonUtils Documentation

Real-world usage examples for every method on the `JsonUtils` component,
organized by the same categories used in the source code (each method's
`[Category]` attribute, which is also how Robot Studio's own designer
surface groups them) and the top-level [README](../README.md).

- [Core](Core.md) — deserialize/serialize a .NET object, and get/set a single JSONPath value
- [Validation](Validation.md) — checking whether text is well-formed JSON
- [Get](Get.md) — extracting a JSONPath value as a specific .NET type
- [Query](Query.md) — multi-match extraction, inspecting a value's kind, and finding matching paths by property name
- [Array](Array.md) — removing, counting, appending, filtering, and sorting array elements
- [Format](Format.md) — pretty-printing and minifying JSON text
- [Merge](Merge.md) — combining two JSON objects
- [Compare](Compare.md) — diffing two JSON documents
- [Convert](Convert.md) — converting between JSON and XML text

All examples assume a `JsonUtils` instance named `json`, as it would appear
dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var json = new JsonUtils();`).
