# JsonUtils — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new `JsonUtils` component (`src/jsonutils`) as a full
replacement for Pega Robotics' native `Json` component (`DeserializeObject`,
`GetValueFromJSON`, `SerializeObject`, `SetValueInJSON`), built on
Newtonsoft.Json so path-based get/set support real JSONPath (wildcards,
recursive descent, filter expressions) instead of dot-notation-only, plus a
set of scalar-getter and array/validation gap-fillers the native component
has no equivalent for. Not a review-cycle pass — this is a brand-new
component, following the precedent set by `EventLogUtils` (PR #63).

**Non-goals for this pass (phase 2):** `MergeJson`, `DiffJson`, JSON↔XML
conversion, and array filter/sort helpers (`FilterJsonArrayByField`,
`SortJsonArrayByField`). These are real candidates but out of scope here to
keep this PR reviewable; revisit as a follow-on component pass once this
lands. JSON **Schema** validation is deliberately excluded, not deferred:
Newtonsoft's own schema validator (`Newtonsoft.Json.Schema`) is a separate
commercially-licensed package, unlike Json.NET itself — adding it would mean
either paying for a license or pulling in a different (unvetted) free schema
library, neither of which is worth doing just for this method.

## Architecture

- New `src/jsonutils/JsonUtils.csproj`:
  - `TargetFrameworks net8.0-windows;net10.0-windows`, `LangVersion latest`,
    `ImplicitUsings disable`, `Nullable disable` — matches every other
    component in the suite.
  - `AssemblyName`/`RootNamespace` = `JsonAutomation` (the suite's
    `<Name>Automation` convention: `DialogAutomation`, `ServiceAutomation`,
    `EventLogAutomation`, etc.).
  - `EnableWindowsTargeting true`, `GenerateDocumentationFile true`,
    `Platforms AnyCPU;x64`.
  - `PackageReference Include="Newtonsoft.Json"` — the suite's **second**
    external NuGet dependency (after `ServiceUtils`'s
    `System.ServiceProcess.ServiceController`). README Notes & Caveats gets
    the same "why this dependency" callout `ServiceUtils` uses: Json.NET's
    `SelectToken`/`SelectTokens` are what make real JSONPath (as opposed to
    dot-notation-only) possible without hand-rolling a path parser.
  - Own `NeverThrowsGuard.cs` (per-component copy, matching every other
    component — no shared internal library between components in this
    repo).
  - `JsonUtils.Tests/` subfolder, `<Compile Remove="JsonUtils.Tests/**/*.cs" />`
    in the csproj, registered in `src/AwesomeRpaUtils.sln` (project + 12
    config lines + nesting).
  - `InternalsVisibleTo Include="JsonUtils.Tests"` if any private helper
    (e.g. a path-parsing or flatten-walk helper) ends up worth testing
    directly rather than only through the public surface.
- No Win32/Windows-only APIs anywhere in this component — it's pure
  JSON/string logic over Newtonsoft's `JToken`/`JObject`/`JArray`. Like
  `ServiceUtils`/`EventLogUtils`, `JsonUtils.Tests` should be fully
  Linux-runnable despite the Windows-flavored TFM (verify with `dotnet test`
  on this host, not just `dotnet build`).

## Method surface

Every method follows the suite's never-throws contract: `bool` return +
trailing `out string message`; invalid JSON, a path that doesn't resolve, or
a type mismatch at a resolved path all produce `false` + a descriptive
message, never an exception.

**Native parity** (same 4 capabilities as Pega's `Json` component, rebuilt
on Newtonsoft's JSONPath):

| Method | Purpose |
|---|---|
| `TryDeserializeObject(string json, string typeName, out object result, out string message)` | JSON string → an instance of the named .NET type |
| `TrySerializeObject(object value, out string json, out string message)` | object → JSON string |
| `TryGetValueFromJson(string json, string path, out string value, out string message)` | single value at a JSONPath, as string |
| `TrySetValueInJson(string json, string path, string value, out string updatedJson, out string message)` | update a value at a JSONPath |

**On `TryDeserializeObject` not being generic:** an earlier draft of this
plan used `TryDeserializeObject<T>(string json, out T result, out string message)`.
A code-quality review during implementation caught that this is wrong: no
other public method anywhere in this 17-component suite uses a generic type
parameter (confirmed by grep), and the native `Json` component's own
`DeserializeObject` takes the target type as a **string** and returns
**`object`** (`DeserializeObject(string jsonString, string typeString, out object deserializedObject)`
— see `pega-robotic-automation` skill, ch11) rather than using a generic —
almost certainly because Robot Studio's drag-and-drop designer binds
parameters/outputs via reflection over closed, concrete types and can't
offer a canvas user a way to pick an open generic `T`. `TryDeserializeObject`
therefore mirrors the native shape: `(string json, string typeName, out object result, out string message)`,
resolving `typeName` via `Type.GetType(typeName)` (an assembly-qualified or
in-scope simple type name) before calling `JsonConvert.DeserializeObject(json, type)`.

**Gap-fillers** (this pass):

| Method | Purpose |
|---|---|
| `IsValidJson(string json, out string message)` | never-throws parse check |
| `TryGetStringValue` / `TryGetIntValue` / `TryGetBoolValue` / `TryGetDoubleValue` / `TryGetDateTimeValue(string json, string path, out T value, out string message)` | typed scalar getters at a path — no caller-side string parsing |
| `TryGetValuesFromJson(string json, string path, string delimiter, out string delimitedValues, out string message)` | multi-match (wildcard/filter paths, via `SelectTokens`) joined into one delimited string — the suite's established `ListXNamesDelimited` pattern (see `ServiceUtils`) applied to JSONPath results |
| `TryRemoveValueFromJson(string json, string path, out string updatedJson, out string message)` | delete a node at a path |
| `TryGetArrayLength(string json, string path, out int length, out string message)` | element count at a path |
| `TryAppendToJsonArray(string json, string path, string valueJson, out string updatedJson, out string message)` | append a JSON-fragment element to an existing array |
| `TryGetValueType(string json, string path, out JsonValueKind kind, out string message)` | reports a **repo-owned** `JsonValueKind` enum (`String`, `Number`, `Boolean`, `Array`, `Object`, `Null`, `NotFound`) — mirrors `ServiceUtils`'s repo-owned `ServiceStatus` enum so Newtonsoft's own `JTokenType` doesn't leak into the public contract |
| `TryPrettyPrintJson(string json, out string formattedJson, out string message)` | indented formatting |
| `TryMinifyJson(string json, out string minifiedJson, out string message)` | whitespace-stripped formatting |

**On `SerializeObject`'s native `⚠@default=SingleOutput` annotation:** Pega's
own component collapses that method to a single visible output port in the
designer. `TrySerializeObject` keeps the standard bool+out signature for
consistency with every other method in this suite and every other component;
the README documents that Robot Studio's designer can still be configured to
show only the `json` output if a single-port surface is wanted, without that
being baked into the method signature itself.

## Testing

Input-guard tests for every method above: malformed JSON, a path that
doesn't resolve, and a type mismatch at a resolved path (e.g.
`TryGetIntValue` against a string node). Plus real round-trip tests:
serialize → deserialize, set → get, append → length, remove → get-returns-
not-found. Standard xunit project layout matching `ServiceUtils.Tests`.

## Documentation

- `src/jsonutils/README.md` with the standard 3-column method table
  (`Method | Signature | Description`) and a Notes & Caveats section
  covering: the Newtonsoft.Json dependency and why, a short JSONPath syntax
  primer (dot/bracket, wildcards, filters) with 3-4 worked examples, the
  `SerializeObject` single-output-port note above, and an explicit "Phase 2"
  callout listing `MergeJson`/`DiffJson`/XML conversion/array filter-sort as
  deferred, plus the JSON Schema exclusion and why.
- Root `README.md` gains the new component's one-line entry, matching the
  existing per-component list.
- `scripts/Package-Release.ps1`'s `$releaseAssemblies` array — hardcoded,
  not auto-discovered — needs `JsonAutomation` added manually (the gotcha
  caught post-merge on `EventLogUtils`, PR #63). `Package-Documentation.ps1`
  needs no change (auto-discovers via `Get-ChildItem` globs).

## Suite conventions followed

- Never-throws contract: `bool` + `out string message`.
- `<Name>Automation` assembly/namespace naming.
- Per-component `NeverThrowsGuard.cs`, no shared internal library between
  components.
- Test project in a subfolder with `<Compile Remove>`, registered in the
  `.sln`.
- **Every public method gets `[Category("Json - <Group>")]` and
  `[Description("...")]` attributes.** This was missed in an earlier draft
  of this plan and caught by code-quality review after Task 2 landed —
  every other component in the suite (`ServiceUtils`, `EventLogUtils`,
  `DialogUtils`, `StackUtils`, `CommandLineUtils`, etc.) decorates every
  public automation method this way; Robot Studio's designer uses
  `[Category]` to group methods in its component browser and `[Description]`
  as the developer-facing blurb shown when the method is placed on a canvas.
  Group names used in this component: `Json - Core` (native-parity methods),
  `Json - Validation` (`IsValidJson` and the typed scalar getters),
  `Json - Query` (multi-match getter and value-type inspector),
  `Json - Array` (array/removal methods), `Json - Format` (pretty-print/minify).
- New work happens in `.worktrees/jsonutils` on branch `add-jsonutils`,
  PR'd and the worktree removed afterward — the established new-component
  workflow (see `EventLogUtils`, PR #63).

---

## Task 1: Scaffold the project

**Files:**
- Create: `src/jsonutils/JsonUtils.csproj`
- Create: `src/jsonutils/NeverThrowsGuard.cs`
- Create: `src/jsonutils/JsonValueKind.cs`
- Create: `src/jsonutils/JsonUtils.cs`
- Create: `src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj`
- Create: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`
- Modify: `src/AwesomeRpaUtils.sln`

- [ ] **Step 1: Create the worktree**

Run from the repo root:

```bash
git worktree add .worktrees/jsonutils -b add-jsonutils main
cd .worktrees/jsonutils
```

All later steps in this plan happen inside `.worktrees/jsonutils`.

- [ ] **Step 2: `JsonUtils.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0-windows;net10.0-windows</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AssemblyName>JsonAutomation</AssemblyName>
    <RootNamespace>JsonAutomation</RootNamespace>
    <Platforms>AnyCPU;x64</Platforms>
    <!-- Allows building this Windows-targeted project from a non-Windows SDK host -->
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <!-- Emit JsonAutomation.xml next to the DLL so the XML doc comments are consumable -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <!-- Second NuGet dependency in this repo (after ServiceUtils' System.ServiceProcess.ServiceController)
         - see README's Notes & Caveats for why. SelectToken/SelectTokens are what make real
         JSONPath (wildcards, recursive descent, filters) possible without hand-rolling a parser. -->
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

  <ItemGroup>
    <!-- The test project lives in a subfolder; keep its sources out of this DLL -->
    <Compile Remove="JsonUtils.Tests/**/*.cs" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: `NeverThrowsGuard.cs`**

```csharp
using System;

namespace JsonAutomation
{
    internal static class NeverThrowsGuard
    {
        internal static bool IsRecoverable(Exception exception) => exception is not OutOfMemoryException && exception is not StackOverflowException && exception is not AccessViolationException;
        internal static string Failure(string operation, Exception exception) => $"{operation} failed unexpectedly ({exception.GetType().Name}): {exception.Message}";
    }
}
```

- [ ] **Step 4: `JsonValueKind.cs`**

```csharp
namespace JsonAutomation
{
    /// <summary>
    /// The kind of value found at a JSON path, reported without leaking Newtonsoft's own
    /// <c>JTokenType</c> (which has more granularity than a Pega automation needs, e.g.
    /// separate Integer/Float members) into this component's public contract.
    /// </summary>
    public enum JsonValueKind
    {
        /// <summary>The path did not resolve to any value.</summary>
        NotFound,
        /// <summary>The value at the path is a JSON null literal.</summary>
        Null,
        /// <summary>The value at the path is a string.</summary>
        String,
        /// <summary>The value at the path is a number (integer or floating-point).</summary>
        Number,
        /// <summary>The value at the path is a boolean.</summary>
        Boolean,
        /// <summary>The value at the path is a JSON array.</summary>
        Array,
        /// <summary>The value at the path is a JSON object.</summary>
        Object
    }
}
```

- [ ] **Step 5: `JsonUtils.cs` skeleton**

```csharp
using System.ComponentModel;

namespace JsonAutomation
{
    /// <summary>
    /// A Pega Robot Studio-ready component for reading, updating, validating, and
    /// transforming JSON via real JSONPath (Newtonsoft.Json's <c>SelectToken</c>/
    /// <c>SelectTokens</c>), as a full replacement for the native <c>Json</c> component's
    /// dot-notation-only path support. Like every component in this suite, its methods
    /// report recoverable failures as <c>False</c> with a descriptive message instead of
    /// throwing.
    /// </summary>
    [Description("Reads, updates, validates, and transforms JSON via JSONPath. " +
                 "All methods return True/False with a failure message instead of throwing. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class JsonUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public JsonUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public JsonUtils(IContainer container)
        {
            container?.Add(this);
        }

        #region Native parity

        #endregion

        #region Validation and typed getters

        #endregion

        #region Multi-match and inspection

        #endregion

        #region Array and removal

        #endregion

        #region Formatting

        #endregion
    }
}
```

Note: `IContainer` lives in `System.ComponentModel`, already `using`'d above —
no separate `System.ComponentModel.Container`-only using needed (matches
`ServiceUtils.cs`'s single `using System.ComponentModel;`).

- [ ] **Step 6: `JsonUtils.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <!-- Lets `dotnet build` (not `dotnet test`) work on non-Windows CI machines. -->
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <AssemblyName>JsonUtils.Tests</AssemblyName>
    <RootNamespace>JsonAutomation.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\JsonUtils.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 7: `JsonUtilsTests.cs` smoke test**

```csharp
using JsonAutomation;
using Xunit;

namespace JsonAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for JsonUtils. This component is pure JSON/string
    /// logic over Newtonsoft.Json with no Win32 dependency, so unlike most of this suite's
    /// Windows-flavored components, every test here runs on Linux CI too.
    /// </summary>
    public class JsonUtilsTests
    {
        private readonly JsonUtils _json = new JsonUtils();

        [Fact]
        public void Constructor_DoesNotThrow()
        {
            Assert.NotNull(_json);
        }
    }
}
```

- [ ] **Step 8: Register both projects in `src/AwesomeRpaUtils.sln`**

Add a solution folder plus the two projects, right after the existing
`DataBagUtils.Tests` `EndProject` line (the last entry in the file's project
list):

```
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "jsonutils", "jsonutils", "{A5FF9977-0242-4669-A29C-100506089000}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "JsonUtils", "jsonutils\JsonUtils.csproj", "{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "JsonUtils.Tests", "jsonutils\JsonUtils.Tests\JsonUtils.Tests.csproj", "{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}"
EndProject
```

Add 12 lines to the `GlobalSection(ProjectConfigurationPlatforms)` section
(alongside every other project's block, immediately before its closing
`EndGlobalSection`):

```
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Debug|x64.ActiveCfg = Debug|x64
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Debug|x64.Build.0 = Debug|x64
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Debug|x86.ActiveCfg = Debug|Any CPU
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Debug|x86.Build.0 = Debug|Any CPU
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Release|Any CPU.Build.0 = Release|Any CPU
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Release|x64.ActiveCfg = Release|x64
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Release|x64.Build.0 = Release|x64
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Release|x86.ActiveCfg = Release|Any CPU
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E}.Release|x86.Build.0 = Release|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Debug|x64.ActiveCfg = Debug|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Debug|x64.Build.0 = Debug|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Debug|x86.ActiveCfg = Debug|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Debug|x86.Build.0 = Debug|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Release|Any CPU.Build.0 = Release|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Release|x64.ActiveCfg = Release|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Release|x64.Build.0 = Release|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Release|x86.ActiveCfg = Release|Any CPU
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11}.Release|x86.Build.0 = Release|Any CPU
```

Add 2 lines to the `GlobalSection(NestedProjects)` section (nests both
projects under the new `jsonutils` solution folder), immediately before its
closing `EndGlobalSection`:

```
		{A5A691FF-604A-43EC-A370-3FD6AFBFD45E} = {A5FF9977-0242-4669-A29C-100506089000}
		{8599313A-7B6F-4E55-ADD4-9743D1F5EE11} = {A5FF9977-0242-4669-A29C-100506089000}
```

- [ ] **Step 9: Build and run the smoke test**

```bash
dotnet build src/jsonutils/JsonUtils.csproj
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: both succeed, 1 test passing.

- [ ] **Step 10: Commit**

```bash
git add src/jsonutils src/AwesomeRpaUtils.sln
git commit -m "Scaffold JsonUtils component"
```

## Task 2: Native-parity methods

**Files:**
- Modify: `src/jsonutils/JsonUtils.cs` (the `#region Native parity` block)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `JsonUtilsTests.cs` (inside the `JsonUtilsTests` class, alongside the
existing constructor test — add `using Newtonsoft.Json;` at the top of the
file, needed for `Newtonsoft.Json.Linq.JObject` used below):

```csharp
private sealed class SamplePerson
{
    public string Name { get; set; }
    public int Age { get; set; }
}

[Fact]
public void TryDeserializeObject_ValidJsonAndTypeName_ReturnsPopulatedObject()
{
    bool succeeded = _json.TryDeserializeObject("{\"Name\":\"Ada\",\"Age\":30}", typeof(SamplePerson).AssemblyQualifiedName, out object result, out string message);

    Assert.True(succeeded);
    Assert.Null(message);
    SamplePerson person = Assert.IsType<SamplePerson>(result);
    Assert.Equal("Ada", person.Name);
    Assert.Equal(30, person.Age);
}

[Fact]
public void TryDeserializeObject_MalformedJson_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryDeserializeObject("{not json", typeof(SamplePerson).AssemblyQualifiedName, out object result, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryDeserializeObject_UnresolvableTypeName_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryDeserializeObject("{\"Name\":\"Ada\"}", "NoSuch.Type, NoSuchAssembly", out object result, out string message);

    Assert.False(succeeded);
    Assert.Null(result);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TrySerializeObject_SimpleObject_ReturnsJson()
{
    bool succeeded = _json.TrySerializeObject(new SamplePerson { Name = "Ada", Age = 30 }, out string json, out string message);

    Assert.True(succeeded);
    Assert.Null(message);
    Assert.Equal("Ada", (string)Newtonsoft.Json.Linq.JObject.Parse(json)["Name"]);
}

[Fact]
public void TryGetValueFromJson_ExistingPath_ReturnsValue()
{
    bool succeeded = _json.TryGetValueFromJson("{\"order\":{\"items\":[{\"sku\":\"ABC\"}]}}", "order.items[0].sku", out string value, out string message);

    Assert.True(succeeded);
    Assert.Null(message);
    Assert.Equal("ABC", value);
}

[Fact]
public void TryGetValueFromJson_PathNotFound_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryGetValueFromJson("{\"order\":{}}", "order.missing", out string value, out string message);

    Assert.False(succeeded);
    Assert.Null(value);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryGetValueFromJson_MalformedJson_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryGetValueFromJson("{not json", "a", out string value, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryGetValueFromJson_NullLiteralPath_ReturnsTrueWithNullValue()
{
    bool succeeded = _json.TryGetValueFromJson("{\"a\":null}", "a", out string value, out string message);

    Assert.True(succeeded);
    Assert.Null(value);
    Assert.Null(message);
}

[Fact]
public void TrySetValueInJson_ExistingPath_ReturnsUpdatedJson()
{
    bool succeeded = _json.TrySetValueInJson("{\"order\":{\"status\":\"open\"}}", "order.status", "closed", out string updatedJson, out string message);

    Assert.True(succeeded);
    Assert.Null(message);
    Assert.Equal("closed", (string)Newtonsoft.Json.Linq.JObject.Parse(updatedJson)["order"]["status"]);
}

[Fact]
public void TrySetValueInJson_PathNotFound_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TrySetValueInJson("{\"order\":{}}", "order.missing", "x", out string updatedJson, out string message);

    Assert.False(succeeded);
    Assert.Null(updatedJson);
    Assert.False(string.IsNullOrEmpty(message));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — `JsonUtils` has no `TryDeserializeObject`/
`TrySerializeObject`/`TryGetValueFromJson`/`TrySetValueInJson` members yet.

- [ ] **Step 3: Implement the four methods**

Add `using Newtonsoft.Json;` and `using Newtonsoft.Json.Linq;` to the top of
`JsonUtils.cs`, then fill in the `#region Native parity` block. Every public
method in this suite carries `[Category]`/`[Description]` attributes so
Robot Studio's designer can group and describe it — this was missed in an
earlier draft of this plan; use group name `"Json - Core"` for this region:

```csharp
#region Native parity

/// <summary>Deserializes a JSON string into an instance of the given .NET type.</summary>
/// <param name="json">The JSON text to deserialize.</param>
/// <param name="typeName">An assembly-qualified or in-scope simple type name, resolved via <see cref="Type.GetType(string)"/>.</param>
/// <param name="result">The deserialized object on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if deserialization succeeded.</returns>
[Category("Json - Core")]
[Description("Deserializes a JSON string into an instance of the named .NET type. Never throws.")]
public bool TryDeserializeObject(string json, string typeName, out object result, out string message)
{
    result = null;
    message = null;
    try
    {
        Type type = Type.GetType(typeName);
        if (type == null)
        {
            message = $"Type '{typeName}' could not be resolved.";
            return false;
        }
        result = JsonConvert.DeserializeObject(json, type);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryDeserializeObject), exception);
        return false;
    }
}

/// <summary>Serializes an object to a JSON string.</summary>
/// <param name="value">The object to serialize.</param>
/// <param name="json">The JSON text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if serialization succeeded.</returns>
[Category("Json - Core")]
[Description("Serializes an object to a JSON string. Never throws.")]
public bool TrySerializeObject(object value, out string json, out string message)
{
    json = null;
    message = null;
    try
    {
        json = JsonConvert.SerializeObject(value);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TrySerializeObject), exception);
        return false;
    }
}

/// <summary>Extracts a single value from a JSON string using a JSONPath expression.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression, e.g. <c>order.items[0].sku</c>.</param>
/// <param name="value">The value at <paramref name="path"/> as a string on success (or
/// <c>null</c> if the value is a JSON null literal); <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to a value.</returns>
[Category("Json - Core")]
[Description("Extracts a single value from a JSON string using a JSONPath expression. Never throws.")]
public bool TryGetValueFromJson(string json, string path, out string value, out string message)
{
    value = null;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        JToken token = root.SelectToken(path);
        if (token == null)
        {
            message = $"Path '{path}' did not resolve to a value.";
            return false;
        }
        value = token.Type == JTokenType.Null ? null : token.ToString();
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryGetValueFromJson), exception);
        return false;
    }
}

/// <summary>Updates a value in a JSON string using a JSONPath expression. The path must
/// already resolve to an existing value - this replaces a value in place, it does not
/// create new object properties or array elements along the way.</summary>
/// <param name="json">The JSON text to update.</param>
/// <param name="path">A JSONPath expression identifying an existing value.</param>
/// <param name="value">The new value. Always written as a JSON string scalar, even if the
/// path currently holds a number or boolean.</param>
/// <param name="updatedJson">The updated JSON text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to an existing value that was updated.</returns>
[Category("Json - Core")]
[Description("Updates a value in a JSON string using a JSONPath expression. The path must already exist. Never throws.")]
public bool TrySetValueInJson(string json, string path, string value, out string updatedJson, out string message)
{
    updatedJson = null;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        JToken target = root.SelectToken(path);
        if (target == null)
        {
            message = $"Path '{path}' did not resolve to an existing value; TrySetValueInJson can only replace a value at a path that already exists.";
            return false;
        }
        target.Replace(JToken.FromObject(value));
        updatedJson = root.ToString(Formatting.None);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TrySetValueInJson), exception);
        return false;
    }
}

#endregion
```

Also add `using System;` to the top of `JsonUtils.cs` if not already present
(needed for the `Exception` catch clauses and `Type.GetType`).

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 11 tests (1 smoke + 10 new).

- [ ] **Step 5: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils native-parity methods (deserialize/serialize/get/set)"
```

## Task 3: Validation and typed scalar getters

**Files:**
- Modify: `src/jsonutils/JsonUtils.cs` (the `#region Validation and typed getters` block)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("{\"a\":1}")]
[InlineData("[1,2,3]")]
[InlineData("\"just a string\"")]
public void IsValidJson_ValidJson_ReturnsTrue(string json)
{
    bool valid = _json.IsValidJson(json, out string message);

    Assert.True(valid);
    Assert.Null(message);
}

[Fact]
public void IsValidJson_MalformedJson_ReturnsFalseWithMessage()
{
    bool valid = _json.IsValidJson("{not json", out string message);

    Assert.False(valid);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryGetStringValue_StringPath_ReturnsValue()
{
    bool succeeded = _json.TryGetStringValue("{\"name\":\"Ada\"}", "name", out string value, out string message);

    Assert.True(succeeded);
    Assert.Equal("Ada", value);
}

[Fact]
public void TryGetIntValue_IntegerPath_ReturnsValue()
{
    bool succeeded = _json.TryGetIntValue("{\"age\":30}", "age", out int value, out string message);

    Assert.True(succeeded);
    Assert.Equal(30, value);
}

[Fact]
public void TryGetIntValue_NonNumericPath_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryGetIntValue("{\"age\":\"thirty\"}", "age", out int value, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryGetBoolValue_BooleanPath_ReturnsValue()
{
    bool succeeded = _json.TryGetBoolValue("{\"active\":true}", "active", out bool value, out string message);

    Assert.True(succeeded);
    Assert.True(value);
}

[Fact]
public void TryGetDoubleValue_NumberPath_ReturnsValue()
{
    bool succeeded = _json.TryGetDoubleValue("{\"price\":19.99}", "price", out double value, out string message);

    Assert.True(succeeded);
    Assert.Equal(19.99, value);
}

[Fact]
public void TryGetDateTimeValue_DateStringPath_ReturnsValue()
{
    bool succeeded = _json.TryGetDateTimeValue("{\"created\":\"2026-01-15T00:00:00\"}", "created", out DateTime value, out string message);

    Assert.True(succeeded);
    Assert.Equal(new DateTime(2026, 1, 15), value);
}

[Fact]
public void TryGetStringValue_PathNotFound_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryGetStringValue("{}", "missing", out string value, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}
```

Add `using System;` to the test file's usings if not already present (for
`DateTime`).

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — the five new methods don't exist yet.

- [ ] **Step 3: Implement `IsValidJson` and the five typed getters**

Fill in the `#region Validation and typed getters` block in `JsonUtils.cs`.
Every public method here gets `[Category("Json - Validation")]` and a
`[Description]` (the private `TryGetTypedValue<T>` helper is not
Robot-Studio-visible, so it gets neither):

```csharp
#region Validation and typed getters

/// <summary>Checks whether a string is well-formed JSON.</summary>
/// <param name="json">The text to check.</param>
/// <param name="message"><c>null</c> if valid; a description of the parse failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="json"/> parses as JSON.</returns>
[Category("Json - Validation")]
[Description("Checks whether a string is well-formed JSON. Never throws.")]
public bool IsValidJson(string json, out string message)
{
    message = null;
    try
    {
        JToken.Parse(json);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(IsValidJson), exception);
        return false;
    }
}

private bool TryGetTypedValue<T>(string json, string path, string methodName, out T value, out string message)
{
    value = default;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        JToken token = root.SelectToken(path);
        if (token == null)
        {
            message = $"Path '{path}' did not resolve to a value.";
            return false;
        }
        value = token.Value<T>();
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(methodName, exception);
        return false;
    }
}

/// <summary>Extracts a value at a JSONPath as a <see cref="string"/>.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression.</param>
/// <param name="value">The value on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="string"/>.</returns>
[Category("Json - Validation")]
[Description("Extracts a value at a JSONPath as a string. Never throws.")]
public bool TryGetStringValue(string json, string path, out string value, out string message) =>
    TryGetTypedValue(json, path, nameof(TryGetStringValue), out value, out message);

/// <summary>Extracts a value at a JSONPath as an <see cref="int"/>.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression.</param>
/// <param name="value">The value on success; <c>0</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="int"/>.</returns>
[Category("Json - Validation")]
[Description("Extracts a value at a JSONPath as an int. Never throws.")]
public bool TryGetIntValue(string json, string path, out int value, out string message) =>
    TryGetTypedValue(json, path, nameof(TryGetIntValue), out value, out message);

/// <summary>Extracts a value at a JSONPath as a <see cref="bool"/>.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression.</param>
/// <param name="value">The value on success; <c>false</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="bool"/>.</returns>
[Category("Json - Validation")]
[Description("Extracts a value at a JSONPath as a bool. Never throws.")]
public bool TryGetBoolValue(string json, string path, out bool value, out string message) =>
    TryGetTypedValue(json, path, nameof(TryGetBoolValue), out value, out message);

/// <summary>Extracts a value at a JSONPath as a <see cref="double"/>.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression.</param>
/// <param name="value">The value on success; <c>0</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="double"/>.</returns>
[Category("Json - Validation")]
[Description("Extracts a value at a JSONPath as a double. Never throws.")]
public bool TryGetDoubleValue(string json, string path, out double value, out string message) =>
    TryGetTypedValue(json, path, nameof(TryGetDoubleValue), out value, out message);

/// <summary>Extracts a value at a JSONPath as a <see cref="DateTime"/>.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression.</param>
/// <param name="value">The value on success; <see cref="DateTime.MinValue"/> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="DateTime"/>.</returns>
[Category("Json - Validation")]
[Description("Extracts a value at a JSONPath as a DateTime. Never throws.")]
public bool TryGetDateTimeValue(string json, string path, out DateTime value, out string message) =>
    TryGetTypedValue(json, path, nameof(TryGetDateTimeValue), out value, out message);

#endregion
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 22 tests (11 prior + 11 new — `IsValidJson_ValidJson_ReturnsTrue`'s `[Theory]` contributes 3 cases).

- [ ] **Step 5: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils validation and typed scalar getters"
```

## Task 4: Multi-match getter and value-type inspector

**Files:**
- Modify: `src/jsonutils/JsonUtils.cs` (the `#region Multi-match and inspection` block)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void TryGetValuesFromJson_WildcardPath_ReturnsDelimitedValues()
{
    bool succeeded = _json.TryGetValuesFromJson("{\"items\":[{\"sku\":\"A\"},{\"sku\":\"B\"}]}", "items[*].sku", ",", out string delimitedValues, out string message);

    Assert.True(succeeded);
    Assert.Equal("A,B", delimitedValues);
}

[Fact]
public void TryGetValuesFromJson_NoMatches_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryGetValuesFromJson("{\"items\":[]}", "items[*].sku", ",", out string delimitedValues, out string message);

    Assert.False(succeeded);
    Assert.Null(delimitedValues);
    Assert.False(string.IsNullOrEmpty(message));
}

[Theory]
[InlineData("{\"a\":\"x\"}", "a", JsonValueKind.String)]
[InlineData("{\"a\":1}", "a", JsonValueKind.Number)]
[InlineData("{\"a\":true}", "a", JsonValueKind.Boolean)]
[InlineData("{\"a\":null}", "a", JsonValueKind.Null)]
[InlineData("{\"a\":[1,2]}", "a", JsonValueKind.Array)]
[InlineData("{\"a\":{\"b\":1}}", "a", JsonValueKind.Object)]
public void TryGetValueType_VariousTypes_ReturnsExpectedKind(string json, string path, JsonValueKind expectedKind)
{
    bool succeeded = _json.TryGetValueType(json, path, out JsonValueKind kind, out string message);

    Assert.True(succeeded);
    Assert.Equal(expectedKind, kind);
}

[Fact]
public void TryGetValueType_PathNotFound_ReturnsFalseWithNotFoundKind()
{
    bool succeeded = _json.TryGetValueType("{}", "missing", out JsonValueKind kind, out string message);

    Assert.False(succeeded);
    Assert.Equal(JsonValueKind.NotFound, kind);
    Assert.False(string.IsNullOrEmpty(message));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — `TryGetValuesFromJson`/`TryGetValueType` don't exist yet.

- [ ] **Step 3: Implement both methods**

Add `using System.Collections.Generic;` to `JsonUtils.cs`'s usings, then fill
in the `#region Multi-match and inspection` block. Both new public methods
get `[Category("Json - Query")]` and a `[Description]`:

```csharp
#region Multi-match and inspection

/// <summary>Extracts every value matching a JSONPath (e.g. a wildcard or filter
/// expression) and joins them into one delimited string.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression that may match zero or more values.</param>
/// <param name="delimiter">The delimiter to join matched values with.</param>
/// <param name="delimitedValues">The joined values on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> matched at least one value.</returns>
[Category("Json - Query")]
[Description("Extracts every value matching a JSONPath and joins them into one delimited string. Never throws.")]
public bool TryGetValuesFromJson(string json, string path, string delimiter, out string delimitedValues, out string message)
{
    delimitedValues = null;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        List<string> values = new List<string>();
        foreach (JToken token in root.SelectTokens(path))
        {
            values.Add(token.Type == JTokenType.Null ? string.Empty : token.ToString());
        }
        if (values.Count == 0)
        {
            message = $"Path '{path}' did not match any values.";
            return false;
        }
        delimitedValues = string.Join(delimiter, values);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryGetValuesFromJson), exception);
        return false;
    }
}

/// <summary>Reports the kind of value found at a JSONPath.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression.</param>
/// <param name="kind">The value's kind on success; <see cref="JsonValueKind.NotFound"/> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to a value.</returns>
[Category("Json - Query")]
[Description("Reports the kind of value found at a JSONPath. Never throws.")]
public bool TryGetValueType(string json, string path, out JsonValueKind kind, out string message)
{
    kind = JsonValueKind.NotFound;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        JToken token = root.SelectToken(path);
        if (token == null)
        {
            message = $"Path '{path}' did not resolve to a value.";
            return false;
        }
        kind = token.Type switch
        {
            JTokenType.Null => JsonValueKind.Null,
            JTokenType.String => JsonValueKind.String,
            JTokenType.Integer => JsonValueKind.Number,
            JTokenType.Float => JsonValueKind.Number,
            JTokenType.Boolean => JsonValueKind.Boolean,
            JTokenType.Array => JsonValueKind.Array,
            JTokenType.Object => JsonValueKind.Object,
            _ => JsonValueKind.String
        };
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryGetValueType), exception);
        return false;
    }
}

#endregion
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 31 tests (22 prior + 9 new — `TryGetValueType_VariousTypes_ReturnsExpectedKind`'s `[Theory]` contributes 6 cases).

- [ ] **Step 5: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils multi-match getter and value-type inspector"
```

## Task 5: Array operations and removal

**Files:**
- Modify: `src/jsonutils/JsonUtils.cs` (the `#region Array and removal` block)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void TryRemoveValueFromJson_ExistingPath_ReturnsUpdatedJson()
{
    bool succeeded = _json.TryRemoveValueFromJson("{\"a\":1,\"b\":2}", "b", out string updatedJson, out string message);

    Assert.True(succeeded);
    Assert.False(Newtonsoft.Json.Linq.JObject.Parse(updatedJson).ContainsKey("b"));
}

[Fact]
public void TryRemoveValueFromJson_PathNotFound_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryRemoveValueFromJson("{\"a\":1}", "missing", out string updatedJson, out string message);

    Assert.False(succeeded);
    Assert.Null(updatedJson);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryGetArrayLength_ArrayPath_ReturnsCount()
{
    bool succeeded = _json.TryGetArrayLength("{\"items\":[1,2,3]}", "items", out int length, out string message);

    Assert.True(succeeded);
    Assert.Equal(3, length);
}

[Fact]
public void TryGetArrayLength_NonArrayPath_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryGetArrayLength("{\"items\":1}", "items", out int length, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryAppendToJsonArray_ArrayPath_ReturnsUpdatedJsonWithNewElement()
{
    bool succeeded = _json.TryAppendToJsonArray("{\"items\":[1,2]}", "items", "3", out string updatedJson, out string message);

    Assert.True(succeeded);
    Assert.Equal(3, Newtonsoft.Json.Linq.JObject.Parse(updatedJson)["items"].Count());
}

[Fact]
public void TryAppendToJsonArray_NonArrayPath_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryAppendToJsonArray("{\"items\":1}", "items", "3", out string updatedJson, out string message);

    Assert.False(succeeded);
    Assert.Null(updatedJson);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryAppendToJsonArray_MalformedElementJson_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryAppendToJsonArray("{\"items\":[1]}", "items", "{not json", out string updatedJson, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}
```

Add `using System.Linq;` to the test file's usings (for `.Count()` on a
`JToken`).

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — the three new methods don't exist yet.

- [ ] **Step 3: Implement the three methods**

Fill in the `#region Array and removal` block in `JsonUtils.cs`. All three
new public methods get `[Category("Json - Array")]` and a `[Description]`:

```csharp
#region Array and removal

/// <summary>Removes a value at a JSONPath.</summary>
/// <param name="json">The JSON text to update.</param>
/// <param name="path">A JSONPath expression identifying an existing value.</param>
/// <param name="updatedJson">The updated JSON text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to an existing value that was removed.</returns>
[Category("Json - Array")]
[Description("Removes a value at a JSONPath. Never throws.")]
public bool TryRemoveValueFromJson(string json, string path, out string updatedJson, out string message)
{
    updatedJson = null;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        JToken target = root.SelectToken(path);
        if (target == null)
        {
            message = $"Path '{path}' did not resolve to an existing value.";
            return false;
        }
        target.Remove();
        updatedJson = root.ToString(Formatting.None);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryRemoveValueFromJson), exception);
        return false;
    }
}

/// <summary>Reports the element count of an array at a JSONPath.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression identifying an array.</param>
/// <param name="length">The element count on success; <c>0</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to an array.</returns>
[Category("Json - Array")]
[Description("Reports the element count of an array at a JSONPath. Never throws.")]
public bool TryGetArrayLength(string json, string path, out int length, out string message)
{
    length = 0;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        JToken token = root.SelectToken(path);
        if (token == null)
        {
            message = $"Path '{path}' did not resolve to a value.";
            return false;
        }
        if (token is not JArray array)
        {
            message = $"Path '{path}' resolved to a {token.Type}, not an array.";
            return false;
        }
        length = array.Count;
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryGetArrayLength), exception);
        return false;
    }
}

/// <summary>Appends a JSON-fragment element to an array at a JSONPath.</summary>
/// <param name="json">The JSON text to update.</param>
/// <param name="path">A JSONPath expression identifying an array.</param>
/// <param name="valueJson">The new element, as a JSON fragment (e.g. <c>"3"</c>, <c>"\"text\""</c>, <c>"{\"sku\":\"C\"}"</c>).</param>
/// <param name="updatedJson">The updated JSON text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to an array that the element was appended to.</returns>
[Category("Json - Array")]
[Description("Appends a JSON-fragment element to an array at a JSONPath. Never throws.")]
public bool TryAppendToJsonArray(string json, string path, string valueJson, out string updatedJson, out string message)
{
    updatedJson = null;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        JToken token = root.SelectToken(path);
        if (token == null)
        {
            message = $"Path '{path}' did not resolve to a value.";
            return false;
        }
        if (token is not JArray array)
        {
            message = $"Path '{path}' resolved to a {token.Type}, not an array.";
            return false;
        }
        JToken element = JToken.Parse(valueJson);
        array.Add(element);
        updatedJson = root.ToString(Formatting.None);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryAppendToJsonArray), exception);
        return false;
    }
}

#endregion
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 38 tests (31 prior + 7 new).

- [ ] **Step 5: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils array operations and removal"
```

## Task 6: Formatting

**Files:**
- Modify: `src/jsonutils/JsonUtils.cs` (the `#region Formatting` block)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void TryPrettyPrintJson_ValidJson_ReturnsIndentedText()
{
    bool succeeded = _json.TryPrettyPrintJson("{\"a\":1}", out string formattedJson, out string message);

    Assert.True(succeeded);
    Assert.Contains("\n", formattedJson);
}

[Fact]
public void TryPrettyPrintJson_MalformedJson_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryPrettyPrintJson("{not json", out string formattedJson, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryMinifyJson_IndentedJson_ReturnsCompactText()
{
    bool succeeded = _json.TryMinifyJson("{\n  \"a\": 1\n}", out string minifiedJson, out string message);

    Assert.True(succeeded);
    Assert.Equal("{\"a\":1}", minifiedJson);
}

[Fact]
public void TryMinifyJson_MalformedJson_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryMinifyJson("{not json", out string minifiedJson, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — `TryPrettyPrintJson`/`TryMinifyJson` don't exist yet.

- [ ] **Step 3: Implement both methods**

Fill in the `#region Formatting` block in `JsonUtils.cs`. Both new public
methods get `[Category("Json - Format")]` and a `[Description]`:

```csharp
#region Formatting

/// <summary>Reformats JSON text with indentation.</summary>
/// <param name="json">The JSON text to reformat.</param>
/// <param name="formattedJson">The indented JSON text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="json"/> parsed and was reformatted.</returns>
[Category("Json - Format")]
[Description("Reformats JSON text with indentation. Never throws.")]
public bool TryPrettyPrintJson(string json, out string formattedJson, out string message)
{
    formattedJson = null;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        formattedJson = root.ToString(Formatting.Indented);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryPrettyPrintJson), exception);
        return false;
    }
}

/// <summary>Reformats JSON text with all insignificant whitespace removed.</summary>
/// <param name="json">The JSON text to reformat.</param>
/// <param name="minifiedJson">The compact JSON text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="json"/> parsed and was reformatted.</returns>
[Category("Json - Format")]
[Description("Reformats JSON text with all insignificant whitespace removed. Never throws.")]
public bool TryMinifyJson(string json, out string minifiedJson, out string message)
{
    minifiedJson = null;
    message = null;
    try
    {
        JToken root = JToken.Parse(json);
        minifiedJson = root.ToString(Formatting.None);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryMinifyJson), exception);
        return false;
    }
}

#endregion
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 42 tests (38 prior + 4 new).

- [ ] **Step 5: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils formatting methods"
```

## Task 7: Documentation

**Files:**
- Create: `src/jsonutils/README.md`
- Modify: `README.md`
- Modify: `scripts/Package-Release.ps1`

- [ ] **Step 1: `src/jsonutils/README.md`**

```markdown
# JsonAutomation

A Pega Robot Studio-ready component (`JsonUtils`) for reading, updating,
validating, and transforming JSON via real JSONPath, as a full replacement
for the native `Json` component's dot-notation-only path support. Like every
component in this suite, its methods report recoverable failures as `False`
with a descriptive message instead of throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `JsonAutomation`
- Assembly: `JsonAutomation`

## Methods

| Method | Signature | Description |
|---|---|---|
| `TryDeserializeObject` | `(string json, string typeName, out object result, out string message) : bool` | Deserializes JSON into an instance of the named .NET type. |
| `TrySerializeObject` | `(object value, out string json, out string message) : bool` | Serializes an object to JSON. |
| `TryGetValueFromJson` | `(string json, string path, out string value, out string message) : bool` | Extracts a single value at a JSONPath. |
| `TrySetValueInJson` | `(string json, string path, string value, out string updatedJson, out string message) : bool` | Updates a value at an existing JSONPath. |
| `IsValidJson` | `(string json, out string message) : bool` | Checks whether text is well-formed JSON. |
| `TryGetStringValue` | `(string json, string path, out string value, out string message) : bool` | Extracts a value as a string. |
| `TryGetIntValue` | `(string json, string path, out int value, out string message) : bool` | Extracts a value as an int. |
| `TryGetBoolValue` | `(string json, string path, out bool value, out string message) : bool` | Extracts a value as a bool. |
| `TryGetDoubleValue` | `(string json, string path, out double value, out string message) : bool` | Extracts a value as a double. |
| `TryGetDateTimeValue` | `(string json, string path, out DateTime value, out string message) : bool` | Extracts a value as a DateTime. |
| `TryGetValuesFromJson` | `(string json, string path, string delimiter, out string delimitedValues, out string message) : bool` | Extracts every value matching a JSONPath, delimited. |
| `TryGetValueType` | `(string json, string path, out JsonValueKind kind, out string message) : bool` | Reports the kind of value at a JSONPath. |
| `TryRemoveValueFromJson` | `(string json, string path, out string updatedJson, out string message) : bool` | Removes a value at a JSONPath. |
| `TryGetArrayLength` | `(string json, string path, out int length, out string message) : bool` | Reports an array's element count. |
| `TryAppendToJsonArray` | `(string json, string path, string valueJson, out string updatedJson, out string message) : bool` | Appends an element to an array. |
| `TryPrettyPrintJson` | `(string json, out string formattedJson, out string message) : bool` | Reformats JSON with indentation. |
| `TryMinifyJson` | `(string json, out string minifiedJson, out string message) : bool` | Reformats JSON with whitespace removed. |

## JSONPath syntax

Path expressions use Newtonsoft.Json's JSONPath dialect:

- `order.status` — a property.
- `items[0].sku` — an array element's property.
- `items[*].sku` — every element's `sku` (use with `TryGetValuesFromJson`).
- `items[?(@.price > 10)].sku` — a filter expression (use with `TryGetValuesFromJson`).
- `$..sku` — recursive descent: every `sku` anywhere in the document.

## Notes & Caveats

- **Never throws.** Malformed JSON, a path that doesn't resolve, and a type
  mismatch at a resolved path (e.g. `TryGetIntValue` against a string) all
  return `False` with a descriptive message instead of throwing.
- **Newtonsoft.Json dependency.** This is the suite's second component (after
  `ServiceUtils`) with an external NuGet dependency. It's what makes real
  JSONPath - wildcards, recursive descent, filter expressions - possible
  without hand-rolling a path parser; the suite's other JSON handling
  elsewhere uses the BCL's `System.Text.Json`, which doesn't support JSONPath
  querying.
- **`TrySetValueInJson` requires the path to already exist.** It replaces a
  value in place; it does not create new object properties or array elements
  along the way. Use `TryAppendToJsonArray` to add array elements.
- **`TrySetValueInJson`'s new value is always set as a JSON string scalar** -
  it does not accept a JSON fragment for nested objects/arrays. This matches
  the native `Json` component's string-typed `value` parameter.
- **`TryDeserializeObject` takes the target type as a string, not a generic
  parameter.** This matches the native `Json` component's own
  `DeserializeObject(string jsonString, string typeString, out object deserializedObject)`
  shape (see the `pega-robotic-automation` skill, ch11) and avoids being the
  only generic public method in this 17-component suite — Robot Studio's
  designer binds parameters/outputs via reflection over closed, concrete
  types. `typeName` is resolved via `Type.GetType(typeName)`: a simple name
  only resolves types in `mscorlib`/already-loaded assemblies, so a type
  defined elsewhere in the same Robot Studio project may need its
  assembly-qualified name (`Type.AssemblyQualifiedName`).
- **On the native `Json` component's `SerializeObject` `⚠@default=SingleOutput`
  annotation:** `TrySerializeObject` keeps the standard bool+out signature
  for consistency with every other method in this suite. Robot Studio's
  designer can still be configured to show only the `json` output if a
  single-port surface is wanted.
- **Phase 2 (not yet implemented):** `MergeJson`, `DiffJson`, JSON↔XML
  conversion, and array filter/sort helpers (`FilterJsonArrayByField`,
  `SortJsonArrayByField`). JSON **Schema** validation is not planned at all -
  Newtonsoft's schema validator is a separate commercially-licensed package.
```

- [ ] **Step 2: Add the component to the root `README.md` table**

In `README.md`, insert this row into the component table, alphabetically
between the `filewatchutils` and `keyboardutils` rows:

```
| [jsonutils](src/jsonutils/README.md) | `JsonAutomation` | Reads, updates, validates, and transforms JSON via real JSONPath, replacing the native Json component's dot-notation-only path support. |
```

Also add `JsonUtils` and its `dotnet test` command to the "Testing" section's
Linux-runnable-xunit-projects list (the paragraph starting "`DialogUtils`,
`CommandLineUtils`, ... additionally have plain xunit projects"), following
the exact pattern of the other entries in that comma-and-"and"-joined list
and its matching `dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj`
command.

- [ ] **Step 3: Register the assembly in `scripts/Package-Release.ps1`**

In `scripts/Package-Release.ps1`, add `"JsonAutomation.dll"` to the
`$releaseAssemblies` array (this array is hardcoded, not auto-discovered -
see the `EventLogUtils` PR #63 gotcha this repo has already hit once):

```powershell
$releaseAssemblies = @(
    "MouseAutomation.dll"
    "ScreenCaptureAutomation.dll"
    "KeyboardAutomation.dll"
    "WindowAutomation.dll"
    "OcrAutomation.dll"
    "DialogAutomation.dll"
    "UIAutomation.dll"
    "CommandLineAutomation.dll"
    "ServiceAutomation.dll"
    "EventAutomation.dll"
    "EventLogAutomation.dll"
    "SessionAutomation.dll"
    "FileWatchAutomation.dll"
    "ArchiveAutomation.dll"
    "TerminalAutomation.dll"
    "LocalQueueAutomation.dll"
    "StackAutomation.dll"
    "DataBagAutomation.dll"
    "JsonAutomation.dll"
)
```

`scripts/Package-Documentation.ps1` needs no change - it auto-discovers
component READMEs via `Get-ChildItem` globs under `src/`.

- [ ] **Step 4: Commit**

```bash
git add src/jsonutils/README.md README.md scripts/Package-Release.ps1
git commit -m "Add JsonUtils documentation and release packaging entry"
```

## Task 8: Final verification

- [ ] **Step 1: Full build**

```bash
dotnet build src/AwesomeRpaUtils.sln
```

Expected: clean build, all projects including the new `JsonUtils`/
`JsonUtils.Tests` succeed.

- [ ] **Step 2: Full test run**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 42/42.

- [ ] **Step 3: Package-Release dry run**

```bash
pwsh ./scripts/Package-Release.ps1
```

If this can't run on this host (Windows-only build outputs), at minimum
confirm `dotnet build src/AwesomeRpaUtils.sln` produced a `JsonAutomation.dll`
under each TFM's output directory, matching what the other 18 entries in
`$releaseAssemblies` resolve to.

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin add-jsonutils
gh pr create --title "Add JsonUtils component" --body "$(cat <<'EOF'
## Summary
- New JsonUtils component: full replacement for Pega's native Json component,
  built on Newtonsoft.Json for real JSONPath support (wildcards, recursive
  descent, filter expressions).
- Adds validation, typed scalar getters, multi-match querying, array
  operations, and formatting gap-fillers beyond native parity.
- See project-docs/plans/2026-09-08-jsonutils-design.md for the full design
  and phase-2 (deferred) items.

## Test plan
- [x] dotnet build src/AwesomeRpaUtils.sln
- [x] dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj (42/42)
EOF
)"
```

- [ ] **Step 5: Remove the worktree after the PR is open**

```bash
cd /mnt/disk0/csharp/awesome_rpa_utils
git worktree remove .worktrees/jsonutils
```

## Verification

- [ ] `dotnet build src/AwesomeRpaUtils.sln` - clean, no regressions to the
      other 18 shipped components.
- [ ] `dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj` -
      42/42 passing, fully on this Linux host (no Windows-only skips, unlike
      `UIAutomation.Tests`/`OcrUtils.Tests`/`ScreenCaptureUtils.Tests`).
- [ ] `scripts/Package-Release.ps1`'s `$releaseAssemblies` includes
      `JsonAutomation.dll`.
- [ ] Root `README.md` and `src/jsonutils/README.md` both updated.
- [ ] Worktree removed after the PR is opened.
