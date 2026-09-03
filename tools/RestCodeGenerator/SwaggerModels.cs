using System.Collections.Generic;

namespace RestCodeGenerator;

/// <summary>A parsed Swagger 2.0 / OpenAPI 3.x document, reduced to what the generator needs.</summary>
public sealed record SwaggerDoc(
    string Title,
    string Version,
    string? DefaultBaseUrl,     // null when the spec has no absolute http(s) OA3 server URL (templated or relative, e.g. "/api/v3")
    IReadOnlyList<SwaggerOperation> Operations,
    IReadOnlyList<SwaggerSecurityScheme> SecuritySchemes,
    IReadOnlyList<string> SkippedOperations,   // "<METHOD> <path>" for non-JSON-body operations

    // ---- `info` metadata (all null when absent from the spec) ----
    string? Description = null,
    string? TermsOfService = null,       // spec stores it as-is: a URL string per the swagger spec
    string? ContactName = null,          // `info.contact.{name,email,url}`, parts individually optional
    string? ContactEmail = null,
    string? ContactUrl = null,
    string? LicenseName = null,          // `info.license.{name,url}`
    string? LicenseUrl = null)
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
    bool HasBody,                // true when the operation carries a JSON request body
    SwaggerSchema? BodySchema = null);   // the body's resolved JSON schema, when one could be parsed; null falls back to a raw bodyJson parameter

/// <summary>A single non-body parameter. <see cref="SwaggerOperation"/> buckets these by location.</summary>
public sealed record SwaggerParameter(string Name, bool IsPath, string? Description);

/// <summary>
/// A JSON Schema node for a request body, reduced to what the generator needs to flatten
/// scalar fields into typed method parameters and reassemble them into JSON at runtime.
/// $refs are resolved before this is built. A missing/unrecognized "type" defaults to
/// "object" when "properties" is present, "array" when "items" is present, else "string".
/// </summary>
public sealed record SwaggerSchema(
    string JsonType,                              // "object" | "array" | "string" | "integer" | "number" | "boolean"
    IReadOnlyList<SwaggerSchemaProperty> Properties,  // for JsonType == "object"; empty otherwise
    SwaggerSchema? Items,                          // for JsonType == "array"; null otherwise
    int MaxItems);                                 // for JsonType == "array": fixed slot count (>=1, defaults to 1 when the schema omits maxItems); unused otherwise

/// <summary>One named property of an object <see cref="SwaggerSchema"/>.</summary>
public sealed record SwaggerSchemaProperty(string Name, SwaggerSchema Schema);