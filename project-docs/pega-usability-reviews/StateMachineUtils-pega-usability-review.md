# StateMachineUtils Pega usability review

This review was completed before implementation, as EventLogUtils' and
LocalQueueUtils' were, so the API is shaped for Pega Robot Studio's component
tray and design surface rather than retrofitted.

## Summary

Every port is a scalar (`string`, `bool`, `int`, `double`). No collection,
object, or generic type crosses the public surface: the machine's structure is
JSON text in, and its history, context and available triggers come back as JSON
or delimited text. States are strings, not an enum, because a state list is
data the automation author supplies, not something the component can predefine.

| Area | Rating | Notes |
|---|---|---|
| Definition | Direct | One `LoadDefinitionJson` call (from a Robot Studio asset or file text) or small `AddState`/`AddTransitionSimple`/`AddTransition` calls; whole-definition validation returns a JSON report. |
| Running | Direct | `Fire` returns scalar `fired`/`newState`/`rejectionReason`; a declined trigger is a normal outcome, not a failure, so a step never depends on an exception or on parsing `message`. |
| Query | Direct | `CanFire`, `GetCurrentState`, `GetAvailableTriggersDelimited`; read-only `CurrentState`/`IsFinished` properties bind to data ports. |
| Context | Direct | Text key/value; guards are declarative, so no code has to be passed into the component. |
| Events | Chainable | Five events with all-string payloads. Raised synchronously on the caller's thread (no thread-affinity trap, unlike the worker-thread events in FileWatchUtils/InterruptUtils). |
| Persistence | Direct | Three scalar methods; the durable-first contract means a failed write leaves the machine unchanged. |

## Findings applied

- **One machine per component instance.** No names, handles or registries to
  route between; two machines are two components dropped on the surface.
- **A declined trigger is `True` + `fired == False` + a stable `rejectionReason`.**
  This is `TryTakeNext`'s `itemAvailable` idea applied to transitions. `False`
  plus `message` is reserved for bad input and persistence failures, so an
  automation can branch on `fired` and never has to distinguish "illegal here"
  from "the component broke" by string-matching.
- **All names are unique.** No overloads, so the signature-uniqueness standard
  holds by construction; `ConventionTests` enforces it (and the never-throws
  shape and designer attributes) mechanically for every future addition.
- **Guards are data, not code.** Robot Studio cannot pass a delegate into a
  component, so a guard is `key op value` against a text context. Ordered
  transitions supply if/else without an OR operator, and a missing key fails
  closed so an unset value cannot satisfy a guard by accident.
- **Misspelled JSON is an error.** An unknown or repeated property is rejected
  rather than ignored; silently dropping `"trigers"` would leave a plausible,
  wrong machine and no error to notice it by.
- **Case-insensitive names**, with case-only collisions rejected, so a designer
  typing `Approve` for `approve` gets the transition, not a silent
  `NoTransition`.
- **Events cannot be the only route to information.** Everything an event
  reports is also readable by a method or property (`CurrentState`,
  `GetHistoryJson`), matching InterruptUtils' rule that a step never depends on
  an event having been observed.
- **Re-entrancy is bounded (16 per thread).** A handler may call back into the
  machine, but a runaway chain returns `ReentrancyLimit` rather than
  overflowing the stack; a rejection handler cannot recurse forever because that
  refusal raises no event.
- **No timers or threads.** `GetSecondsInState` gives polling-based timeout
  detection without a background thread that would need its own lifecycle and
  error channel.
- **Documentation for the pattern people will actually use.**
  `Documentation/WorkingAQueue.md` is a complete worker built with
  LocalQueueUtils, and `QueueWorkerExampleTests` runs the definition extracted
  from that page, so the example cannot go stale.

## Accepted caveats

- **Handlers do not advance a long loop.** Driving many transitions through
  nested handlers hits the re-entrancy cap; long-running work uses a flat loop
  that reads `CurrentState` (documented prominently, with the example).
- **Concurrent callers take turns.** A transition and its events are one unit, so
  events are delivered in transition order; a handler must not wait for another
  thread that fires the same machine.
- **Restoring a saved run is silent** (no events), because nothing changed.
  The automation reads `CurrentState` on resume.
- **Context is text and is persisted in plain text.** Numbers are compared with
  `greaterThan`/`lessThan`; secrets do not belong in it.
- **One guard per `AddTransition` call.** Several guards on one transition need
  the JSON form.
- **Not in v1:** timed auto-transitions, on-enter/on-exit actions, and
  hierarchical or parallel states.
- **The Robot Studio event-port binding is unverified from this repository.**
  The manual step in `TESTING.md` (wire `StateEntered` to a Switch on the state
  name) confirms the payload appears as data on a real design surface.
