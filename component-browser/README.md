# Component Browser

A WPF desktop tool for browsing what's actually inside a built release of this
repo. Point it at a release zip (`AwesomeRpaUtils-{tfm}-vX.Y.Z.zip`, from
`scripts/Package-Release.ps1`) and it lists the components found inside,
lets you drill into each one's Properties/Methods/Events (PMEs), and shows
full detail for a selected PME: its real signature (read directly from the
DLL), its category, its shipped description, the component's Notes &
Caveats, and a pointer to a matching `Documentation/*.md` worked example
when one exists.

This is a standalone project, deliberately kept outside `src/` and out of
`AwesomeRpaUtils.sln` — it's developer tooling, not one of the shipped
components (the same treatment `test-harness/` and `tools/RestCodeGenerator/`
already get).

## Why a release zip, not a plain folder

A release zip has two data sources this tool combines:

- **The component DLLs** — reflected (never executed — see below) for the
  ground-truth PME list, real signatures, and `[Category]`/`[Description]`
  attribute values, even if the shipped docs ever drift from the code.
- **The nested `AwesomeRpaUtils-Documentation.zip`** — every component's
  `README.md` (its method tables and "Notes & Caveats" section) and
  `Documentation/*.md` worked examples, the same curated content a human
  reads. `Package-Release.ps1` does not bundle each DLL's XML-doc output, so
  this is the only real source of description text in a release archive.

## How it works

All the logic lives in **`ComponentBrowser.Core`**, a plain `net10.0` class
library with no Windows/WPF dependency at all — deliberately split out from
the WPF exe so it (and the tests that exercise it) run on any OS, not just
build there. `ComponentBrowser.csproj` (the actual WPF app) references it
via a project reference; `MainWindow.xaml.cs` only wires button
clicks/selection changes to it and binds the results.

- **`ReleaseZipLoader`** extracts the release zip, its nested
  `AwesomeRpaUtils-Documentation.zip`, and its nested
  `AwesomeRpaUtils-SupportLibraries.zip` to a per-load temp directory
  (support libraries land in the *same* folder as the component DLLs, not a
  subfolder — that's genuinely required, not cosmetic: `EventLogAutomation.dll`/
  `ServiceAutomation.dll` depend on packages (`System.Diagnostics.EventLog`,
  `System.ServiceProcess.ServiceController`) that `Package-Release.ps1` ships
  in that separate nested archive, and reflecting a dependent DLL under
  `MetadataLoadContext` fails outright without them resolvable). Exposes
  `PrimaryDllFileNames` — the file names that were actually at the outer
  zip's root, captured *before* the support-libraries overlay — since some
  of those support packages themselves define an unrelated
  `System.ComponentModel.Component`-derived BCL type (`System.Diagnostics.EventLog`,
  `System.ServiceProcess.ServiceController` are themselves `Component`
  subclasses); without this distinction the browser would list them as
  spurious extra "components" alongside this suite's real ones. Cleaned up
  on window close or before the next load.
- **`AssemblyInspector`** reflects over the extracted DLLs using
  `System.Reflection.MetadataLoadContext` — deliberately not
  `Assembly.LoadFrom`, which would execute the assembly's code just by
  loading it (several of this suite's own components P/Invoke into Windows
  APIs on load). `InspectDirectory`'s optional candidate-file-names
  parameter (pass `LoadedRelease.PrimaryDllFileNames`) restricts *which*
  DLLs are scanned for a component type without restricting which DLLs are
  available to the resolver, for the reason above. For each candidate it
  finds the one public `System.ComponentModel.Component`-derived type (this
  suite's own convention) and reflects its public Properties/Methods/Events.
  `[Category]`/`[Description]` attribute values are read via
  `CustomAttributeData` rather than `GetCustomAttribute<T>()`, since
  `MetadataLoadContext` never constructs real attribute instances.
- **`ReadmeDocParser`** parses a component's `README.md` (its `### Category`
  method tables and `## Notes & Caveats` section) and detects a matching
  `Documentation/<Category>.md` worked-example page when one exists.
- **`ComponentCatalogBuilder`** joins the two: a reflected DLL is matched to
  its shipped README by assembly name (read from the README's own leading
  `# AssemblyName` heading — the only path both sides agree on, since a
  DLL's filename and its source folder name don't match, e.g.
  `EventLogAutomation.dll` ships from `src/eventlogutils/`). Each PME's
  description is matched to a README row by method name (falling back to a
  signature-text tiebreak for overloaded names); `[Category]` always comes
  from the reflected attribute, not from which README heading a row
  happened to sit under, so it stays correct even if the docs ever drift.

## Building, running, and testing

Requires Windows to *run* the WPF app (WPF doesn't run headless). On a
Windows machine with the .NET 10 SDK:

```bash
dotnet run --project component-browser/ComponentBrowser.csproj
```

The app also compiles (but can't run) on non-Windows hosts via
`EnableWindowsTargeting`, so `dotnet build component-browser/ComponentBrowser.csproj`
still catches compile errors in CI or on a Linux dev machine.
`ComponentBrowser.Core` and `ComponentBrowser.Tests` need no such
allowance — plain `net10.0`, no Windows dependency, real functional tests
run on any OS:

```bash
dotnet test component-browser/ComponentBrowser.Tests/ComponentBrowser.Tests.csproj
```

`AssemblyInspectorTests` reflects this repo's own real built
`EventLogAutomation.dll`/`SessionAutomation.dll` (build them first —
`dotnet build src/eventlogutils/EventLogUtils.csproj` and the SessionUtils
equivalent — the tests fail with a clear message naming the exact command
if they're missing). `ReleaseZipLoaderEndToEndTests` builds a real release
zip with `scripts/Package-Release.ps1 -NoBuild` against an already-built
`Release` solution (`dotnet build src/AwesomeRpaUtils.sln --configuration
Release` first) and loads it for real — the single most valuable test in
this project, since it proves the app works against a real shipped
artifact, not an imagined one; it skips (not fails) if `pwsh` isn't on
PATH. To produce a release zip yourself to point the running app at:

```bash
pwsh ./scripts/Package-Release.ps1 -ArchivePath "artifacts/AwesomeRpaUtils-{tfm}.zip"
```

## Limitations

- Detection of "the component" in a DLL is exactly this suite's own
  convention (one public `Component`-derived class per DLL) — a DLL that
  doesn't follow that shape (e.g. a support library) is skipped, not an
  error.
- Description matching is by method name, with a best-effort signature
  tiebreak for overloads — not guaranteed exact when a README's signature
  text and the DLL's real reflected signature differ in more than
  whitespace.
- Worked-example detection only records which `Documentation/*.md` file
  matches a PME's category; it doesn't render that file's content in the
  detail pane.
