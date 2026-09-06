using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static RestCodeGenerator.JsonHelpers;

namespace RestCodeGenerator;

/// <summary>Parses a Postman Collection (v2.1 schema) into the same <see cref="ApiSpec"/> model
/// the OpenAPI/Swagger parser produces, so <see cref="ComponentRenderer"/> and
/// <see cref="MethodNameMapper"/> handle Postman-sourced operations identically.</summary>
public static class PostmanCollectionParser
{
    public static ApiSpec ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Postman collection file not found: {path}", path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return Parse(doc.RootElement);
    }

    public static ApiSpec Parse(JsonElement root)
    {
        var info = root.GetPropertyOrNull("info");
        var title = info is { } infoElement ? GetStringOrNull(infoElement, "name") ?? "Api" : "Api";

        var requests = new List<(string Name, JsonElement Request)>();
        if (root.GetPropertyOrNull("item") is { ValueKind: JsonValueKind.Array } items)
            WalkItems(items, "", requests);

        var operations = new List<ApiOperation>();
        var skipped = new List<string>();
        string? commonHost = null;
        var hostSeen = false;
        var hostAmbiguous = false;

        foreach (var (name, request) in requests)
        {
            var method = GetStringOrNull(request, "method")?.ToUpperInvariant() ?? "GET";
            var urlElement = request.GetPropertyOrNull("url");

            var (host, templated) = urlElement is { } url ? ExtractHost(url) : (null, true);
            if (!hostSeen) { commonHost = host; hostAmbiguous = templated; hostSeen = true; }
            else if (templated || host != commonHost) hostAmbiguous = true;

            var path = urlElement is { } urlForPath ? ExtractPath(urlForPath) : "/";
            var pathParams = ExtractPathParams(path);
            var queryParams = urlElement is { } urlForQuery ? ExtractQuery(urlForQuery) : new List<ApiParameter>();
            var headerParams = ExtractHeaders(request);
            var (hasBody, bodySchema, nonJsonBody) = ExtractBody(request);

            if (nonJsonBody)
                skipped.Add($"{method} {path}");
            else
                operations.Add(new ApiOperation(method, path, name, null, pathParams, queryParams, headerParams, hasBody, bodySchema));
        }

        var defaultBaseUrl = hostSeen && !hostAmbiguous ? commonHost : null;
        return new ApiSpec(title, "1.0.0", defaultBaseUrl, operations, ParseSecuritySchemes(root, requests), skipped);
    }

    /// <summary>Recursively walks Postman's <c>item[]</c> tree: a node with a <c>request</c>
    /// object is a leaf request; a node with a nested <c>item[]</c> is a folder. Folder names
    /// prefix the request name with "/" (e.g. "Users/Get User"), which
    /// <see cref="MethodNameMapper.Pascalize"/> already splits on as a word separator.</summary>
    private static void WalkItems(JsonElement items, string prefix, List<(string Name, JsonElement Request)> requests)
    {
        foreach (var item in items.EnumerateArray())
        {
            var name = GetStringOrNull(item, "name") ?? "";
            var fullName = prefix.Length == 0 ? name : prefix + "/" + name;
            if (item.GetPropertyOrNull("request") is { ValueKind: JsonValueKind.Object } request)
                requests.Add((fullName, request));
            else if (item.GetPropertyOrNull("item") is { ValueKind: JsonValueKind.Array } subItems)
                WalkItems(subItems, fullName, requests);
        }
    }

    /// <summary>Extracts the request's origin (scheme+host) and whether it's templated/unusable
    /// as a default base URL. Postman's <c>url</c> is usually an object with a <c>host[]</c>
    /// array, but some exports use a plain URL string instead — both are handled.</summary>
    private static (string? Host, bool Templated) ExtractHost(JsonElement urlElement)
    {
        if (urlElement.ValueKind == JsonValueKind.String)
        {
            var raw = urlElement.GetString() ?? "";
            if (raw.Contains("{{", StringComparison.Ordinal)) return (null, true);
            return Uri.TryCreate(raw, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https"
                ? (abs.Scheme + "://" + abs.Authority, false)
                : (null, true);
        }
        if (urlElement.ValueKind != JsonValueKind.Object) return (null, true);

        string? hostText = null;
        if (urlElement.GetPropertyOrNull("host") is { ValueKind: JsonValueKind.Array } hostArray)
            hostText = string.Join(".", hostArray.EnumerateArray().Select(h => h.GetString() ?? ""));
        else
            hostText = GetStringOrNull(urlElement, "host");

        if (string.IsNullOrEmpty(hostText) || hostText.Contains("{{", StringComparison.Ordinal))
            return (null, true);
        var protocol = GetStringOrNull(urlElement, "protocol") ?? "https";
        return (protocol + "://" + hostText, false);
    }

    /// <summary>Extracts the request path, rewriting Postman's <c>:name</c> path-variable
    /// segments to <c>{name}</c> so the existing <c>.Replace("{name}", …)</c> codegen works
    /// unchanged regardless of source format.</summary>
    private static string ExtractPath(JsonElement urlElement)
    {
        if (urlElement.ValueKind == JsonValueKind.String)
        {
            var raw = urlElement.GetString() ?? "";
            var withoutQuery = raw.Split('?')[0];
            if (Uri.TryCreate(withoutQuery, UriKind.Absolute, out var abs))
                return RewritePathVars(abs.AbsolutePath);
            return RewritePathVars(withoutQuery.StartsWith('/') ? withoutQuery : "/" + withoutQuery);
        }
        if (urlElement.ValueKind != JsonValueKind.Object) return "/";

        if (urlElement.GetPropertyOrNull("path") is { ValueKind: JsonValueKind.Array } pathArray)
        {
            var segments = pathArray.EnumerateArray().Select(p => p.GetString() ?? "");
            return "/" + string.Join("/", segments.Select(RewriteSegmentVar));
        }
        var pathString = GetStringOrNull(urlElement, "path");
        return pathString != null
            ? RewritePathVars(pathString.StartsWith('/') ? pathString : "/" + pathString)
            : "/";
    }

    private static string RewritePathVars(string path) =>
        string.Join("/", path.Split('/').Select(RewriteSegmentVar));

    private static string RewriteSegmentVar(string segment) =>
        segment.StartsWith(':') && segment.Length > 1 ? "{" + segment[1..] + "}" : segment;

    /// <summary>Derives path parameters directly from the already-rewritten <c>{name}</c>
    /// segments, rather than depending on <c>url.variable[]</c> declarations that may be
    /// missing from the export.</summary>
    private static List<ApiParameter> ExtractPathParams(string rewrittenPath) =>
        rewrittenPath.Split('/')
            .Where(seg => seg.StartsWith('{') && seg.EndsWith('}'))
            .Select(seg => new ApiParameter(seg[1..^1], true, null))
            .ToList();

    private static List<ApiParameter> ExtractQuery(JsonElement urlElement)
    {
        var result = new List<ApiParameter>();
        if (urlElement.ValueKind != JsonValueKind.Object) return result;
        if (urlElement.GetPropertyOrNull("query") is not { ValueKind: JsonValueKind.Array } queryArray) return result;
        foreach (var q in queryArray.EnumerateArray())
        {
            if (q.GetPropertyOrNull("disabled") is { ValueKind: JsonValueKind.True }) continue;
            var name = GetStringOrNull(q, "key");
            if (string.IsNullOrEmpty(name)) continue;
            result.Add(new ApiParameter(name, false, GetStringOrNull(q, "description")));
        }
        return result;
    }

    private static List<ApiParameter> ExtractHeaders(JsonElement request)
    {
        var result = new List<ApiParameter>();
        if (request.GetPropertyOrNull("header") is not { ValueKind: JsonValueKind.Array } headerArray) return result;
        foreach (var h in headerArray.EnumerateArray())
        {
            if (h.GetPropertyOrNull("disabled") is { ValueKind: JsonValueKind.True }) continue;
            var name = GetStringOrNull(h, "key");
            if (string.IsNullOrEmpty(name)) continue;
            result.Add(new ApiParameter(name, false, GetStringOrNull(h, "description")));
        }
        return result;
    }

    /// <summary>Only a raw body whose text parses as JSON is modeled (flattened into typed
    /// parameters via <see cref="ExampleSchemaInference"/>). Every other body mode
    /// (<c>urlencoded</c>, <c>formdata</c>, <c>file</c>, <c>graphql</c>) — or raw text that
    /// isn't valid JSON — is skipped, exactly like a non-JSON OpenAPI request body.</summary>
    private static (bool HasBody, ApiSchema? Schema, bool NonJsonBody) ExtractBody(JsonElement request)
    {
        if (request.GetPropertyOrNull("body") is not { ValueKind: JsonValueKind.Object } body)
            return (false, null, false);
        if (GetStringOrNull(body, "mode") != "raw")
            return (false, null, true);
        var raw = body.GetPropertyOrNull("raw") is { ValueKind: JsonValueKind.String } rawElement
            ? rawElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(raw))
            return (false, null, false);
        try
        {
            using var parsed = JsonDocument.Parse(raw);
            return (true, ExampleSchemaInference.Infer(parsed.RootElement), false);
        }
        catch (JsonException)
        {
            return (false, null, true);
        }
    }

    /// <summary>Maps Postman auth blocks (collection-level, plus every request's own override)
    /// into deduplicated <see cref="ApiSecurityScheme"/> kinds — the renderer only cares which
    /// kinds are present anywhere, not which operation declared them, matching how OpenAPI's
    /// global <c>securityDefinitions</c> already work in this model.</summary>
    private static List<ApiSecurityScheme> ParseSecuritySchemes(
        JsonElement root, List<(string Name, JsonElement Request)> requests)
    {
        var kinds = new HashSet<string>();
        if (root.GetPropertyOrNull("auth") is { } collectionAuth)
            AddAuthKind(collectionAuth, kinds);
        foreach (var (_, request) in requests)
            if (request.GetPropertyOrNull("auth") is { } requestAuth)
                AddAuthKind(requestAuth, kinds);
        return kinds.Select(k => new ApiSecurityScheme(k, k)).ToList();
    }

    private static void AddAuthKind(JsonElement auth, HashSet<string> kinds)
    {
        var type = GetStringOrNull(auth, "type");
        if (type is null or "noauth") return;
        kinds.Add(type switch
        {
            "basic" => "basic",
            "bearer" => "bearer",
            "apikey" => (GetAuthParam(auth, "apikey", "in") ?? "header") == "query" ? "apiKeyQuery" : "apiKeyHeader",
            "oauth2" => GetAuthParam(auth, "oauth2", "grant_type") is "client_credentials" or "clientCredentials" or "application"
                ? "oauth2ClientCredentials"
                : "unsupported",
            _ => "unsupported",
        });
    }

    /// <summary>Postman auth params are stored as a <c>{type}Auth: [{key, value, type}]</c>
    /// array under <c>auth</c>, e.g. <c>auth.oauth2[{key:"grant_type", value:"client_credentials"}]</c>.</summary>
    private static string? GetAuthParam(JsonElement auth, string authTypeKey, string paramKey)
    {
        if (auth.GetPropertyOrNull(authTypeKey) is not { ValueKind: JsonValueKind.Array } items) return null;
        foreach (var kv in items.EnumerateArray())
            if (GetStringOrNull(kv, "key") == paramKey)
                return GetStringOrNull(kv, "value");
        return null;
    }
}
