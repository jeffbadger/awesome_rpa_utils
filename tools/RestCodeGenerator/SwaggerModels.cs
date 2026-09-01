using System.Collections.Generic;

namespace RestCodeGenerator;

/// <summary>A parsed Swagger 2.0 / OpenAPI 3.x document, reduced to what the generator needs.</summary>
public sealed record SwaggerDoc(
    string Title,
    string Version,
    string? DefaultBaseUrl,     // null when the spec has no concrete host/server (or a templated OA3 server URL)
    IReadOnlyList<SwaggerOperation> Operations,
    IReadOnlyList<SwaggerSecurityScheme> SecuritySchemes,
    IReadOnlyList<string> SkippedOperations)   // "<METHOD> <path>" for non-JSON-body operations
{
    /// <summary>Alias for <see cref="SkippedOperations"/>.</summary>
    public IReadOnlyList<string> Skipped => SkippedOperations;
}

/// <summary>One declared security scheme. <see cref="Kind"/> drives which auth helper the renderer emits.</summary>
public sealed record SwaggerSecurityScheme(
    string Name,          // scheme name from the spec, e.g. "api_key"
    string Kind);         // "basic" | "bearer" | "apiKeyHeader" | "apiKeyQuery" | "oauth2ClientCredentials" | "unsupported"

/// <summary>One callable operation (method + path). Parameters flatten path- and operation-level entries, with $refs resolved.</summary>
public sealed record SwaggerOperation(
    string HttpMethod,          // GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS — uppercased
    string Path,                // original path, e.g. "/pet/{petId}"
    string? OperationId,        // swagger operationId, may be null
    string? Summary,            // short summary for the generated XML doc comment
    IReadOnlyList<SwaggerParameter> PathParams,
    IReadOnlyList<SwaggerParameter> QueryParams,     // `in: query`; arrays documented as CSV-delimited
    IReadOnlyList<SwaggerParameter> HeaderParams,    // `in: header`
    bool HasBody);              // true when the operation carries a JSON request body

/// <summary>A single non-body parameter. <see cref="SwaggerOperation"/> buckets these by location.</summary>
public sealed record SwaggerParameter(string Name, bool IsPath, string? Description);