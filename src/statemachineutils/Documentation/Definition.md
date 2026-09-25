# Definition

A machine is a set of **states** and **transitions**. Each transition says: *from
this state, when this trigger is fired, go to that state* - optionally only if
its guards pass (see [Context and guards](Context.md)).

## JSON

```json
{
  "name": "Onboarding",
  "initial": "Draft",
  "states": [ "Draft", "Review", { "name": "Approved", "final": true } ],
  "transitions": [
    { "from": "Draft",  "trigger": "submit",  "to": "Review" },
    { "from": "Review", "trigger": "approve", "to": "Approved" },
    { "from": "Review", "trigger": "reject",  "to": "Draft" }
  ]
}
```

```csharp
if (!machine.LoadDefinitionJson(json, out string message))
    Log(message);            // says exactly what is wrong, and the previous definition is untouched
```

- A state is a plain string, or `{ "name": ..., "final": true }`. A **final**
  state ends the machine: no transitions leave it and further triggers are
  declined with `Finished`.
- `"from": "*"` matches any non-final state - handy for `abort` or `timeout`.
- Transitions for the same (state, trigger) are tried **in the order written**;
  the first whose guards pass wins.
- Names are case-insensitive; two states differing only by case are an error.
  Values come back in the casing you declared.
- Comments and trailing commas are accepted.

## Checked as a whole, before anything changes

`LoadDefinitionJson` validates everything first. A definition with any error is
rejected whole, and the machine keeps the previous one. Errors include: no
states, missing or unknown or final `initial`, duplicate states, transitions
to or from unknown states, transitions leaving a final state, unknown guard
operators, non-numeric values for numeric guards, size limits - and **misspelled
or repeated JSON properties** (`"trigers"`, or two `"initial"` values), which
are errors rather than silently ignored.

`ValidateDefinitionJson` runs the same checks without loading and returns a
report - useful in a design-time check or a test:

```csharp
machine.ValidateDefinitionJson(json, out string report, out string message);
// {"valid":true,"errors":[],"warnings":["State 'Orphan' is unreachable ..."],"stateCount":4,"transitionCount":5}
```

Warnings do not stop a load. They flag likely mistakes: a state that cannot be
reached, a non-final state with no way out, and a transition that an earlier
unguarded one always shadows (so it can never fire).

## Building with method calls

For a small machine you can skip JSON:

```csharp
machine.AddState("Draft", out message);
machine.AddState("Review", out message);
machine.AddState("Approved", out message, isFinal: true);
machine.SetInitialState("Draft", out message);
machine.AddTransition("Draft",  "submit",  "Review",   out message);
machine.AddTransition("Review", "approve", "Approved", out message,
                      guardKey: "reviewer", guardOp: GuardOperator.Exists);
```

Add states before the transitions that use them. `AddTransition` takes one
optional guard; use JSON when a transition needs several. `guardOp` is a
`GuardOperator`, which Robot Studio shows as a drop-down: `Equal`, `NotEqual`,
`In`, `NotIn`, `GreaterThan`, `LessThan`, `Exists`, `NotExists` (or `None`, the
default, for no guard). They mean the same as the JSON operators in
[Context and guards](Context.md). The definition is
fully validated at `Start`. `GetDefinitionJson` returns what you built.

## Changing a definition

`LoadDefinitionJson` and `ClearDefinition` stop the machine (context is kept).
`AddState` / `SetInitialState` / `AddTransition` are refused while it is running.
While [persistence](Persistence.md) is enabled the definition is frozen.
