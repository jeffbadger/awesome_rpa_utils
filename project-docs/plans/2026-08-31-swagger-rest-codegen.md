# Swagger-Driven REST Component Code Generator — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A generator that reads a Swagger/OpenAPI file and emits a self-contained, single-`.cs`-file Robot Studio component with one never-throw public method per API operation, consumed via Robot Studio's **Script component** (which compiles a single `.cs` in place) — or, as a fallback, compiled with the emitted boilerplate `.csproj`. The per-endpoint methods persist once placed; the swagger file is only needed to regenerate.

**Architecture:** A C# console code generator (`tools/RestCodeGenerator/`) with a thin PowerShell entry point (`scripts/Generate-RestComponent.ps1`). The generator parses Swagger 2.0 / OpenAPI 3.x JSON, maps each operation to a designer-friendly C# method (primitive parameters, `out string responseJson`, `out int statusCode`, `out string message`), and renders one standalone `.cs` file (inline HTTP/auth core, inline `NeverThrowsGuard`-style wrapping — no shared base, no NuGet dependencies beyond the BCL). Primary consumption is a Robot Studio Script component; the generator also emits a fixed `.csproj` so `dotnet build` can verify (and CI can compile-check) the generated file. Response JSON is returned raw; automations parse it with Robot Studio's built-in JSON methods (ch11 of the R25 reference). The generator itself is test-first (xunit, Linux-runnable, golden content against a checked-in trimmed Petstore swagger).

**Tech Stack:** .NET 10, `System.Text.Json` (in-box), xunit 2.9.2, PowerShell wrapper. No new package dependencies anywhere.

**Suite conventions this plan follows** (from the repo's established pattern):
- Never-throws contract: `bool` + `out string message`; failure → `false` + actionable message; sentinels (`""` / `0` / `null`) for every other `out` param on failure.
- Never-throw wrapping duplicated locally into the generated file (no shared internal library between components in this repo).
- `[Description]` attributes on the class and every public method (matching DialogUtils/CommandLineUtils style).
- Signature uniqueness: two public methods may not share an identical ordered non-`out` parameter type list.
- Test projects run on Linux (no UseWindowsForms/UseWPF) — the generator and its tests are pure logic, so they do.
- New work happens in `.worktrees/rest-codegen` on branch `rest-codegen-initial`, PR'd and the worktree removed afterward.

---

## Endpoint coverage contract

What the generator must handle for "all forms of endpoints":

| Swagger/OpenAPI feature | Generated behavior |
|---|---|
| Path parameters (`in: path`) | `string` method param; substituted with `Uri.EscapeDataString` |
| Query parameters (`in: query`) | `string` method param appended to `?`; `""` omits the param |
| Array/collection query params | `string` method param, **CSV-delimited** value passed as-is (suite convention, e.g. `allowedProgramsCsv`) |
| Header parameters (`in: header`) | `string` method param sent with `TryAddWithoutValidation` |
| Swagger 2.0 `in: body` / `in: formData` (JSON only) | single `bodyJson` string param |
| OpenAPI 3 `requestBody` `application/json` | single `bodyJson` string param |
| **Non-JSON bodies** (`multipart`, `application/x-www-form-urlencoded`, binary) | Operation **skipped**; listed in an auto-generated comment at the top of the file |
| File-level / path-level `$ref` parameters | Resolved to their definition before parameter extraction |
| OAuth2/API-key `securityDefinitions` | Parsed into scheme-specific auth helpers — see the auth contract below |
| OA3 `servers[].url` with `{variables}` | No default base URL — first `SetBaseUrl` call is mandatory (documented in class doc comment) |
| HEAD/OPTIONS/DELETE (no body) | Same method shape minus `bodyJson` |
| 4xx/5xx responses | Transport success (`true`), visible via `statusCode` out |
| operationId naming, collisions, reserved names | Handled by `MethodNameMapper` (Task 3) |

**Auth contract** — helpers are emitted from the spec's declared `securityDefinitions` (2.0) / `components.securitySchemes` (3.x); the always-present baseline covers undeclared cases:

| Declared scheme | Generated helper |
|---|---|
| `http basic` / Swagger 2.0 `basic` | `SetBasicAuthentication(username, password)` — always available |
| `http bearer` | `SetBearerAuthentication(token)` — always available |
| any/undeclared | `SetCustomAuthentication(headerValue)` — always available (sets the raw `Authorization` value) |
| `apiKey` `in: header` | `SetApiKeyAuthentication(name, value)` — stored once, sent as that named header on every call |
| `apiKey` `in: query` | `SetApiKeyQueryAuthentication(name, value)` — appended to every call's query string |
| `oauth2` with `client_credentials` flow | `SetOAuth2ClientCredentials(clientId, clientSecret, tokenUrl)` — form-POSTs to the token endpoint, caches the access token, refreshes automatically on expiry or on one 401-then-retry per call |
| `oauth2` implicit / authorization-code / password flows | No generated helper — browser-redirect flows don't fit unattended robots; the class doc comment points to `SetBearerAuthentication` with a caller-managed token |
| digest / AWS SigV4 / mTLS | Not generated; listed as unsupported in the generated header comment |

---

## File Structure

| Path | Responsibility |
|---|---|
| `tools/RestCodeGenerator/RestCodeGenerator.csproj` | Console app (`net10.0`). NOT in `src/AwesomeRpaUtils.sln`, NOT in release packaging (builds explicitly, like the test-harness convention). |
| `tools/RestCodeGenerator/SwaggerModels.cs` | Parsed-Swagger record types. |
| `tools/RestCodeGenerator/SwaggerParser.cs` | Swagger 2.0 / OpenAPI 3 JSON → `SwaggerDoc` (with `$ref` resolution, header params, non-JSON-body skip list). |
| `tools/RestCodeGenerator/MethodNameMapper.cs` | Operation → unique PascalCase C# method name (operationId, else verb+path; numeric collision suffixes). |
| `tools/RestCodeGenerator/ComponentRenderer.cs` | Model → single-file `.cs` text + fallback `.csproj` text. |
| `tools/RestCodeGenerator/Program.cs` | CLI: `RestCodeGenerator <swaggerPath> <apiName> <outputDirectory>`. |
| `tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj` | xunit test project (subfolder convention). |
| `tools/RestCodeGenerator.Tests/SwaggerParserTests.cs` | Parser tests. |
| `tools/RestCodeGenerator.Tests/MethodNameMapperTests.cs` | Naming + collision tests. |
| `tools/RestCodeGenerator.Tests/ComponentRendererTests.cs` | Golden-content tests against rendered output. |
| `tools/RestCodeGenerator.Tests/TestData/petstore-minimal.json` | Checked-in trimmed Petstore swagger (no network access in tests). |
| `scripts/Generate-RestComponent.ps1` | User entry point: builds tool, runs it, prints next steps. |
| `README.md` | New "Swagger REST code generation" section under Tooling. |
| `tools/README.md` | Tool usage reference. |

Generated output (per API, e.g. Petstore, `generated/` is git-ignored via `.gitignore` addition):

| Path | Responsibility |
|---|---|
| `<OutputDir>/<ApiName>RestUtils.cs` | The entire component — one file, self-contained, ready for a Robot Studio Script component. |
| `<OutputDir>/<ApiName>RestUtils.csproj` | Fallback/CI boilerplate so `dotnet build` just works if not using the Script component. |

Component naming convention (mirrors the suite): file/class `<ApiName>RestUtils` (e.g. `PetStoreRestUtils`), namespace `<ApiName>RestAutomation`. Confirmed: Robot Studio's Script component compiles a single `.cs` in place, surfaces public methods with parameters/`out`-shape and `[Description]` attributes on the designer, and accepts `System.Net.Http` — so Script-component consumption is the primary path and the method shapes below are final.

---

### Task 1: Generator project + Swagger models

**Files:**
- Create: `tools/RestCodeGenerator/RestCodeGenerator.csproj`
- Create: `tools/RestCodeGenerator/SwaggerModels.cs`

- [ ] **Step 1: Create the console project**

`tools/RestCodeGenerator/RestCodeGenerator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <AssemblyName>RestCodeGenerator</AssemblyName>
    <RootNamespace>RestCodeGenerator</RootNamespace>
  </PropertyGroup>

</Project>
```

(Outside `src/`, so `src/Directory.Build.props` does not route its output into the release `src/bin/` folder.)

- [ ] **Step 2: Write the parsed-model types**

`tools/RestCodeGenerator/SwaggerModels.cs`:

```csharp
namespace RestCodeGenerator;

/// <summary>A parsed Swagger 2.0 / OpenAPI 3.x document, reduced to what the generator needs.</summary>
public sealed record SwaggerDoc(
    string Title,
    string Version,
    string? DefaultBaseUrl,     // null when the spec has no concrete host/server (or a templated OA3 server URL)
    IReadOnlyList<SwaggerOperation> Operations,
    IReadOnlyList<SwaggerSecurityScheme> SecuritySchemes,
    IReadOnlyList<string> SkippedOperations);   // "<METHOD> <path>" for non-JSON-body operations

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
```

- [ ] **Step 3: Commit**

```bash
git add tools/RestCodeGenerator
git commit -m "rest-codegen: scaffold generator project and swagger models"
```

---

### Task 2: Swagger parser (TDD)

**Files:**
- Create: `tools/RestCodeGenerator/SwaggerParser.cs`
- Test: `tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj`, `tools/RestCodeGenerator.Tests/SwaggerParserTests.cs`, `tools/RestCodeGenerator.Tests/TestData/petstore-minimal.json`

- [ ] **Step 1: Create the test project**

`tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <AssemblyName>RestCodeGenerator.Tests</AssemblyName>
    <RootNamespace>RestCodeGenerator.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RestCodeGenerator\RestCodeGenerator.csproj" />
  </ItemGroup>

</Project>
```

(Only the generated component projects multi-target `net8.0-windows`/`net10.0-windows`; the generator is a plain dev tool, so single-target `net10.0` — its tests run everywhere with no WindowsDesktop runtime.)

- [ ] **Step 2: Check in the trimmed Petstore swagger**

`tools/RestCodeGenerator.Tests/TestData/petstore-minimal.json` — Swagger 2.0 (exercises: file-level `$ref` parameter, path-parameter extraction, header parameter, query parameter, operation-level + path-level parameters merging, `in: body`, a non-JSON `in: formData` operation that must be skipped, and two operations on the same path which must not collide):

```json
{
  "swagger": "2.0",
  "info": { "title": "Petstore", "version": "1.0.0" },
  "host": "petstore.example.com",
  "basePath": "/v2",
  "securityDefinitions": {
    "api_key": { "type": "apiKey", "name": "api_key", "in": "header" },
    "petstore_auth": {
      "type": "oauth2",
      "flow": "implicit",
      "authorizationUrl": "https://petstore.example.com/oauth/dialog",
      "scopes": { "read:pets": "read your pets" }
    }
  },
  "parameters": {
    "StatusFilter": {
      "name": "status",
      "in": "query",
      "type": "string"
    }
  },
  "paths": {
    "/pet/{petId}": {
      "parameters": [ { "name": "petId", "in": "path", "type": "integer", "required": true } ],
      "get": {
        "operationId": "getPetById",
        "summary": "Returns a pet by ID.",
        "parameters": [
          { "$ref": "#/parameters/StatusFilter" },
          { "name": "apiKey", "in": "query", "type": "string" }
        ],
        "responses": { "200": { "description": "ok" } }
      },
      "delete": {
        "operationId": "deletePet",
        "summary": "Deletes a pet.",
        "parameters": [ { "name": "apiKey2", "in": "query", "type": "string" } ],
        "responses": { "204": { "description": "deleted" } }
      },
      "post": {
        "summary": "Updates a pet with form data.",
        "parameters": [ { "name": "body", "in": "body", "schema": { "type": "object" } } ],
        "responses": { "200": { "description": "ok" } }
      }
    },
    "/pet/findByStatus": {
      "get": {
        "operationId": "findPetsByStatus",
        "summary": "Finds pets by status.",
        "parameters": [ { "name": "X-Request-Source", "in": "header", "type": "string" } ],
        "responses": { "200": { "description": "ok" } }
      }
    },
    "/pet/{petId}/uploadImage": {
      "post": {
        "summary": "Uploads an image (multipart form). Not supported — must be skipped.",
        "consumes": [ "multipart/form-data" ],
        "parameters": [
          { "name": "petId", "in": "path", "type": "integer", "required": true },
          { "name": "file", "in": "formData", "type": "file" }
        ],
        "responses": { "200": { "description": "ok" } }
      }
    }
  }
}
```

- [ ] **Step 3: Write the failing parser tests**

`tools/RestCodeGenerator.Tests/SwaggerParserTests.cs`:

```csharp
using System.IO;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class SwaggerParserTests
    {
        private static SwaggerDoc ParsePetstore()
        {
            var path = Path.Combine("TestData", "petstore-minimal.json");
            return SwaggerParser.ParseFile(path);
        }

        [Fact]
        public void ParseFile_ExtractsInfoAndBaseUrl()
        {
            var doc = ParsePetstore();
            Assert.Equal("Petstore", doc.Title);
            Assert.Equal("1.0.0", doc.Version);
            Assert.Equal("https://petstore.example.com/v2", doc.DefaultBaseUrl);
        }

        [Fact]
        public void ParseFile_KeepsFourOperations_AndSkipsMultipartUpload()
        {
            var doc = ParsePetstore();
            Assert.Equal(4, doc.Operations.Count); // uploadImage is skipped, not modeled
            Assert.Contains("POST /pet/{petId}/uploadImage", doc.Skipped); // formData "file" is non-JSON
        }

        [Fact]
        public void ParseFile_ResolvesFileLevelRefs_AndMergesPathAndOperationParams()
        {
            var doc = ParsePetstore();
            var get = Assert.Single(doc.Operations, o => o.OperationId == "getPetById");
            Assert.Equal("GET", get.HttpMethod);
            Assert.Equal("/pet/{petId}", get.Path);
            Assert.Single(get.PathParams, p => p.Name == "petId");
            Assert.Collection(get.QueryParams,
                p => Assert.Equal("status", p.Name),      // arrived via $ref to #/parameters/StatusFilter
                p => Assert.Equal("apiKey", p.Name));     // operation-level
        }

        [Fact]
        public void ParseFile_ExtractsHeaderParameters()
        {
            var doc = ParsePetstore();
            var get = Assert.Single(doc.Operations, o => o.OperationId == "findPetsByStatus");
            Assert.Equal("X-Request-Source", Assert.Single(get.HeaderParams).Name);
        }

        [Fact]
        public void ParseFile_BodyOpsGetHasBody_NonGetOpsDontCollide()
        {
            var doc = ParsePetstore();
            var post = Assert.Single(doc.Operations, o => o.Path == "/pet/{petId}" && o.HttpMethod == "POST");
            Assert.True(post.HasBody);
            var del = Assert.Single(doc.Operations, o => o.OperationId == "deletePet");
            Assert.False(del.HasBody);
        }

        [Fact]
        public void ParseFile_ExtractsSecuritySchemeKinds()
        {
            var doc = ParsePetstore();
            Assert.Equal("apiKeyHeader", Assert.Single(doc.SecuritySchemes, s => s.Name == "api_key").Kind);
            var oauth = Assert.Single(doc.SecuritySchemes, s => s.Name == "petstore_auth");
            Assert.Equal("unsupported", oauth.Kind); // implicit flow — no generated helper
        }

        [Fact]
        public void ParseFile_MissingFile_ThrowsFileNotFoundException()
        {
            // The generator CLI is allowed to throw on bad usage — it is a dev tool,
            // not a Robot Studio component; the never-throws contract applies to the
            // generated component, not to this generator.
            Assert.Throws<FileNotFoundException>(() => SwaggerParser.ParseFile("nope.json"));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

```bash
dotnet test tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj
```
Expected: build FAILS with "The name 'SwaggerParser' does not exist".

- [ ] **Step 5: Implement the parser**

`tools/RestCodeGenerator/SwaggerParser.cs` — handles Swagger 2.0 (`parameters` with `in: path|query|header|body|formData`, `host`+`basePath`, file-level `#/parameters/X` refs) and OpenAPI 3.x (`servers[0].url` — **any `{variable}` in it forces `BaseUrl = null`**; `requestBody` counts as body only when `application/json` is among its content types):

```csharp
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
        e.ValueKind == JsonElement.ValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    private static JsonElement? ResolveRef(JsonElement root, JsonElement? maybe)
    {
        if (maybe is not { } e || e.ValueKind != JsonElement.ValueKind.Object) return maybe;
        if (e.TryGetProperty("$ref", out var @ref))
        {
            var pointer = @ref.GetString()?.TrimStart('#', '/');   // e.g. "parameters/StatusFilter"
            if (string.IsNullOrEmpty(pointer)) return null;
            var current = root;
            foreach (var part in pointer.Split('/'))
                current = current.GetPropertyOrNull(part) ?? default;
            return current.ValueKind == JsonElement.ValueKind.Undefined ? null : current;
        }
        return maybe;
    }

    public static SwaggerDoc Parse(JsonElement root)
    {
        var title = root.GetPropertyOrNull("info")?.GetPropertyOrNull("title")?.GetString() ?? "Api";
        var version = root.GetPropertyOrNull("info")?.GetPropertyOrNull("version")?.GetString() ?? "1.0.0";

        string? baseUrl = null;
        if (root.TryGetProperty("host", out var host))
        {
            var scheme = root.GetPropertyOrNull("schemes")?.EnumerateArray().FirstOrDefault().GetString() ?? "https";
            var basePath = root.GetPropertyOrNull("basePath")?.GetString() ?? "";
            baseUrl = $"{scheme}://{host.GetString()}{basePath}";
        }
        else if (root.GetPropertyOrNull("servers") is { } servers &&
                 servers.ValueKind == JsonValueKind.Array && servers.GetArrayLength() > 0)
        {
            var url = servers[0].GetPropertyOrNull("url")?.GetString();
            baseUrl = url != null && !url.Contains('{') ? url : null; // templated server → require SetBaseUrl
        }

        var operations = new List<SwaggerOperation>();
        var skipped = new List<string>();
        var paths = root.GetPropertyOrNull("paths");
        if (paths is not null)
        {
            foreach (var pathEntry in paths.EnumerateObject())
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

                    foreach (var scope in new[] { pathLevelParams, opEntry.Value.GetPropertyOrNull("parameters") })
                    {
                        if (scope is null) continue;
                        foreach (var raw in scope.EnumerateArray())
                        {
                            var p = ResolveRef(root, raw);
                            if (p is not { } param) continue;
                            switch (param.GetPropertyOrNull("in")?.GetString())
                            {
                                case "path": pathParams.Add(ToParameter(param)); break;
                                case "query": queryParams.Add(ToParameter(param)); break;
                                case "header": headerParams.Add(ToParameter(param)); break;
                                case "body": hasBody = true; break;
                                case "formData":
                                    // 2.0 formData without type "file" is JSON-encodable; file uploads are not
                                    hasBody = param.GetPropertyOrNull("type")?.GetString() != "file";
                                    break;
                            }
                        }
                    }

                    if (root.GetPropertyOrNull("requestBody") is not null || opEntry.Value.GetPropertyOrNull("requestBody") is { } rb)
                    {
                        var content = rb.GetPropertyOrNull("content");
                        hasBody = content is { } c && c.EnumerateObject().Any(ct =>
                            ct.Name.StartsWith("application/json"));
                        // non-JSON requestBody (multipart, form, binary) → not hasBody; recorded as skipped below
                    }

                    var consumes = opEntry.Value.GetPropertyOrNull("consumes");
                    bool hasFileBody = consumes is { } c2 && c2.EnumerateArray().Any(t =>
                        t.GetString()?.StartsWith("multipart/") == true);

                    if (hasFileBody)
                        skipped.Add($"{http} {pathEntry.Name}"); // non-JSON body, per the coverage contract
                    else
                        operations.Add(new SwaggerOperation(
                            http, pathEntry.Name,
                            opEntry.Value.GetPropertyOrNull("operationId")?.GetString(),
                            opEntry.Value.GetPropertyOrNull("summary")?.GetString(),
                            pathParams, queryParams, headerParams, hasBody));
                }
            }
        }
        return new SwaggerDoc(title, version, baseUrl, operations, ParseSecuritySchemes(root), skipped);
    }

    private static List<SwaggerSecurityScheme> ParseSecuritySchemes(JsonElement root)
    {
        var schemes = new List<SwaggerSecurityScheme>();
        var defs = root.GetPropertyOrNull("securityDefinitions");                            // Swagger 2.0
        if (defs is null && root.GetPropertyOrNull("components") is { } components)
            defs = components.GetPropertyOrNull("securitySchemes");                          // OpenAPI 3.x
        if (defs is null) return schemes;
        foreach (var d in defs.EnumerateObject())
        {
            var type = d.Value.GetPropertyOrNull("type")?.GetString();
            var scheme = d.Value.GetPropertyOrNull("scheme")?.GetString();
            var location = d.Value.GetPropertyOrNull("in")?.GetString();
            var flow = d.Value.GetPropertyOrNull("flow")?.GetString();
            if (flow is null && d.Value.GetPropertyOrNull("flows") is { } flows &&
                flows.GetPropertyOrNull("clientCredentials") is not null)
                flow = "clientCredentials";                                                  // OpenAPI 3.x flows object
            var kind = type switch
            {
                "basic" => "basic",
                "http" when scheme == "bearer" => "bearer",
                "http" when scheme == "basic" => "basic",
                "apiKey" when location == "header" => "apiKeyHeader",
                "apiKey" when location == "query" => "apiKeyQuery",
                "oauth2" when flow == "clientCredentials" || flow == "application" => "oauth2ClientCredentials",
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
```

(If the `FirstOrDefault()` LINQ call needs `using System.Linq;` under `ImplicitUsings disable`, add it — the using block above already includes it.)

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj
```
Expected: 7/7 PASS (includes the security-scheme extraction test).

- [ ] **Step 7: Commit**

```bash
git add tools
git commit -m "rest-codegen: swagger parser with $ref resolution, header params, and skip rules"
```

---

### Task 3: Method-name mapper (TDD)

**Files:**
- Create: `tools/RestCodeGenerator/MethodNameMapper.cs`
- Test: `tools/RestCodeGenerator.Tests/MethodNameMapperTests.cs`

Mapping rules (this keeps the generated surface designer-usable per the Signature Uniqueness Standard):
- `operationId` present → PascalCase it and strip non-identifier characters (`getPetById` → `GetPetById`).
- No `operationId` → `<Verb>` + PascalCase path segments with `{param}` braces dropped (`POST /pet/{petId}` → `PostPetPetId`).
- Collisions → append `2`, `3`, … in first-seen order.
- Names colliding with the reserved always-present members (`SetBaseUrl`, `SetBearerAuthentication`, `SetBasicAuthentication`, `SetCustomAuthentication`, `SetApiKeyAuthentication`, `SetApiKeyQueryAuthentication`, `SetOAuth2ClientCredentials`, `ClearAuthentication`, `SetTimeoutSeconds`, `GetLastStatusCode`) get the `2` suffix too.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class MethodNameMapperTests
    {
        private static SwaggerOperation Op(string method, string path, string? id = null) =>
            new(method, path, id, null,
                new List<SwaggerParameter>(), new List<SwaggerParameter>(),
                new List<SwaggerParameter>(), false);

        [Theory]
        [InlineData("getPetById", "/pet/{petId}", "GET", "GetPetById")]
        [InlineData(null, "/pet/findByStatus", "GET", "GetPetFindByStatus")]
        [InlineData(null, "/pet/{petId}", "POST", "PostPetPetId")]
        [InlineData(null, "/pet/{petId}", "DELETE", "DeletePetPetId")]
        [InlineData("user-login_GET!", "/user/login", "GET", "UserLoginGet")]
        public void Map_ReturnsExpectedNames(string? id, string path, string method, string expected)
        {
            var map = MethodNameMapper.Map(new[] { Op(method, path, id) });
            Assert.Equal(expected, map[Op(method, path, id)]);
        }

        [Fact]
        public void Map_CollidingNames_GetNumericSuffixes()
        {
            var ops = new[] { Op("doThing", "/a", "GET"), Op("doThing", "/b", "GET") };
            var map = MethodNameMapper.Map(ops);
            Assert.Equal("DoThing", map[ops[0]]);
            Assert.Equal("DoThing2", map[ops[1]]);
        }

        [Fact]
        public void Map_ReservesHelperMethodNames()
        {
            var ops = new[] { Op("setBaseUrl", "/x", "GET") };
            var map = MethodNameMapper.Map(ops);
            Assert.Equal("SetBaseUrl2", map[ops[0]]);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: build FAILS — "The name 'MethodNameMapper' does not exist".

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;
using System.Text;

namespace RestCodeGenerator;

public static class MethodNameMapper
{
    public static readonly string[] Reserved =
    {
        "SetBaseUrl", "SetBearerAuthentication", "SetBasicAuthentication",
        "SetCustomAuthentication", "SetApiKeyAuthentication",
        "SetApiKeyQueryAuthentication", "SetOAuth2ClientCredentials",
        "ClearAuthentication", "SetTimeoutSeconds", "GetLastStatusCode",
    };

    public static IReadOnlyDictionary<SwaggerOperation, string> Map(
        IReadOnlyList<SwaggerOperation> operations)
    {
        var map = new Dictionary<SwaggerOperation, string>();
        var seen = new HashSet<string>(Reserved);
        foreach (var op in operations)
        {
            var candidate = Pascalize(op.OperationId) ?? FromVerbAndPath(op);
            while (!seen.Add(candidate))
                candidate = candidate + "2"; // 3, 4, … resolve on the next loop pass
            map[op] = candidate;
        }
        return map;
    }

    private static string FromVerbAndPath(SwaggerOperation op)
    {
        var sb = new StringBuilder();
        sb.Append(char.ToUpper(op.HttpMethod[0])).Append(op.HttpMethod[1..].ToLower());
        foreach (var segment in op.Path.Split('/', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var cleaned = segment.Replace("{", "").Replace("}", "");
            sb.Append(Pascalize(cleaned) ?? "Segment");
        }
        return sb.ToString();
    }

    /// <summary>PascalCase with non-identifier characters as word separators. Shared with the renderer.</summary>
    public static string Pascalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Segment";
        var sb = new StringBuilder(s.Length);
        bool upper = true;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(upper ? char.ToUpper(c) : c);
                upper = false;
            }
            else upper = true;   // separators (-, _, ., {) start a new word
        }
        var result = sb.ToString();
        if (result.Length == 0 || !char.IsLetter(result[0])) result = "X" + result;
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: all PASS (7 parser + 3 mapper = 10 so far).

- [ ] **Step 5: Commit**

```bash
git add tools
git commit -m "rest-codegen: unique method-name mapping with tests"
```

---

### Task 4: Component renderer (TDD — the heart)

**Files:**
- Create: `tools/RestCodeGenerator/ComponentRenderer.cs`
- Test: `tools/RestCodeGenerator.Tests/ComponentRendererTests.cs`

**Generated-file design** (what `ComponentRenderer` emits — one `.cs`, plus the fallback `.csproj`):

- `namespace <ApiName>RestAutomation { public class <ApiName>RestUtils }`, `[Description]` on class and methods, XML doc comments from swagger summaries.
- Fields: `private string _baseUrl = "";` (prefilled from the swagger `BaseUrl` when the parser found a concrete one), `private string _authorizationHeader = "";`, `private int _lastStatusCode;`, `private int _timeoutSeconds = 30;`, `private static readonly HttpClient _client = new HttpClient();`
- Always-present public methods (all never-throw, `bool` + `out string message`): `SetBaseUrl`, `SetBearerAuthentication`, `SetBasicAuthentication`, `SetCustomAuthentication` (raw `Authorization` value), `ClearAuthentication`, `SetTimeoutSeconds` (rejects ≤ 0), plus `int LastStatusCode` property (designer-readable, `0` before any call).
- **Scheme-specific auth helpers, emitted only when the spec declares a matching scheme** (see the Auth contract table above): `apiKey` in header → `SetApiKeyAuthentication(name, value)`; `apiKey` in query → `SetApiKeyQueryAuthentication(name, value)`; `oauth2` with client_credentials flow → `SetOAuth2ClientCredentials(clientId, clientSecret, tokenUrl)` plus the private `TryRefreshOAuth2Token`/`ExtractJsonPropertyText` core in the template. Schemes that don't fit unattended robots (implicit/auth-code/password OAuth2, digest, SigV4) generate nothing and are listed as unsupported in the header comment — the class doc comment points their callers at `SetBearerAuthentication`.
- Per-endpoint methods, one fixed shape per group (signature-unique by construction):
  - **GET/HEAD/OPTIONS/DELETE:** `bool <Name>(string <pathParams…>, string <queryParams…>, string <headerParams…>, out string responseJson, out int statusCode, out string message)`
  - **POST/PUT/PATCH:** same plus `string bodyJson` before the `out` parameters.
  - All parameters are `string` (designer-friendly primitives; caller converts numbers with Pega's built-in methods). `""` for any string param omits it (query/header). Array-typed query params documented as CSV-delimited — passed through as-is.
  - Header params are sent with `request.Headers.TryAddWithoutValidation(name, value)` — only when non-empty.
  - Returns `true` when the HTTP call completed and returned *any* status (transport success); `false` + message on transport failure. `responseJson` is the raw body (`""` sentinel on failure).
- A generated file-header comment lists skipped operations (non-JSON bodies) so nothing disappears silently.

- [ ] **Step 1: Write the failing renderer tests**

Assert on *content* (regex/`Contains`), not whole-file equality, so the template can evolve:

```csharp
using System.IO;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class ComponentRendererTests
    {
        private static readonly string Petstore = Path.Combine("TestData", "petstore-minimal.json");

        [Fact]
        public void Render_EmitsExpectedNamespaceClassAndFile()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("namespace PetStoreRestAutomation", output.Source);
            Assert.Contains("public class PetStoreRestUtils", output.Source);
            Assert.Equal("PetStoreRestUtils", output.FileNameBase);
        }

        [Fact]
        public void Render_EmitsOneMethodPerOperation_WithDesignerShape()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("public bool GetPetById(string petId, string status, string apiKey, out string responseJson, out int statusCode, out string message)", output.Source);
            Assert.Contains("public bool PostPetPetId(string petId, string bodyJson, out string responseJson, out int statusCode, out string message)", output.Source);
            Assert.Contains("public bool DeletePetPetId(string petId, string apiKey2, out string responseJson, out int statusCode, out string message)", output.Source);
            Assert.Contains("public bool FindPetsByStatus(string xRequestSource, out string responseJson, out int statusCode, out string message)", output.Source);
        }

        [Fact]
        public void Render_ListsSkippedOperations_InHeaderComment()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("POST /pet/{petId}/uploadImage", output.Source);
            Assert.Contains("Skipped", output.Source);
        }

        [Fact]
        public void Render_EmitsAlwaysPresentHelpers_AndNeverThrowsContract()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("public bool SetBaseUrl(string url, out string message)", output.Source);
            Assert.Contains("public bool SetBearerAuthentication(string token, out string message)", output.Source);
            Assert.Contains("public bool SetCustomAuthentication(string headerValue, out string message)", output.Source);
            // Petstore declares an apiKey-in-header scheme ("api_key") → its scheme-specific helper is emitted:
            Assert.Contains("public bool SetApiKeyAuthentication(string name, string value, out string message)", output.Source);
            // ...but its oauth2 scheme is implicit-flow → "unsupported" → no client-credentials helper:
            Assert.DoesNotContain("SetOAuth2ClientCredentials", output.Source);
        }

        [Fact]
        public void Render_CsprojFallbackIsMultiTargetedStandalone()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("<TargetFrameworks>net8.0-windows;net10.0-windows</TargetFrameworks>", output.Project);
            Assert.Contains("<AssemblyName>PetStoreRestAutomation</AssemblyName>", output.Project);
            Assert.DoesNotContain("PackageReference", output.Project); // zero packages — pure BCL
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: build FAILS — "The name 'ComponentRenderer' does not exist".

- [ ] **Step 3: Implement the renderer**

The renderer's structure — `Render(SwaggerDoc doc, string apiName)` returns `(string FileNameBase, string Source, string Project)`, where class name = `MethodNameMapper.Pascalize(apiName) + "RestUtils"`, `Source` = StringBuilder over the template below with `<apiName>`/endpoint substitutions, `Project` = the fixed csproj template at the end of this task.

*Generated-file template — the contract. One public endpoint method per `SwaggerOperation` generated mechanically from the records; the private core sections are emitted verbatim once per file:*

```csharp
// <auto-generated>Generated by RestCodeGenerator from Petstore swagger 1.0.0. Do not edit.</auto-generated>
// Skipped operations (non-JSON bodies are not supported):
//   POST /pet/{petId}/uploadImage
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace PetStoreRestAutomation
{
    /// <summary>
    /// Generated REST client component for "Petstore" (Petstore 1.0.0).
    /// One method per operation in the source swagger file. Response bodies are
    /// returned as raw JSON strings; parse them with Robot Studio's built-in JSON
    /// methods. Every public method follows the Never-Throws Standard: success
    /// returns true with message null; failure returns false with a reason.
    /// Pass "" for any optional string parameter to omit it.
    /// </summary>
    [System.ComponentModel.Description("Generated REST client for Petstore (Petstore 1.0.0). Never throws.")]
    public class PetStoreRestUtils
    {
        private string _baseUrl = "";     // prefilled by the generator when the swagger has a concrete base URL
        private string _authorizationHeader = "";
        private string _apiKeyHeaderName = "";      // emitted only for apiKey-in-header schemes
        private string _apiKeyHeaderValue = "";
        private string _apiKeyQueryName = "";       // emitted only for apiKey-in-query schemes
        private string _apiKeyQueryValue = "";
        private string _oauthClientId = "";         // emitted only for oauth2 client_credentials schemes
        private string _oauthClientSecret = "";
        private string _oauthTokenUrl = "";
        private string _oauthAccessToken = "";
        private System.DateTime _oauthTokenExpiresAtUtc = System.DateTime.MinValue;
        private int _lastStatusCode;
        private int _timeoutSeconds = 30;
        private static readonly HttpClient _client = new HttpClient();

        // ---- Always-present helpers (never throws) ----

        /// <summary>Sets the base URL used for every request. Required when BaseUrl was templated in the source spec. Never throws.</summary>
        public bool SetBaseUrl(string url, out string message)
        {
            if (string.IsNullOrWhiteSpace(url)) { message = "SetBaseUrl: url must be a non-empty string."; return false; }
            _baseUrl = url.TrimEnd('/');
            message = null; return true;
        }

        /// <summary>Sets the Authorization header to "Bearer &lt;token&gt;". Never throws.</summary>
        public bool SetBearerAuthentication(string token, out string message)
        {
            if (string.IsNullOrWhiteSpace(token)) { message = "SetBearerAuthentication: token must be non-empty."; return false; }
            _authorizationHeader = "Bearer " + token.Trim();
            message = null; return true;
        }

        /// <summary>Sets the Authorization header to HTTP Basic. Never throws.</summary>
        public bool SetBasicAuthentication(string username, string password, out string message)
        {
            try
            {
                _authorizationHeader = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes((username ?? "") + ":" + (password ?? "")));
                message = null; return true;
            }
            catch (Exception ex)
            {
                _authorizationHeader = "";
                message = "SetBasicAuthentication: " + ex.Message; return false;
            }
        }

        /// <summary>Sets the Authorization header to an explicit value (e.g. an API key that arrives as the Authorization value). Never throws.</summary>
        public bool SetCustomAuthentication(string headerValue, out string message)
        {
            if (string.IsNullOrWhiteSpace(headerValue)) { message = "SetCustomAuthentication: headerValue must be non-empty."; return false; }
            _authorizationHeader = headerValue.Trim();
            message = null; return true;
        }

        /// <summary>Sets an API key sent as a named header on every call. Never throws.</summary>
        public bool SetApiKeyAuthentication(string name, string value, out string message)
        {
            if (string.IsNullOrWhiteSpace(name)) { message = "SetApiKeyAuthentication: name must be non-empty."; return false; }
            _apiKeyHeaderName = name.Trim();
            _apiKeyHeaderValue = value ?? "";
            message = null; return true;
        }

        /// <summary>Sets an API key appended to the query string of every call. Never throws.</summary>
        public bool SetApiKeyQueryAuthentication(string name, string value, out string message)
        {
            if (string.IsNullOrWhiteSpace(name)) { message = "SetApiKeyQueryAuthentication: name must be non-empty."; return false; }
            _apiKeyQueryName = name.Trim();
            _apiKeyQueryValue = value ?? "";
            message = null; return true;
        }

        /// <summary>Configures OAuth2 client-credentials authentication. The access token is fetched on the
        /// next call, cached, and refreshed automatically when expired or after a single 401-then-retry. Never throws.</summary>
        public bool SetOAuth2ClientCredentials(string clientId, string clientSecret, string tokenUrl, out string message)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(tokenUrl))
            { message = "SetOAuth2ClientCredentials: clientId, clientSecret, and tokenUrl must all be non-empty."; return false; }
            _oauthClientId = clientId.Trim();
            _oauthClientSecret = clientSecret;
            _oauthTokenUrl = tokenUrl.Trim();
            _oauthAccessToken = "";
            _oauthTokenExpiresAtUtc = System.DateTime.MinValue;
            message = null; return true;
        }

        /// <summary>Clears all configured authentication (Authorization header, API keys, cached OAuth2 token — the client-credentials configuration itself is kept). Never throws.</summary>
        public bool ClearAuthentication(out string message)
        {
            _authorizationHeader = "";
            _apiKeyHeaderName = ""; _apiKeyHeaderValue = "";
            _apiKeyQueryName = ""; _apiKeyQueryValue = "";
            _oauthAccessToken = "";
            message = null; return true;
        }

        /// <summary>Sets the request timeout in seconds (1–600). Never throws.</summary>
        public bool SetTimeoutSeconds(int seconds, out string message)
        {
            if (seconds < 1 || seconds > 600) { message = "SetTimeoutSeconds: seconds must be between 1 and 600."; return false; }
            _timeoutSeconds = seconds;
            message = null; return true;
        }

        /// <summary>The HTTP status code of the most recent endpoint call; 0 before the first call. Never throws.</summary>
        public int LastStatusCode
        {
            get { return _lastStatusCode; }
        }

        // ---- Endpoint methods (one per usable swagger operation) ----

        /// <summary>Returns a pet by ID. Returns true if the HTTP call completed; check statusCode for 4xx/5xx. Never throws.</summary>
        [System.ComponentModel.Description("Returns a pet by ID. (GET /pet/{petId})")]
        public bool GetPetById(string petId, string status, string apiKey, out string responseJson, out int statusCode, out string message)
        {
            var path = "/pet/{petId}".Replace("{petId}", Uri.EscapeDataString(petId ?? ""));
            var query = BuildQuery()
                .Add("status", status)
                .Add("apiKey", apiKey)
                .ToString();
            return Send("GET", path, query, null, NewHeaders(), out responseJson, out statusCode, out message);
        }

        /// <summary>Deletes a pet. Returns true if the HTTP call completed; check statusCode for 4xx/5xx. Never throws.</summary>
        [System.ComponentModel.Description("Deletes a pet. (DELETE /pet/{petId})")]
        public bool DeletePetPetId(string petId, string apiKey2, out string responseJson, out int statusCode, out string message)
        {
            var path = "/pet/{petId}".Replace("{petId}", Uri.EscapeDataString(petId ?? ""));
            var query = BuildQuery().Add("apiKey2", apiKey2).ToString();
            return Send("DELETE", path, query, null, NewHeaders(), out responseJson, out statusCode, out message);
        }

        /// <summary>Updates a pet with form data. Returns true if the HTTP call completed; check statusCode for 4xx/5xx. Never throws.</summary>
        [System.ComponentModel.Description("Updates a pet with form data. (POST /pet/{petId})")]
        public bool PostPetPetId(string petId, string bodyJson, out string responseJson, out int statusCode, out string message)
        {
            var path = "/pet/{petId}".Replace("{petId}", Uri.EscapeDataString(petId ?? ""));
            return Send("POST", path, BuildQuery().ToString(), bodyJson, NewHeaders(), out responseJson, out statusCode, out message);
        }

        // ... same mechanical shape for every other operation; header params get one
        //     .WithHeader(name) chain call each per non-empty string, e.g.:
        // return Send("GET", path, "", null, NewHeaders().WithHeader("X-Request-Source", xRequestSource), out responseJson, out statusCode, out message);

        // ---- Private HTTP core (emitted once per file, never throws) ----

        private struct HeaderBuilder
        {
            private List<KeyValuePair<string, string>> _headers;
            public HeaderBuilder WithHeader(string name, string value)
            {
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                    _headers.Add(new KeyValuePair<string, string>(name, value));
                return this;
            }
        }

        private HeaderBuilder NewHeaders() => new HeaderBuilder { _headers = new List<KeyValuePair<string, string>>() };

        private class QueryBuilder
        {
            private readonly List<string> _parts = new List<string>();
            public QueryBuilder Add(string name, string value)
            {
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                    _parts.Add(name + "=" + Uri.EscapeDataString(value));
                return this;
            }
            public string ToString() => _parts.Count == 0 ? "" : "?" + string.Join("&", _parts);
        }

        private static QueryBuilder BuildQuery() { return new QueryBuilder(); }

        private bool Send(string httpMethod, string path, string query, string bodyJson,
            HeaderBuilder headers, out string responseJson, out int statusCode, out string message)
        {
            responseJson = "";
            statusCode = 0;
            if (string.IsNullOrEmpty(_baseUrl))
            {
                message = "Base URL is not set. Call SetBaseUrl first.";
                return false;
            }
            if (!string.IsNullOrEmpty(_apiKeyQueryName))   // API-key-in-query auth is appended to every call
            {
                var keyValue = _apiKeyQueryName + "=" + Uri.EscapeDataString(_apiKeyQueryValue ?? "");
                query = string.IsNullOrEmpty(query) ? "?" + keyValue : query + "&" + keyValue;
            }
            var uri = _baseUrl + path + (query ?? "");
            try
            {
                var ok = Execute(httpMethod, uri, bodyJson, headers, out responseJson, out statusCode, out message);
                if (ok && statusCode == 401 && _oauthTokenUrl != "" && TryRefreshOAuth2Token(out var refreshMessage))
                {
                    // one silent token refresh + single retry on 401 with client-credentials auth configured
                    ok = Execute(httpMethod, uri, bodyJson, headers, out responseJson, out statusCode, out _);
                    if (!ok) message = refreshMessage;
                }
                return ok;
            }
            catch (Exception ex)
            {
                responseJson = "";
                statusCode = 0;
                message = httpMethod + " " + uri + " failed: " + ex.Message;
                return false;
            }
        }

        private bool Execute(string httpMethod, string uri, string bodyJson, HeaderBuilder headers,
            out string responseJson, out int statusCode, out string message)
        {
            responseJson = "";
            statusCode = 0;
            try
            {
                if (_oauthTokenExpiresAtUtc <= System.DateTime.UtcNow && !TryRefreshOAuth2Token(out var oauthError))
                {
                    // only a hard failure when client-credentials auth is configured but the token endpoint fails
                    if (_oauthTokenUrl != "") { message = "OAuth2 token refresh failed: " + oauthError; return false; }
                }
                using var request = new HttpRequestMessage(new HttpMethod(httpMethod), uri);
                if (!string.IsNullOrEmpty(_apiKeyHeaderName) && !string.IsNullOrEmpty(_apiKeyHeaderValue))
                    request.Headers.TryAddWithoutValidation(_apiKeyHeaderName, _apiKeyHeaderValue);
                if (!string.IsNullOrEmpty(_authorizationHeader))
                    request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authorizationHeader);
                else if (!string.IsNullOrEmpty(_oauthAccessToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _oauthAccessToken);
                foreach (var h in headers._headers)
                    request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                if (bodyJson != null)
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                using var response = _client.SendAsync(request, cts.Token).GetAwaiter().GetResult();
                statusCode = (int)response.StatusCode;
                responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                message = null;
                return true;
            }
            catch (Exception ex)
            {
                message = httpMethod + " " + uri + " failed: " + ex.Message;
                return false;
            }
        }

        private bool TryRefreshOAuth2Token(out string message)
        {
            if (_oauthTokenUrl == "" || _oauthTokenExpiresAtUtc > System.DateTime.UtcNow)
            { message = null; return true; }   // not configured, or cache still fresh
            try
            {
                using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, _oauthTokenUrl);
                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", _oauthClientId),
                    new KeyValuePair<string, string>("client_secret", _oauthClientSecret),
                });
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                using var response = _client.SendAsync(request, cts.Token).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                if ((int)response.StatusCode != 200)
                { message = "token endpoint returned " + (int)response.StatusCode; return false; }
                var token = ExtractJsonPropertyText(body, "access_token");
                if (token == "")
                { message = "token response had no access_token"; return false; }
                _oauthAccessToken = token;
                var expiresInText = ExtractJsonPropertyText(body, "expires_in");
                _ = int.TryParse(expiresInText, out var expiresIn);
                _oauthTokenExpiresAtUtc = System.DateTime.UtcNow.AddSeconds(
                    expiresIn > 0 ? expiresIn - 30 : 300);   // 30s safety margin; 5-minute default if unspecified
                message = null;
                return true;
            }
            catch (Exception ex)
            { message = ex.Message; return false; }
        }

        /// <summary>Minimal dependency-free JSON string-property extractor for the token endpoint response.</summary>
        private static string ExtractJsonPropertyText(string json, string propertyName)
        {
            if (json == null) return "";
            var marker = "\"" + propertyName + "\"";
            var i = json.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return "";
            var colon = json.IndexOf(':', i + marker.Length);
            if (colon < 0) return "";
            var quote = json.IndexOf('"', colon + 1);
            if (quote < 0) return "";
            var sb = new StringBuilder();
            for (var j = quote + 1; j < json.Length; j++)
            {
                var c = json[j];
                if (c == '"') break;
                if (c == '\\' && j + 1 < json.Length) { sb.Append(json[++j]); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
```

*(Renderer work is mechanical from here: one endpoint method per `SwaggerOperation` in swagger order, wiring `PathParams` into `.Replace("{name}", Uri.EscapeDataString(name ?? ""))` chains, `QueryParams` into `BuildQuery().Add(name, value)` chains, `HeaderParams` into `.WithHeader(name, value)` chains, and `bodyJson` only for `HasBody` operations. Set `AuthenticationHeaderValue.Parse` inside the same try as the send, since a malformed auth header value throws there — the golden tests above fix the public surface; everything after `Send`'s first line is private and free to refactor as long as the public signatures match.)*

The fallback `.csproj` template (15 fixed lines — makes the single `.cs` buildable with zero edits for CI verification or non-Script-component consumption):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0-windows;net10.0-windows</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AssemblyName>PetStoreRestAutomation</AssemblyName>
    <RootNamespace>PetStoreRestAutomation</RootNamespace>
    <Platforms>AnyCPU;x64</Platforms>
    <!-- Emit the XML doc file next to the DLL so the generated method docs are consumable -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <!-- Lets `dotnet build` of the generated project work on non-Windows machines too. -->
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
  </PropertyGroup>

</Project>
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj
```
Expected: all PASS (7 parser + 3 mapper + 5 renderer = 15).

- [ ] **Step 5: Commit**

```bash
git add tools
git commit -m "rest-codegen: single-file component renderer with golden tests"
```

---

### Task 5: CLI entry + generated project build verification

**Files:**
- Create: `tools/RestCodeGenerator/Program.cs`

- [ ] **Step 1: Write the CLI**

```csharp
using System;
using System.IO;

namespace RestCodeGenerator;

internal static class Program
{
    // Dev tool, not a Robot Studio component: throwing on bad usage is correct here.
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: RestCodeGenerator <swaggerPath> <apiName> <outputDirectory>");
            return 1;
        }
        var (swaggerPath, apiName, outputDirectory) = (args[0], args[1], args[2]);
        var doc = SwaggerParser.ParseFile(swaggerPath);
        if (doc.Operations.Count == 0)
        {
            Console.Error.WriteLine($"No usable operations found in {swaggerPath}.");
            if (doc.Skipped.Count > 0)
                Console.Error.WriteLine($"(all {doc.Skipped.Count} operations had non-JSON bodies and were skipped)");
            return 1;
        }
        var rendered = ComponentRenderer.Render(doc, apiName);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, rendered.FileNameBase + ".cs"), rendered.Source);
        File.WriteAllText(Path.Combine(outputDirectory, rendered.FileNameBase + ".csproj"), rendered.Project);
        Console.WriteLine($"Generated {rendered.FileNameBase}.cs and {rendered.FileNameBase}.csproj in {outputDirectory}");
        Console.WriteLine("Primary: paste the .cs into a Robot Studio Script component - the per-endpoint");
        Console.WriteLine("  methods are compiled and persisted right there, and the swagger file is not");
        Console.WriteLine("  needed at runtime.");
        Console.WriteLine("Fallback: dotnet build <outputDirectory>/<apiName>RestUtils.csproj, then load");
        Console.WriteLine("  the built DLL into Robot Studio.");
        return 0;
    }
}
```

- [ ] **Step 2: End-to-end build check (the real compile proof)**

```bash
dotnet run --project tools/RestCodeGenerator -- \
  tools/RestCodeGenerator.Tests/TestData/petstore-minimal.json pet-store generated/petstore
dotnet build generated/petstore/PetStoreRestUtils.csproj
```
Expected: generator prints the two emitted paths; `dotnet build` succeeds for **both** `net8.0-windows` and `net10.0-windows`. This is where renderer mistakes surface — the Script component hides compile errors from CI, so this gate is what keeps the generated single file provably valid C#.

- [ ] **Step 3: Ensure `generated/` never lands in git**

Append to `.gitignore`:

```
generated/
```

- [ ] **Step 4: Commit**

```bash
git add tools/RestCodeGenerator/Program.cs .gitignore
git commit -m "rest-codegen: CLI entry point and end-to-end generated-build verification"
```

---

### Task 6: PowerShell entry point

**Files:**
- Create: `scripts/Generate-RestComponent.ps1`

- [ ] **Step 1: Write the script**

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SwaggerPath,

    [Parameter(Mandatory = $true)]
    [string]$ApiName,

    [string]$OutputDirectory = "generated/$ApiName"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

dotnet run --project (Join-Path $repositoryRoot "tools/RestCodeGenerator") -- $SwaggerPath $ApiName $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "RestCodeGenerator failed with exit code $LASTEXITCODE." }

Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Paste $OutputDirectory/$($ApiName)RestUtils.cs into a Robot Studio Script component."
Write-Host "     The per-endpoint methods are compiled and persisted there - the swagger file is"
Write-Host "     not needed at runtime."
Write-Host "  (Alternative) dotnet build $OutputDirectory/$($ApiName)RestUtils.csproj and load the"
Write-Host "     built DLL into Robot Studio."
```

- [ ] **Step 2: Verify (pwsh on Linux or PowerShell on Windows)**

```bash
pwsh -NoProfile -File scripts/Generate-RestComponent.ps1 \
  -SwaggerPath tools/RestCodeGenerator.Tests/TestData/petstore-minimal.json -ApiName pet-store
pwsh -NoProfile -File scripts/Generate-RestComponent.ps1 -SwaggerPath does-not-exist.json -ApiName x
```
Expected: first command prints the same generation output as Task 5 Step 2 and the paste-into-Script-component next steps; second command throws with the generator's exit code.

- [ ] **Step 3: Commit**

```bash
git add scripts/Generate-RestComponent.ps1
git commit -m "rest-codegen: Generate-RestComponent.ps1 entry point"
```

---

### Task 6b: Documentation

**Files:**
- Create: `tools/README.md`
- Modify: `README.md` (new section after "Packaging a release", before "Documentation")

- [ ] **Step 1: Write `tools/README.md`**

Coverage:
- What the tool does; the Script-component consumption story ("paste the generated `.cs` into a Script component — Robot Studio compiles it in place, the per-endpoint methods are persisted, and the swagger file is only needed to regenerate") plus the csproj/DLL fallback.
- CLI usage (`dotnet run --project tools/RestCodeGenerator -- <swagger> <apiName> <outDir>` and the ps1 wrapper).
- The endpoint-coverage table from this plan (path/query/header/CSV/body/skip rules) — so users know exactly what shapes work.
- The **auth contract** table: which security schemes generate which helpers (`SetApiKeyAuthentication`, `SetApiKeyQueryAuthentication`, `SetOAuth2ClientCredentials` with cached/auto-refreshed tokens), which are always-present baseline, and which are unsupported with the `SetBearerAuthentication` workaround — plus the note that API-key/client-secret values live only at runtime, entered in robot variables or Pega's CredentialStore, never in the generated file.
- Generated method-shape table (`bool <Method>(..., out string responseJson, out int statusCode, out string message)`) and the always-present helpers table.
- The never-throws + designer-friendly-type rationale; signature-uniqueness suffixing.
- Why responses are raw JSON (Robot Studio's built-in JSON methods (ch11) parse them; the generated file needs no JSON dependency).

- [ ] **Step 2: Update main `README.md`**

New section between "Packaging a release" and "Documentation":

````markdown
## Swagger REST code generation

Rather than hand-writing a REST component per API, the repository includes a
generator that reads any Swagger 2.0 / OpenAPI 3.x file and emits a
ready-to-use Robot Studio component with one never-throw method per
operation — a single self-contained `.cs` file you paste into a Script
component (or compile with the emitted `.csproj`):

```powershell
./scripts/Generate-RestComponent.ps1 -SwaggerPath petstore.json -ApiName pet-store
```

Generated methods take designer-friendly primitive parameters, return raw
JSON the automation parses with Robot Studio's built-in JSON methods, and
need no NuGet packages. See [tools/README.md](tools/README.md).
````

- [ ] **Step 3: Commit**

```bash
git add tools/README.md README.md
git commit -m "rest-codegen: document the swagger component generator"
```

---

### Task 7: PR

- [ ] **Step 1: Full test run before PR**

```bash
dotnet test tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj   # 15/15 expected
dotnet build src/AwesomeRpaUtils.sln                                       # untouched, must stay green
```

- [ ] **Step 2: Worktree + push + PR** (per the repo's convention; do all implementation inside `.worktrees/rest-codegen`, branch `rest-codegen-initial`, created from repo root)

```bash
git push -u origin rest-codegen-initial
gh pr create --title "Add Swagger-driven REST component code generator" \
  --body "🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```

PR body covers: motivation (Pega's RestClient ch32 is generic-URI-shaped only — no per-endpoint methods, no swagger import); the Script-component consumption path (public methods, `out`-parameters, `[Description]` attributes, and `System.Net.Http` all surface correctly there) and the design-time-codegen/persist reasoning (the designer shows compiled public methods; a runtime-reflecting DLL cannot); the endpoint-coverage and auth contracts; the never-throws shape of generated methods; evidence (15/15 generator tests, generated Petstore project builds both TFMs).

- [ ] **Step 3: After PR opens — `git worktree remove .worktrees/rest-codegen`** (check `ls .worktrees/` for bin/obj remnants first, per the worktree-hygiene gotcha).

---

## Verification

1. `dotnet test tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj` — 15/15 green on Linux.
2. Task 5 Step 2 end-to-end: generate from the checked-in Petstore swagger, `dotnet build` the emitted csproj — success for `net8.0-windows` **and** `net10.0-windows` proves the generated single `.cs` compiles standalone with zero packages (the Script component hides compile errors from CI; this gate is the compile proof).
3. Spot-read `generated/petstore/PetStoreRestUtils.cs` against the golden tests: namespace/class names, one method per usable operation with the exact designer shape, skipped-operations comment, never-throw wrappers around every HTTP call.
4. Robot Studio smoke test (Jeff, on Windows): paste the generated Petstore `.cs` into a Script component; confirm every `public bool …` method appears in the automation designer and a live call against `https://petstore.example.com` returns `true` + JSON.
5. `dotnet build src/AwesomeRpaUtils.sln` — confirms the ten shipped components (and packaging surface in `src/bin/`) are untouched by `tools/`.