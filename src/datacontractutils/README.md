# DataBagAutomation

A Pega Robot Studio-ready component (`DataBagUtils`) for storing named, typed
values without creating a custom class or collection proxy. Initialization
defines the data contract; after sealing, runtime setters can update existing
items but cannot accidentally create misspelled names or change types. Like
every component in this suite, its methods report recoverable failures as
`False` with a descriptive message instead of throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `DataBagAutomation`
- Assembly: `DataBagAutomation`

See the [Documentation](Documentation/README.md) folder for initialization,
transaction-processing, bulk-update, and reset examples.

## Types

### `DataBagValueType`

Supported declared types: `String`, `Boolean`, `Int32`, `Int64`, `Decimal`,
`Double`, `DateTime`, `Json`, and `Null`.

### Lifecycle and policy enums

| Enum | Values | Purpose |
|---|---|---|
| `DataBagState` | `NotInitialized`, `Initializing`, `Ready`, `Disposed` | Reports the component lifecycle phase. |
| `DataBagInitializationSource` | `None`, `DesignTimeJson`, `JsonFile` | Selects how `Initialize` preloads definitions. |
| `DataBagConflictPolicy` | `Fail`, `Replace`, `KeepExisting` | Controls duplicate names during initialization loads. |
| `DataBagUnknownNamePolicy` | `Fail`, `Ignore` | Controls unknown names during runtime bulk updates. |

## Constructors

| Constructor | Description |
|---|---|
| `DataBagUtils()` | Creates an empty, uninitialized component. |
| `DataBagUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Properties

| Property | Type | Description |
|---|---|---|
| `MaximumItems` | `int` | Maximum definitions, from 1 through 1,000,000. Default 10,000. |
| `CaseSensitiveNames` | `bool` | Selects ordinal or ordinal-ignore-case names before initialization. Default `False`. |
| `InitialItemsJson` | `string` | Embedded typed definitions used by `Initialize`. |
| `InitialItemsFilePath` | `string` | Absolute or application-base-relative typed JSON definition file. |
| `InitializationSource` | `DataBagInitializationSource` | Selects empty, embedded-JSON, or JSON-file initialization. |
| `SealAfterInitialization` | `bool` | Makes `Initialize` enter `Ready` immediately. Default `True`. |

## Design-time initialization template

For a known set of values, set `InitializationSource` to `DesignTimeJson`,
paste the following template into `InitialItemsJson`, and call `Initialize`
once during project startup. The template is valid JSON and demonstrates every
supported definition option:

```json
{
  "values": [
    {
      "name": "CustomerId",
      "type": "String",
      "defaultValue": null,
      "mustHaveValue": true
    },
    {
      "name": "InvoiceTotal",
      "type": "Decimal",
      "defaultValue": 0,
      "mustHaveValue": true
    },
    {
      "name": "RequiresReview",
      "type": "Boolean",
      "defaultValue": false
    },
    {
      "name": "AttemptNumber",
      "type": "Int32",
      "defaultValue": 0
    },
    {
      "name": "ExternalReference",
      "type": "Int64",
      "defaultValue": null
    },
    {
      "name": "ConfidenceScore",
      "type": "Double",
      "defaultValue": 0.0
    },
    {
      "name": "ReceivedUtc",
      "type": "DateTime",
      "defaultValue": null
    },
    {
      "name": "SourceSystem",
      "type": "String",
      "defaultValue": "SAP",
      "readOnly": true
    },
    {
      "name": "ConfirmationNumber",
      "type": "String",
      "defaultValue": null,
      "writeOnce": true
    },
    {
      "name": "AccountNumber",
      "type": "String",
      "defaultValue": null,
      "sensitive": true
    },
    {
      "name": "InvoicePayload",
      "type": "Json",
      "defaultValue": {
        "lines": [],
        "currency": "USD"
      }
    },
    {
      "name": "OptionalComment",
      "type": "Null",
      "defaultValue": null
    }
  ]
}
```

Each entry requires `name` and `type`. `defaultValue` is restored by
`ResetValues`; `mustHaveValue` is checked by `ValidateRequiredValuesPresent`; `readOnly`
prevents runtime changes; `writeOnce` permits one assignment until reset; and
`sensitive` redacts the value from `GetSnapshotJson`. Sensitivity is metadata,
not a separate type or encryption mechanism, so it can be applied to strings,
numbers, dates, or JSON values.

## Typical workflow

Use the bag in distinct initialization and processing phases:

1. **Define the contract.** Preload `InitialItemsJson` at design time, or call
   `PopulateInitialItemsJsonTemplate` and replace its blank `name` with the
   fields your automation expects. The template action selects
   `DesignTimeJson` automatically.
2. **Initialize once.** Call `Initialize` during project startup. It validates
   every definition and, with the default `SealAfterInitialization = True`,
   moves the component to `Ready`. An initialization failure leaves the active
   bag unchanged and returns an actionable `message`.
3. **Populate values.** During transaction processing, use `SetString`,
   `SetDecimal`, `SetJson`, or `SetValue` to update existing definitions. A
   misspelled name or wrong type fails instead of silently creating a new item.
4. **Validate before handoff.** Call `ValidateRequiredValuesPresent` before
   submitting, saving, or passing work to another system. It reports whether
   every definition marked `mustHaveValue` has a non-null/non-empty value and
   returns the missing names as JSON.
5. **Reuse for the next transaction.** Call `ResetValues` to restore defaults,
   clear mutable values, and reset write-once fields while keeping the sealed
   schema. Then populate the next transaction using the same contract.

For dynamic schemas, use `BeginInitialization`, one or more `SetTypedValue` or
`Load*` methods, and `CompleteInitialization`. Those changes are staged and
published atomically; `CancelInitialization` discards them.

## Methods

### Initialization

| Method | Signature | Description |
|---|---|---|
| `Initialize` | `bool Initialize(out int loadedCount, out string message)` | Atomically loads the selected design-time source and optionally seals it. |
| `BeginInitialization` | `bool BeginInitialization(bool clearExisting, out string message)` | Starts a staged schema transaction, either empty or copied from the active bag. |
| `SetTypedValue` | `bool SetTypedValue(string name, DataBagValueType valueType, string value, out string message)` | Defines or updates one staged item using invariant text. |
| `LoadTypedJson` | `bool LoadTypedJson(string typedJson, DataBagConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)` | Atomically loads explicit typed definitions into staging. |
| `LoadJsonObject` | `bool LoadJsonObject(string jsonObject, DataBagConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)` | Infers definitions from a flat JSON object and loads them atomically. |
| `LoadDataTable` | `bool LoadDataTable(DataTable table, DataBagConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)` | Loads conventional `Name`, `Type`, `Value`, and optional flag columns. |
| `LoadDataTableMapped` | `bool LoadDataTableMapped(DataTable table, string nameColumn, string typeColumn, string valueColumn, string mustHaveValueColumn, string readOnlyColumn, string writeOnceColumn, string sensitiveColumn, DataBagConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)` | Loads definitions from explicitly mapped columns. |
| `CompleteInitialization` | `bool CompleteInitialization(out int itemCount, out string message)` | Atomically publishes staging and enters `Ready`. |
| `CancelInitialization` | `bool CancelInitialization(out int discardedCount, out string message)` | Discards staging and preserves the last active bag. |
| `PopulateInitialItemsJsonTemplate` | `bool PopulateInitialItemsJsonTemplate(out string message)` | Design-time helper that fills `InitialItemsJson` with an editable one-row, all-fields template and selects `DesignTimeJson`. |

### Set

| Method | Signature | Description |
|---|---|---|
| `SetValue` | `bool SetValue(string name, string value, out string message)` | Parses invariant text using an existing item's declared type. Never creates or retypes. |
| `SetString` | `bool SetString(string name, string value, out string message)` | Sets an existing `String` item. |
| `SetBoolean` | `bool SetBoolean(string name, bool value, out string message)` | Sets an existing `Boolean` item. |
| `SetInt32` | `bool SetInt32(string name, int value, out string message)` | Sets an existing `Int32` item. |
| `SetInt64` | `bool SetInt64(string name, long value, out string message)` | Sets an existing `Int64` item. |
| `SetDecimal` | `bool SetDecimal(string name, decimal value, out string message)` | Sets an existing `Decimal` item. |
| `SetDouble` | `bool SetDouble(string name, double value, out string message)` | Sets an existing `Double` item. |
| `SetDateTime` | `bool SetDateTime(string name, DateTime value, out string message)` | Sets an existing `DateTime` item. |
| `SetJson` | `bool SetJson(string name, string valueJson, out string message)` | Validates and sets an existing `Json` item. |
| `SetNull` | `bool SetNull(string name, out string message)` | Clears an existing mutable item without changing its declared type. |
| `SetFromJsonObject` | `bool SetFromJsonObject(string valuesJson, DataBagUnknownNamePolicy unknownNamePolicy, out int updatedCount, out int skippedCount, out string message)` | Atomically maps JSON properties to existing definitions. |
| `SetFromDataRow` | `bool SetFromDataRow(DataTable table, int rowIndex, DataBagUnknownNamePolicy unknownNamePolicy, out int updatedCount, out int skippedCount, out string message)` | Atomically maps one row's column names to existing definitions. |

### Get

| Method | Signature | Description |
|---|---|---|
| `TryGetValue` | `bool TryGetValue(string name, out bool found, out DataBagValueType valueType, out string value, out string message)` | Gets any value as invariant text with its declared type. |
| `TryGetString` | `bool TryGetString(string name, out bool found, out string value, out string message)` | Gets a `String` item. |
| `TryGetBoolean` | `bool TryGetBoolean(string name, out bool found, out bool value, out string message)` | Gets a `Boolean` item. |
| `TryGetInt32` | `bool TryGetInt32(string name, out bool found, out int value, out string message)` | Gets an `Int32` item. |
| `TryGetInt64` | `bool TryGetInt64(string name, out bool found, out long value, out string message)` | Gets an `Int64` item. |
| `TryGetDecimal` | `bool TryGetDecimal(string name, out bool found, out decimal value, out string message)` | Gets a `Decimal` item. |
| `TryGetDouble` | `bool TryGetDouble(string name, out bool found, out double value, out string message)` | Gets a `Double` item. |
| `TryGetDateTime` | `bool TryGetDateTime(string name, out bool found, out DateTime value, out string message)` | Gets a `DateTime` item. |
| `TryGetJson` | `bool TryGetJson(string name, out bool found, out string valueJson, out string message)` | Gets a `Json` item's raw JSON text. |

### Query

| Method | Signature | Description |
|---|---|---|
| `Contains` | `bool Contains(string name, out bool exists, out string message)` | Checks whether a name is defined. |
| `GetState` | `bool GetState(out DataBagState dataBagState, out int itemCount, out string message)` | Returns lifecycle state and active item count. |
| `GetSnapshotJson` | `bool GetSnapshotJson(out string snapshotJson, out string message)` | Returns typed definitions and values; sensitive values are redacted. |

### Reset

| Method | Signature | Description |
|---|---|---|
| `ResetValues` | `bool ResetValues(out int resetCount, out string message)` | Restores mutable defaults and resets write-once assignment state. |

### Validation

| Method | Signature | Description |
|---|---|---|
| `ValidateRequiredValuesPresent` | `bool ValidateRequiredValuesPresent(out bool ready, out int missingCount, out string missingNamesJson, out string message)` | Reports must-have-value items whose current values are null or empty strings. |

## Notes & Caveats

- **Initialization defines the contract.** Definition operations target staging;
  `CompleteInitialization` publishes the entire schema atomically. Runtime
  setters require `Ready` state, an existing name, and the declared type.
- **Design-time preload is explicit.** Properties are assigned after component
  construction, so an automation calls `Initialize` once during project startup
  to validate and publish them with a routable result/message.
- **Typed definition JSON accepts an array or `{ "values": [...] }`.** Each
  entry requires `name` and `type`; optional fields are `defaultValue` (or
  `value`), `mustHaveValue`, `readOnly`, `writeOnce`, and `sensitive`.
- **Bulk operations are atomic.** A malformed value, unknown name under `Fail`,
  type mismatch, capacity failure, or immutable target leaves all values from
  that call unchanged.
- **Type conversion is strict and culture-independent.** `SetValue` parses
  numeric text with invariant culture and dates as round-trip/ISO-compatible
  values. Typed getters never silently convert another declared type.
- **Missing values are normal results.** A typed getter returns `True` with
  `found == False`; a present item of the wrong type returns `False` with a
  message.
- **Read-only, write-once, must-have-value, and sensitive are enforced metadata.** A
  redacted snapshot is diagnostic output, not a lossless export format.
- **DataTable methods are optional bridges.** JSON methods keep ordinary Pega
  workflows scalar and proxy-free when no table already exists.
- **Each component owns one in-memory bag.** There is no persistence,
  cross-runtime sharing, automatic logging, expression evaluation, or arbitrary
  object storage. Disposal clears active and staged values.
