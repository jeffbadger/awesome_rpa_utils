using System.Collections.Generic;
using System.Text.Json;

namespace RestCodeGenerator;

/// <summary>Infers an <see cref="ApiSchema"/> from a concrete example JSON value (object/array/
/// scalar) — used by parsers (Postman, Bruno, curl) whose input carries an example body rather
/// than a declared JSON Schema. Mirrors <c>SwaggerParser.ParseSchema</c>'s shape rules exactly,
/// so a flattened body renders identically regardless of source format.</summary>
internal static class ExampleSchemaInference
{
    /// <summary>Infers a schema from one example JSON value. Depth &gt; 8 collapses to a string
    /// leaf — the same guard <c>SwaggerParser.ParseSchema</c> uses for pathological/circular
    /// schemas, applied here to pathologically deep example payloads.</summary>
    internal static ApiSchema Infer(JsonElement example, int depth = 0)
    {
        if (depth > 8)
            return new ApiSchema("string", System.Array.Empty<ApiSchemaProperty>(), null, 1);

        switch (example.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var props = new List<ApiSchemaProperty>();
                foreach (var member in example.EnumerateObject())
                    props.Add(new ApiSchemaProperty(member.Name, Infer(member.Value, depth + 1)));
                return new ApiSchema("object", props, null, 1);
            }
            case JsonValueKind.Array:
            {
                var length = example.GetArrayLength();
                var items = length > 0
                    ? Infer(example[0], depth + 1)
                    : new ApiSchema("string", System.Array.Empty<ApiSchemaProperty>(), null, 1);
                return new ApiSchema("array", System.Array.Empty<ApiSchemaProperty>(), items, length > 0 ? length : 1);
            }
            case JsonValueKind.Number:
            {
                var jsonType = example.TryGetInt64(out _) ? "integer" : "number";
                return new ApiSchema(jsonType, System.Array.Empty<ApiSchemaProperty>(), null, 1);
            }
            case JsonValueKind.True:
            case JsonValueKind.False:
                return new ApiSchema("boolean", System.Array.Empty<ApiSchemaProperty>(), null, 1);
            default:   // String, Null, Undefined — no reliable type signal, fall back to string
                return new ApiSchema("string", System.Array.Empty<ApiSchemaProperty>(), null, 1);
        }
    }
}
