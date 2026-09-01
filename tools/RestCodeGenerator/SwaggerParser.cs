using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RestCodeGenerator;

public static class SwaggerParser
{
    public static SwaggerDoc ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Swagger file not found: {path}", path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return Parse(doc.RootElement);
    }

    private static JsonElement? GetPropertyOrNull(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

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

    /// <summary>Reads an optional string property; a missing property or a value whose
    /// JSON type is not string yields null instead of throwing
    /// (<c>JsonElement.GetString()</c> throws <c>InvalidOperationException</c> on
    /// non-string values such as numbers, objects, or arrays).</summary>
    private static string? GetStringOrNull(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var v) ||
            v.ValueKind != JsonValueKind.String)
            return null;
        return v.GetString();
    }

    public static SwaggerDoc Parse(JsonElement root)
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
            // A templated server URL ({variable}) has no concrete value in the spec,
            // so the generated component must require an explicit SetBaseUrl instead.
            baseUrl = url != null && !url.Contains('{', StringComparison.Ordinal) ? url : null;
        }

        var operations = new List<SwaggerOperation>();
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

                    var pathParams = new List<SwaggerParameter>();
                    var queryParams = new List<SwaggerParameter>();
                    var headerParams = new List<SwaggerParameter>();
                    bool hasBody = false;
                    bool hasNonJsonBody = false;

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
                                case "body": hasBody = true; break;
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
                        var content = rb.GetPropertyOrNull("content");
                        if (content is { } c && c.EnumerateObject().Any(ct => ct.Name.StartsWith("application/json", StringComparison.Ordinal)))
                            hasBody = true;    // JSON requestBody → modeled
                        else
                            hasNonJsonBody = true;   // multipart/form/binary requestBody → skipped
                    }

                    var consumes = opEntry.Value.GetPropertyOrNull("consumes");        // Swagger 2.0
                    if (consumes is { } c2 && c2.EnumerateArray().Any(t => t.GetString()?.StartsWith("multipart/", StringComparison.Ordinal) == true))
                        hasNonJsonBody = true;

                    if (hasNonJsonBody)
                        skipped.Add($"{http} {pathEntry.Name}");
                    else
                        operations.Add(new SwaggerOperation(
                            http, pathEntry.Name,
                            opEntry.Value.GetPropertyOrNull("operationId")?.GetString(),
                            opEntry.Value.GetPropertyOrNull("summary")?.GetString(),
                            pathParams, queryParams, headerParams, hasBody));
                }
            }
        }
        return new SwaggerDoc(title, version, baseUrl, operations, ParseSecuritySchemes(root), skipped)
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

    private static List<SwaggerSecurityScheme> ParseSecuritySchemes(JsonElement root)
    {
        var schemes = new List<SwaggerSecurityScheme>();
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
            schemes.Add(new SwaggerSecurityScheme(d.Name, kind));
        }
        return schemes;
    }

    private static SwaggerParameter ToParameter(JsonElement p) =>
        new(p.GetPropertyOrNull("name")?.GetString() ?? "",
            p.GetPropertyOrNull("in")?.GetString() == "path",
            p.GetPropertyOrNull("description")?.GetString());
}