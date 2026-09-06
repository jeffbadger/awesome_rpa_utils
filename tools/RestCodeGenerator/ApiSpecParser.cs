using System;
using System.IO;
using System.Text.Json;

namespace RestCodeGenerator;

/// <summary>Dispatches to the right format-specific parser, either from an explicit
/// <see cref="InputFormat"/> or by auto-detecting one from the input's extension/content.</summary>
public static class ApiSpecParser
{
    public static ApiSpec Parse(string input, InputFormat? explicitFormat, out InputFormat usedFormat)
    {
        usedFormat = explicitFormat ?? Detect(input);
        return usedFormat switch
        {
            InputFormat.OpenApi => SwaggerParser.ParseFile(input),
            InputFormat.Postman => PostmanCollectionParser.ParseFile(input),
            InputFormat.Bruno => BrunoCollectionParser.ParsePath(input),
            InputFormat.Curl => File.Exists(input) ? CurlCommandParser.ParseFile(input) : CurlCommandParser.Parse(input),
            _ => throw new ArgumentOutOfRangeException(nameof(usedFormat)),
        };
    }

    /// <summary>Detection order: an existing directory is always Bruno (the only format that
    /// accepts one); a <c>.bru</c> file is Bruno; otherwise the file's content is loaded as
    /// YAML/JSON and inspected for OpenAPI's <c>swagger</c>/<c>openapi</c>/<c>paths</c> keys or
    /// Postman's top-level <c>item</c> array; content that isn't JSON/YAML at all (or matches
    /// neither shape) is sniffed as a curl command by its leading "curl " text. The same "curl "
    /// sniff applies when <paramref name="input"/> isn't an existing path at all — the inline
    /// curl-string case.</summary>
    internal static InputFormat Detect(string input)
    {
        if (Directory.Exists(input))
            return InputFormat.Bruno;

        if (File.Exists(input))
        {
            if (string.Equals(Path.GetExtension(input), ".bru", StringComparison.OrdinalIgnoreCase))
                return InputFormat.Bruno;

            var text = File.ReadAllText(input);
            try
            {
                using var doc = SwaggerParser.LoadDocument(text);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("swagger", out _) || root.TryGetProperty("openapi", out _) || root.TryGetProperty("paths", out _))
                        return InputFormat.OpenApi;
                    if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Array)
                        return InputFormat.Postman;
                }
            }
            catch (Exception)
            {
                // Not valid JSON/YAML at all — fall through to curl text sniffing below.
            }

            if (LooksLikeCurl(text))
                return InputFormat.Curl;

            throw new InvalidOperationException(
                $"Cannot auto-detect input format for '{input}'; pass --format explicitly.");
        }

        if (LooksLikeCurl(input))
            return InputFormat.Curl;

        throw new InvalidOperationException(
            $"'{input}' is not an existing file or directory and does not look like a curl command; pass --format explicitly.");
    }

    private static bool LooksLikeCurl(string text) =>
        text.TrimStart().StartsWith("curl ", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text.Trim(), "curl", StringComparison.OrdinalIgnoreCase);
}
