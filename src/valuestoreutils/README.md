# ValueStoreAutomation

A Pega Robot Studio-ready component (`ValueStoreUtils`) for passing loosely-
typed data between RPA automation steps — screen-scrape results, business-
object fields, config values, work-item data — where values often arrive as
strings and need forgiving conversion to the type the automation actually
needs. Unlike `DataContractUtils`, there is no schema to define or seal: any key
can be set at any time. Like every component in this suite, its methods
report recoverable failures as `False` with a descriptive message instead of
throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `ValueStoreAutomation`
- Assembly: `ValueStoreAutomation`

See the [Documentation](Documentation/README.md) folder for worked examples
of each method category.

## ValueStoreUtils vs. DataContractUtils

Use `ValueStoreUtils` when the shape of your data isn't fixed up front, or
when values naturally arrive as strings/loosely-typed and you want forgiving
conversion plus dot-notation access into nested structures, with no
initialization/seal step in the way. If you want a validated contract
instead — known fields, enforced types, required/read-only/write-once/
sensitive rules — use
[`DataContractUtils`](../datacontractutils/README.md) — see the [root
README](../../README.md#datacontractutils-vs-valuestoreutils) for the full
comparison.

## Methods

### Core

| Method | Signature | Description |
|---|---|---|
| `SetValue` | `(string key, object value, out string message) : bool` | Sets a value for a key, creating or overwriting it. |
| `TryGetValue` | `(string key, out bool found, out object value, out string message) : bool` | Gets the raw value for a key. |
| `ContainsKey` | `(string key, out bool exists, out string message) : bool` | Checks whether a key is present. |
| `Remove` | `(string key, out bool removed, out string message) : bool` | Removes a key. |
| `Clear` | `(out int removedCount, out string message) : bool` | Removes every key. |
| `GetCount` | `(out int count, out string message) : bool` | Returns the current entry count. |
| `GetKeysJson` | `(out string keysJson, out string message) : bool` | Returns every key as a JSON array. |
| `GetKeys` | `(out string[] keys, out string message) : bool` | Returns every key as a string array, for direct iteration. |

### Set

| Method | Signature | Description |
|---|---|---|
| `SetString` / `SetInt32` / `SetInt64` / `SetDouble` / `SetDecimal` / `SetBoolean` / `SetDateTime` / `SetGuid` | `(string key, T value, out string message) : bool` | Sets a typed entry. |
| `SetNull` | `(string key, out string message) : bool` | Sets an entry to null without removing the key. |
| `SetJson` | `(string key, string valueJson, out string message) : bool` | Validates and sets an entry from a JSON fragment; objects/arrays become nested structures. |

### Get (default-on-failure)

| Method | Signature | Description |
|---|---|---|
| `GetString` | `(string key, string defaultValue = null) : string` | Gets the value as a string. |
| `GetInt32` / `GetInt64` / `GetDouble` / `GetDecimal` | `(string key, T defaultValue = 0) : T` | Gets the value as a number, converting from string/numeric sources. |
| `GetBoolean` | `(string key, bool defaultValue = false) : bool` | Accepts true/false, 1/0, and yes/no/y/n. |
| `GetDateTime` | `(string key, DateTime defaultValue = default, string format = null) : DateTime` | Optional exact parse format. |
| `GetGuid` | `(string key, Guid defaultValue = default) : Guid` | Parses from string if necessary. |

These never throw on their own: a missing key, a null value, or a failed
conversion all fall back to `defaultValue`. See
[Normal negative vs. operational failure](#normal-negative-vs-operational-failure)
below for why these don't use the `bool`/`out message` shape.

### Try Get

| Method | Signature | Description |
|---|---|---|
| `TryGetString` / `TryGetInt32` / `TryGetInt64` / `TryGetDouble` / `TryGetDecimal` / `TryGetBoolean` / `TryGetDateTime` / `TryGetGuid` | `(string key, out T value) : bool` | `True` only if the key was present and convertible. |
| `TryGetEnum` | `(string key, string enumTypeName, out bool found, out object value, out string message) : bool` | Parses a named enum type's member by name (case-insensitive). |

### Helpers

| Method | Signature | Description |
|---|---|---|
| `IsEmpty` | `(string key, out bool isEmpty, out string message) : bool` | Missing, null, or blank/whitespace text. |
| `AddIfMissing` | `(string key, object value, out bool added, out string message) : bool` | Sets a value only if the key is not already present. |
| `RemoveAndGetValue` | `(string key, out bool found, out object value, out string message) : bool` | Removes a key and returns its value in one step. |
| `Merge` | `(string sourceJson, bool overwrite, out int mergedCount, out string message) : bool` | Merges a flat JSON object's top-level properties in. |
| `FindKeysJson` | `(string wildcardPattern, out string keysJson, out string message) : bool` | Keys matching a simple `*` wildcard pattern. |

### Path

| Method | Signature | Description |
|---|---|---|
| `TryGetPathValue` | `(string path, out bool found, out object value, out string message) : bool` | Dot-notation get through nested structures. |
| `GetPathString` / `GetPathInt32` / `GetPathBoolean` / `GetPathDateTime` | `(string path, T defaultValue) : T` | Typed dot-notation get, default on any missing segment. |
| `SetPath` | `(string path, object value, out string message) : bool` | Dot-notation set, creating intermediate nested structures as needed. |

### Json

| Method | Signature | Description |
|---|---|---|
| `TryGetJson` | `(out string json, out string message) : bool` | Serializes the whole store to JSON. |
| `TryLoadFromJson` | `(string json, bool clearExisting, out int loadedCount, out string message) : bool` | Loads a JSON object's properties in. |
| `TryLoadFromObject` | `(object source, bool clearExisting, out int loadedCount, out string message) : bool` | Loads a POCO's public readable properties via reflection (shallow, atomic). |

### Configuration

| Property | Type | Description |
|---|---|---|
| `CaseSensitiveKeys` | `bool` | Whether keys use case-sensitive comparison. Default `False`. |

## Typical workflow

```csharp
var store = new ValueStoreUtils();

store.SetString("CustomerName", "Acme Corp", out string message);
store.SetInt32("OrderId", 4471, out message);

int qty = store.GetInt32("Quantity", defaultValue: 0);
bool active = store.GetBoolean("IsActive");

// Nested structures via JSON
store.SetJson("Customer", "{\"Address\":{\"City\":\"Columbus\"}}", out message);
string city = store.GetPathString("Customer.Address.City"); // "Columbus"

// Round-trip through JSON
store.TryGetJson(out string json, out message);
var reloaded = new ValueStoreUtils();
reloaded.TryLoadFromJson(json, clearExisting: true, out int loadedCount, out message);
```

## Normal negative vs. operational failure

Per this suite's
[Never-Throws Standard](../../project-docs/coding-standards/never-throws-standard.md),
a missing key is a normal outcome, not a failure:

- `TryGetValue`/`ContainsKey`/`Remove`/`TryGetPathValue` return `True` (the
  lookup completed) with `found`/`exists`/`removed` reporting the actual
  state and `message` staying `null`. Only an unexpected exception at the
  boundary returns `False` with a message.
- `GetString`/`GetInt32`/... and the `TryGetX` scalar converters intentionally
  skip the `bool`/`out message` shape: a missing key, a null value, and an
  unconvertible value are all indistinguishable "normal negative" outcomes
  for a screen-scraped or loosely-typed field, so they fall back to
  `defaultValue` (or `False` for the `TryGetX` family) rather than reporting
  an operational failure. This mirrors the standard's documented allowance
  for "a documented sentinel for normal absence."

## Notes & Caveats

- **No schema.** Unlike `DataContractUtils`, any key can be set or retyped at any
  time; there is no `Initialize`/seal step. Use `DataContractUtils` instead when a
  fixed, validated contract is what you want.
- **`CaseSensitiveKeys` rebuilds the internal map when changed**, preserving
  existing entries under the new comparer. Switching from case-sensitive to
  case-insensitive after adding keys that differ only by case causes a silent
  collision (last write wins) — set this once, before adding data that could
  collide.
- **Nested structures created via `SetJson`/`TryLoadFromJson`/`SetPath` are
  always case-insensitive**, regardless of the outer store's
  `CaseSensitiveKeys` setting — this matches how `SetPath` creates
  intermediate levels and cannot be configured per level.
- **`Merge`/`TryLoadFromJson` only merge top-level JSON properties**, not a
  recursive deep merge — a conflicting nested object is replaced wholesale
  under `overwrite: true`, not merged key-by-key. Use `JsonUtils.TryMergeJson`
  first if a recursive merge is needed, then `TryLoadFromJson` the result.
- **`TryLoadFromObject`/`TryLoadFromJson` are atomic.** On failure (a
  malformed JSON document, a non-object root, or an unexpected exception),
  nothing already in the store is changed.
- **`TryGetEnum`'s `enumTypeName` should be a trusted, design-time-authored
  value**, the same trust model as `JsonUtils.TryDeserializeObject`'s
  `typeName` parameter — not a value populated from untrusted runtime data.
- **`TryGetJson` can fail** if a value was set via `SetValue`/
  `TryLoadFromObject` with a type `System.Text.Json` cannot serialize (for
  example a raw handle or stream); this surfaces as `False` with a message
  rather than throwing.
