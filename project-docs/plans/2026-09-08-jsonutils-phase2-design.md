# JsonUtils Phase 2 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the four capability groups explicitly deferred as
"Phase 2" in the original `JsonUtils` design
([2026-09-08-jsonutils-design.md](2026-09-08-jsonutils-design.md)):
`MergeJson`, `DiffJson`, JSON↔XML conversion, and array filter/sort helpers.
This is an extension of the existing `JsonUtils` component (`src/jsonutils/`,
assembly `JsonAutomation`) — no new component, no new `.csproj`, no `.sln`
changes. PR #77 (Phase 1) is already merged to `main`.

**Non-goals:** JSON Schema validation remains excluded (not deferred) for
the same reason as Phase 1 — Newtonsoft's schema validator is a separate
commercially-licensed package. A JSON Patch (RFC 6902) diff format was
considered and rejected in favor of a delimited differing-paths list,
matching this suite's existing convention (see Design Decisions below).

## Design decisions (resolved via brainstorming before this doc was written)

- **`TryMergeJson`'s conflict rule**: the second document (`overrideJson`)
  wins on scalar conflicts — the common "apply overrides on top of a base"
  use case. Arrays present in both documents concatenate (base's elements
  then override's), matching Newtonsoft's own `MergeArrayHandling.Concat`
  default rather than replacing or merging by index (replace-by-index is
  surprising for simple value arrays and mismatched lengths).
- **`TryDiffJson`'s output shape**: a delimited list of differing JSONPath
  strings, not a JSON Patch document. Matches this suite's existing
  delimited-list convention (`TryGetValuesFromJson`) and is easier for a
  Robot Studio developer to consume than a new patch format this suite has
  never used elsewhere. The outer `bool` return follows the suite's
  established "did the operation itself succeed" convention (`false` only
  on malformed JSON input) — NOT "are the documents equal." The actual
  comparison result lives in the `areEqual`/`differingPaths` out parameters,
  the same separation-of-concerns `TryGetArrayLength` and similar methods
  already use (operation success vs. domain answer).
- **`TryConvertJsonToXml`'s root element name**: a required parameter, not
  optional/auto-detected. Newtonsoft's `JsonConvert.DeserializeXmlNode`
  needs an explicit root name whenever the JSON's top level isn't already a
  single-rooted object (a bare array, or an object with multiple top-level
  keys); making it always-required avoids two different code paths to
  document and get right.

## Method surface

All six new methods follow the established never-throws contract (`bool`
return + trailing `out string message`) and use the shared `ParseJson`
helper (never raw `JToken.Parse`) for any JSON text they read.

**Merge** (new category `Json - Merge`):

| Method | Purpose |
|---|---|
| `TryMergeJson(string baseJson, string overrideJson, out string mergedJson, out string message)` | Merges two JSON objects; `overrideJson` wins on scalar conflicts, arrays concatenate. Fails if either input isn't a JSON object at the root. |

**Compare** (new category `Json - Compare`):

| Method | Purpose |
|---|---|
| `TryDiffJson(string json1, string json2, string delimiter, out bool areEqual, out string differingPaths, out string message)` | Compares two JSON documents; `areEqual` is the comparison result, `differingPaths` is a delimited list of paths where values differ (empty when equal). Returns `false` only if either input is malformed JSON. |

**Convert** (new category `Json - Convert`):

| Method | Purpose |
|---|---|
| `TryConvertJsonToXml(string json, string rootElementName, out string xml, out string message)` | Converts JSON to XML text via Newtonsoft's `JsonConvert.DeserializeXmlNode`, wrapping the result under `rootElementName`. |
| `TryConvertXmlToJson(string xml, out string json, out string message)` | Converts XML text to JSON via `JsonConvert.SerializeXmlNode`. No root name needed — XML always has exactly one root. |

**Array filter/sort** (extends existing category `Json - Array`):

| Method | Purpose |
|---|---|
| `TryFilterJsonArrayByField(string json, string path, string fieldName, JsonComparisonOperator comparisonOperator, string value, out string filteredJson, out string message)` | Filters an array at `path` to elements whose `fieldName` matches `value` under `comparisonOperator`. |
| `TrySortJsonArrayByField(string json, string path, string fieldName, bool ascending, out string sortedJson, out string message)` | Sorts an array at `path` by `fieldName`'s value. |

**New repo-owned enum**, `JsonComparisonOperator` (mirrors the
`JsonValueKind` pattern from Phase 1 — a small, purpose-built enum rather
than exposing a library type):

```csharp
public enum JsonComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains
}
```

`Contains` applies to string fields (substring match); the four ordering
operators use `JValue`'s own `IComparable` semantics (numeric-aware when
both sides are numeric, ordinal string comparison otherwise) via
`JToken.CompareTo`; `Equals`/`NotEquals` use `JToken.DeepEquals` against the
field value parsed from the caller-supplied `value` string through the
field's own token type (so `"30"` compares equal to a JSON number `30`, not
just to the JSON string `"30"`).

## Architecture

- No new `.csproj`/`.sln` changes — this extends the existing
  `src/jsonutils/JsonUtils.cs` with a 6th, 7th, and 8th-ish `#region` block
  (actually: `Merge`, `Compare`, `Convert` as three new regions; filter/sort
  are added to the existing `Array and removal` region, renamed in
  documentation-only prose to reflect the broader scope — the region
  marker itself can stay `Array and removal` since C# region names aren't
  part of the public contract and renaming it would be a no-op diff noise
  generator for zero benefit).
- New file `src/jsonutils/JsonComparisonOperator.cs` for the new enum,
  matching the precedent of `JsonValueKind.cs` living in its own file.
- `TryDiffJson`'s path-walking comparison is genuinely new logic (not a
  thin wrapper over a Newtonsoft one-liner like nearly everything in
  Phase 1) — implemented as a private recursive helper,
  `CollectDifferingPaths(JToken left, JToken right, string currentPath, List<string> differingPaths)`,
  walking `JObject` keys (union of both sides' property names) and `JArray`
  indices (up to the longer side's length; a missing index on the shorter
  side counts as a difference), and using `JToken.DeepEquals` at the leaves.
  This is the one method in this pass warranting closer implementation-plan
  detail and more thorough test coverage than a typical "wrap one
  Newtonsoft call" method.
- `TryConvertJsonToXml`/`TryConvertXmlToJson` use `System.Xml.XmlDocument`
  internally but never expose it publicly — both methods are string-in,
  string-out, keeping the component's string-based JSON-processing
  convention consistent even where the intermediate representation is XML.
  Needs `using System.Xml;` added to `JsonUtils.cs`.

## Testing

Same TDD-per-method-group approach as Phase 1: input-guard tests
(malformed JSON, non-object root for `TryMergeJson`, malformed XML) plus
real behavioral tests for each method. `TryDiffJson` gets the most test
cases given it's genuinely new logic: equal documents, a changed scalar, an
added/removed key, a changed array element, an added/removed array element,
and nested-path differences. `TryFilterJsonArrayByField` gets one test per
`JsonComparisonOperator` value.

## Documentation

- `src/jsonutils/README.md`'s method table gains 6 new rows; the JSONPath
  syntax / Typical workflow / Notes & Caveats sections get one addition
  each (a worked merge/diff/convert/filter-sort example; a caveat on
  `TryConvertJsonToXml`'s root-name requirement; a caveat documenting
  `TryDiffJson`'s path ordering — a deterministic pre-order walk of
  `json1`'s structure, with any keys/indices that exist only in `json2`
  appended after all of `json1`'s keys at that level. Not alphabetical, not
  guaranteed stable across a Newtonsoft version bump if its own property
  enumeration order ever changed, but deterministic for a given input pair
  on a given Newtonsoft version).
- No root `README.md`/`Package-Release.ps1` changes needed — the component
  and assembly already exist and are already registered.

## Suite conventions followed

- Never-throws contract, `ParseJson` for all reads, `[Category]`/
  `[Description]` on every new public method, repo-owned enum for
  `JsonComparisonOperator` (not exposing a library-specific type).
- New work happens in `.worktrees/jsonutils-phase2` on branch
  `jsonutils-phase2`, PR'd and the worktree removed afterward — same
  workflow as Phase 1, forked from the now-current `main` (which already
  contains the merged Phase 1 component).

---

## Task 1: Scaffold the worktree

- [ ] **Step 1: Create the worktree**

Run from the repo root:

```bash
git worktree add .worktrees/jsonutils-phase2 -b jsonutils-phase2 main
cd .worktrees/jsonutils-phase2
```

All later steps in this plan happen inside `.worktrees/jsonutils-phase2`.

- [ ] **Step 2: Confirm the starting state**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 52/52 (Phase 1's full suite, already merged to `main`).

## Task 2: TryMergeJson

**Files:**
- Modify: `src/jsonutils/JsonUtils.cs` (new `#region Merge` block, added after `#region Formatting`'s `#endregion`)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void TryMergeJson_ScalarConflict_SecondDocumentWins()
{
    bool succeeded = _json.TryMergeJson("{\"a\":1,\"b\":2}", "{\"b\":3}", out string mergedJson, out string message);

    Assert.True(succeeded);
    Assert.Null(message);
    Assert.Equal(3, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["b"]);
    Assert.Equal(1, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["a"]);
}

[Fact]
public void TryMergeJson_ArrayValues_Concatenates()
{
    bool succeeded = _json.TryMergeJson("{\"items\":[1,2]}", "{\"items\":[3]}", out string mergedJson, out string message);

    Assert.True(succeeded);
    Assert.Equal(new[] { 1, 2, 3 }, Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["items"].ToObject<int[]>());
}

[Fact]
public void TryMergeJson_BaseNotObject_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryMergeJson("[1,2]", "{\"a\":1}", out string mergedJson, out string message);

    Assert.False(succeeded);
    Assert.Null(mergedJson);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryMergeJson_OverrideNotObject_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryMergeJson("{\"a\":1}", "[1,2]", out string mergedJson, out string message);

    Assert.False(succeeded);
    Assert.Null(mergedJson);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryMergeJson_MalformedJson_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryMergeJson("{not json", "{\"a\":1}", out string mergedJson, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryMergeJson_KeyOnlyInOverride_IsAdded()
{
    bool succeeded = _json.TryMergeJson("{\"a\":1}", "{\"c\":2}", out string mergedJson, out string message);

    Assert.True(succeeded);
    Assert.Equal(1, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["a"]);
    Assert.Equal(2, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["c"]);
}

[Fact]
public void TryMergeJson_NestedObjects_RecursesRatherThanReplacing()
{
    bool succeeded = _json.TryMergeJson("{\"nested\":{\"x\":1,\"y\":2}}", "{\"nested\":{\"y\":9,\"z\":3}}", out string mergedJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JObject nested = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["nested"];
    Assert.Equal(1, (int)nested["x"]);
    Assert.Equal(9, (int)nested["y"]);
    Assert.Equal(3, (int)nested["z"]);
}

[Fact]
public void TryMergeJson_ExplicitNullInOverride_DoesNotClearBaseValue()
{
    bool succeeded = _json.TryMergeJson("{\"a\":1}", "{\"a\":null}", out string mergedJson, out string message);

    Assert.True(succeeded);
    Assert.Equal(1, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["a"]);
}
```

**Note (added after code-quality review):** the three tests above (key-only-
in-override, nested-object recursion, explicit-null-in-override) were
missing from this plan's original test list. `JObject.Merge`'s default
`MergeNullValueHandling.Ignore` means an explicit JSON `null` in
`overrideJson` does NOT clear the base's existing value for that key - a
real, easy-to-get-wrong assumption for a method framed as "apply overrides
on a base," now locked in by a test and documented in the method's XML
comment (added below).

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — `JsonUtils` has no `TryMergeJson` member yet.

- [ ] **Step 3: Implement `TryMergeJson`**

Add a new `#region Merge` block in `JsonUtils.cs`, immediately after the
existing `#region Formatting` block's `#endregion` (i.e. as a 6th region,
after the file's current last region):

```csharp
#region Merge

/// <summary>Merges two JSON objects. Values in <paramref name="overrideJson"/> win on
/// scalar conflicts; array values present in both documents are concatenated; nested
/// objects present in both documents are merged recursively, not replaced wholesale.
/// An explicit JSON <c>null</c> in <paramref name="overrideJson"/> does NOT clear the
/// base's existing value for that key (Newtonsoft's default null-merge behavior).</summary>
/// <param name="baseJson">The base JSON object.</param>
/// <param name="overrideJson">The JSON object whose values take precedence on conflict.</param>
/// <param name="mergedJson">The merged JSON text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if both inputs parsed as JSON objects and were merged.</returns>
[Category("Json - Merge")]
[Description("Merges two JSON objects, with the second document's values winning on conflict. Never throws.")]
public bool TryMergeJson(string baseJson, string overrideJson, out string mergedJson, out string message)
{
    mergedJson = null;
    message = null;
    try
    {
        JToken baseToken = ParseJson(baseJson);
        JToken overrideToken = ParseJson(overrideJson);
        if (baseToken is not JObject baseObject)
        {
            message = $"baseJson must be a JSON object, but was a {baseToken.Type}.";
            return false;
        }
        if (overrideToken is not JObject overrideObject)
        {
            message = $"overrideJson must be a JSON object, but was a {overrideToken.Type}.";
            return false;
        }
        baseObject.Merge(overrideObject, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Concat
        });
        mergedJson = baseObject.ToString(Formatting.None);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryMergeJson), exception);
        return false;
    }
}

#endregion
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 60 tests (52 prior + 8 new — the null-merge, key-addition, and nested-object tests added after code-quality review).

- [ ] **Step 5: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils TryMergeJson"
```

## Task 3: TryDiffJson

**Files:**
- Modify: `src/jsonutils/JsonUtils.cs` (new `#region Compare` block)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

This is the one method in this pass with genuinely new logic (not a thin
wrapper over a single Newtonsoft call) — a recursive tree-walk comparing two
`JToken`s and collecting the JSONPath-style paths where they differ. Give it
proportionally more attention during implementation and review than the
other tasks in this plan.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void TryDiffJson_EqualDocuments_ReturnsTrueWithEmptyPaths()
{
    bool succeeded = _json.TryDiffJson("{\"a\":1,\"b\":2}", "{\"a\":1,\"b\":2}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.True(areEqual);
    Assert.Equal(string.Empty, differingPaths);
}

[Fact]
public void TryDiffJson_ChangedScalar_ReturnsPathToScalar()
{
    bool succeeded = _json.TryDiffJson("{\"a\":1}", "{\"a\":2}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.False(areEqual);
    Assert.Equal("a", differingPaths);
}

[Fact]
public void TryDiffJson_AddedKey_ReturnsPathToKey()
{
    bool succeeded = _json.TryDiffJson("{\"a\":1}", "{\"a\":1,\"b\":2}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.False(areEqual);
    Assert.Equal("b", differingPaths);
}

[Fact]
public void TryDiffJson_ChangedArrayElement_ReturnsIndexedPath()
{
    bool succeeded = _json.TryDiffJson("{\"items\":[1,2,3]}", "{\"items\":[1,9,3]}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.False(areEqual);
    Assert.Equal("items[1]", differingPaths);
}

[Fact]
public void TryDiffJson_ArrayLengthDifference_ReturnsPathForExtraElement()
{
    bool succeeded = _json.TryDiffJson("{\"items\":[1,2]}", "{\"items\":[1,2,3]}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.False(areEqual);
    Assert.Equal("items[2]", differingPaths);
}

[Fact]
public void TryDiffJson_NestedPathDifference_ReturnsFullDottedPath()
{
    bool succeeded = _json.TryDiffJson("{\"order\":{\"items\":[{\"sku\":\"A\"}]}}", "{\"order\":{\"items\":[{\"sku\":\"B\"}]}}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.False(areEqual);
    Assert.Equal("order.items[0].sku", differingPaths);
}

[Fact]
public void TryDiffJson_MultipleDifferences_ReturnsAllPathsDelimited()
{
    bool succeeded = _json.TryDiffJson("{\"a\":1,\"b\":2}", "{\"a\":9,\"b\":9}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.False(areEqual);
    Assert.Equal("a,b", differingPaths);
}

[Fact]
public void TryDiffJson_EntirelyDifferentRootTypes_ReturnsDollarSign()
{
    bool succeeded = _json.TryDiffJson("{\"a\":1}", "[1,2,3]", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.False(areEqual);
    Assert.Equal("$", differingPaths);
}

[Fact]
public void TryDiffJson_MalformedJson_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryDiffJson("{not json", "{\"a\":1}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryDiffJson_NestedNullVsValue_ReturnsPathNotConflatedWithAbsentKey()
{
    bool succeeded = _json.TryDiffJson("{\"a\":{\"b\":null}}", "{\"a\":{\"b\":1}}", ",", out bool areEqual, out string differingPaths, out string message);

    Assert.True(succeeded);
    Assert.False(areEqual);
    Assert.Equal("a.b", differingPaths);
}
```

**Note (added after code-quality review):** the test above was missing
from this plan's original list. The code already correctly distinguishes a
key holding a JSON `null` (a `JValue` of type `Null`, which recurses and
diffs normally) from a key that's genuinely absent (`leftObject[key] ==
null` in C#, which short-circuits to reporting the whole key as differing)
- but nothing proved it. Added to lock in a distinction a future refactor
using `?.` shorthand could easily and silently break.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — `TryDiffJson` doesn't exist yet.

- [ ] **Step 3: Implement `TryDiffJson`**

Add a new `#region Compare` block in `JsonUtils.cs`, after the new `#region
Merge` block:

```csharp
#region Compare

private static void CollectDifferingPaths(JToken left, JToken right, string currentPath, List<string> differingPaths)
{
    if (JToken.DeepEquals(left, right))
    {
        return;
    }

    if (left is JObject leftObject && right is JObject rightObject)
    {
        List<string> keys = new List<string>();
        foreach (JProperty property in leftObject.Properties())
        {
            keys.Add(property.Name);
        }
        foreach (JProperty property in rightObject.Properties())
        {
            if (!keys.Contains(property.Name))
            {
                keys.Add(property.Name);
            }
        }
        foreach (string key in keys)
        {
            string childPath = currentPath.Length == 0 ? key : $"{currentPath}.{key}";
            JToken leftChild = leftObject[key];
            JToken rightChild = rightObject[key];
            if (leftChild == null || rightChild == null)
            {
                differingPaths.Add(childPath);
            }
            else
            {
                CollectDifferingPaths(leftChild, rightChild, childPath, differingPaths);
            }
        }
        return;
    }

    if (left is JArray leftArray && right is JArray rightArray)
    {
        int maxLength = Math.Max(leftArray.Count, rightArray.Count);
        for (int i = 0; i < maxLength; i++)
        {
            string childPath = $"{currentPath}[{i}]";
            if (i >= leftArray.Count || i >= rightArray.Count)
            {
                differingPaths.Add(childPath);
            }
            else
            {
                CollectDifferingPaths(leftArray[i], rightArray[i], childPath, differingPaths);
            }
        }
        return;
    }

    differingPaths.Add(currentPath.Length == 0 ? "$" : currentPath);
}

/// <summary>Compares two JSON documents and reports the paths where they differ. Paths
/// are collected via a pre-order walk of <paramref name="json1"/>'s structure - not
/// alphabetically sorted - with any keys/indices that exist only in <paramref name="json2"/>
/// appended after all of <paramref name="json1"/>'s keys at that level. A difference at
/// the document root itself (e.g. mismatched root types) is reported as <c>"$"</c>.</summary>
/// <param name="json1">The first JSON document.</param>
/// <param name="json2">The second JSON document.</param>
/// <param name="delimiter">The delimiter to join differing paths with.</param>
/// <param name="areEqual"><c>True</c> if the two documents are equivalent.</param>
/// <param name="differingPaths">A delimited list of paths where the documents differ; empty when <paramref name="areEqual"/> is <c>true</c>.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if the comparison completed, regardless of whether the documents are
/// equal - <c>False</c> only if either input is malformed JSON.</returns>
[Category("Json - Compare")]
[Description("Compares two JSON documents and reports the paths where they differ. Never throws.")]
public bool TryDiffJson(string json1, string json2, string delimiter, out bool areEqual, out string differingPaths, out string message)
{
    areEqual = false;
    differingPaths = null;
    message = null;
    try
    {
        JToken left = ParseJson(json1);
        JToken right = ParseJson(json2);
        List<string> paths = new List<string>();
        CollectDifferingPaths(left, right, string.Empty, paths);
        areEqual = paths.Count == 0;
        differingPaths = string.Join(delimiter, paths);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryDiffJson), exception);
        return false;
    }
}

#endregion
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 70 tests (60 prior + 10 new — includes the nested-null-vs-value test added after code-quality review).

- [ ] **Step 5: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils TryDiffJson"
```

## Task 4: JSON↔XML conversion

**Files:**
- Modify: `src/jsonutils/JsonUtils.cs` (new `#region Convert` block; `System.Xml.XmlDocument` referenced fully-qualified inline, no new `using`)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

**A note on the test data below**: Newtonsoft's `JsonConvert.DeserializeXmlNode`
can use a JSON object's single top-level property as the XML root instead
of `rootElementName` when the JSON has exactly one top-level key - the
exact rule isn't being guessed at here. Both tests below use a JSON object
with **two** top-level keys specifically so `rootElementName` is
unambiguously required and used, avoiding that edge case entirely. If you
hit a build/test surprise suggesting single-key JSON behaves differently
than expected, that's an orthogonal detail this plan deliberately routes
around rather than one you need to chase down.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void TryConvertJsonToXml_MultiKeyObject_WrapsInRootElement()
{
    bool succeeded = _json.TryConvertJsonToXml("{\"a\":1,\"b\":2}", "root", out string xml, out string message);

    Assert.True(succeeded);
    Assert.Contains("<root>", xml);
    Assert.Contains("<a>1</a>", xml);
    Assert.Contains("<b>2</b>", xml);
}

[Fact]
public void TryConvertJsonToXml_MalformedJson_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryConvertJsonToXml("{not json", "root", out string xml, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TryConvertXmlToJson_SimpleXml_ReturnsJson()
{
    bool succeeded = _json.TryConvertXmlToJson("<root><a>1</a></root>", out string json, out string message);

    Assert.True(succeeded);
    Assert.Equal("1", (string)Newtonsoft.Json.Linq.JObject.Parse(json)["root"]["a"]);
}

[Fact]
public void TryConvertXmlToJson_MalformedXml_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryConvertXmlToJson("<root><a>1</a>", out string json, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void ConvertRoundTrip_JsonToXmlToJson_PreservesData()
{
    bool toXmlSucceeded = _json.TryConvertJsonToXml("{\"name\":\"Ada\",\"age\":30}", "person", out string xml, out string toXmlMessage);
    Assert.True(toXmlSucceeded);

    bool toJsonSucceeded = _json.TryConvertXmlToJson(xml, out string json, out string toJsonMessage);
    Assert.True(toJsonSucceeded);

    bool getSucceeded = _json.TryGetValueFromJson(json, "person.name", out string value, out string getMessage);
    Assert.True(getSucceeded);
    Assert.Equal("Ada", value);
}

[Fact]
public void TryConvertXmlToJson_XmlWithDtd_ReturnsFalseWithMessage()
{
    string xmlWithDtd = "<?xml version=\"1.0\"?><!DOCTYPE root [<!ENTITY foo \"bar\">]><root><a>&foo;</a></root>";
    bool succeeded = _json.TryConvertXmlToJson(xmlWithDtd, out string json, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}
```

**Security note (added after automated security review):** the original
`TryConvertXmlToJson` used `XmlDocument.LoadXml(xml)` directly on
caller-supplied text - a known XXE/entity-expansion risk pattern.
`.NET Core`'s `XmlDocument.XmlResolver` defaults to `null` on this
component's target frameworks, which already blocks the classic
external-entity data-exfiltration attack - but that alone does NOT stop
internal DTD entity-expansion attacks (e.g. "billion laughs"), which don't
need the resolver at all. The fix routes parsing through
`XmlReader.Create(stringReader)` (whose `XmlReaderSettings.DtdProcessing`
defaults to `Prohibit`, rejecting any DOCTYPE outright) and
`document.Load(reader)` instead of `LoadXml`, which is the standard .NET
guidance for safely parsing untrusted XML text - simpler and more complete
than the minimal "just set `XmlResolver = null`" fix, since this component
has no legitimate need to support DTDs for JSON-conversion input at all.
`TryConvertJsonToXml` needed no equivalent fix - it only ever *generates*
XML from JSON via `JsonConvert.DeserializeXmlNode`, never parses
caller-supplied XML text, so there's no untrusted-XML-parsing surface on
that path.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — `TryConvertJsonToXml`/`TryConvertXmlToJson` don't exist yet.

- [ ] **Step 3: Implement both methods**

Add a new `#region Convert` block, after the new `#region Compare` block.
**Do NOT add `using System.Xml;`** - `System.Xml` and `Newtonsoft.Json` both
declare a type/enum named `Formatting`, so a blanket `using` makes every
existing bare `Formatting.None`/`Formatting.Indented` reference elsewhere
in this file (across the `Merge`, `Compare`, `Array and removal`, and
`Formatting` regions - all off-limits for this task) ambiguous, failing the
build with `CS0104`. Fully-qualify `System.Xml.XmlDocument` inline in these
two methods instead - this was caught during implementation of this exact
task, not a hypothetical:

```csharp
#region Convert

/// <summary>Converts JSON text to XML text.</summary>
/// <param name="json">The JSON text to convert.</param>
/// <param name="rootElementName">The XML root element name to wrap the converted content in.</param>
/// <param name="xml">The XML text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if conversion succeeded.</returns>
[Category("Json - Convert")]
[Description("Converts JSON text to XML text. Never throws.")]
public bool TryConvertJsonToXml(string json, string rootElementName, out string xml, out string message)
{
    xml = null;
    message = null;
    try
    {
        System.Xml.XmlDocument document = JsonConvert.DeserializeXmlNode(json, rootElementName);
        xml = document.OuterXml;
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryConvertJsonToXml), exception);
        return false;
    }
}

/// <summary>Converts XML text to JSON text. Rejects any XML containing a DOCTYPE
/// declaration (DTD) - this component has no legitimate need to support DTDs for
/// JSON-conversion use cases, and <c>XmlDocument.LoadXml</c>'s permissive parsing
/// defaults are a known XXE/entity-expansion risk for caller-supplied XML that
/// <c>XmlReader.Create</c>'s safe-by-default settings close.</summary>
/// <param name="xml">The XML text to convert.</param>
/// <param name="json">The JSON text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if conversion succeeded.</returns>
[Category("Json - Convert")]
[Description("Converts XML text to JSON text. Never throws.")]
public bool TryConvertXmlToJson(string xml, out string json, out string message)
{
    json = null;
    message = null;
    try
    {
        System.Xml.XmlDocument document = new System.Xml.XmlDocument();
        using (StringReader stringReader = new StringReader(xml))
        using (System.Xml.XmlReader reader = System.Xml.XmlReader.Create(stringReader))
        {
            document.Load(reader);
        }
        json = JsonConvert.SerializeXmlNode(document);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryConvertXmlToJson), exception);
        return false;
    }
}

#endregion
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 76 tests (70 prior + 6 new — includes the DTD-rejection security regression test).

- [ ] **Step 5: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils JSON<->XML conversion"
```

## Task 5: Array filter/sort helpers

**Files:**
- Create: `src/jsonutils/JsonComparisonOperator.cs`
- Modify: `src/jsonutils/JsonUtils.cs` (extends the existing `#region Array and removal` block — the region name itself is unchanged, since it's not part of the public contract and renaming it would just be diff noise)
- Modify: `src/jsonutils/JsonUtils.Tests/JsonUtilsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void TryFilterJsonArrayByField_Equals_ReturnsMatchingElements()
{
    bool succeeded = _json.TryFilterJsonArrayByField("{\"items\":[{\"sku\":\"A\",\"qty\":1},{\"sku\":\"B\",\"qty\":2}]}", "items", "sku", JsonComparisonOperator.Equals, "A", out string filteredJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray filtered = Newtonsoft.Json.Linq.JArray.Parse(filteredJson);
    Assert.Single(filtered);
    Assert.Equal("A", (string)filtered[0]["sku"]);
}

[Fact]
public void TryFilterJsonArrayByField_NotEquals_ExcludesMatchingElements()
{
    bool succeeded = _json.TryFilterJsonArrayByField("{\"items\":[{\"sku\":\"A\"},{\"sku\":\"B\"}]}", "items", "sku", JsonComparisonOperator.NotEquals, "A", out string filteredJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray filtered = Newtonsoft.Json.Linq.JArray.Parse(filteredJson);
    Assert.Single(filtered);
    Assert.Equal("B", (string)filtered[0]["sku"]);
}

[Fact]
public void TryFilterJsonArrayByField_GreaterThan_ComparesNumerically()
{
    bool succeeded = _json.TryFilterJsonArrayByField("{\"items\":[{\"qty\":1},{\"qty\":5},{\"qty\":10}]}", "items", "qty", JsonComparisonOperator.GreaterThan, "4", out string filteredJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray filtered = Newtonsoft.Json.Linq.JArray.Parse(filteredJson);
    Assert.Equal(2, filtered.Count);
}

[Fact]
public void TryFilterJsonArrayByField_GreaterThanOrEqual_IncludesEqualElements()
{
    bool succeeded = _json.TryFilterJsonArrayByField("{\"items\":[{\"qty\":4},{\"qty\":5},{\"qty\":6}]}", "items", "qty", JsonComparisonOperator.GreaterThanOrEqual, "5", out string filteredJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray filtered = Newtonsoft.Json.Linq.JArray.Parse(filteredJson);
    Assert.Equal(2, filtered.Count);
}

[Fact]
public void TryFilterJsonArrayByField_LessThan_ComparesNumerically()
{
    bool succeeded = _json.TryFilterJsonArrayByField("{\"items\":[{\"qty\":1},{\"qty\":5},{\"qty\":10}]}", "items", "qty", JsonComparisonOperator.LessThan, "5", out string filteredJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray filtered = Newtonsoft.Json.Linq.JArray.Parse(filteredJson);
    Assert.Single(filtered);
}

[Fact]
public void TryFilterJsonArrayByField_LessThanOrEqual_IncludesEqualElements()
{
    bool succeeded = _json.TryFilterJsonArrayByField("{\"items\":[{\"qty\":4},{\"qty\":5},{\"qty\":6}]}", "items", "qty", JsonComparisonOperator.LessThanOrEqual, "5", out string filteredJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray filtered = Newtonsoft.Json.Linq.JArray.Parse(filteredJson);
    Assert.Equal(2, filtered.Count);
}

[Fact]
public void TryFilterJsonArrayByField_Contains_MatchesSubstring()
{
    bool succeeded = _json.TryFilterJsonArrayByField("{\"items\":[{\"name\":\"Widget A\"},{\"name\":\"Gadget B\"}]}", "items", "name", JsonComparisonOperator.Contains, "Widget", out string filteredJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray filtered = Newtonsoft.Json.Linq.JArray.Parse(filteredJson);
    Assert.Single(filtered);
}

[Fact]
public void TryFilterJsonArrayByField_NonArrayPath_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TryFilterJsonArrayByField("{\"items\":1}", "items", "sku", JsonComparisonOperator.Equals, "A", out string filteredJson, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}

[Fact]
public void TrySortJsonArrayByField_Ascending_SortsNumerically()
{
    bool succeeded = _json.TrySortJsonArrayByField("{\"items\":[{\"qty\":3},{\"qty\":1},{\"qty\":2}]}", "items", "qty", true, out string sortedJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray sorted = Newtonsoft.Json.Linq.JArray.Parse(sortedJson);
    Assert.Equal(new[] { 1, 2, 3 }, sorted.Select(element => (int)element["qty"]).ToArray());
}

[Fact]
public void TrySortJsonArrayByField_Descending_ReversesOrder()
{
    bool succeeded = _json.TrySortJsonArrayByField("{\"items\":[{\"qty\":1},{\"qty\":3},{\"qty\":2}]}", "items", "qty", false, out string sortedJson, out string message);

    Assert.True(succeeded);
    Newtonsoft.Json.Linq.JArray sorted = Newtonsoft.Json.Linq.JArray.Parse(sortedJson);
    Assert.Equal(new[] { 3, 2, 1 }, sorted.Select(element => (int)element["qty"]).ToArray());
}

[Fact]
public void TrySortJsonArrayByField_NonArrayPath_ReturnsFalseWithMessage()
{
    bool succeeded = _json.TrySortJsonArrayByField("{\"items\":1}", "items", "qty", true, out string sortedJson, out string message);

    Assert.False(succeeded);
    Assert.False(string.IsNullOrEmpty(message));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: build error — `JsonComparisonOperator`, `TryFilterJsonArrayByField`, `TrySortJsonArrayByField` don't exist yet.

- [ ] **Step 3: Create `JsonComparisonOperator.cs`**

```csharp
namespace JsonAutomation
{
    /// <summary>
    /// A comparison operator for filtering a JSON array by a field's value, kept as a
    /// repo-owned enum so this component's public contract doesn't depend on any
    /// particular .NET comparison-operator type.
    /// </summary>
    public enum JsonComparisonOperator
    {
        /// <summary>The field's value equals the comparison value.</summary>
        Equals,
        /// <summary>The field's value does not equal the comparison value.</summary>
        NotEquals,
        /// <summary>The field's value is greater than the comparison value.</summary>
        GreaterThan,
        /// <summary>The field's value is greater than or equal to the comparison value.</summary>
        GreaterThanOrEqual,
        /// <summary>The field's value is less than the comparison value.</summary>
        LessThan,
        /// <summary>The field's value is less than or equal to the comparison value.</summary>
        LessThanOrEqual,
        /// <summary>The field's value, as a string, contains the comparison value as a substring.</summary>
        Contains
    }
}
```

- [ ] **Step 4: Implement the two methods**

Add both new methods and their two private helpers to the END of the
existing `#region Array and removal` block (before its `#endregion`), right
after `TryAppendToJsonArray`:

```csharp
private static int CompareValues(JToken left, JToken right)
{
    double leftNumber;
    double rightNumber;
    bool bothNumeric = double.TryParse(left?.ToString(), out leftNumber) && double.TryParse(right?.ToString(), out rightNumber);
    if (bothNumeric)
    {
        return leftNumber.CompareTo(rightNumber);
    }
    return string.CompareOrdinal(left?.ToString(), right?.ToString());
}

private static bool CompareField(JToken fieldToken, JsonComparisonOperator comparisonOperator, string value)
{
    if (comparisonOperator == JsonComparisonOperator.Contains)
    {
        return fieldToken.ToString().Contains(value);
    }
    int comparison = CompareValues(fieldToken, new JValue(value));
    switch (comparisonOperator)
    {
        case JsonComparisonOperator.Equals:
            return comparison == 0;
        case JsonComparisonOperator.NotEquals:
            return comparison != 0;
        case JsonComparisonOperator.GreaterThan:
            return comparison > 0;
        case JsonComparisonOperator.GreaterThanOrEqual:
            return comparison >= 0;
        case JsonComparisonOperator.LessThan:
            return comparison < 0;
        case JsonComparisonOperator.LessThanOrEqual:
            return comparison <= 0;
        default:
            return false;
    }
}

/// <summary>Filters an array at a JSONPath to elements whose field matches a comparison.
/// Numeric-looking field and comparison values are compared numerically; otherwise
/// comparison falls back to ordinal string comparison.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression identifying an array of objects.</param>
/// <param name="fieldName">The property name to compare on each array element.</param>
/// <param name="comparisonOperator">The comparison to apply.</param>
/// <param name="value">The value to compare each element's field against, as plain text.</param>
/// <param name="filteredJson">The filtered JSON array text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to an array of objects and was filtered.</returns>
[Category("Json - Array")]
[Description("Filters an array at a JSONPath to elements whose field matches a comparison. Never throws.")]
public bool TryFilterJsonArrayByField(string json, string path, string fieldName, JsonComparisonOperator comparisonOperator, string value, out string filteredJson, out string message)
{
    filteredJson = null;
    message = null;
    try
    {
        JToken root = ParseJson(json);
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
        JArray filtered = new JArray();
        foreach (JToken element in array)
        {
            JToken fieldToken = element[fieldName];
            if (fieldToken != null && CompareField(fieldToken, comparisonOperator, value))
            {
                filtered.Add(element);
            }
        }
        filteredJson = filtered.ToString(Formatting.None);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TryFilterJsonArrayByField), exception);
        return false;
    }
}

/// <summary>Sorts an array at a JSONPath by a field's value. Numeric-looking values sort
/// numerically; otherwise sorting falls back to ordinal string comparison.</summary>
/// <param name="json">The JSON text to read.</param>
/// <param name="path">A JSONPath expression identifying an array of objects.</param>
/// <param name="fieldName">The property name to sort each array element by.</param>
/// <param name="ascending"><c>True</c> to sort ascending; <c>false</c> for descending.</param>
/// <param name="sortedJson">The sorted JSON array text on success; <c>null</c> on failure.</param>
/// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
/// <returns><c>True</c> if <paramref name="path"/> resolved to an array of objects and was sorted.</returns>
[Category("Json - Array")]
[Description("Sorts an array at a JSONPath by a field's value. Never throws.")]
public bool TrySortJsonArrayByField(string json, string path, string fieldName, bool ascending, out string sortedJson, out string message)
{
    sortedJson = null;
    message = null;
    try
    {
        JToken root = ParseJson(json);
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
        List<JToken> elements = new List<JToken>(array);
        elements.Sort((left, right) =>
        {
            JToken leftField = left[fieldName];
            JToken rightField = right[fieldName];
            int comparison = CompareValues(leftField, rightField);
            return ascending ? comparison : -comparison;
        });
        JArray sorted = new JArray(elements);
        sortedJson = sorted.ToString(Formatting.None);
        return true;
    }
    catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
    {
        message = NeverThrowsGuard.Failure(nameof(TrySortJsonArrayByField), exception);
        return false;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 87 tests (76 prior + 11 new).

- [ ] **Step 6: Commit**

```bash
git add src/jsonutils
git commit -m "Add JsonUtils array filter/sort helpers"
```

## Task 6: Documentation

**Files:**
- Modify: `src/jsonutils/README.md`

- [ ] **Step 1: Add 6 rows to the `## Methods` table**

Insert immediately after the existing `TryMinifyJson` row (the table's
current last row):

```
| `TryMergeJson` | `(string baseJson, string overrideJson, out string mergedJson, out string message) : bool` | Merges two JSON objects; the second wins on conflicts. |
| `TryDiffJson` | `(string json1, string json2, string delimiter, out bool areEqual, out string differingPaths, out string message) : bool` | Compares two JSON documents and reports differing paths. |
| `TryConvertJsonToXml` | `(string json, string rootElementName, out string xml, out string message) : bool` | Converts JSON text to XML text. |
| `TryConvertXmlToJson` | `(string xml, out string json, out string message) : bool` | Converts XML text to JSON text. |
| `TryFilterJsonArrayByField` | `(string json, string path, string fieldName, JsonComparisonOperator comparisonOperator, string value, out string filteredJson, out string message) : bool` | Filters an array to elements matching a field comparison. |
| `TrySortJsonArrayByField` | `(string json, string path, string fieldName, bool ascending, out string sortedJson, out string message) : bool` | Sorts an array by a field's value. |
```

- [ ] **Step 2: Add a worked example to `## Typical workflow`**

Append after the existing `TryDeserializeObject` example in that section
(before the "does not extend to its own code path" sentence that closes
the section), a new paragraph reading exactly:

> Merge two documents, diff the result against a baseline, and convert to XML:

followed by a single ` ```csharp ` fenced code block containing exactly
this content:

```
json.TryMergeJson("{\"status\":\"open\"}", "{\"status\":\"closed\",\"priority\":1}", out string merged, out message);
// merged == "{\"status\":\"closed\",\"priority\":1}"

json.TryDiffJson("{\"status\":\"open\"}", merged, ",", out bool areEqual, out string differingPaths, out message);
// areEqual == false, differingPaths == "status,priority"

json.TryConvertJsonToXml(merged, "order", out string xml, out message);
// xml == "<order><status>closed</status><priority>1</priority></order>"
```

- [ ] **Step 3: Add Notes & Caveats entries**

Insert these bullets immediately before the existing `**Phase 2 (not yet
implemented)**` bullet, then delete that bullet entirely (Phase 2 is now
implemented):

```markdown
- **`TryMergeJson` requires both inputs to be JSON objects at the root.**
  A bare array or scalar at either root fails with a descriptive message —
  merging only makes sense between two objects' properties.
- **`TryDiffJson`'s path ordering is a deterministic pre-order walk of
  `json1`'s structure**, not alphabetical: keys/indices are visited in
  `json1`'s own order, with any keys/indices that exist only in `json2`
  appended after. A whole-document-level difference (e.g. mismatched root
  types) is reported as `$`.
- **`TryConvertJsonToXml` always requires an explicit `rootElementName`.**
  Newtonsoft's underlying converter can sometimes infer a root from JSON
  shaped as a single top-level property, but this component always
  requires the name explicitly for predictability.
- **`TryFilterJsonArrayByField`/`TrySortJsonArrayByField` compare
  numerically when both sides look like numbers, and fall back to ordinal
  string comparison otherwise.** `Contains` always compares as strings
  (substring match).
```

- [ ] **Step 4: Commit**

```bash
git add src/jsonutils/README.md
git commit -m "Document JsonUtils Phase 2 methods"
```

## Task 7: Final verification

- [ ] **Step 1: Full build**

```bash
dotnet build src/AwesomeRpaUtils.sln
```

Expected: clean build, 0 errors.

- [ ] **Step 2: Full test run**

```bash
dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj
```

Expected: PASS, 87/87.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin jsonutils-phase2
gh pr create --title "Add JsonUtils Phase 2 (merge/diff/XML/filter-sort)" --body "$(cat <<'EOF'
## Summary
- Implements the four capability groups deferred as Phase 2 in the
  original JsonUtils design (PR #77): TryMergeJson, TryDiffJson,
  JSON<->XML conversion, and array filter/sort helpers.
- New repo-owned JsonComparisonOperator enum for the filter/sort methods,
  matching the JsonValueKind precedent from Phase 1.
- See project-docs/plans/2026-09-08-jsonutils-phase2-design.md for the
  full design and the resolved design decisions (merge conflict rule,
  diff output shape, XML root-name handling).

## Test plan
- [x] dotnet build src/AwesomeRpaUtils.sln
- [x] dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj (87/87)
EOF
)"
```

- [ ] **Step 4: Remove the worktree after the PR is open**

```bash
cd /mnt/disk0/csharp/awesome_rpa_utils
git worktree remove .worktrees/jsonutils-phase2
```

## Verification

- [ ] `dotnet build src/AwesomeRpaUtils.sln` - clean, no regressions to any
      shipped component including Phase 1's `JsonUtils`.
- [ ] `dotnet test src/jsonutils/JsonUtils.Tests/JsonUtils.Tests.csproj` -
      87/87 passing, fully on this Linux host.
- [ ] `src/jsonutils/README.md` updated with all 6 new methods, a worked
      example, and Notes & Caveats entries; the "Phase 2 (not yet
      implemented)" bullet removed.
- [ ] Worktree removed after the PR is opened.
