# Exerciser

A menu-driven console app for interactively exercising the host-dependent,
non-UI-shaped Utils components that don't fit [`test-harness/`](../test-harness/README.md)'s
WinForms-target pattern: `SessionUtils`, `ServiceUtils`, `EventLogUtils`,
`OcrUtils`, `TerminalUtils`, and `ResourceLockUtils`. It picks a component,
picks a method, prompts for its inputs, and prints the `bool` return plus
every `out` parameter and the `message`, so manual verification against
[`TESTING.md`](../TESTING.md)'s documented per-method cases is fast and
consistent instead of ad-hoc PowerShell/Robot Studio one-offs.

This is a standalone project, deliberately kept outside `src/` and out of
`AwesomeRpaUtils.sln` — it's test tooling, not one of the shipped components.

## Why this project references multiple components directly

Every shipped component's own README states it is "fully standalone, with no
project references between them" — that rule is scoped to components
referencing *each other*, not tooling referencing components. This project's
whole purpose is exercising several components interactively from one
process, so it `ProjectReference`s each one directly, the same way each
component's own `.Tests` project already does.

## Building and running

Requires Windows. On a Windows machine with the .NET 10 SDK:

```bash
dotnet run --project exerciser/Exerciser.csproj
```

It also compiles (but can't run) on non-Windows hosts via
`EnableWindowsTargeting`, so `dotnet build exerciser/Exerciser.csproj` can
still catch compile errors in CI or on a Linux dev machine.

## Menu structure

- **`[0] Setup / Cleanup`** — one entry per component that needs a disposable
  fixture (a test service, a test event log source) or a reminder of a manual
  prerequisite (locking the workstation, an OCR fixture image's path).
- One entry per component (`SessionUtils`, `OcrUtils`, `TerminalUtils`,
  `ServiceUtils`, `EventLogUtils` today; `ResourceLockUtils` in a follow-up PR —
  see `TESTING.md`'s per-component sections for what's covered where).
- Each component's menu lists its methods with a short hint carrying
  TESTING.md's caveats ("needs a real Windows service", "DISRUPTIVE", and so
  on). Picking one prompts for its inputs via the shared `Prompt` helper, then
  prints the result via `Report.Result` — which explicitly labels a `null`
  message as `(no message - normal negative)`, since every method here follows
  the repo's never-throws (`bool` + `out string message`) contract, and a null
  vs. non-null message is a meaningful, documented distinction, not an
  afterthought.

`Simple`-suffixed wrapper overloads (e.g. `IsWorkstationLockedSimple` next to
`IsWorkstationLocked`) are generally omitted from these menus in favor of
their non-`Simple` siblings, which return strictly more output for the same
underlying call.

## Fixtures

`Fixtures/ocr-sample.png` — a 400x120 image with the known text
"Hello Exerciser", for `OcrUtils.GetTextFromImageFile`'s default input. Copied
to the output directory on build.

## Setup helpers

`NullService/` (`NullService.csproj`) is a trivial do-nothing Windows service,
built separately (`dotnet build exerciser/NullService/NullService.csproj`) and
installed as the disposable `ZZTestSvc` by the Setup/Cleanup menu's "ServiceUtils:
create ZZTestSvc" entry (`sc create`/`sc delete`, via a plain `ServiceBase`
subclass that answers start/stop/pause/continue and does nothing else). Kept
out of `Exerciser.csproj`'s own compile glob via an explicit `Compile Remove`,
the same way each component's `.Tests` subfolder is excluded from its own
assembly. **Never point `ServiceUtils` at a real system service** — this
disposable target is the only safe one, per TESTING.md's explicit warning.

The Setup/Cleanup menu's "EventLogUtils: create ZZTestEventLogUtils source"
entry calls `CreateEventSourceSimple` directly (needs an elevated session —
this doesn't remove that requirement, just saves hand-typing); its "remove"
entry shells out to PowerShell's `Remove-EventLog`, since the component itself
has no programmatic delete. **Never point `EventLogUtils` at the
Application/System/Security logs directly.**
