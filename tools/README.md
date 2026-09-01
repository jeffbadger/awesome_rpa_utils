# RestCodeGenerator

`RestCodeGenerator` reads a Swagger 2.0 / OpenAPI 3.x specification and emits a
ready-to-use Robot Studio REST component: one never-throw method per operation,
plus a set of runtime helpers, in a single self-contained `.cs` file. The tool
is a development utility — it is not a Robot Studio component and ships in no
release archive.

## Usage

```powershell
dotnet run --project tools/RestCodeGenerator -- <swaggerPath> <apiName> <outputDirectory> [--component]
```

Pass the optional `--component` flag (or the wrapper's `-Component` switch) to
make the generated class derive from `System.ComponentModel.Component`,
shaping the fallback DLL like a Robot Studio component-tray component. It also
switches the HttpClient from the shared static to a per-instance one and emits
the standard Dispose pattern — Robot Studio disposes tray components on
teardown, releasing that instance's HTTP connections. This is only relevant
when you build the DLL fallback; Script-component paste-in does not need it.
The flag is off by default, which leaves the class line plain.

The `<apiName>` becomes the class name: it is PascalCased and suffixed with
`RestUtils`, so `pet-store` produces class `PetStoreRestUtils` in namespace
`PetStoreRestAutomation`. Two files are written to `<outputDirectory>`:

- `<ApiName>RestUtils.cs` — the entire component.
- `<ApiName>RestUtils.csproj` — fallback/CI boilerplate that builds the same source.

Exit code is `1` on wrong argument count or an unrecognized flag, on a spec
that fails to parse, or when the spec contains no usable operations (for
example when every operation had a non-JSON body and was skipped); otherwise
`0`.

The `scripts/Generate-RestComponent.ps1` wrapper runs the same generator and
prints next steps:

```powershell
./scripts/Generate-RestComponent.ps1 -SwaggerPath petstore.json -ApiName pet-store

# same, with the -Component switch (adds --component to the invocation:
# Component-tray-shaped class for the DLL fallback)
./scripts/Generate-RestComponent.ps1 -SwaggerPath petstore.json -ApiName pet-store -Component
```

`-OutputDirectory` defaults to `generated/<apiName>`; everything below
`generated/` is git-ignored.

## Consuming the generated component

**Primary — paste into a Script component.** Paste the generated `.cs` into a
Robot Studio Script component. Robot Studio compiles the file in place, the
per-endpoint methods are persisted right there, and the swagger file is not
needed at runtime — only to regenerate.

**Fallback — build the `.csproj`.** Run `dotnet build <outputDirectory>/<ApiName>RestUtils.csproj`
and load the built DLL into Robot Studio.

Both paths need only the .NET SDK; the emitted project targets
`net8.0-windows` and `net10.0-windows` and references zero NuGet packages.

## Endpoint coverage

| Swagger/OpenAPI feature | Generated behavior |
|---|---|
| Path parameters (`in: path`) | `string` method param; substituted with `Uri.EscapeDataString` |
| Query parameters (`in: query`) | `string` method param appended to the URL; `""` omits the param |
| Array/collection query params | `string` method param, **CSV-delimited** value passed as-is |
| Header parameters (`in: header`) | `string` method param sent with `TryAddWithoutValidation` |
| Swagger 2.0 `in: body` / `in: formData` (JSON only) | single `bodyJson` string param |
| OpenAPI 3 `requestBody` `application/json` | single `bodyJson` string param |
| **Non-JSON bodies** (`multipart`, form-urlencoded, binary) | Operation **skipped**, listed in the auto-generated comment at the top of the file |
| File-level / path-level `$ref` parameters | Resolved to their definition before parameter extraction |
| OAuth2 / API-key security schemes | Parsed into scheme-specific auth helpers — see [Authentication](#authentication) |
| OA3 `servers[].url` with `{variables}` | No default base URL — the first `SetBaseUrl` call is mandatory |
| HEAD / OPTIONS / DELETE (no body) | Same method shape minus `bodyJson` |
| 4xx/5xx responses | Transport success (`true`), visible via the `statusCode` out |
| `operationId` naming, collisions, reserved names | Mapped by `MethodNameMapper` (see below) |

The `$ref` caveat: JSON-Pointer escapes (`~0` for `~`, `~1` for `/`) are **not**
decoded, so `$ref` pointers that rely on them cannot be resolved. Refs using
simple names (e.g. `#/parameters/StatusFilter`) resolve normally.

## Authentication

Helpers are emitted from the spec's declared `securityDefinitions` (Swagger
2.0) or `components.securitySchemes` (OpenAPI 3.x). The always-present baseline
also covers specs that declare nothing.

**Always present, in every generated component (all never-throw, `bool` +
`out string message`):**

| Helper | Purpose |
|---|---|
| `SetBaseUrl(url)` | Required when the spec's base URL was templated; the concrete base URL from the spec is prefilled |
| `SetBearerAuthentication(token)` | Raw bearer token on the `Authorization` header |
| `SetBasicAuthentication(username, password)` | HTTP Basic |
| `SetCustomAuthentication(headerValue)` | Any other scheme — sets the raw `Authorization` value |
| `ClearAuthentication()` | Drops every configured credential |
| `SetTimeoutSeconds(seconds)` | Request timeout; rejected outside 1–600 (default 30) |
| `LastStatusCode` | Designer-readable `int` property; `0` before any call |

**Scheme-conditional — emitted only when the spec declares a matching scheme:**

| Declared scheme | Generated helper |
|---|---|
| `http basic` / Swagger 2.0 `basic` | `SetBasicAuthentication(username, password)` |
| `http bearer` | `SetBearerAuthentication(token)` |
| `apiKey` `in: header` | `SetApiKeyAuthentication(name, value)` — sent as that named header on every call |
| `apiKey` `in: query` | `SetApiKeyQueryAuthentication(name, value)` — appended to every call's query string |
| `oauth2` with `client_credentials` flow | `SetOAuth2ClientCredentials(clientId, clientSecret, tokenUrl)` |

`SetOAuth2ClientCredentials` form-POSTs to the token endpoint and caches the
access token, refreshing it automatically before expiry (30-second safety
margin; a 5-minute token lifetime is assumed when the spec specifies no
`expires_in`) and retrying a call once, silently, after a 401.

**Worked example.** Declaring this in the spec:

```json
"securitySchemes": {
  "api_key": { "type": "apiKey", "in": "header", "name": "X-API-Key" }
}
```

generates the `SetApiKeyAuthentication` helper, called with the header name
from the spec and the key value supplied at runtime:

```csharp
ok = petStoreRestUtils.SetApiKeyAuthentication("X-API-Key", apiKeyValue, out message);
```

**Unsupported — no helper generated**, listed as unsupported in the generated
header comment: `oauth2` implicit / authorization-code / password flows
(browser-redirect flows don't fit unattended robots), `digest`, AWS SigV4,
mTLS. Use `SetBearerAuthentication` with a caller-managed token, or
`SetCustomAuthentication` with a hand-built `Authorization` value.

**Security note:** API keys, client IDs/secrets, and bearer tokens live only at
runtime — enter them in robot variables or Pega's CredentialStore. Nothing is
ever hard-coded into the generated file.

## Generated method shape

One method per operation, with a fixed shape per HTTP-method group:

| HTTP method | Signature |
|---|---|
| GET / HEAD / OPTIONS / DELETE | `bool <Method>(string <pathParams>, string <queryParams>, string <headerParams>, out string responseJson, out int statusCode, out string message)` |
| POST / PUT / PATCH | Same, plus `string bodyJson` before the `out` parameters |

- Every parameter is a plain `string` — designer-friendly primitives, no exotic
  types, each method carries a `[Description]` so it is self-explanatory on the
  Robot Studio designer. Callers convert numbers with Robot Studio's built-in
  methods. `""` for query/header params omits them.
- Returns `true` when the HTTP call completed and returned *any* status
  (including 4xx/5xx). Transport failure returns `false`, sets `message` to the
  cause, and leaves `responseJson` as `""`.
- `responseJson` is the raw response body; `statusCode` is the HTTP status.
- Method names come from `operationId` (or verb + path when absent) and are
  PascalCased. Collisions are resolved by numeric suffix — `Name2`, `Name3` —
  so the file never contains two same-shaped overloads, which would be
  unselectable on the designer. Helper names (`SetBaseUrl`, `HeaderBuilder`,
  `QueryBuilder`, and the rest) are reserved and never collide.

## API metadata

The spec's `info` object (same shape in Swagger 2.0 and OpenAPI 3.x) feeds the
generated class's metadata: the `description`, `termsOfService`, `contact`
(`name`, `email`, `url`), and `license` (`name`, `url`) fields are extracted
alongside the title and version. They land in two places on the generated class:

- **Class XML docs** — the `<summary>` carries the title, version, and the
  description (multi-line descriptions flattened to one logical line, wrapped
  into `///` lines when long); a `<remarks>` block carries the
  terms-of-service, contact, and license lines, each omitted entirely when
  absent from the spec. Shown in IntelliSense when you paste the file into a
  Script component with an XML doc file next to the DLL.
- **Class-level `[System.ComponentModel.Description]`** —
  `"<Title> (version <Version>) - <first sentence of the description>"`, so the
  component is self-explanatory on the Robot Studio designer.

## Why raw JSON responses

Methods return the raw response body rather than parsed objects so the
automation parses responses with Robot Studio's built-in JSON methods (chapter
11 of the Robot Studio documentation). That keeps the generated file free of
any JSON library dependency — which in turn keeps the primary consumption path
(a Script component compiling a single `.cs` in place) dependency-free.

## Regenerating

Rerun the same command with the same swagger file and `<apiName>` and the
output is regenerated byte-identically — the generator is deterministic. Point
`-OutputDirectory` at the same folder to overwrite, and keep the swagger file
checked in (or otherwise archived) as the source of truth for the component.