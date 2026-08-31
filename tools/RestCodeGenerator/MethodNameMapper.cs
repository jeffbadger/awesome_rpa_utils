using System;
using System.Collections.Generic;
using System.Text;

namespace RestCodeGenerator;

public static class MethodNameMapper
{
    public static IReadOnlyList<string> Reserved = new[]
    {
        "SetBaseUrl", "SetBearerAuthentication", "SetBasicAuthentication",
        "SetCustomAuthentication", "SetApiKeyAuthentication",
        "SetApiKeyQueryAuthentication", "SetOAuth2ClientCredentials",
        "ClearAuthentication", "SetTimeoutSeconds", "LastStatusCode",
        "HeaderBuilder", "QueryBuilder",
    };

    public static IReadOnlyDictionary<SwaggerOperation, string> Map(
        IReadOnlyList<SwaggerOperation> operations)
    {
        // Identity comparer so callers can index with an equal-valued record: List-typed
        // parameter members compare by reference, so record equality alone never matches.
        var map = new Dictionary<SwaggerOperation, string>(new OperationIdentityComparer());
        var seen = new HashSet<string>(Reserved);
        foreach (var op in operations)
        {
            var baseName = Pascalize(op.OperationId) ?? FromVerbAndPath(op);
            var candidate = baseName;
            var suffix = 2;
            while (!seen.Add(candidate))
                candidate = baseName + suffix++;   // DoThing2, then DoThing3, …
            map[op] = candidate;
        }
        return map;
    }

    private static string FromVerbAndPath(SwaggerOperation op)
    {
        var sb = new StringBuilder();
        sb.Append(char.ToUpperInvariant(op.HttpMethod[0])).Append(op.HttpMethod[1..].ToLowerInvariant());
        foreach (var segment in op.Path.Split('/', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var cleaned = segment.Replace("{", "").Replace("}", "");
            sb.Append(Pascalize(cleaned) ?? "Segment");
        }
        return sb.ToString();
    }

    /// <summary>
    /// PascalCase with non-identifier characters as word separators; null when there are no
    /// word characters at all. Shared with the renderer.
    /// </summary>
    public static string? Pascalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
                current.Append(c);
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) words.Add(current.ToString());
        if (words.Count == 0) return null;

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var rest = word[1..];
            sb.Append(char.ToUpperInvariant(word[0]));
            // an all-uppercase remainder reads as an acronym (GET, ID …): title-case it
            if (i > 0 && rest.Length > 0 && rest == rest.ToUpperInvariant())
                sb.Append(rest.ToLowerInvariant());
            else
                sb.Append(rest);
        }
        var result = sb.ToString();
        if (!char.IsLetter(result[0])) result = "X" + result;
        return result;
    }

    /// <summary>Operations are identified by their (method, path, operationId) triple.</summary>
    private sealed class OperationIdentityComparer : IEqualityComparer<SwaggerOperation>
    {
        public bool Equals(SwaggerOperation? x, SwaggerOperation? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null &&
                x.HttpMethod == y.HttpMethod && x.Path == y.Path &&
                x.OperationId == y.OperationId);

        public int GetHashCode(SwaggerOperation obj) =>
            HashCode.Combine(obj.HttpMethod, obj.Path, obj.OperationId);
    }
}