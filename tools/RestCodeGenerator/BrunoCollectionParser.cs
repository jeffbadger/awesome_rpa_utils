using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RestCodeGenerator;

/// <summary>Parses a Bruno collection — a single <c>.bru</c> file, or a directory of them —
/// into the same <see cref="ApiSpec"/> model the other format parsers produce. <c>.bru</c> is
/// not JSON/YAML, so this uses a small hand-rolled block tokenizer (no new package).</summary>
public static class BrunoCollectionParser
{
    private static readonly HashSet<string> HttpMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };

    private static readonly HashSet<string> NonRequestFileNames =
        new(StringComparer.OrdinalIgnoreCase) { "folder.bru", "collection.bru", "bruno.json" };

    public static ApiSpec ParsePath(string path)
    {
        if (Directory.Exists(path)) return ParseDirectory(path);
        if (File.Exists(path)) return ParseSingleFile(path);
        throw new FileNotFoundException($"Bruno collection path not found: {path}", path);
    }

    /// <summary>Files are sorted by relative path before parsing — Bruno gives no other
    /// ordering signal, and without a stable sort, operation order (and
    /// <see cref="MethodNameMapper"/>'s collision-suffix numbers) would vary across
    /// filesystems/OSes, breaking the "same input → byte-identical output" guarantee.</summary>
    private static ApiSpec ParseDirectory(string directory)
    {
        var files = Directory.GetFiles(directory, "*.bru", SearchOption.AllDirectories)
            .Where(f => !NonRequestFileNames.Contains(Path.GetFileName(f)))
            .OrderBy(f => Path.GetRelativePath(directory, f), StringComparer.Ordinal)
            .ToList();

        var operations = new List<ApiOperation>();
        var skipped = new List<string>();
        var authKinds = new HashSet<string>();
        string? commonHost = null;
        var hostSeen = false;
        var hostAmbiguous = false;

        foreach (var file in files)
        {
            var result = ParseFileBlocks(Tokenize(File.ReadAllText(file)), Path.GetFileNameWithoutExtension(file));
            if (result.Operation is null && result.Skipped is null)
                continue;   // not a request file (no recognized HTTP method block)

            if (!hostSeen) { commonHost = result.Host; hostAmbiguous = result.Templated; hostSeen = true; }
            else if (result.Templated || result.Host != commonHost) hostAmbiguous = true;

            if (result.Operation is { } op) operations.Add(op);
            if (result.Skipped is { } sk) skipped.Add(sk);
            if (result.AuthKind is { } ak) authKinds.Add(ak);
        }

        var title = new DirectoryInfo(directory).Name;
        var defaultBaseUrl = hostSeen && !hostAmbiguous ? commonHost : null;
        return new ApiSpec(title, "1.0.0", defaultBaseUrl, operations,
            authKinds.Select(k => new ApiSecurityScheme(k, k)).ToList(), skipped);
    }

    private static ApiSpec ParseSingleFile(string file)
    {
        var fallbackName = Path.GetFileNameWithoutExtension(file);
        var result = ParseFileBlocks(Tokenize(File.ReadAllText(file)), fallbackName);
        var operations = result.Operation is { } op ? new List<ApiOperation> { op } : new List<ApiOperation>();
        var skipped = result.Skipped is { } sk ? new List<string> { sk } : new List<string>();
        var authKinds = result.AuthKind is { } ak ? new List<ApiSecurityScheme> { new(ak, ak) } : new List<ApiSecurityScheme>();
        var defaultBaseUrl = result.Templated ? null : result.Host;
        return new ApiSpec(fallbackName, "1.0.0", defaultBaseUrl, operations, authKinds, skipped);
    }

    private readonly record struct FileParseResult(
        ApiOperation? Operation, string? Skipped, string? AuthKind, string? Host, bool Templated);

    private static FileParseResult ParseFileBlocks(List<BruBlock> blocks, string fallbackName)
    {
        var methodBlock = blocks.FirstOrDefault(b => HttpMethods.Contains(b.Name ?? ""));
        if (methodBlock.Name is null)
            return new FileParseResult(null, null, null, null, true);   // not a request file

        var method = methodBlock.Name.ToUpperInvariant();
        var methodFields = ParseKeyValueLines(methodBlock.Content ?? "");
        var urlRaw = GetField(methodFields, "url") ?? "";
        var (host, templated, path) = ParseUrl(urlRaw);

        var pathParams = ExtractPathParams(path);

        var queryBlock = blocks.FirstOrDefault(b => b.Name == "params:query");
        var queryParams = queryBlock.Name != null
            ? ParseKeyValueLines(queryBlock.Content ?? "")
                .Where(f => !f.Disabled)
                .Select(f => new ApiParameter(f.Key, false, null))
                .ToList()
            : new List<ApiParameter>();

        var headerBlock = blocks.FirstOrDefault(b => b.Name == "headers");
        var headerParams = headerBlock.Name != null
            ? ParseKeyValueLines(headerBlock.Content ?? "")
                .Where(f => !f.Disabled)
                .Select(f => new ApiParameter(f.Key, false, null))
                .ToList()
            : new List<ApiParameter>();

        var bodyMode = GetField(methodFields, "body");
        var hasBody = false;
        ApiSchema? bodySchema = null;
        var nonJsonBody = false;
        if (bodyMode == "json")
        {
            var jsonBlock = blocks.FirstOrDefault(b => b.Name == "body:json");
            var raw = jsonBlock.Content?.Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    using var parsed = JsonDocument.Parse(raw);
                    hasBody = true;
                    bodySchema = ExampleSchemaInference.Infer(parsed.RootElement);
                }
                catch (JsonException) { nonJsonBody = true; }
            }
        }
        else if (!string.IsNullOrEmpty(bodyMode) && bodyMode != "none")
        {
            nonJsonBody = true;   // body:text/xml/formUrlEncoded/multipartForm/graphql/sparql/file — skipped
        }

        var metaBlock = blocks.FirstOrDefault(b => b.Name == "meta");
        var name = metaBlock.Name != null ? GetField(ParseKeyValueLines(metaBlock.Content ?? ""), "name") : null;
        var operationName = string.IsNullOrEmpty(name) ? fallbackName : name;

        var authKind = MapAuthKind(DetermineAuthMode(blocks, methodFields), blocks);

        if (nonJsonBody)
            return new FileParseResult(null, $"{method} {path}", authKind, host, templated);
        var operation = new ApiOperation(method, path, operationName, null, pathParams, queryParams, headerParams, hasBody, bodySchema);
        return new FileParseResult(operation, null, authKind, host, templated);
    }

    /// <summary>A top-level Bruno block: <c>name { ...content... }</c> (block names may contain
    /// ":", e.g. "params:query", "auth:bearer", "body:json").</summary>
    private readonly record struct BruBlock(string? Name, string? Content);

    /// <summary>Splits a .bru file's text into top-level blocks. Brace-matched (tracks depth so
    /// a body's own embedded braces, e.g. JSON content, don't end the block early) and
    /// string-literal-aware (braces inside a quoted value don't affect depth).</summary>
    private static List<BruBlock> Tokenize(string text)
    {
        var blocks = new List<BruBlock>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            var nameStart = i;
            while (i < text.Length && text[i] != '{' && !char.IsWhiteSpace(text[i])) i++;
            var name = text[nameStart..i];
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length || text[i] != '{') break;   // malformed trailing content; stop
            i++;   // skip '{'

            var contentStart = i;
            var depth = 1;
            var inString = false;
            var stringQuote = '\0';
            while (i < text.Length && depth > 0)
            {
                var c = text[i];
                if (inString)
                {
                    if (c == '\\' && i + 1 < text.Length) { i += 2; continue; }
                    if (c == stringQuote) inString = false;
                }
                else if (c is '"' or '\'')
                {
                    inString = true;
                    stringQuote = c;
                }
                else if (c == '{') depth++;
                else if (c == '}') depth--;
                i++;
            }
            var contentEnd = depth == 0 ? i - 1 : text.Length;
            blocks.Add(new BruBlock(name, text[contentStart..contentEnd]));
        }
        return blocks;
    }

    private readonly record struct BruField(string Key, string Value, bool Disabled);

    /// <summary>Parses a block's content as "key: value" lines. A leading "~" marks the line
    /// disabled (Bruno's own convention for a commented-out header/query entry).</summary>
    private static List<BruField> ParseKeyValueLines(string content)
    {
        var result = new List<BruField>();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var disabled = line.StartsWith('~');
            if (disabled) line = line[1..].TrimStart();
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim();
            if (key.Length == 0) continue;
            result.Add(new BruField(key, line[(colon + 1)..].Trim(), disabled));
        }
        return result;
    }

    private static string? GetField(List<BruField> fields, string key)
    {
        foreach (var f in fields)
            if (f.Key == key) return f.Value;
        return null;
    }

    /// <summary>Extracts the origin (scheme+host) and path from a Bruno request URL, which may
    /// use "{{variable}}" templating the same way Postman does. When templated, the path
    /// portion is still recovered (everything from the first "/" after the templated
    /// placeholder's closing "}}").</summary>
    private static (string? Host, bool Templated, string Path) ParseUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, true, "/");
        if (raw.Contains("{{", StringComparison.Ordinal))
        {
            var closing = raw.IndexOf("}}", StringComparison.Ordinal);
            var searchFrom = closing >= 0 ? closing + 2 : 0;
            var slash = raw.IndexOf('/', searchFrom);
            var pathPart = slash >= 0 ? raw[slash..] : "/";
            return (null, true, RewritePathVars(pathPart.Split('?')[0]));
        }
        if (Uri.TryCreate(raw, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https")
            return (abs.Scheme + "://" + abs.Authority, false, RewritePathVars(abs.AbsolutePath));
        var rel = raw.Split('?')[0];
        return (null, true, RewritePathVars(rel.StartsWith('/') ? rel : "/" + rel));
    }

    private static string RewritePathVars(string path) =>
        string.Join("/", path.Split('/').Select(RewriteSegmentVar));

    private static string RewriteSegmentVar(string segment) =>
        segment.StartsWith(':') && segment.Length > 1 ? "{" + segment[1..] + "}" : segment;

    private static List<ApiParameter> ExtractPathParams(string rewrittenPath) =>
        rewrittenPath.Split('/')
            .Where(seg => seg.StartsWith('{') && seg.EndsWith('}'))
            .Select(seg => new ApiParameter(seg[1..^1], true, null))
            .ToList();

    /// <summary>The active auth mode: a top-level <c>auth { mode: ... }</c> block takes
    /// precedence; falls back to an inline <c>auth: &lt;kind&gt;</c> field in the method block.</summary>
    private static string? DetermineAuthMode(List<BruBlock> blocks, List<BruField> methodFields)
    {
        var authBlock = blocks.FirstOrDefault(b => b.Name == "auth");
        if (authBlock.Name != null)
        {
            var mode = GetField(ParseKeyValueLines(authBlock.Content ?? ""), "mode");
            if (mode != null) return mode;
        }
        return GetField(methodFields, "auth");
    }

    /// <summary><c>mode: inherit</c> is a known limitation — folder-tree auth inheritance is
    /// out of scope, so it contributes no scheme (not silently misreported as "none").</summary>
    private static string? MapAuthKind(string? mode, List<BruBlock> blocks)
    {
        if (mode is null or "none" or "inherit") return null;
        switch (mode)
        {
            case "basic": return "basic";
            case "bearer": return "bearer";
            case "apikey":
            {
                var sub = blocks.FirstOrDefault(b => b.Name == "auth:apikey");
                var placement = sub.Name != null ? GetField(ParseKeyValueLines(sub.Content ?? ""), "placement") : null;
                return placement == "queryparams" ? "apiKeyQuery" : "apiKeyHeader";
            }
            case "oauth2":
            {
                var sub = blocks.FirstOrDefault(b => b.Name == "auth:oauth2");
                var grantType = sub.Name != null ? GetField(ParseKeyValueLines(sub.Content ?? ""), "grant_type") : null;
                return grantType == "client_credentials" ? "oauth2ClientCredentials" : "unsupported";
            }
            default: return "unsupported";
        }
    }
}
