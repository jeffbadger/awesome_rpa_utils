# Pushback Brief

**Companion to:** [`REST_CONNECTOR_ROADMAP.md`](REST_CONNECTOR_ROADMAP.md) · [`EXECUTIVE_BRIEF.md`](EXECUTIVE_BRIEF.md)
**Purpose:** Prep notes for the roadmap review — the sharpest objections, answered before the room asks.

## Two facts that do most of the work below

1. **Delivery model:** no estimates — the team builds to have releasable code at each stage. A release ships when its acceptance criteria pass, not on a calendar date.
2. **Confirmed first use case:** API key auth against an OpenAPI-described API. Release 1's scope is built against that, not against engineering convenience.

Several objections below only look strong until one of these two facts lands.

## Objections and answers

### 1. "You built a whole shared-runtime architecture to authenticate with an API key. That's overkill." — `200 · Holds`

The Harness's complexity doesn't need to match Release 1's scope — it needs to be *correct* for it. The reason to place the assembly boundary now, while there's almost nothing behind it, is that boundaries get more expensive to introduce the more code already exists on either side of them. Extracting a shared harness later, after logic is embedded across an installed base of generated components, recreates the exact "regenerate everyone" problem this design exists to avoid. The cheapest time to draw the line is before there's anything to split.

### 2. "Your prototype doesn't validate the risky part of this plan." — `501 · Open gap`

Correct — worth saying plainly rather than overselling. The prototype validates Intake (parsing, the normalized model) end to end; that's real, tested prior art. It validates nothing about the Harness contract or rollout mechanics, which is exactly why those sit in Open Decisions rather than being presented as settled. **Fix:** make the Harness contract's first version itself a releasable spike — wire API key auth through it end to end as the very first slice, and let that be the proof instead of an assumption.

### 3. "Cutting OAuth2 from Release 1 is backwards — that's what customers actually need." — `200 · Holds`

Not backwards. The confirmed first use case is API key auth against an OpenAPI-described API — Release 1's scope is built directly against named, real demand, not an engineering-convenience simplification. Basic and Bearer ride along for free because they share API key's request-shaping and credential-resolution path; only the header changes. OAuth2 is the one thing that actually needs new machinery — token lifecycle — and it isn't what the first use case calls for.

### 4. "Where's the estimate?" — `200 · Holds`

We don't estimate — we build to have releasable code at each stage. That's the actual reason the plan is a release sequence instead of a task list: each release is scoped to be a complete, shippable increment by itself, so "how long" isn't the operative question. Release 1 ships when API key + OpenAPI passes its acceptance criteria, full stop — there's no calendar commitment to defend or miss.

### 5. "No owners or dates on five open decisions." — `501 · Open gap`

No defense here — this is a real gap, not a philosophical difference. Put a name and a checkpoint on each of the five before this goes further.

### 6. "Letting a property hold a literal secret undercuts the whole credential-reference story." — `409 · Adjust`

Fair — and this is a place to actually change the design, not explain it away. Restrict the literal-value path to connections explicitly marked non-production, or don't expose it at all where a provider-backed reference is available.

### 7. "Nothing stops a resolved credential from being replayed against an arbitrary host." — `501 · Open gap`

Real gap, not yet covered by Release 1's acceptance criteria. Add destination governance — an allow-list check before a resolved credential attaches to a request — as an explicit non-functional requirement, and make it the first agenda item for the security review gate already in the plan.

### 8. "Zero drift detection until Release 7 — for something whose whole job is calling other people's live APIs." — `409 · Adjust`

Sequencing, not a blind spot. Release 1's never-throw contract already surfaces a moved or changed endpoint as a normal failed-call result the first time it's invoked — the same failure mode automations get today from hand-built HTTP calls. Release 7 is about catching drift proactively, at regeneration time, before an automation ever runs. Worth having open as a gap, but "you find out reactively" is the status quo, not a regression.

### 9. "Has anyone actually confirmed the shared-dependency mechanism, or just that scripts compile?" — `409 · Adjust`

Be honest about the difference: script compilation is confirmed; a generated component referencing a separately-versioned shared assembly, updatable independently via Sync Server, is not yet confirmed. That's precisely why it's an open decision rather than folded into "ignore compilation concerns." Make it the actual first spike of Release 1, before Intake or Generation code — a week of work that tells you whether the Harness split holds its current shape or needs to change.

---

Prepared alongside the REST Connector Roadmap. Objections are paraphrased for the review conversation, not quoted from any individual.
