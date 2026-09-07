# PRD: REST API Component Generator for Pega Robot Studio

Status: Draft for product architect review
Date: September 6, 2026
Project: awesome_rpa_utils

See [`EXECUTIVE_BRIEF.md`](EXECUTIVE_BRIEF.md) for the one-page leadership summary, [`REST_CONNECTOR_ROADMAP.md`](REST_CONNECTOR_ROADMAP.md) for the phased delivery plan that scopes this PRD into releasable increments, and [`PUSHBACK_BRIEF.md`](PUSHBACK_BRIEF.md) for that roadmap's review prep notes.

## Table of contents

- [1. Product summary](#1-product-summary)
- [2. Goals](#2-goals)
- [3. Intended users](#3-intended-users)
- [4. User workflow](#4-user-workflow)
  - [User experience and guided configuration](#user-experience-and-guided-configuration)
- [5. Supported inputs](#5-supported-inputs)
  - [Manual endpoint wizard](#manual-endpoint-wizard)
- [6. Architecture](#6-architecture)
- [7. Designer properties and actions](#7-designer-properties-and-actions)
- [8. Generation lifecycle](#8-generation-lifecycle)
- [9. Generated methods and responses](#9-generated-methods-and-responses)
  - [Response contract](#response-contract)
  - [JSON path syntax](#json-path-syntax)
  - [Response helper methods](#response-helper-methods)
  - [Conversion and error behavior](#conversion-and-error-behavior)
  - [Headers, text, and files](#headers-text-and-files)
  - [Response helper example](#response-helper-example)
- [10. Authentication](#10-authentication)
  - [Certificate-based authentication](#certificate-based-authentication)
- [11. Request correctness and flexibility](#11-request-correctness-and-flexibility)
- [12. Regeneration and portability](#12-regeneration-and-portability)
- [13. Acceptance criteria](#13-acceptance-criteria)
- [14. Product architect questions](#14-product-architect-questions)
- [15. Delivery approach](#15-delivery-approach)
- [16. Out of scope](#16-out-of-scope)
- [17. Review status](#17-review-status)

## 1. Product summary

Create a Robot Studio utility that imports REST API definitions or saved requests, or guides users through manually defining endpoints, and generates a C# component with callable methods for selected endpoints.

Each component instance targets one base URL at a time. Users add separate components for APIs requiring different base URLs. Environment selection can replace an instance's base URL as a whole; endpoint-specific base URLs are outside scope.

Robot Studio compiles the generated source into a DLL and references it in the project. Compilation can run on designer load or through an explicit designer action.

Users configure connection properties and invoke endpoint methods from Pega automations without writing HTTP or authentication code.

Robot Studio’s ability to compile and reference generated C# is a user-confirmed premise. The precise integration APIs and lifecycle behavior require product architect review.

## 2. Goals

- Make REST endpoints easy to consume from Pega automations.
- Import Swagger/OpenAPI, Postman, Bruno, and cURL.
- Define endpoints through a guided wizard without an existing definition file or saved request.
- Generate correctly configured, discoverable endpoint methods.
- Support OAuth2, Basic, API key, bearer token, Microsoft Entra ID, and client-certificate authentication (mTLS).
- Allow design-time and runtime connection configuration.
- Preserve existing automation compatibility during regeneration.
- Deploy compiled components without requiring original import files or runtime compilation.

## 3. Intended users

Automation developers need a straightforward import-and-configure workflow with friendly method names and properties.

Advanced developers need parameter mappings, raw payload support, authentication overrides, and control over generated signatures.

Maintainers need reproducible generation, actionable diagnostics, and predictable updates.

## 4. User workflow

1. Add the REST API designer utility to a Robot Studio project.
2. Select Add API… and choose Import, Paste, or Define manually, or populate its input properties.
3. Browse for a file or collection folder, paste content, or enter endpoint details in the wizard.
4. Review imported or manually defined endpoints, parameters, authentication, and validation issues.
5. Select endpoints and optionally rename methods or adjust parameter mappings.
6. Configure the base URL, environment, and authentication.
7. Select Generate / Refresh Component.
8. Use generated methods in automations.

Simple inputs should require minimal configuration. Advanced settings should remain available through expandable groups and editors.

Importing, defining endpoints, or generating must not automatically send endpoint requests.

### User experience and guided configuration

The default experience must guide a Pega developer from an API request to usable automation inputs and outputs without handwritten C# or JSON paths. Advanced configuration and raw-response helpers remain available when needed.

| Area | Required user experience |
| --- | --- |
| Starting point | One Add API… action with Import, Paste, and Define manually choices. Existing Import API… and Define Endpoints… actions are shortcuts into the same flow. |
| Essential fields first | Initially show the component name, single base URL, endpoint method/path, authentication, and required inputs. Put serialization details, optional headers, certificate-store details, and other specialized options under Advanced. |
| Input mapping | Suggest friendly names and types for method inputs. Let users choose fixed values, environment bindings, or automation inputs; distinguish defaults from sample values. |
| Method preview | Show the proposed method and its automation inputs/outputs before generation, including required values and descriptions. Match the actual Pega presentation after architect validation. |
| Authentication guidance | Ask whether execution is attended or unattended, explain compatible choices, and guide credential/certificate binding. Report incompatible choices before deployment. |
| Environment profiles | Provide named profiles such as Development, Test, and Production. Each profile supplies one base URL and its connection bindings. Show the effective URL and masked credential references before applying it. |
| Troubleshooting | Present errors against the affected field, endpoint, or binding with a corrective action. Distinguish import, compilation, HTTP, token, certificate-access, and response-mapping failures. |
| Update review | Preview added/removed methods, changed inputs/outputs, and affected existing automation usages where the host supports discovery. If usages cannot be discovered, explicitly state that limitation rather than claiming an update is safe. |

**Visual response mapping** is required product scope:

1. Paste a sample response or explicitly run Test Request… using supplied test inputs. Preview the effective URL and HTTP method before sending; clearly identify state-changing requests. Testing is optional for component generation.
2. Browse JSON in a field tree showing objects, arrays, scalar types, nulls, and example values. Allow raw text/file output selection for non-JSON responses.
3. Select a field and choose **Expose as output**. Suggest a friendly output name and type, with an editable advanced path generated automatically.
4. Choose missing/null behavior explicitly: report a mapping failure, expose an absent/null state through a host-compatible output, or use a user-specified compatible default. Conversion failures remain errors and must not silently take a missing-value default.
5. For arrays, offer collection/item mapping and a loop-oriented preview. Clearly distinguish selecting one example index from mapping every array item; one sample must not imply a fixed array length.
6. Preview the generated automation outputs, save the mapping, and generate the component. Keep the complete RestResponse available for diagnostics and advanced helpers.

Mappings are persisted with stable names, paths, types, and missing/null policies. Changed output names/types participate in breaking-change review and generation fingerprints. Label sample-inferred types as inferred and let users correct them. Do not require a sample, live endpoint, or typed mapping to generate a raw-response method. Sanitize retained samples and previews using the existing secret-handling rules.

Mapping failures must be distinct from HTTP success: preserve the raw response and report the failed output/path through a host-compatible mapping result or completion/error mechanism. The exact Pega output, collection, and nullable-value representation requires architect validation; visual mapping and no-code consumption are product requirements regardless of that representation.

Usability acceptance must include representative Pega developers independently completing these tasks without handwritten C# or assistance:

- Define a simple endpoint, bind an input, generate it, and use it in an automation.
- Import an API, select an endpoint, and expose a nested response field without typing a JSON path.
- Iterate a mapped response array and handle a missing optional value explicitly.
- Configure unattended authentication and diagnose an inaccessible certificate binding from the displayed guidance.
- Switch environment profiles without compilation and identify the effective base URL before making a request.
- Review a signature/output change and understand its effect on existing automations.

Record completion, errors, requests for help, and time taken. Agree quantitative usability targets before release. Guided configuration, visual mapping, explicit request testing, and actionable diagnostics are required release scope; they are not optional polish.

## 5. Supported inputs

| Input | Required behavior |
| --- | --- |
| Swagger 2.0 | Import JSON/YAML definitions and generate selected operations. |
| OpenAPI 3.x | Import JSON/YAML definitions, parameters, security requirements, and supported schemas. |
| Postman | Import collections, supported inherited settings, variables, and optional environment files. |
| Bruno | Import collection folders and individual requests, including supported .bru and YAML formats. |
| cURL | Parse pasted commands or text files into callable requests without executing shell content. |
| Manual endpoint wizard | Guide users through defining one or more endpoints without an import file, collection, or cURL command. |

Publish an explicit compatibility matrix covering format versions, schema features, and supported cURL syntax.

OpenAPI schemas can provide declared types. Collections and cURL frequently provide only examples. The utility must label inferred types and offer raw JSON/text input when reliable typing
is unavailable.

Missing references, unresolved variables, unsupported options, and script-dependent behavior must produce actionable import diagnostics.

### Manual endpoint wizard

The **Define Endpoints…** action provides a first-class input option using labeled fields, examples, sensible defaults, and inline validation. Users can create one endpoint or build a reusable group of endpoints in the same component.

1. **Connection:** enter the component name and single base URL. Accept a full endpoint URL and help separate the server/base path from the endpoint path, with an editable preview. Additional endpoints must resolve under that base URL; direct users to another component when a different base URL is needed.
2. **Endpoint:** enter a friendly method name, description, HTTP method, and relative path. Detect placeholders such as `{customerId}` and offer matching required path parameters.
3. **Parameters and headers:** add path, query, header, and cookie values. For each value, choose a fixed value, environment/variable binding, or generated method parameter; configure its name, type, required status, default, and description. Expose array serialization and other advanced settings when needed.
4. **Request body:** choose no body, JSON, text, URL-encoded form, multipart, or binary/file. Allow raw content, an optional example, and editable fields or runtime bindings. Make structured/typed generation optional; users must be able to proceed with a raw body.
5. **Authentication:** inherit the component configuration or choose an endpoint override using any supported authentication mode. Bind secrets through the same credential mechanism as imported endpoints.
6. **Response:** choose raw text/JSON or file output. Optionally supply a schema or sample response to assist model and extraction configuration. Label sample-derived types as inferred and allow correction; no sample or schema is required.
7. **Review:** show the resolved request structure with placeholders for runtime values, masked credentials, and the proposed C# method signature. Allow users to return to earlier steps, add another endpoint, save the definition, or generate the component.

Wizard requirements:

- Support adding, editing, duplicating, and removing manually defined endpoints, and reopening their saved configuration after project reload.
- Validate URLs, method names, duplicate names, path placeholder bindings, required configuration, body syntax where applicable, and conflicting parameter/header definitions. Show field-level corrections before generation.
- Distinguish values required at generation time from those intentionally supplied at runtime; runtime inputs must not require credentials or live endpoint access during generation.
- Feed manually defined endpoints into the same normalized model, compilation pipeline, authentication runtime, and regeneration checks as imported endpoints.
- Allow manual endpoints alongside imported endpoints. Preserve their origin and stable identity so reimporting a collection does not overwrite or remove manually authored operations. Resolve naming collisions explicitly.
- Save wizard changes as definition changes. Saving alone does not compile; generation follows the configured load mode or explicit Generate / Refresh Component action.
- Keep request testing optional and separate. Completing the wizard must work without contacting the endpoint.

## 6. Architecture

The utility has three main parts:

1. **Designer utility:** imports definitions or guides manual endpoint entry, lets users review endpoints and configure generation, and asks Robot Studio to compile the generated C# source and reference the DLL. Generation runs on designer load according to the selected mode or through an explicit designer action.
2. **Generated API component:** exposes named endpoint methods and instance-specific connection properties, including base URL and authentication settings, for use in Pega automations. Each method delegates request execution to the shared REST runtime.
3. **Shared REST runtime:** executes HTTP requests and handles authentication, token management, parameter encoding, serialization, responses, and errors consistently across generated components.

At design time, the designer utility produces the generated API component. At execution time, an automation calls that component's methods, which use the shared REST runtime to call the endpoints. Deployed robots use the packaged component and runtime without requiring the designer utility or compilation.

The table below details these three parts and their supporting internals. Import adapters, the normalized API model, the C# generator, and the Pega compilation adapter support the designer utility's generation workflow.

| Component | Responsibility |
| --- | --- |
| Designer utility | Input editors, properties, previews, generation actions, and lifecycle integration |
| Import adapters | Parse supported formats and identify compatibility issues |
| Manual endpoint wizard | Collect and validate user-supplied endpoint details and produce the normalized API model |
| Normalized API model | Represent operations, parameters, bodies, responses, variables, and authentication consistently |
| C# generator | Generate stable component types, endpoint methods, and optional data models |
| Pega compilation adapter | Compile source, validate output, and manage project references |
| Generated API component | Expose endpoint methods and instance-specific connection properties |
| Shared REST runtime | Handle authentication, serialization, HTTP execution, responses, and diagnostics |

Generated methods delegate execution to the shared runtime. Imported content must be handled as data, never inserted as executable source fragments.

## 7. Designer properties and actions

| Property | Purpose |
| --- | --- |
| DefinitionSource | Select file/folder, pasted content, or manual endpoint wizard |
| DefinitionPath | Locate the input |
| DefinitionContent | Multiline content editor |
| InputFormat | Auto-detect or explicitly select format |
| ComponentName | Name the generated component |
| GenerationMode | Control automatic/manual generation |
| MethodStyle | Select simple or typed methods, subject to Pega support |
| Endpoints | Collection editor for reviewing operations and opening the wizard to add or edit manually defined endpoints |

Generated component instances expose:

| Property | Purpose |
| --- | --- |
| BaseUrl | One design-time endpoint base per instance; environment/runtime changes replace it for the whole instance, with no per-endpoint or per-call base URL override |
| Authentication | Expandable authentication configuration |
| ClientCertificate | Expandable certificate selection and binding for HTTPS client authentication, independently configurable alongside application authentication |
| Environment | Select variable/connection settings |
| TimeoutSeconds | Configure request timeout |
| DefaultHeaders | Configure shared request headers |
| ErrorBehavior | Return failed results or throw exceptions |

Designer actions:

- Add API… — unified entry point for Import, Paste, or Define manually
- Import API…
- Define Endpoints…
- Preview Methods…
- Generate / Refresh Component
- View Generation Results
- Map Response… — visually configure automation outputs from an optional schema/sample
- Test Request… — required capability, invoked only by explicit user action; optional for completing generation

Changing connection properties must not require compilation.

## 8. Generation lifecycle

| Mode | Behavior |
| --- | --- |
| OnLoadIfChanged | Recommended default: generate on designer/project load when inputs changed or the artifact is missing |
| Manual | Generate through the designer action only |
| AlwaysOnLoad | Regenerate on every designer/project load |

Requirements:

- Determine freshness from the definition, mappings, generation options, generator version, and target/runtime compatibility.
- Avoid compiling on every property keystroke.
- Prevent concurrent generation of the same component.
- Compile and validate a replacement before switching references.
- Retain the last working component when compilation or reference activation fails.
- Show stale, missing, generating, successful, and failed states.
- Require review before applying breaking method changes during automatic load generation.
- Surface any host-required reload or restart.
- Package compiled artifacts for deployed robots; runtime compilation is outside the default behavior.

## 9. Generated methods and responses

Generate named methods for selected operations, with readable names and parameter descriptions.

Support:

- Scalar parameters and raw JSON/text bodies.
- Typed request/response models where schemas and Robot Studio support them.
- Required and optional parameters.
- Stable naming when operation identifiers are missing or duplicated.
- File uploads and downloads.
- A generic execution capability for dynamic parameter sets.
- Host-compatible timeout, cancellation, and completion behavior.

Illustrative signatures, pending Pega validation:

```csharp
public RestResponse GetCustomer(string customerId);
public RestResponse CreateCustomer(string requestBodyJson);
```

The public asynchronous pattern remains an architect decision; direct Task<T> usability in the designer is not assumed. The following helper signatures define the intended C# contract. Product architect review must confirm their discoverability in Pega and any required wrapper types while preserving the defined behavior.

### Response contract

Each invocation returns its own response object. Helpers must not depend on a component-wide last response, and reading one response must not change another response or its error state.

| Property | Type | Definition |
| --- | --- | --- |
| HasHttpResponse | bool | True when HTTP response headers were received, even if reading the body later failed |
| IsSuccess | bool | True only for a 200–299 response whose body was received successfully; does not interpret business-specific fields in JSON |
| StatusCode | int | HTTP status code; 0 if no HTTP response was received |
| ContentType | string | Full Content-Type value, including parameters, or empty when absent |
| Headers | host-compatible read-only collection | Response and content headers preserving each individual value; convenience methods are defined below |
| HasBody | bool | True when at least one body byte is available |
| IsBodyComplete | bool | True when the entire response body was received, including a valid zero-length body; false on interrupted reads or when no response was received |
| BodyLength | long | Number of available body bytes, independent of whether the body is text or file-backed |
| BodyText | string | Body decoded by the text rules below; empty for a complete zero-length body; reports a specific read/decode error when unavailable or unsupported |
| IsJson | bool | True when the complete, available body parses as one valid JSON value; false for empty, incomplete, unavailable, or invalid JSON bodies, regardless of Content-Type |
| ErrorKind | string | None, Http, Authentication, Transport, Timeout, Cancelled, BodyRead, or Serialization; describes the request outcome, not a later helper failure |
| ErrorMessage | string | Sanitized request failure description, or empty on success |

An HTTP error response retains its status, headers, and available body. Helpers can inspect a complete error body just as they inspect a successful response. For a non-2xx response with a failed body read, ErrorKind is BodyRead and StatusCode still records the HTTP error. Typed-model deserialization failure is Serialization; preserve raw body access. A raw-response call does not become a serialization failure merely because JSON parsing is unsuccessful.

### JSON path syntax

Use a documented, single-value path syntax shared by every JSON helper:

- `$` selects the root value. An empty path is invalid.
- `$.customer.name` selects object properties. Dot names use `[A-Za-z_][A-Za-z0-9_]*`.
- `$["customer"]["display.name"]` uses JSON double-quoted property names with standard JSON string escaping; it supports spaces, punctuation, Unicode, and empty property names.
- `$.items[0].id` selects a zero-based array item; `$[0]` works for a root array.
- Property names are case-sensitive. Negative indexes, wildcards, filters, recursive descent, slices, and expressions are unsupported and report InvalidPath.
- A missing property or out-of-range array index is MissingPath. Applying a property step to a scalar/array, or an index step to a non-array, is TypeMismatch. Traversing through JSON null reports NullValue.
- Duplicate object property names are rejected as InvalidJson so selection never silently chooses an ambiguous value.

The helper implementation must not evaluate expressions or scripts. Examples and designer descriptions must use this exact syntax rather than imply support for arbitrary JSONPath queries.

### Response helper methods

| Method | Return type | Behavior |
| --- | --- | --- |
| GetString(string path) | string | Read a JSON string without coercing numbers, booleans, arrays, or objects |
| GetInt32(string path) | int | Read an integral JSON number within Int32 range |
| GetInt64(string path) | long | Read an integral JSON number within Int64 range |
| GetDecimal(string path) | decimal | Read a JSON number exactly representable as a C# decimal |
| GetDouble(string path) | double | Read a JSON number as a finite double; normal floating-point rounding is allowed |
| GetBoolean(string path) | bool | Read a JSON true/false value |
| GetDateTimeOffset(string path) | DateTimeOffset | Read a JSON string containing a date and time with explicit Z or numeric UTC offset, according to the format below |
| Exists(string path) | bool | True when the addressed node exists, including JSON null; false only for MissingPath |
| IsNull(string path) | bool | True for an explicitly null node; false for any other existing node; MissingPath remains an error |
| GetValueKind(string path) | string | Return Object, Array, String, Number, Boolean, or Null for an existing node |
| GetJson(string path) | string | Return valid compact JSON for the selected node, including quoted strings and literal null; original whitespace is not preserved |
| GetArrayCount(string path) | int | Return array length; zero for an empty array |
| GetArrayItem(string path, int index) | RestJsonValue | Return an array item as a JSON value wrapper; negative index is InvalidArgument and an index beyond the array is MissingPath |
| GetHeader(string name) | string | Return the first stored value for the named response/content header; empty if missing; use HasHeader to distinguish an empty value |
| HasHeader(string name) | bool | Indicate whether the response/content header exists |
| GetHeaderValueCount(string name) | int | Return the number of stored values, or zero when absent |
| GetHeaderValue(string name, int index) | string | Return an individual header value; negative index is InvalidArgument, missing header is MissingHeader, and an out-of-range index is IndexOutOfRange |
| SaveBodyToFile(string path) | string | Save complete raw body bytes without overwriting an existing file; return the absolute saved path |
| SaveBodyToFileOverwrite(string path) | string | Explicitly allow replacement of an existing destination file; return the absolute saved path |

RestJsonValue exposes the same JSON selection, typed getter, array, and safe-read methods as RestResponse. Its `$` is the wrapped node, so array items can be used directly in automation loops. It also exposes ValueKind and JsonText properties. It has no HTTP/header/file helpers. A wrapper for a JSON null item is a valid object; it is not a C# null reference. Wrappers retain their backing JSON independently of temporary download-file cleanup.

### Conversion and error behavior

Strict getters must never silently substitute zero, false, an empty string, or a caller default for a failed read. They report a RestResponseHelperException with Code, Path, ExpectedType, and a sanitized message. Array/header index and file errors include the relevant argument context without including credentials or body content.

JSON rules:

- Numbers encoded as strings are not automatically converted. Boolean strings such as `"true"` or `"1"` are not booleans.
- Integral getters accept numerically integral values such as `12.0` or `1e3`, but reject fractional values and range overflow. Parsing must not round a large number through double before checking it.
- Decimal overflow or loss of required precision is ConversionFailed. Double overflow or underflow of a nonzero number to zero is ConversionFailed; NaN and infinity are not valid JSON numbers.
- Date/time strings must match `yyyy-MM-dd'T'HH:mm:ss[.fffffff](Z|+HH:mm|-HH:mm)` with 1–7 optional fractional digits and valid calendar/offset values. Preserve the supplied offset. Date-only strings and timestamps without an offset are ConversionFailed; no workstation-local timezone inference is allowed.
- Conversions are culture-independent. Objects and arrays are obtained with GetJson or wrappers rather than implicit string conversion.
- Parse JSON once per response when first needed and cache the immutable parsed representation. Enforce configured body/JSON-depth limits with a documented LimitExceeded error.

For every typed JSON getter, and for GetJson, GetValueKind, GetArrayCount, and GetArrayItem, provide a corresponding non-throwing `Read...` method with the same arguments, such as `ReadInt32(path)` or `ReadArrayItem(path, index)`. These return a non-generic RestValueResult suitable for automation branching:

| Result property | Definition |
| --- | --- |
| Success | Whether the requested read completed successfully |
| Code | None on success; otherwise a defined helper error code |
| Path | Requested selection path |
| Message | Sanitized failure explanation, empty on success |
| StringValue / Int32Value / Int64Value / DecimalValue / DoubleValue / BooleanValue / DateTimeOffsetValue / JsonValue | Typed output slot corresponding to the invoked method; JsonValue holds RestJsonValue for ReadArrayItem |

Only the matching output slot is meaningful when Success is true. All other slots, and all slots on failure, remain at their type default; users must branch on Success before consuming a value. Each result is independent. The safe-read methods capture expected helper failures, including parsing and path errors, without changing the response's request ErrorKind or ErrorMessage.

Defined JSON helper codes are InvalidArgument, InvalidPath, MissingPath, NullValue, TypeMismatch, ConversionFailed, EmptyBody, BodyUnavailable, IncompleteBody, InvalidJson, TextDecodeFailed, and LimitExceeded. Missing JSON body bytes after an HTTP response with an explicitly empty body produce EmptyBody; no HTTP response produces BodyUnavailable. Invalid path/argument syntax is validated before body access; body availability, completeness, decoding/parsing, path traversal, and conversion are then checked in that order. Exists and IsNull follow these same rules and do not hide malformed JSON or invalid traversal. Explicit null is returned successfully by GetJson/GetValueKind; typed getters report NullValue.

Helpers report invalid arguments predictably rather than depending on underlying library exception text. Header/file helpers use the applicable argument/body codes plus MissingHeader, IndexOutOfRange, FileExists, and FileWriteFailed. Their strict exceptions can be handled through the host's standard exception mechanism. The response's ErrorBehavior setting controls request execution failures; it does not change strict getters into default-returning helpers.

### Headers, text, and files

- Header lookup is case-insensitive and includes response and content headers. Reject null/empty or invalid header names as InvalidArgument. Preserve separately stored values, especially Set-Cookie; never blindly join them with commas. GetHeader returns the first stored value, not an arbitrary split of a comma-containing value.
- BodyText honors a supported declared charset. Without a charset, JSON and text media types use UTF-8. Unsupported charsets or malformed byte sequences report TextDecodeFailed instead of silent replacement. Non-text bodies report TypeMismatch through BodyText; raw bytes remain available through file saving.
- JSON helpers may attempt UTF-8 JSON parsing even when the media type is missing or non-text; a declared supported charset is honored. Parsing succeeds based on the content, not the header alone.
- BodyText may expose available text from an interrupted read, with IsBodyComplete false. JSON helpers and SaveBodyToFile require a complete body and report IncompleteBody otherwise. No-response body access reports BodyUnavailable.
- SaveBodyToFile writes body bytes as exposed by the HTTP runtime after transport decompression, without text re-encoding. A complete empty response can be saved as a zero-byte file.
- Resolve relative destinations against the robot's documented working directory. Require the parent directory to exist. Do not derive destination paths from untrusted response filenames automatically.
- Use a temporary destination and commit the completed write so failures do not leave a partial final file or corrupt an existing target. The non-overwrite method must preserve an existing file even under concurrent creation. Report FileExists or FileWriteFailed as appropriate and clean up temporary write artifacts.
- Support file-backed response bodies for large downloads. Helpers must not consume a one-time stream: repeated text, JSON, and save calls remain valid while the response is alive. Configure in-memory/text parsing limits and a managed temporary-storage lifecycle; document defaults after platform benchmarking.
- Raw response access remains available when typed models are generated. Helpers operate on the captured body without sending another network request.

### Response helper example

For the body `{"customer":{"name":"Ada","nickname":null},"items":[{"id":42}]}`:

```csharp
response.GetString("$.customer.name");             // "Ada"
response.Exists("$.customer.nickname");            // true
response.IsNull("$.customer.nickname");            // true
response.Exists("$.customer.email");               // false
response.GetArrayCount("$.items");                 // 1
response.GetArrayItem("$.items", 0).GetInt32("$.id"); // 42

var result = response.ReadString("$.customer.email");
// result.Success == false; result.Code == "MissingPath"
// An automation branches on Success before reading StringValue.
```

An automation can check IsSuccess, inspect an error body's JSON if needed, loop from zero to GetArrayCount(path) minus one, extract values from each item, and save a download through an explicit file method. No handwritten JSON parsing is required.

## 10. Authentication

| Mode | Required capabilities |
| --- | --- |
| None | Unauthenticated requests |
| Basic | Username and password binding |
| API key | Configurable name and header/query location; cookie placement where required |
| Bearer token | Supplied token or runtime token provider |
| OAuth2 | Client credentials and authorization code with PKCE; device authorization where supported |
| Entra ID | Tenant, client, scopes, app-only secret/certificate, delegated authentication, and managed identity where supported |
| Client certificate (mTLS) | Present a configured client certificate during HTTPS authentication; support standalone use and combinations with application authentication |

Additional requirements:

- Support shared defaults and endpoint-specific overrides.
- Preserve explicit unauthenticated operations.
- Support alternative authentication choices and schemes required together.
- Cache and renew/reacquire tokens according to the selected flow.
- Separate credentials and token state between unrelated configurations.
- Avoid unexpected interactive sign-in during unattended execution.
- Use credential references or an approved secret provider.
- Exclude secrets from generated source and sanitize persisted imported content.
- Mask sensitive values in previews and diagnostics.

The Pega credential-store integration requires architect review.

### Certificate-based authentication

Support two distinct certificate uses with clear designer labels:

- **HTTPS client certificate (mTLS):** authenticate the robot to an HTTPS endpoint using a certificate and its private key. Configure this independently of Basic, API key, bearer token, or OAuth2/Entra authentication so an endpoint can require both.
- **Entra ID certificate credential:** use a certificate credential to obtain an access token for app-only authentication. This does not automatically present a certificate to the REST endpoint. Certificate binding and selection should share the same user experience where practical, but resource-endpoint and token-acquisition bindings remain separate.

Generic OAuth2 certificate-signed client assertions and certificate-bound access tokens require explicit provider support and a separately documented compatibility decision; they must not be implied by selecting HTTPS mTLS.

| Certificate setting | Required behavior |
| --- | --- |
| Source | None, Windows certificate store, or PKCS#12 file (.pfx/.p12) |
| StoreLocation / StoreName | Select CurrentUser or LocalMachine and an allowed store; default to My (Personal) |
| Thumbprint | Select an exact certificate; provide a friendly picker showing subject, issuer, expiry, thumbprint, and private-key availability |
| CertificatePath | Bind a PFX/P12 path available to the executing robot; allow environment/runtime overrides |
| PasswordCredential | Resolve an encrypted-file password through the approved credential provider; never persist the password in generated source or definition content |
| Resource certificate scope | Bind resource certificates to the active base URL's HTTPS authority; endpoint overrides may change credentials but cannot change the destination base URL |

Wizard and runtime requirements:

- Expose certificate configuration in the manual endpoint wizard and generated instance properties, with inherited defaults and endpoint-specific overrides.
- Detect certificate requirements in supported import formats when represented. Missing certificate files, private keys, or external tool settings must result in an actionable binding task; importing a definition must never imply that a usable certificate was imported with it.
- Validate certificate selection, validity dates, private-key presence/access, and suitability for the selected authentication use. A public-only certificate cannot satisfy these modes. Certificate-store selection must resolve one exact certificate rather than silently choosing a subject-name match.
- Provide **Validate Certificate** as a local check that does not call the endpoint. Report expiry and binding issues clearly; successful local validation does not guarantee server acceptance.
- Resolve store access and file/private-key permissions under the actual robot execution identity. Designer access does not establish unattended runtime access.
- Require HTTPS for mTLS and retain normal server-certificate and hostname validation. Do not offer an implicit trust-all fallback.
- Keep certificates scoped to their configured destinations and connection pools. Do not present them to unrelated redirect hosts or reuse authenticated connections across different certificate bindings. Token endpoints require their own explicit binding when they also require mTLS.
- Permit certificate rotation through updated configuration and an explicit **Reload Certificate** action without regenerating C# methods. New calls use new connections for the replacement binding; existing in-flight calls may complete with the previous binding.
- Keep private keys and PFX contents out of generated source, DLLs, normalized snapshots, and automatic deployment bundles. Provision certificates separately and package only configuration references. Do not export store private keys as part of generation.
- Report certificate selection/key-access failures as Authentication errors with sanitized diagnostics. TLS negotiation failures may remain Transport errors, with certificate/TLS context where available; do not invent an HTTP status when the handshake failed.

The architect review must establish supported certificate/key providers, non-exportable-key compatibility, runtime identity access, file-loading/key-storage behavior, certificate rotation, and deployment provisioning conventions for the targeted Pega/.NET versions.

## 11. Request correctness and flexibility

The shared runtime must preserve:

- HTTP verb and URL semantics.
- Base paths and parameter escaping.
- Path, query, header, and cookie parameters.
- Supported array/object parameter serialization.
- Optional, omitted, and null values.
- JSON, text, form, multipart, and binary bodies.
- Content types and response headers.

Each component instance has exactly one active absolute HTTP(S) BaseUrl, including an optional API path prefix. Endpoint definitions use relative paths under that base. The runtime must preserve the prefix and parameter encoding; reject absolute/network-path endpoint overrides and path traversal that would escape the configured prefix. Query/path parameters cannot alter the destination authority. BaseUrl itself must not contain credentials, a query, or a fragment.

For collections containing multiple base URLs or OpenAPI server alternatives, the import flow shows the candidate groups and asks the user to select one base URL for this component. Only compatible selected operations are included. Clearly list excluded operations and guide the user to add another component for each additional base URL; never silently rewrite unrelated requests to the chosen server. Alternative environment servers for the same API may become profiles, each with one active base URL.

Changing an environment or runtime BaseUrl updates the whole instance without recompilation. Snapshot connection settings per invocation so in-flight requests remain on their original configuration. Authentication authorities and token endpoints may have separate URLs solely for authentication; they do not permit generated business endpoint methods to target additional base URLs.

Do not automatically follow resource redirects outside the configured authority/base path. Return the redirect response for explicit handling; other business destinations require separate components. Any future pagination helper must apply the same boundary to returned next links.

Configuration precedence must be documented and predictable, with explicit call settings overriding endpoint settings, instance/environment settings, and imported defaults for settings that allow overrides. BaseUrl is always resolved at the instance/environment level and is never overridden per call or endpoint.

HTTP errors, transport failures, authentication failures, timeouts, and serialization failures must remain distinguishable.

Automatic retries should be disabled by default. Enabled retry policies must account for operation safety and avoid blindly repeating state-changing requests.

## 12. Regeneration and portability

Persist:

- Sanitized API definition snapshot.
- Manually authored endpoint definitions, source origin, and wizard configuration, including bindings and optional schemas or sanitized examples.
- Selected operations and method-name mappings.
- Generation settings and fingerprint.
- Artifact and dependency manifest.
- Connection settings separately from generated source.

Regeneration must:

- Preserve stable names and identities where possible.
- Preview added, changed, and removed methods.
- Identify potential automation-breaking changes.
- Preserve user settings.
- Retain the last working artifact on failure.

Deployment must include generated DLLs and required dependencies. Robots must not depend on the original import path, developer workstation, or compiler.

## 13. Acceptance criteria

| Scenario | Expected outcome |
| --- | --- |
| Import each supported format | Selected endpoints become discoverable component methods |
| Define an endpoint using only the wizard | A discoverable method sends the configured verb, URL, parameters, headers, and body without an import file or handwritten source |
| Define an endpoint with no response schema or example | Generation succeeds with raw response access |
| Enter invalid or incomplete endpoint details | Inline validation identifies corrections; intentional runtime bindings remain valid |
| Save and reopen manually defined endpoints | Wizard settings, bindings, and stable method identities are preserved |
| Reimport a collection containing additional manual endpoints | Manual operations remain intact and naming conflicts are surfaced |
| Complete the wizard without testing | No endpoint request or interactive authentication occurs |
| Compare calls against controlled fixtures | Verb, URL, encoding, headers, and body match expected semantics |
| Change base URL or credentials | Subsequent calls use new settings without compilation |
| Import requests targeting multiple base URLs | User selects one base URL, sees excluded operations, and can configure additional components; no silent host rewriting occurs |
| Attempt an absolute endpoint override or out-of-scope redirect | The component preserves its single-base-URL boundary and reports the rejected configuration or redirect response |
| Map a nested response field through the visual editor | A named automation output is generated without writing C# or a JSON path |
| Map an array or missing/null field | Collection handling and configured missing/null policies match the preview; conversion errors remain visible |
| Change an existing response output mapping | Saved mappings survive reload and signature changes appear in regeneration review |
| Complete representative developer usability tasks | Developers complete the documented guided tasks independently; observations and agreed release targets are recorded |
| Load an unchanged project | OnLoadIfChanged reuses the valid artifact |
| Change the definition | Regeneration runs according to the selected mode |
| Trigger designer generation | Compilation and reference activation expose the methods |
| Cause import, compilation, or reference failure | Last working component remains available with actionable diagnostics |
| Change an existing signature | Breaking-change review appears before replacement |
| Exercise required authentication modes | Authentication and applicable token renewal succeed |
| Call an mTLS endpoint using store and PFX/P12 certificate sources | Both sources authenticate under the robot identity without embedding key material in generated artifacts |
| Combine mTLS with bearer/API key authentication | Both requirements are applied; unrelated hosts never receive the configured certificate |
| Select an expired, missing, public-only, or inaccessible certificate | Local validation/runtime diagnostics identify the binding problem without leaking secrets |
| Use an Entra certificate credential | Token acquisition works independently of the resource endpoint's optional mTLS binding |
| Rotate and reload a certificate | New requests use the replacement certificate without source regeneration or reuse of the old binding's connections |
| Deploy with separately provisioned certificates | The robot resolves the configured certificate under its execution identity and authenticates successfully |
| Import unresolved variables or script dependencies | Affected operations show actionable compatibility issues |
| Process empty, JSON, text, binary, and error responses | Results preserve status and content predictably |
| Extract nested values and property names containing punctuation | Dot and JSON-quoted bracket paths resolve according to the documented syntax |
| Compare missing, null, wrong-type, and invalid-path reads | Strict helpers report distinct codes; safe reads return independent failure results without changing request errors |
| Read arrays containing objects, scalars, and null | Counts and zero-based wrappers work, wrapper-relative paths are supported, and bounds errors are predictable |
| Convert fractional, large, string-encoded, and date/time values under different machine cultures | Conversions follow the defined precision, range, offset, and type rules consistently |
| Inspect empty, malformed, duplicate-key, incomplete, and oversized JSON bodies | Parsing and limit failures match documented codes; no silent fallback or extra request occurs |
| Read repeated response headers with mixed-case names | Lookup is case-insensitive and individual values remain intact |
| Save binary/empty bodies and attempt an existing-file destination | Bytes are preserved; overwrite requires its explicit method; failed writes preserve the destination |
| Invoke helpers repeatedly and concurrently on separate responses | Results remain isolated, reusable, and independent of component-wide mutable state |
| Use safe result slots and array wrappers in Robot Studio | Properties and methods are discoverable and usable in branching and looping automations |
| Deploy to a clean supported robot | Packaged components work without source files or compilation |
| Inspect generated artifacts and diagnostics | Credentials are absent from generated source and masked in diagnostics |

Performance and collection-size targets will be established after the integration prototype. Release validation must cover the agreed Robot Studio and robot runtime version matrix.

## 14. Product architect questions

| Topic | Decision needed |
| --- | --- |
| Supported platform | Robot Studio/runtime versions, .NET target, and process architecture |
| Compilation | Supported source-compilation API, dependency references, and diagnostics |
| Load trigger | Safe designer/project lifecycle hook |
| Designer actions | Supported extension points for actions and property editors |
| Method discovery | Required base classes, attributes, signatures, and registration |
| Assembly replacement | Refresh behavior and reload/restart restrictions |
| Automation compatibility | How method bindings persist and how affected usages can be identified |
| Execution model | Supported synchronous/asynchronous, cancellation, and event patterns |
| Designer types | Support for complex types, optional parameters, collections, and overloads |
| Visual response outputs | Supported Pega output bindings, collection iteration, nullable/absent values, and mapping failure reporting |
| Credentials | Approved secret storage and identity libraries |
| Certificates | Supported stores/key providers, private-key access under robot identities, PFX loading, rotation, and separate provisioning |
| Deployment | Artifact locations and dependency packaging |
| Scale | Supported collection sizes and acceptable generation times |

Requested review output: a minimal integration example, supported version matrix, and agreed conventions for method exposure, compilation, reference refresh, credentials, threading, and
deployment.

## 15. Delivery approach

1. Validate Pega integration: prove compilation, referencing, method discovery, load/manual triggers, regeneration, and clean deployment.
2. Build the foundation: normalized model, runtime, simple methods, properties, manual endpoint wizard, cURL/OpenAPI import, and diagnostics.
3. Complete required coverage: Postman, Bruno, all authentication modes, variable inheritance, payload formats, guided configuration, visual response mapping, explicit request testing, and compatibility checks.
4. Validate release readiness: conformance fixtures, authentication tests, designer lifecycle tests, deployment, usability, and performance.

These increments do not reduce the required product scope.

## 16. Out of scope

- Full Postman/Bruno scripting or test-runtime compatibility.
- Executing imported shell commands.
- SOAP, WebSocket, or API-server generation.
- General-purpose collection testing and monitoring.
- Automatic business workflow generation.
- Multiple active business API base URLs within one component, including endpoint-specific or per-call URL overrides; use separate components.

Unsupported behavior must be reported clearly rather than silently omitted.

## 17. Review status

The architecture reflects the user discussion. Exact property names, defaults, method signatures, and implementation sequencing remain proposals.

Product architect review is pending. No Pega integration prototype or repository implementation verification has been completed.
