# DataContractUtils Pega usability review

This review was completed before implementation.

## Findings applied

- JSON preload and bulk-update methods provide scalar alternatives to custom
  objects and collection proxies; DataTable methods remain optional bridges.
- Every public automation method has a unique name, a `bool` result, scalar or
  standard DataTable inputs, initialized outputs, and outputs after inputs.
- `SetTypedValue` defines names/types only during initialization. `SetValue`
  updates only existing definitions, preventing runtime spelling errors and
  accidental type changes without exposing ambiguous overloads.
- Design-time properties preload known contracts, while explicit `Initialize`
  exposes configuration failures through routable output ports.
- `PopulateInitialItemsJsonTemplate` provides a discoverable design-time action
  for creating the first editable definition row without hand-authoring the
  JSON envelope or option names.
- Staging makes multi-step initialization atomic. Separate lifecycle and policy
  enums render as designer-selectable constants.
- Typed getters avoid conversion ambiguity and distinguish missing names from
  operational/type failures. Sensitive values are redacted from snapshots.

## Accepted friction

The DataTable loaders require a proxy when the caller does not already have a
table; JSON alternatives cover that case. Raw JSON is used for design-time
definitions because a custom Property Grid collection editor would add a much
larger designer/deployment surface and requires separate Pega validation.

## Addendum: renamed from DataBagUtils (2026-09)

Renamed the component (class, namespace/assembly, enums, folder) from
`DataBagUtils`/`DataBagAutomation` to `DataContractUtils`/
`DataContractAutomation`. The original name read as the looser, freeform
component and `ValueStoreUtils` read as the more structured one - the
opposite of their actual behavior (this component is the schema-validated,
sealed-contract one; `ValueStoreUtils` is the freeform one). No behavior
change - pure rename, done pre-adoption so no compatibility shim was needed.
