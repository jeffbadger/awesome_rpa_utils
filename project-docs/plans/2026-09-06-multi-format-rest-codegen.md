# Multi-Format REST Component Code Generation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `tools/RestCodeGenerator` (see
[2026-08-31-swagger-rest-codegen.md](2026-08-31-swagger-rest-codegen.md) for
its original design) so it also imports Postman collections, Bruno
collections, and raw curl commands — normalizing all of them into the same
internal spec model the OpenAPI/Swagger parser already produces, so
`ComponentRenderer` and `MethodNameMapper` handle every source format
identically. Also make Microsoft Entra ID's OAuth2 client-credentials auth
actually work end-to-end (it was documentable but non-functional without a
`scope` request parameter). Treat the existing tool as the proof of concept
being built out, not replaced — no separate/parallel tool, no runtime dynamic
compilation (Robot Studio's Script component already compiles pasted C# into
the project's own dynamic assembly; there is no mechanism for a live
component to publish new typed methods to the designer at runtime), no new
secret-storage mechanism (credential-bearing properties stay plain `string`,
matching this repo's own `ServiceUtils` precedent and Pega's native
`RestClient` component, both of which also take plain-string credentials and
leave sourcing them to the calling automation).

**Architecture:** Three new parsers (`PostmanCollectionParser`,
`BrunoCollectionParser`, `CurlCommandParser`) each produce an `ApiSpec` — the
internal model, renamed from `SwaggerDoc`/`SwaggerOperation`/etc. to
format-neutral names now that it's shared across formats. A new
`ApiSpecParser` dispatches to the right parser, either from an explicit
`--format` flag or by auto-detecting from the input's extension/content
(directory or `.bru` → Bruno; JSON/YAML with `swagger`/`openapi`/`paths` →
OpenAPI; JSON with a top-level `item` array → Postman; text starting with
`curl ` → curl). A new `ExampleSchemaInference` helper infers an `ApiSchema`
from a concrete example JSON value (mirroring `SwaggerParser.ParseSchema`'s
rules), since Postman/Bruno/curl bodies carry examples rather than declared
JSON Schemas. `ComponentRenderer` gains one additive property, `OAuthScope`,
needed for Entra ID's v2.0 token endpoint to actually accept a token request.

**Suite conventions this plan follows** (unchanged from the original plan):
- Never-throws contract in generated output: `bool` + `out string message`.
- No shared library between the generator's parsers beyond `ApiModels.cs`,
  `JsonHelpers.cs`, and `ExampleSchemaInference.cs` — trivial per-parser
  helpers (path-variable rewriting, etc.) are duplicated rather than
  factored into a shared base, matching this repo's "no shared internal
  library between components" norm.
- Deterministic, idempotent generation: same input → byte-identical output.
- xunit tests (Linux-runnable) with golden-content assertions, following
  `SwaggerParserTests.cs`'s existing style.
- New work happens in `.worktrees/multi-format-rest-codegen` on branch
  `multi-format-rest-codegen`, PR'd and the worktree removed afterward.

---

## Task 1: Rename the internal model, extract shared JSON helpers

- [x] Rename `SwaggerModels.cs` → `ApiModels.cs`; rename every type
      (`SwaggerDoc`→`ApiSpec`, `SwaggerOperation`→`ApiOperation`,
      `SwaggerParameter`→`ApiParameter`, `SwaggerSchema`→`ApiSchema`,
      `SwaggerSchemaProperty`→`ApiSchemaProperty`,
      `SwaggerSecurityScheme`→`ApiSecurityScheme`) across `SwaggerParser.cs`,
      `MethodNameMapper.cs`, `ComponentRenderer.cs`, `Program.cs`, and the
      three existing test files. Field shapes unchanged — mechanical rename.
- [x] Extract `SwaggerParser.cs`'s private `GetPropertyOrNull`/
      `GetStringOrNull`/`GetPositiveIntOrNull` into a new shared
      `JsonHelpers.cs` (`internal static class JsonHelpers`), reused by the
      new parsers.
- [x] Regression check: regenerate the checked-in Petstore fixture through
      the OpenAPI path before/after the rename and diff the output — must be
      byte-identical (verified: identical, prior to Task 5's header-wording
      change).
- [x] `dotnet test` — all pre-existing tests stay green (43/43).

## Task 2: Shared example→schema inference

- [x] `ExampleSchemaInference.Infer(JsonElement, int depth = 0) : ApiSchema` —
      object → named properties; array → `Items` from the first element,
      `MaxItems` from the actual element count (≥1); number → integer vs.
      number via `TryGetInt64`; string/bool/null passthrough; depth > 8
      collapses to a string leaf (mirrors `SwaggerParser.ParseSchema`).
- [x] `ExampleSchemaInferenceTests.cs`: object/array/scalar cases, `MaxItems`
      from actual count, integer-vs-number, depth-guard collapse.

## Task 3: Postman collection parser

- [x] `PostmanCollectionParser.ParseFile(path)` / `Parse(JsonElement)`.
      Recursive `item[]` walk → folder-prefixed operation names; `:name` →
      `{name}` path rewrite; `{{variable}}`-templated host → null
      `DefaultBaseUrl`, else the one consistent literal host; only `raw` JSON
      bodies modeled (via `ExampleSchemaInference`), everything else skipped;
      disabled query/header entries excluded; collection- and request-level
      `auth` blocks mapped into deduplicated `ApiSecurityScheme` kinds.
- [x] `PostmanCollectionParserTests.cs` + `TestData/postman-collection.json`:
      folder nesting/naming, templated-host rule, path-var rewrite, disabled
      exclusion, JSON body flattening, non-JSON skip, auth-kind mapping incl.
      `authorization_code` → `unsupported`.

## Task 4: Bruno collection parser

- [x] Hand-rolled block tokenizer (`Tokenize`, brace-matched and
      string-literal-aware — no new package) for `.bru`'s `name { ... }`
      syntax.
- [x] `BrunoCollectionParser.ParsePath(path)` — single file or directory;
      directory input sorted by relative path for deterministic output;
      `folder.bru`/`collection.bru`/`bruno.json` excluded; `~`-prefixed lines
      disabled; `:name` → `{name}`; `body:json` modeled via
      `ExampleSchemaInference`; `auth { mode: inherit }` documented as
      contributing no scheme (folder-tree inheritance out of scope).
- [x] `BrunoCollectionParserTests.cs` + `TestData/bruno-single.bru` and
      `TestData/bruno-collection/`: single-file vs. directory, deterministic
      ordering across repeated parses, path-var rewrite, disabled-line
      exclusion, body inference, `inherit` contributing no scheme,
      `auth:apikey { placement: queryparams }` → `apiKeyQuery`.

## Task 5: curl command parser

- [x] `CurlArgvTokenizer` — minimal shell-argv lexer (quotes, backslash
      escapes, backslash-newline continuations).
- [x] `CurlCommandParser.Parse(text)` / `ParseFile(path)` — single-operation
      spec; `BaseUrl` always prefilled (one concrete origin); no path
      parameters (a concrete URL can't distinguish a variable segment);
      query string → designer-settable query parameters (names only); body
      flags parsed-as-JSON-if-possible via `ExampleSchemaInference`;
      `Authorization` header and `-u`/`--user` dropped entirely (never
      surface anywhere in the parsed spec); no security scheme ever
      inferred.
- [x] `CurlCommandParserTests.cs` + `TestData/curl-example.txt`: no-flags GET,
      POST+JSON-body flattening, repeated headers with Authorization proven
      absent from the whole serialized spec (not just the rendered output),
      `-u` proven absent, query-string extraction, non-JSON body fallback,
      file-input/inline-string parity, method inference from `-d` alone.

## Task 6: `--format` dispatcher and CLI wiring

- [x] `InputFormat` enum (`OpenApi`/`Postman`/`Bruno`/`Curl`).
- [x] `ApiSpecParser.Parse(input, explicitFormat, out usedFormat)` /
      `Detect(input)` — directory/`.bru` → Bruno; JSON/YAML content sniffed
      for OpenAPI vs. Postman shape (reusing `SwaggerParser.LoadDocument`,
      made `internal`); unparsable content or a non-existent path sniffed for
      a leading `curl ` text; otherwise an actionable exception naming
      `--format`.
- [x] `Program.cs`: usage string updated; `--format` flag parsing; replaced
      `SwaggerParser.ParseFile` call with `ApiSpecParser.Parse`; reports the
      detected/used format; existing 3-positional-arg OpenAPI invocations
      keep working unchanged (auto-detect is the default).
- [x] `scripts/Generate-RestComponent.ps1`: `-SwaggerPath` → `-InputPath`
      (with a `-SwaggerPath` alias for backward compatibility), new optional
      `-Format`.
- [x] `ApiSpecParserTests.cs`: detection per format/extension/directory,
      explicit `--format` overriding content-based detection, unparsable
      input throwing actionably.

## Task 7: Entra ID — make OAuth2 client-credentials actually work

- [x] `ComponentRenderer`: new `OAuthScope` property (blank by default,
      additive, same invalidate-cached-token pattern as the other three
      OAuth2 properties); `TryRefreshOAuth2Token`'s token-request form fields
      only add `scope` when non-blank, so existing non-Entra consumers see
      byte-identical wire behavior; `MethodNameMapper.Reserved` gains
      `"OAuthScope"`.
- [x] File-header wording fix: `"... swagger {Version}"` → generic
      `"... v{Version}"` (no longer a true provenance claim for
      Postman/Bruno/curl-sourced output).
- [x] `ComponentRendererTests.cs`: `OAuthScope` emitted when an OAuth2 scheme
      is declared; the `scope` form field is conditional on it being
      non-blank.
- [x] No `TenantId` convenience property — pasting the full `OAuthTokenUrl`
      is trivial; documented as a worked example instead.

## Task 8: Documentation

- [x] `tools/README.md`: multi-format intro/usage/examples; new "Supported
      input formats" section (OpenAPI table kept, new Postman/Bruno/curl
      subsections with their mapping rules and named limitations);
      `OAuthScope` + Entra ID worked example in Authentication; security note
      extended to curl's stripping behavior and restated for every format;
      Regenerating section notes Bruno's directory-sort determinism
      requirement.
- [x] Root `README.md`: "Swagger REST code generation" → "REST component
      code generation" (heading + anchor + all references); CLI snippets
      updated for the new arg name/`--format` flag; fixed a pre-existing
      broken link (`tools/RestCodeGenerator/README.md` → `tools/README.md`)
      while touching that paragraph.
- [x] This plan document.

## Verification

- [x] `dotnet build tools/RestCodeGenerator/RestCodeGenerator.csproj` — clean,
      zero new NuGet dependencies (Postman is plain `System.Text.Json`;
      Bruno/curl are hand-rolled string parsing; YamlDotNet reuse only for
      OpenAPI/format-sniffing).
- [x] `dotnet test tools/RestCodeGenerator.Tests/RestCodeGenerator.Tests.csproj`
      — full suite green (84 tests: 43 pre-existing + 41 new).
- [x] `dotnet build src/AwesomeRpaUtils.sln` — confirm the shipped components
      are unaffected (no project references into `src/`).
- [x] Rename regression: diffed the OpenAPI-path Petstore output before/after
      the `ApiSpec` rename — identical except the one intentional
      header-wording change.
- [ ] Manual smoke tests (Windows, real Robot Studio): generate from a real
      Postman export, a real Bruno folder, and a real "Copy as cURL" — confirm
      correct format detection, successful `dotnet build` of each emitted
      `.csproj`, and (for curl) that no captured header/credential value
      leaked into the output. With a real or throwaway Entra ID app
      registration, confirm a live token request succeeds once `OAuthScope`
      is set.
