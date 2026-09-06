using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RestCodeGenerator;

/// <summary>Parses a single curl command (a literal string, or the contents of a file) into a
/// single-operation <see cref="ApiSpec"/>. Unlike the other parsers, one concrete example call
/// can't reliably distinguish OAuth2 from a static bearer token from an API key, so no security
/// scheme is ever inferred — and any captured Authorization header or <c>-u</c>/<c>--user</c>
/// credential is dropped entirely rather than baked into generated source (the always-present
/// SetBearerAuthentication/SetBasicAuthentication/SetCustomAuthentication helpers cover it).</summary>
public static class CurlCommandParser
{
    public static ApiSpec ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"curl command file not found: {path}", path);
        return Parse(File.ReadAllText(path));
    }

    public static ApiSpec Parse(string curlCommandOrText)
    {
        var tokens = CurlArgvTokenizer.Tokenize(curlCommandOrText);
        var start = tokens.Count > 0 && string.Equals(tokens[0], "curl", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        string? url = null;
        string? method = null;
        var headers = new List<(string Name, string Value)>();
        var dataParts = new List<string>();
        var hasDataFlag = false;

        for (var i = start; i < tokens.Count; i++)
        {
            switch (tokens[i])
            {
                case "-X":
                case "--request":
                    if (++i < tokens.Count) method = tokens[i].ToUpperInvariant();
                    break;
                case "-H":
                case "--header":
                    if (++i < tokens.Count) headers.Add(SplitHeader(tokens[i]));
                    break;
                case "-d":
                case "--data":
                case "--data-raw":
                case "--data-binary":
                case "--data-ascii":
                case "--data-urlencode":
                    if (++i < tokens.Count) { dataParts.Add(tokens[i]); hasDataFlag = true; }
                    break;
                case "-u":
                case "--user":
                    if (++i < tokens.Count) { /* deliberately discarded — never surfaces in the parsed spec */ }
                    break;
                case "--url":
                    if (++i < tokens.Count) url = tokens[i];
                    break;
                case "--location":
                case "-L":
                case "-s":
                case "--silent":
                case "-k":
                case "--insecure":
                case "-i":
                case "--include":
                case "-v":
                case "--verbose":
                case "--compressed":
                    break;   // no-arg flags that don't affect the modeled operation
                default:
                    if (url is null && !tokens[i].StartsWith('-'))
                        url = tokens[i];
                    break;
            }
        }

        if (string.IsNullOrEmpty(url))
            return EmptySpec();

        method ??= hasDataFlag ? "POST" : "GET";

        var (host, path, queryParams) = ParseUrl(url);
        var headerParams = headers
            .Where(h => !string.Equals(h.Name, "Authorization", StringComparison.OrdinalIgnoreCase))
            .Select(h => new ApiParameter(h.Name, false, null))
            .ToList();

        var hasBody = false;
        ApiSchema? bodySchema = null;
        if (dataParts.Count > 0)
        {
            hasBody = true;
            var rawBody = string.Join("&", dataParts);
            try
            {
                using var parsed = JsonDocument.Parse(rawBody);
                bodySchema = ExampleSchemaInference.Infer(parsed.RootElement);
            }
            catch (JsonException) { bodySchema = null; }   // non-JSON body: falls back to a raw bodyJson parameter
        }

        var operation = new ApiOperation(method, path, null, null,
            new List<ApiParameter>(), queryParams, headerParams, hasBody, bodySchema);
        return new ApiSpec("Api", "1.0.0", host, new List<ApiOperation> { operation },
            new List<ApiSecurityScheme>(), new List<string>());
    }

    private static ApiSpec EmptySpec() =>
        new("Api", "1.0.0", null, new List<ApiOperation>(), new List<ApiSecurityScheme>(), new List<string>());

    private static (string Name, string Value) SplitHeader(string headerText)
    {
        var colon = headerText.IndexOf(':');
        return colon < 0 ? (headerText.Trim(), "") : (headerText[..colon].Trim(), headerText[(colon + 1)..].Trim());
    }

    /// <summary>A concrete curl URL always names one origin — <c>DefaultBaseUrl</c> is always
    /// prefilled, unlike every other format. There is no way to distinguish a path variable
    /// from a literal segment in a single example call, so <c>PathParams</c> is always empty
    /// and the path is fixed; query keys become designer-settable parameters (values discarded).</summary>
    private static (string? Host, string Path, List<ApiParameter> QueryParams) ParseUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var abs) || abs.Scheme is not ("http" or "https"))
            return (null, "/", new List<ApiParameter>());

        var queryParams = new List<ApiParameter>();
        if (!string.IsNullOrEmpty(abs.Query))
        {
            foreach (var pair in abs.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var name = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
                if (name.Length > 0) queryParams.Add(new ApiParameter(name, false, null));
            }
        }
        return (abs.Scheme + "://" + abs.Authority, abs.AbsolutePath.Length == 0 ? "/" : abs.AbsolutePath, queryParams);
    }
}
