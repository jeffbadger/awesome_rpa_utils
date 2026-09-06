using System.Text.Json;

namespace RestCodeGenerator;

/// <summary>Null-safe <see cref="JsonElement"/> accessors shared by every format parser
/// (OpenAPI/Swagger, Postman, Bruno, curl) — a missing property or a value of the wrong
/// JSON type yields null instead of throwing.</summary>
internal static class JsonHelpers
{
    internal static JsonElement? GetPropertyOrNull(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    /// <summary>Reads an optional string property; a missing property or a value whose
    /// JSON type is not string yields null instead of throwing
    /// (<c>JsonElement.GetString()</c> throws <c>InvalidOperationException</c> on
    /// non-string values such as numbers, objects, or arrays).</summary>
    internal static string? GetStringOrNull(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var v) ||
            v.ValueKind != JsonValueKind.String)
            return null;
        return v.GetString();
    }

    /// <summary>Reads an optional positive-integer property from a value that may be a JSON
    /// number or (as every swagger/OpenAPI spec is loaded through the YAML deserializer,
    /// which reads every plain scalar as a string) a numeric string. Missing, zero/negative,
    /// or unparsable yields null.</summary>
    internal static int? GetPositiveIntOrNull(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var v))
            return null;
        var n = v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => (int?)i,
            JsonValueKind.String when int.TryParse(v.GetString(), out var i) => (int?)i,
            _ => null,
        };
        return n is > 0 ? n : null;
    }
}
