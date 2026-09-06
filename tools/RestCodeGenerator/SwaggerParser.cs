using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
using static RestCodeGenerator.JsonHelpers;

namespace RestCodeGenerator;

public static class SwaggerParser
{
    public static ApiSpec ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Swagger file not found: {path}", path);
        using var doc = LoadDocument(File.ReadAllText(path));
        return Parse(doc.RootElement);
    }

    /// <summary>Parses a Swagger/OpenAPI document from either JSON or YAML text. Every valid
    /// JSON document is also valid YAML, so the spec is always read as YAML into a plain object
    /// graph, then rebuilt as a <see cref="JsonNode"/> tree and handed to
    /// <see cref="System.Text.Json"/> for string escaping — one code path for both formats.
    /// (YamlDotNet's own JsonCompatible serializer leaves control characters like '\t'
    /// unescaped in its output, which System.Text.Json then rejects as invalid JSON.)</summary>
    /// <summary>Internal so <see cref="ApiSpecParser.Detect"/> can reuse the same YAML/JSON
    /// loading path to sniff a file's shape before committing to a parser.</summary>
    internal static JsonDocument LoadDocument(string text)
    {
        var deserializer = new DeserializerBuilder().Build();
        var yamlObject = deserializer.Deserialize<object?>(text);
        var node = ToJsonNode(yamlObject);
        return JsonDocument.Parse(node?.ToJsonString() ?? "null");
    }

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        IDictionary map => ToJsonObject(map),
        IList list => ToJsonArray(list),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        decimal dec => JsonValue.Create(dec),
        _ => JsonValue.Create(value.ToString()),
    };

    private static JsonObject ToJsonObject(IDictionary map)
    {
        var obj = new JsonObject();
        foreach (DictionaryEntry entry in map)
            obj[entry.Key.ToString() ?? ""] = ToJsonNode(entry.Value);
        return obj;
    }

    private static JsonArray ToJsonArray(IList list)
    {
        var arr = new JsonArray();
        foreach (var item in list)
            arr.Add(ToJsonNode(item));
        return arr;
    }

    private static JsonElement? ResolveRef(JsonElement root, JsonElement? maybe)
    {
        if (maybe is not { } e || e.ValueKind != JsonValueKind.Object) return maybe;
        if (e.TryGetProperty("$ref", out var @ref))
        {
            // JSON-Pointer escapes (~0 for ~, ~1 for /) are not decoded — this generator's
            // specs only use simple names like "parameters/StatusFilter".
            var pointer = @ref.GetString()?.TrimStart('#', '/');   // e.g. "parameters/StatusFilter"
            if (string.IsNullOrEmpty(pointer)) return null;
            var current = root;
            foreach (var part in pointer.Split('/'))
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(part, out var next))
                    return null;
                current = next;
            }
            return current;
        }
        return maybe;
    }

    public static ApiSpec Parse(JsonElement root)
    {
        var title = "Api";
        var version = "1.0.0";

        // `info` metadata — the same shape in Swagger 2.0 and OpenAPI 3.x. Direct values
        // (not $refs), so the ~0/~1 JSON-Pointer caveat does not apply. Every field is
        // best-effort: a missing or wrong-typed value, or a malformed sub-object,
        // yields nulls (title/version keep their fallbacks), never a throw.
        string? description = null, termsOfService = null,
            contactName = null, contactEmail = null, contactUrl = null,
            licenseName = null, licenseUrl = null;
        if (root.GetPropertyOrNull("info") is { ValueKind: JsonValueKind.Object } info)
        {
            title = GetStringOrNull(info, "title") ?? "Api";
            version = GetStringOrNull(info, "version") ?? "1.0.0";
            description = GetStringOrNull(info, "description");
            termsOfService = GetStringOrNull(info, "termsOfService");
            if (info.GetPropertyOrNull("contact") is { ValueKind: JsonValueKind.Object } contact)
            {
                contactName = GetStringOrNull(contact, "name");
                contactEmail = GetStringOrNull(contact, "email");
                contactUrl = GetStringOrNull(contact, "url");
            }
            if (info.GetPropertyOrNull("license") is { ValueKind: JsonValueKind.Object } license)
            {
                licenseName = GetStringOrNull(license, "name");
                licenseUrl = GetStringOrNull(license, "url");
            }
        }

        string? baseUrl = null;
        if (root.GetPropertyOrNull("host") is { } host)                          // Swagger 2.0
        {
            string scheme = "https";
            if (root.GetPropertyOrNull("schemes") is { } schemes &&
                schemes.ValueKind == JsonValueKind.Array && schemes.GetArrayLength() > 0)
            {
                scheme = schemes[0].GetString() ?? "https";
            }
            var basePath = root.GetPropertyOrNull("basePath")?.GetString() ?? "";
            baseUrl = $"{scheme}://{host.GetString()}{basePath}";
        }
        else if (root.GetPropertyOrNull("servers") is { } servers &&             // OpenAPI 3.x
                 servers.ValueKind == JsonValueKind.Array && servers.GetArrayLength() > 0)
        {
            var url = servers[0].GetPropertyOrNull("url")?.GetString();
            // Only a concrete, absolute http(s):// server URL can serve as the base for
            // request URIs. A relative one (e.g. petstore-openapi.json's "/api/v3") has
            // no origin, and a templated one ({variable} — which Uri.TryCreate accepts
            // when the braces sit in the path) has no concrete value in the spec, so the
            // generated component must require an explicit BaseUrl instead.
            baseUrl = url != null &&
                      !url.Contains('{', StringComparison.Ordinal) &&
                      Uri.TryCreate(url, UriKind.Absolute, out var abs) &&
                      abs.Scheme is "http" or "https"
                ? url
                : null;
        }

        var operations = new List<ApiOperation>();
        var skipped = new List<string>();
        var paths = root.GetPropertyOrNull("paths");
        if (paths is { } pathsElement)
        {
            foreach (var pathEntry in pathsElement.EnumerateObject())
            {
                var pathLevelParams = pathEntry.Value.GetPropertyOrNull("parameters");
                foreach (var opEntry in pathEntry.Value.EnumerateObject())
                {
                    var http = opEntry.Name.ToUpperInvariant();
                    if (http is not ("GET" or "PUT" or "POST" or "DELETE" or "PATCH" or "HEAD" or "OPTIONS"))
                        continue;   // skip "parameters", "$ref", x- extensions

                    var pathParams = new List<ApiParameter>();
                    var queryParams = new List<ApiParameter>();
                    var headerParams = new List<ApiParameter>();
                    bool hasBody = false;
                    bool hasNonJsonBody = false;
                    ApiSchema? bodySchema = null;

                    foreach (var scope in new[] { pathLevelParams, opEntry.Value.GetPropertyOrNull("parameters") })
                    {
                        if (scope is not { } scopeElement) continue;
                        foreach (var raw in scopeElement.EnumerateArray())
                        {
                            var p = ResolveRef(root, raw);
                            if (p is not { } param || param.ValueKind != JsonValueKind.Object) continue;
                            switch (param.GetPropertyOrNull("in")?.GetString())
                            {
                                case "path": pathParams.Add(ToParameter(param)); break;
                                case "query": queryParams.Add(ToParameter(param)); break;
                                case "header": headerParams.Add(ToParameter(param)); break;
                                case "body":
                                    hasBody = true;
                                    bodySchema = ParseSchema(root, param.GetPropertyOrNull("schema"));
                                    break;
                                case "formData":
                                    // 2.0 formData without type "file" is JSON-encodable; file uploads are not
                                    if (param.GetPropertyOrNull("type")?.GetString() == "file")
                                        hasNonJsonBody = true;
                                    else
                                        hasBody = true;
                                    break;
                            }
                        }
                    }

                    if (opEntry.Value.GetPropertyOrNull("requestBody") is { } rb)      // OpenAPI 3.x
                    {
                        JsonElement? jsonContent = null;
                        if (rb.GetPropertyOrNull("content") is { ValueKind: JsonValueKind.Object } content)
                        {
                            foreach (var ct in content.EnumerateObject())
                            {
                                if (!ct.Name.StartsWith("application/json", StringComparison.Ordinal)) continue;
                                jsonContent = ct.Value;
                                break;
                            }
                        }
                        if (jsonContent is { } jc)
                        {
                            hasBody = true;    // JSON requestBody → modeled
                            bodySchema = ParseSchema(root, jc.GetPropertyOrNull("schema"));
                        }
                        else
                            hasNonJsonBody = true;   // multipart/form/binary requestBody → skipped
                    }

                    var consumes = opEntry.Value.GetPropertyOrNull("consumes");        // Swagger 2.0
                    if (consumes is { } c2 && c2.EnumerateArray().Any(t => t.GetString()?.StartsWith("multipart/", StringComparison.Ordinal) == true))
                        hasNonJsonBody = true;

                    if (hasNonJsonBody)
                        skipped.Add($"{http} {pathEntry.Name}");
                    else
                        operations.Add(new ApiOperation(
                            http, pathEntry.Name,
                            opEntry.Value.GetPropertyOrNull("operationId")?.GetString(),
                            opEntry.Value.GetPropertyOrNull("summary")?.GetString(),
                            pathParams, queryParams, headerParams, hasBody, bodySchema));
                }
            }
        }
        return new ApiSpec(title, version, baseUrl, operations, ParseSecuritySchemes(root), skipped)
        {
            Description = description,
            TermsOfService = termsOfService,
            ContactName = contactName,
            ContactEmail = contactEmail,
            ContactUrl = contactUrl,
            LicenseName = licenseName,
            LicenseUrl = licenseUrl,
        };
    }

    private static List<ApiSecurityScheme> ParseSecuritySchemes(JsonElement root)
    {
        var schemes = new List<ApiSecurityScheme>();
        var defs = root.GetPropertyOrNull("securityDefinitions");                            // Swagger 2.0
        if (defs is null && root.GetPropertyOrNull("components") is { } components)
            defs = components.GetPropertyOrNull("securitySchemes");                          // OpenAPI 3.x
        if (defs is not { } defsElement) return schemes;
        foreach (var d in defsElement.EnumerateObject())
        {
            var type = d.Value.GetPropertyOrNull("type")?.GetString();
            var scheme = d.Value.GetPropertyOrNull("scheme")?.GetString();
            var location = d.Value.GetPropertyOrNull("in")?.GetString();
            var flow = d.Value.GetPropertyOrNull("flow")?.GetString();
            if (flow is null && d.Value.GetPropertyOrNull("flows") is { } flows &&
                flows.GetPropertyOrNull("clientCredentials") is not null)
                flow = "clientCredentials";                                                  // OpenAPI 3.x flows object
            var kind = (type, scheme, location, flow) switch
            {
                ("basic", _, _, _) => "basic",
                ("http", "bearer", _, _) => "bearer",
                ("http", "basic", _, _) => "basic",
                ("apiKey", _, "header", _) => "apiKeyHeader",
                ("apiKey", _, "query", _) => "apiKeyQuery",
                ("oauth2", _, _, "clientCredentials" or "application") => "oauth2ClientCredentials",
                _ => "unsupported",   // implicit/auth-code/password oauth2, digest, openIdConnect…
            };
            schemes.Add(new ApiSecurityScheme(d.Name, kind));
        }
        return schemes;
    }

    private static ApiParameter ToParameter(JsonElement p) =>
        new(p.GetPropertyOrNull("name")?.GetString() ?? "",
            p.GetPropertyOrNull("in")?.GetString() == "path",
            p.GetPropertyOrNull("description")?.GetString());

    /// <summary>
    /// Resolves a request-body schema (Swagger 2.0 body-parameter <c>schema</c>, or OpenAPI 3.x
    /// <c>requestBody.content["application/json"].schema</c>) into a <see cref="ApiSchema"/>
    /// tree, so the renderer can flatten it into typed method parameters. Returns null for a
    /// missing/malformed schema — callers then fall back to a raw bodyJson string parameter.
    /// A depth guard collapses pathological/circular schemas (self-referencing $refs) into a
    /// plain string leaf rather than recursing forever.
    /// </summary>
    private static ApiSchema? ParseSchema(JsonElement root, JsonElement? maybe, int depth = 0)
    {
        if (ResolveRef(root, maybe) is not { ValueKind: JsonValueKind.Object } schema)
            return null;
        if (depth > 8)
            return new ApiSchema("string", System.Array.Empty<ApiSchemaProperty>(), null, 1);

        var type = GetStringOrNull(schema, "type");
        if (type is null && schema.GetPropertyOrNull("properties") is { ValueKind: JsonValueKind.Object })
            type = "object";
        if (type is null && schema.GetPropertyOrNull("items") is not null)
            type = "array";
        type ??= "string";

        switch (type)
        {
            case "object":
            {
                var props = new List<ApiSchemaProperty>();
                if (schema.GetPropertyOrNull("properties") is { ValueKind: JsonValueKind.Object } properties)
                {
                    foreach (var p in properties.EnumerateObject())
                    {
                        var propSchema = ParseSchema(root, p.Value, depth + 1);
                        if (propSchema != null)
                            props.Add(new ApiSchemaProperty(p.Name, propSchema));
                    }
                }
                return new ApiSchema("object", props, null, 1);
            }
            case "array":
            {
                var items = ParseSchema(root, schema.GetPropertyOrNull("items"), depth + 1)
                             ?? new ApiSchema("string", System.Array.Empty<ApiSchemaProperty>(), null, 1);
                var maxItems = GetPositiveIntOrNull(schema, "maxItems") ?? 1;
                return new ApiSchema("array", System.Array.Empty<ApiSchemaProperty>(), items, maxItems);
            }
            default:
            {
                var jsonType = type is "integer" or "number" or "boolean" ? type : "string";
                return new ApiSchema(jsonType, System.Array.Empty<ApiSchemaProperty>(), null, 1);
            }
        }
    }
}