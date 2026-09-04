# Initialization

Preload known definitions through `InitialItemsJson`, select
`DesignTimeJson`, and call `Initialize` in the project startup automation:

If you are starting from an empty property, use the design-time action
`PopulateInitialItemsJsonTemplate`. It fills `InitialItemsJson` with one
editable row containing every definition field and selects `DesignTimeJson`;
replace the blank `name` before calling `Initialize`.

```json
{"values":[
  {"name":"CustomerId","type":"String","defaultValue":null,"mustHaveValue":true},
  {"name":"Total","type":"Decimal","defaultValue":0},
  {"name":"Source","type":"String","defaultValue":"SAP","readOnly":true}
]}
```

For dynamic setup, call `BeginInitialization`, use `SetTypedValue`,
`LoadTypedJson`, `LoadJsonObject`, or a DataTable loader, then call
`CompleteInitialization`. Until completion, active values remain unchanged;
`CancelInitialization` discards the staged schema.
