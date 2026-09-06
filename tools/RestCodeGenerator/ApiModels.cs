using System.Collections.Generic;

namespace RestCodeGenerator;

/// <summary>A normalized API definition, reduced to what the generator needs — produced by any
/// of the format-specific parsers (OpenAPI/Swagger, Postman, Bruno, curl) so the renderer and
/// method-name mapper work identically regardless of the source format.</summary>
public sealed record ApiSpec(
    string Title,
    string Version,
    string? DefaultBaseUrl,     // null when the source had no single concrete absolute http(s) base URL (templated, relative, or ambiguous across operations)
    IReadOnlyList<ApiOperation> Operations,
    IReadOnlyList<ApiSecurityScheme> SecuritySchemes,
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
public sealed record ApiSecurityScheme(
    string Name,          // scheme name from the spec, e.g. "api_key"
    string Kind);         // "basic" | "bearer" | "apiKeyHeader" | "apiKeyQuery" | "oauth2ClientCredentials" | "unsupported"

/// <summary>One callable operation (method + path). Parameters flatten path- and operation-level entries, with $refs resolved.</summary>
public sealed record ApiOperation(
    string HttpMethod,          // GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS — uppercased
    string Path,                // original path, e.g. "/pet/{petId}"
    string? OperationId,        // swagger operationId, may be null
    string? Summary,            // short summary for the generated XML doc comment
    IReadOnlyList<ApiParameter> PathParams,
    IReadOnlyList<ApiParameter> QueryParams,     // `in: query`; arrays documented as CSV-delimited
    IReadOnlyList<ApiParameter> HeaderParams,    // `in: header`
    bool HasBody,                // true when the operation carries a JSON request body
    ApiSchema? BodySchema = null);   // the body's resolved JSON schema, when one could be parsed; null falls back to a raw bodyJson parameter

/// <summary>A single non-body parameter. <see cref="ApiOperation"/> buckets these by location.</summary>
public sealed record ApiParameter(string Name, bool IsPath, string? Description);

/// <summary>
/// A JSON Schema node for a request body, reduced to what the generator needs to flatten
/// scalar fields into typed method parameters and reassemble them into JSON at runtime.
/// $refs are resolved before this is built. A missing/unrecognized "type" defaults to
/// "object" when "properties" is present, "array" when "items" is present, else "string".
/// </summary>
public sealed record ApiSchema(
    string JsonType,                              // "object" | "array" | "string" | "integer" | "number" | "boolean"
    IReadOnlyList<ApiSchemaProperty> Properties,  // for JsonType == "object"; empty otherwise
    ApiSchema? Items,                          // for JsonType == "array"; null otherwise
    int MaxItems);                                 // for JsonType == "array": fixed slot count (>=1, defaults to 1 when the schema omits maxItems); unused otherwise

/// <summary>One named property of an object <see cref="ApiSchema"/>.</summary>
public sealed record ApiSchemaProperty(string Name, ApiSchema Schema);