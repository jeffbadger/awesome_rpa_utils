# StateMachineUtils worked examples

New here? Follow the [step-by-step tutorial](Tutorial.md) first. Start with the
queue-worker example if you want to see everything working together; the other
pages explain one area each.

- [Tutorial](Tutorial.md) — build and run an expense-claim machine in twelve steps: definition, guards, events, persistence, Robot Studio wiring
- [Reproducing the UiPath REFramework](UiPathReFramework.md) — Init / Get Transaction Data / Process Transaction / End Process with business and system exceptions, retries and a consecutive-failure limit, plus crash-safe resume
- [Working a queue](WorkingAQueue.md) — a complete, crash-safe worker built from `StateMachineUtils` + `LocalQueueUtils`: circuit breaker, delayed retries, abort, resume
- [Definition](Definition.md) — the JSON format, the method API, validation and warnings
- [Running](Running.md) — `Start`, `Fire`, declined triggers, `CanFire`, history
- [Context and guards](Context.md) — the key/value context and every guard operator
- [Events](Events.md) — what fires when, on which thread, and what not to do in a handler
- [Persistence](Persistence.md) — resuming a run after a crash, and what is refused

All examples assume a `StateMachineUtils` instance named `machine`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var machine = new StateMachineUtils();`).
