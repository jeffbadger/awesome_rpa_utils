# StateMachineAutomation

A Pega Robot Studio-ready component (`StateMachineUtils`) that models a process
as **states, triggers, and guarded transitions**. Declare the machine once (as
JSON or with method calls), `Fire` triggers from the automation, and react to
`StateEntered` / `TransitionFired` events instead of scattering "where am I?"
variables and Switch components across the surface. Illegal transitions are
declined with a stable reason code instead of being silently accepted. Like
every component in this suite, its methods honor the never-throws contract:
invalid input and runtime failures return `False` with a descriptive message.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `StateMachineAutomation`
- Assembly: `StateMachineAutomation`
- One component instance is one machine (drop two components for two machines)

See the [step-by-step tutorial](Documentation/Tutorial.md) to build your first machine, and the [Documentation](Documentation/README.md) folder for worked examples.

## A machine in one screen

```json
{
  "name": "InvoiceFlow",
  "initial": "Received",
  "states": [
    { "name": "Received" }, { "name": "Validated" },
    { "name": "Posted", "final": true }, { "name": "Failed", "final": true }
  ],
  "transitions": [
    { "from": "Received",  "trigger": "validate", "to": "Validated" },
    { "from": "Validated", "trigger": "post",     "to": "Posted",
      "guards": [ { "key": "amount", "op": "lessThan", "value": "10000" } ] },
    { "from": "Validated", "trigger": "post",     "to": "Failed" },
    { "from": "*",         "trigger": "abort",    "to": "Failed" }
  ]
}
```

```csharp
machine.LoadDefinitionJson(json, out string message);
machine.SetContext("amount", "2500", out message);
machine.Start(out string state, out message);                 // Received
machine.Fire("validate", out state, out message);
machine.Fire("post", out state, out message);  // Posted
```

## Types

### `StateMachineTransitionEventArgs`

Payload of `StateExited`, `TransitionFired`, `StateEntered` and `MachineFinished`.
All properties are non-null strings (an empty string means "not applicable"):
`PreviousState`, `NewState`, `Trigger`.

### `StateMachineRejectedEventArgs`

Payload of `TransitionRejected`: `State`, `Trigger`, `Reason`
(`NoTransition`, `GuardFailed`, `Finished`, `NotStarted`) and `Detail`
(human-readable; names the failing guard as declared in the definition - key,
operator and expected value - but **never** the context's actual value).

## Constructors

| Constructor | Description |
|---|---|
| `StateMachineUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `StateMachineUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Properties

| Property | Type | Description |
|---|---|---|
| `CurrentState` | `string` | Read-only. The current state, or an empty string if the machine has not been started. |
| `IsFinished` | `bool` | Read-only. True once the machine has been started and has reached a final state. |
| `MachineName` | `string` | Read-only. The `name` from the loaded definition, or an empty string. |
| `MaximumHistoryEntries` | `int` | History entries kept (1-10,000, default 100); oldest are dropped first. |

## Events

| Event | Type | Description |
|---|---|---|
| `StateExited` | `EventHandler<StateMachineTransitionEventArgs>` | Raised when the machine leaves a state, before `TransitionFired` and `StateEntered`. |
| `TransitionFired` | `EventHandler<StateMachineTransitionEventArgs>` | Raised once per successful transition. |
| `StateEntered` | `EventHandler<StateMachineTransitionEventArgs>` | Raised when the machine enters a state - including the initial state on `Start`/`Reset`. |
| `MachineFinished` | `EventHandler<StateMachineTransitionEventArgs>` | Raised after `StateEntered` when the new state is final. |
| `TransitionRejected` | `EventHandler<StateMachineRejectedEventArgs>` | Raised when a trigger is declined (`NoTransition`, `GuardFailed`, `Finished`, `NotStarted`). |

## Methods

### Definition

| Method | Signature | Description |
|---|---|---|
| `LoadDefinitionJson` | `bool LoadDefinitionJson(string definitionJson, out string message)` | Validates and loads a JSON definition, replacing the current one and stopping the machine (context is kept). An invalid definition is rejected whole. |
| `ValidateDefinitionJson` | `bool ValidateDefinitionJson(string definitionJson, out string reportJson, out string message)` | Validates without loading. Returns `True` with `{valid, errors, warnings, stateCount, transitionCount}`; an invalid definition is reported inside the JSON. |
| `GetDefinitionJson` | `bool GetDefinitionJson(out string definitionJson, out string message)` | Returns the current definition as JSON. |
| `AddState` | `bool AddState(string name, out string message, bool isFinal = false)` | Adds a state. |
| `SetInitialState` | `bool SetInitialState(string name, out string message)` | Sets the state entered on `Start`. |
| `AddTransition` | `bool AddTransition(string fromState, string trigger, string toState, out string message, string guardKey = null, GuardOperator guardOp = GuardOperator.None, string guardValue = null)` | Adds a transition, optionally with one guard. `guardOp` is a drop-down (`GuardOperator`: `None`, `Equal`, `NotEqual`, `In`, `NotIn`, `GreaterThan`, `LessThan`, `Exists`, `NotExists`); leave it at `None` for no guard. Use JSON for several guards on one transition. |
| `AddTransitionSimple` | `bool AddTransitionSimple(string fromState, string trigger, string toState, out string message)` | The minimal `AddTransition`: an unconditional transition, nothing else. Use `AddTransition` when it needs a guard. |
| `ClearDefinition` | `bool ClearDefinition(out string message)` | Removes the whole definition and stops the machine. |

### Run

| Method | Signature | Description |
|---|---|---|
| `Start` | `bool Start(out string currentState, out string message)` | Validates the definition and enters the initial state, raising `StateEntered`. Refused if already started. |
| `Reset` | `bool Reset(bool clearContext, out string currentState, out string message)` | Returns to the initial state (also starts an unstarted machine), clears history, raises `StateEntered`. |
| `Fire` | `bool Fire(string trigger, out string newState, out string message)` | Fires a trigger. `True` means the call worked: an empty `message` means it fired; text is the reason it was declined (`NoTransition`, `GuardFailed`, `Finished`, `NotStarted`, `ReentrancyLimit`). `False` means an error and `message` says what. |
| `FireSimple` | `bool FireSimple(string trigger, out string message)` | The minimal `Fire`, same result and message rules, without `newState`. |
| `CanFire` | `bool CanFire(string trigger, out bool canFire, out string rejectionReason, out string message)` | Reports whether a trigger would fire right now, without firing or raising events. |

### Query

| Method | Signature | Description |
|---|---|---|
| `GetCurrentState` | `bool GetCurrentState(out string currentState, out string message)` | The current state (empty before `Start`). |
| `IsStarted` | `bool IsStarted(out bool isStarted, out string message)` | Whether `Start`/`Reset` has been called. |
| `IsInFinalState` | `bool IsInFinalState(out bool isFinal, out string message)` | Whether the machine has reached a final state. |
| `GetAvailableTriggersDelimited` | `bool GetAvailableTriggersDelimited(out string triggers, out string message, string delimiter = ",")` | Triggers that would fire right now (guards evaluated), joined by a delimiter. Requires a started machine. |
| `GetAvailableTriggersJson` | `bool GetAvailableTriggersJson(out string json, out string message)` | The same, as a JSON array. |
| `GetSecondsInState` | `bool GetSecondsInState(out double seconds, out string message)` | Seconds since the current state was entered - poll it to detect a stuck step. |
| `GetHistoryJson` | `bool GetHistoryJson(out string json, out string message, int maxEntries = 100)` | The most recent history entries, oldest first: `{seq, utc, kind, trigger, from, to, reason, detail}`. |
| `SetMaximumHistoryEntries` | `bool SetMaximumHistoryEntries(int maximumEntries, out string message)` | Never-throws form of the `MaximumHistoryEntries` property. |

### Context

| Method | Signature | Description |
|---|---|---|
| `SetContext` | `bool SetContext(string key, string value, out string message)` | Sets a text value that guards can test. Keys are case-insensitive. |
| `GetContext` | `bool GetContext(string key, out bool exists, out string value, out string message)` | Reads a value; a missing key is `True` with `exists == False`. |
| `RemoveContext` | `bool RemoveContext(string key, out bool removed, out string message)` | Removes a key; a missing key is `True` with `removed == False`. |
| `ClearContext` | `bool ClearContext(out string message)` | Removes every key. |
| `GetContextJson` | `bool GetContextJson(out string json, out string message)` | The whole context as a JSON object, keys sorted. |

### Persistence

| Method | Signature | Description |
|---|---|---|
| `EnablePersistence` | `bool EnablePersistence(string machineName, out string statePath, out bool restored, out string message)` | Makes state, context and history durable under `%LOCALAPPDATA%\AwesomeRpaUtils\StateMachines\<machineName>`. Call after loading the definition and **before** `Start`; restores a matching saved run. |
| `DisablePersistence` | `bool DisablePersistence(out string message)` | Stops persisting and releases ownership; the saved file is kept. |
| `DiscardPersistedState` | `bool DiscardPersistedState(string machineName, out bool discarded, out string message)` | Deletes a machine's saved state. A missing one is `True` with `discarded == False`. |

## Notes & Caveats

- **A declined trigger is a normal outcome, not a failure.** `Fire` returns
  `True` (the call worked) and `message` tells you which it was: **empty means the
  trigger fired**; text is the reason it was declined - `NoTransition`,
  `GuardFailed`, `Finished`, `NotStarted` or `ReentrancyLimit` - and the machine
  stays where it was. `False` means an error (empty trigger, no definition
  loaded, a persistence write failure): `message` says what, and the machine is
  unchanged. The details of a decline (which guard failed) are in the
  `TransitionRejected` event and `GetHistoryJson`. `FireSimple` is the same
  without the `newState` output.
- **Transitions for one (state, trigger) are tried in the order declared; the
  first whose guards all pass wins.** That gives if/else without an OR
  operator: put the guarded transition first and an unguarded fallback after it.
  `"from": "*"` matches any non-final state.
- **Guard operators:** `equals`, `notEquals` (case-insensitive text), `in`,
  `notIn` (comma-separated list, case-insensitive), `greaterThan`, `lessThan`
  (invariant-culture numbers), `exists`, `notExists`. **A missing key fails
  every comparison** (only `notExists` passes) so an unset value can never
  satisfy a guard by accident. `equals` is text, so `"007"` does not equal
  `"7"`; use the numeric operators for numbers.
- **Names are case-insensitive** (states, triggers, context keys) and
  definitions with two names differing only by case are rejected. Events and
  return values use the casing declared in the definition.
- **The definition is checked as a whole before anything changes.** Unknown or
  repeated JSON properties (a typo like `"trigers"`) are errors, not ignored.
  `ValidateDefinitionJson` also reports non-fatal warnings: unreachable
  states, non-final dead ends, and transitions shadowed by an earlier
  unguarded one.
- **Events fire synchronously on the thread that called `Fire`/`Start`/`Reset`**
  - unlike `FileWatchUtils`/`InterruptUtils`, which raise events on worker
  threads. They fire after the change is committed and outside the
  component's lock, so a handler sees the new state and may call back in. A
  handler that fires again is allowed; nesting is capped at 16 (per thread) -
  past that, `Fire` returns `ReentrancyLimit` (recorded in history, no event,
  so a `TransitionRejected` handler cannot recurse forever) and `Start`/`Reset`
  return `False`.
- **`LoadDefinitionJson`, `ClearDefinition` and `EnablePersistence` are refused from
  inside an event handler** (`False` with a message): they stop or replace the machine, which must not happen
  while the batch of events for the previous transition is still being delivered.
  From another thread they wait for those handlers to finish.
- **Concurrent callers are serialized, so events always arrive in transition
  order.** A transition and the delivery of its events are one unit: while one
  thread's handlers run, another thread's `Fire`/`Start`/`Reset` waits its turn
  (a second thread can never commit a later transition between the first one's
  commit and its handlers). Reads - `CurrentState`, `GetHistoryJson`, `CanFire`,
  the context methods - are never blocked by a running handler. The flip side: a
  handler must not wait for another thread that fires *this same machine*, since
  that thread is waiting for the handler.
- **A subscriber that throws is caught and logged, never propagated.** Other
  subscribers still run and the change stands. The exception is discarded
  after `Debug.WriteLine`; there is no `message` to carry it.
- **Persistence is durable-first.** With persistence enabled every state
  change and context change is written *before* it is applied; if the write
  fails the call returns `False` with a message and nothing changes (no state
  move, no events). Declined triggers are recorded in the in-memory history
  but do not trigger a write, so they cannot fail on a full disk; they reach
  disk with the next real change.
- **Set the context after `EnablePersistence`.** Restoring a saved run replaces the
  whole run state, so context set beforehand would be discarded; when a saved run
  exists the call is refused with a message rather than losing it silently.
  A run that had reached a final state is restored *as* finished - call `Reset` to
  begin a new one. A saved file must carry all of its fields (an absent `started` is
  refused, not read as "not started") and the machine name it was saved under, or
  it is refused. A saved file is untrusted input: it is held to the same limits
  as `SetContext` (1,000 keys, 128-character keys, 4,096-character values), to
  10,000 history entries and 64 MB, and every history record must be well formed
  (known kind, valid timestamp, declared states, consecutive sequence numbers
  ending at the saved counter, and no empty history unless the machine has never
  recorded anything) or the whole file is refused.
- **Restoring is silent.** Resuming a saved run raises no events - the machine
  is being picked up, not moved. Read `CurrentState` to see where it stopped.
  A saved run from a *different* definition is refused with a message, never
  silently resumed; `DiscardPersistedState` abandons it. The definition is
  frozen while persistence is enabled.
- **Do not store secrets in the context.** Context values are written to disk
  in plain text when persistence is enabled. History and event details name a
  failing guard as declared in the definition (key, operator, expected value)
  but never include the context's actual values.
- **Disposing never waits for handlers.** `Dispose` does not block behind a running event
  handler (a handler may be marshalling to the very thread that is disposing the
  component). A call that was already queued behind that handler when disposal
  happened is refused with a "disposed" message instead of running against a disposed
  component; one that had already committed finishes normally.
- **One owner per saved machine.** A second component or process enabling the
  same `machineName` gets `False` ("already open"). Disposal releases it.
- **No timers, by design.** The component starts no threads. To detect a stuck
  step, poll `GetSecondsInState`. Deferred: timed auto-transitions, on-enter/
  on-exit actions, and hierarchical or parallel states.
- **Limits:** 500 states, 5,000 transitions, 20 guards per transition, 128
  characters per name, 1,000 context keys of up to 4,096 characters, a 2 MB
  definition, 10,000 history entries.
- **Never throws.** The one exception in the surface is deliberate and
  design-time only: assigning an out-of-range value to the
  `MaximumHistoryEntries` *property* throws `ArgumentOutOfRangeException`, as
  `StackUtils.MaximumItems` does; `SetMaximumHistoryEntries` is the
  never-throws form.
