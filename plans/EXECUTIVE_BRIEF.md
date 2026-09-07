# Executive Brief

**For:** Leadership review
**Full detail:** [`REST_CONNECTOR_ROADMAP.md`](REST_CONNECTOR_ROADMAP.md)
**Anticipated objections:** [`PUSHBACK_BRIEF.md`](PUSHBACK_BRIEF.md)
**Original product spec:** [`PRD_REST_API_COMPONENT.md`](PRD_REST_API_COMPONENT.md)
**Status:** Release 1 of 8

## Bottom line

Release 1 is scoped, technically de-risked by an already-working prototype, and ready to start. It needs five decisions **made** — not built — before it can be called finished, and one week-long spike to confirm a platform mechanic the whole design leans on. Neither blocks starting the work.

## What this is

A REST Connector lets Pega RPA import an external API definition and generate a typed, callable component for it, so an automation developer picks methods off a list instead of hand-building HTTP calls. It also extends Pega's existing credential system — today built around Robot Manager — to external providers, so a generated component can authenticate to any REST API using the same credential infrastructure already in place.

Release 1 ships OpenAPI/Swagger import with Basic, Bearer, and API key authentication, scoped directly to the first confirmed customer need — API key auth against an OpenAPI-described API — not an engineering guess.

## Why a shared runtime, not one file per API

Every generated component calls into one shared, versioned runtime library rather than carrying its own copy of the HTTP/auth logic. A defect or new capability in that shared library reaches every generated component with a single patch. The alternative — each generated file self-contained — means a fix requires regenerating every deployed component individually, for every customer, every time. The trade is committing to a stable public contract for that shared library now, while there's almost nothing built against it yet.

## How the releases are sequenced

No estimates — each release ships when it's releasable, not on a calendar date. Releases 2/3 and 4/5 are parallel-track pairs; neither member depends on the other.

| Release | Ships | Status |
|---|---|---|
| **R1** | OpenAPI/Swagger import, Basic/Bearer/API key auth, full architecture stood up | In progress |
| **R2** | Typed response helpers — parse a response without hand-rolled string parsing | Next |
| **R3** | OAuth2 client-credentials & Microsoft Entra ID auth | Next |
| **R4** | Postman, Bruno, curl import | Sequenced |
| **R5** | Manual endpoint wizard — no import file required | Sequenced |
| **R6** | Interactive auth flows and client-certificate auth | Sequenced |
| **R7** | Visual editor: map response fields to named outputs | Sequenced |
| **R8** | Safe re-import when a source API changes | Sequenced |

## What we're watching

- **The shared-runtime contract is a one-way door.** Everything generated from Release 1 onward compiles against it. It has to be right — or versioned with a real migration story — before Release 1 ships, not adjusted after.
- **This handles credentials and calls arbitrary external hosts.** A mandatory security review is built into the plan before Release 1 ships — covering credential handling and, specifically, whether anything stops a resolved credential from being sent to an unapproved destination.
- **Patch-once cuts both ways.** The same property that lets one fix reach every deployed component also means a bad fix reaches every deployed component. Rollout needs to be staged, not a flat release to everyone at once.

## Decisions needed from leadership

None of these block starting the work — all five block calling Release 1 finished.

| Decision | Owner | Target date |
|---|---|---|
| Shared-runtime contract & versioning policy | — | — |
| Credential category: new type, or reuse the existing one | — | — |
| Attended vs. unattended auth-flow gating rule | — | — |
| Error-handling model: never-throw only, or configurable | — | — |
| Rollout/canary discipline for the shared runtime | — | — |
