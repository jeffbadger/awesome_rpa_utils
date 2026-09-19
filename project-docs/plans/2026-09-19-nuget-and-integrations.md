# Plan: NuGet Packaging + RPA Platform Integrations

## Context

`awesome_rpa_utils` is a suite of ~22 self-contained .NET components (net8.0-windows / net10.0-windows, MIT) built primarily for Pega Robot Studio, released today as zip archives via `Package-Release.ps1`. Goal: distribute the same components as **NuGet packages** (published **locally** for now) and make them consumable from **UiPath**, **Power Automate Desktop**, **Blue Prism**, and **Automation Anywhere** — phased: NuGet first, then UiPath (modern SDK), then the rest.

Platform TFM reality:
- UiPath Studio 2024.10+ runs modern projects on **.NET 8** → activity assemblies can target `net8.0-windows` and directly consume the existing `net8.0-windows` component DLLs.
- **Blue Prism** (VBOs) and **Power Automate Desktop** custom actions require **.NET Framework 4.8-class** assemblies → need a new `net48` TFM.
- Automation Anywhere 360 custom packages are .NET Framework/SDK-manifest based → also `net48`-class. *(Corrected — see the Phase E status note: A360 custom packages are Java-based.)*

## Decisions

- **Feed**: local folder (`artifacts/nuget/`) for now; structure so GitHub Packages / nuget.org push can be added later without restructuring.
- **net48**: yes — added repo-wide. Expected exception: `OcrUtils` (WinRT-only, stays net8/net10; documented exclusion).
- **Package granularity**: one NuGet package per component (mirrors per-component self-contained csprojs and per-component release archives). Meta-package deferred.
- **Versioning**: package version = repo tag without the `v` (first packaged release: next tag, `v0.4.0`).
- **UiPath flavor**: modern SDK, `net8.0-windows` activities.

## Step 0 — Pull origin (must be first)

Local `main` is ~90 commits behind `origin/main` (new components: InterruptUtils; `feat/clipboardutils` branch exists; current tag `v0.3.19`).

⚠️ Known environment gotchas (from memory):
- Treat any Bash-wrapped git remote-state command (`fetch`, `log <remote-ref>`, `show --stat HEAD`) as potentially **stale under the rtk hook** — cross-check with `rtk proxy git <same-command>` before trusting.
- A plain `git pull` under rtk hooks has previously **silently rebased and pushed** to a shared branch. Do NOT use `git pull`. Instead: `rtk proxy git fetch origin`, then `git merge --ff-only origin/main` (local has zero commits ahead, so ff-only is safe and fails loudly if not).

After the pull, re-derive the authoritative component list from `ls src/` and confirm `Package-Release.ps1`'s `$releaseAssemblies` reflects all current components.

## Phase A — NuGet packaging (per component)

1. **Shared pack settings** in `src/Directory.Build.props` (only projects under `src/` import it; test projects already set `IsPackable=false` explicitly):
   - `<IsPackable>` default `true` (only when unset)
   - `PackageId` default = `AwesomeRpaUtils.$(AssemblyName)` (e.g. `AwesomeRpaUtils.WindowAutomation`); individual csprojs may override
   - `Authors=Jeff Badger`, `PackageLicenseExpression=MIT`, `PackageReadmeFile` + `RepositoryUrl` (github repo), `IncludeSymbols`/`SymbolPackageFormat=snupkg` (optional)
   - `Version` supplied at pack time from the tag (no hardcoded version in csprojs)
   - Conditional `Microsoft.NETFramework.ReferenceAssemblies` PackageReference (`PrivateAssets=all`) so `net48` builds cross-platform (needed on Linux; CI runs windows-latest so it's belt-and-suspenders)
2. **Per-component csproj edits** (~22 files): add `net48` to `<TargetFrameworks>`. OcrUtils: no net48 (WinRT). Fix fallout per component as the audit finds it (see verification).
3. **Pack script** `scripts/Pack-NuGet.ps1`: `dotnet pack` all packable csprojs under `src/` (excluding `*.Tests` and the `OcrUtils` net48 carve-out), `-p:Version` from tag or `-Version` arg, output to `artifacts/nuget/`. Mirrors `Package-Release.ps1` conventions.
4. **CI**: extend `.github/workflows/release.yml` to run `Pack-NuGet.ps1` and upload the `.nupkg`s as release assets. Also keep them as ordinary build artifacts on `build.yml` (optional). No feed push yet.
5. **Docs**: root README "Releasing"/"Consuming" section — how to add `artifacts/nuget` as a folder feed (`nuget.config` in a consumer project) and install `AwesomeRpaUtils.*`.

### Phase A verification
- `dotnet build src/AwesomeRpaUtils.sln -c Release` clean (all TFMs, including net48, on windows CI; locally on Linux where projects allow).
- `dotnet test` every Linux-runnable test project before/after adding net48 — zero regressions.
- Net48 audit findings to fix if present: `Math.Clamp`/`init` accessors/`System.Text.Json`-style net8-only BCL usage (Newtonsoft-based JsonUtils should be fine), WinRT references outside OcrUtils.
- `Pack-NuGet.ps1` produces one nupkg per component; verify with `unzip -l` that each contains lib/net48, lib/net8.0-windows, lib/net10.0-windows and README (per the repo's "verify the artifact, don't eyeball the script" lesson).
- Smoke test: scratch console project with a `nuget.config` pointing at `artifacts/nuget/`, install one package (e.g. JsonUtils), call one method.

### Phase A status (2026-09-19) — COMPLETE, pending review/PR

All of the above is done, with two deviations from the wording above:

- **Pack defaults live in `src/Directory.Build.targets`, not `Directory.Build.props`.** A pack-metadata
  PropertyGroup in props never fires: at props-evaluation time `$(TargetFrameworks)`/`$(AssemblyName)`
  are still unset (the first local pack produced un-prefixed package ids with default authors/description
  before this was caught). Targets are imported after each csproj body, so conditions like
  `$(TargetFrameworks.Contains('net48'))` and the `.Tests` name exclusion work there.
- **OcrUtils needed the `None Include="README.md" Pack` item too** — it was skipped in the first
  csproj edit pass (it has no net48 change), but it *does* have a README.md and `PackageReadmeFile`
  is derived from the file's existence in targets, so pack failed NU5039 until the item was added.

Net48 work: all 20 non-OcrUtils components now multi-target net48. Two build-fix iterations
(42 → 0 errors) introduced per-component compat shims (`WinCompat` in wineventutils/interruptutils,
`Net48Compat` in commandlineutils, `ZipCompat`/`SimpleExpressionMatcher`/`ArchiveCore.GetRelativePath`
+ `ArchiveCore.ExtractToDirectory` in archiveutils) and unconditional call-site rewrites elsewhere.
`ZipCompat` parses the ZIP central directory on net48 for the .NET 8-only `Crc32`/`IsEncrypted`
entry properties (Zip64 out of scope, reads as CRC 0 / not-encrypted); `ArchiveCore.ExtractToDirectory`
reproduces the modern BCL's overwrite-overload semantics entry-by-entry on net48.

Verification performed: full solution build green (43 projects, all TFMs); every Linux-runnable test
suite passes (the one WindowUtils failure — `PlainStateGetters_ZeroHandle_ReturnFalse` — reproduces
on unmodified main, environment-related, not caused by this work; UIAutomation/ScreenCapture/Ocr test
hosts abort on Linux for lack of the WindowsDesktop runtime, also pre-existing). `Pack-NuGet.ps1`
produced 21 packages; every nupkg verified programmatically for the `AwesomeRpaUtils.` id prefix,
all expected `lib/` TFM folders, packed README, MIT license expression, and authors. Consumer smoke
test: scratch net10.0-windows console project restored 3 packages from the folder feed (transitive
deps resolved) and compiled against the public API (execution needs a Windows desktop runtime).

## Phase B — UiPath activities (modern, net8.0-windows)

1. New project `integrations/uipath/AwesomeRpaUtils.Activities/AwesomeRpaUtils.Activities.csproj`:
   - `TargetFramework net8.0-windows`, references `UiPath.Activities.Sdk`, `ProjectReference`s to the wrapped components (start: Window, Mouse, Keyboard, Dialog, Json).
   - NOT in `src/AwesomeRpaUtils.sln` (follows RestCodeGenerator/ComponentBrowser precedent).
   - PackageId `AwesomeRpaUtils.Activities`, packed into the same `artifacts/nuget/` feed.
2. Thin `[Activity]`-decorated wrapper classes delegating to component methods (bool + `out` pattern maps cleanly to UiPath In/Out arguments; keep names aligned with the component method names).
3. ⚠️ Verify `UiPath.Activities.Sdk` supports a `net8.0-windows` target at implementation time. Fallback if not: target `net6.0-windows` and add a `net6.0-windows` TFM to the wrapped components.
4. Local pack; Studio install via "Manage Packages → local feed" (manual, Windows-only QA — document steps).

### Phase B status (2026-09-19) — COMPLETE, pending review/PR

Research resolved step 3's open question: `UiPath.Activities.Sdk` does not exist on nuget.org. The
official `UiPath.Activities.Template` targets **net6.0** and pulls the workflow SDK pieces from the
UiPath Official feed, so the documented fallback is in force:

- Activities project targets `net6.0-windows`; the 5 wrapped components (Window, Mouse, Keyboard,
  Dialog, Json) gained a `net6.0-windows` TFM (all still build/test clean on net8/net10/net48).
- Package set (from the official template): `System.Activities.ViewModels` 1.20260609.1 as a public
  dependency, `UiPath.Activities.Api` 24.10.1 and `UiPath.Workflow` 6.0.0-20240401-07 as
  `PrivateAssets=All` (Studio ships its own copies, so they must not leak into the nuspec deps).
  Sources live in the project's own `nuget.config` (UiPath Official feed + nuget.org).
- 17 thin `CodeActivity`/`CodeActivity<T>` wrappers (5 Window, 5 Mouse, 3 Keyboard, 3 Dialog, 3 Json)
  under `Awesome RPA Utils > <Sub>` categories, delegating to the component methods. Failed
  component calls (bool + out-message pattern) throw an exception carrying the component message.
- Verified: `dotnet pack -p:Version=0.4.0` produces `AwesomeRpaUtils.Activities.0.4.0.nupkg` whose
  nuspec declares all five component packages at **0.4.0** (`-p:Version` is a global property, so it
  flows into the ProjectReference-derived dependency versions — no 1.0.0 drift), excludes the
  PrivateAssets UiPath packages, and contains `lib/net6.0-windows7.0/AwesomeRpaUtils.Activities.dll`
  + README. `Pack-NuGet.ps1` extended to pack the activities project when present (feed grew to 22
  packages); full sln build green; jsonutils/stack/localqueue/datacontract suites pass.

### Phase B verification
- Package builds + packs locally.
- Manual QA checklist in `integrations/uipath/README.md` (Studio load, activity appears in search, executes against a real window).

## Phase C — Power Automate Desktop

Deliverable: consumption guide `integrations/power-automate/README.md` — reference the **net48** assemblies (from the same NuGet packages) as custom actions / .NET script references, with worked examples for 2–3 components. No new build output beyond Phase A's net48 TFM.

### Phase C status (2026-09-19) — COMPLETE, pending review/PR

Guide written against the live Microsoft Learn docs (create-custom-actions, scripting actions
reference, build-custom-action guidance). Two consumption paths documented:

- **Run .NET script** (quick): "References to be loaded" folder + per-component dependency-DLL
  table (JsonAutomation → Newtonsoft.Json 13.0.3; WindowAutomation → System.Text.Json 8.0.5 + its
  dependency set; Keyboard/Mouse → none); three C# examples (Json, Window, Keyboard) written for
  **C# 5.0** — the version PAD's .NET scripting action compiles (no interpolation/`?.`/`out var`,
  verified against the docs) — with Script Parameters (In/Out) mapping, including the
  "Out parameter must be assigned or the action errors" requirement.
- **Native custom actions (Actions SDK)**: net472/48 class library named `Modules.<name>.dll`,
  `Microsoft.PowerPlatform.PowerAutomate.Desktop.Actions.SDK` (renamed namespace — the older
  `Microsoft.PowerAutomate.Desktop…` one is dead), two sample wrapper classes
  (`GetJsonValue`, `WaitForWindow`) with `[Action]`/`[InputArgument]`/`[OutputArgument]` and
  `ActionException` on failure, and the sign-all-DLLs (wrapper *and* dependencies) → cab →
  make.powerautomate.com upload chain, linked to Microsoft's walkthrough for the
  environment-specific commands.

## Phase D — Blue Prism

Deliverable: `integrations/blue-prism/README.md` (add the net48 DLLs as VBO object references, .NET 4.8 requirement) + an importable **VBO XML** wrapping a small starter set of methods (e.g. Window + Keyboard basics). VBO XML is hand-authored — treat as stretch/optional; docs-only fallback is acceptable if the XML proves brittle.

### Phase D status (2026-09-19) — COMPLETE, pending review/PR (docs-only fallback taken)

Guide written against the Blue Prism 7.5 documentation (Code Options tab: language /
External References / Namespace Imports — both required for .NET libraries; Global Code;
Check Code) and the community's runtime-loading findings (DLLs beside `Automate.exe` is the
reliable placement; full-path references compile but some assemblies fail to load at runtime
from other folders). Contents: net48 assembly sources, per-component dependency-DLL table,
deployment on every runtime resource, the one-time object setup steps, and a wrapping pattern
(Global Code instantiation + one code stage per method) with starter snippets for
Window/Json/Keyboard/Mouse mapped onto the `(bool, out message)` contract — including the
WaitForWindow null-message-on-timeout `??` fallback.

**VBO XML intentionally skipped** (the plan's documented fallback): the `.bprelease`/VBO export
format is fragile to hand-author and a broken import is worse than the ~2-minute manual setup;
the guide ships paste-ready Global Code + code-stage snippets instead, with a note that a real
install-exported VBO can be added later if wanted.

## Phase E — Automation Anywhere

Deliverable: A360 custom-package skeleton in `integrations/automation-anywhere/` — `packages.json` manifest + wrapper assembly referencing the net48 DLLs, plus a build/pack script and README. This is the most platform-specific; design it against the AA360 SDK manifest format during implementation and timebox — if the manifest contract turns out to require the full AA Visual Studio tooling, fall back to a documented "invoke via child process/REST" guide.

### Phase E status (2026-09-19) — COMPLETE, pending review/PR (fallback taken; plan assumption corrected)

**The `packages.json`-manifest assumption was wrong**: Automation 360 custom packages are
**Java-based** (Package SDK → JDK 11 + Gradle → JAR of `@BotCommand`/`@CommandPkg`-annotated
actions; verified against AA Community's creating-custom-packages/tutorial posts and the
Package SDK release notes). There is no supported C# custom-package manifest, so the planned
`packages.json` + wrapper-assembly skeleton was dropped entirely.

Delivered instead (the plan's documented fallback): `integrations/automation-anywhere/README.md`
— a **PowerShell bridge** pattern (bots call the PowerShell package's Run script; the script
`Add-Type`/`LoadFrom`s the net48 component + dependency DLLs, calls the component, and emits a
`ConvertTo-Json` envelope on stdout with non-zero exit code + stderr on failure), two worked
examples (Json read; Window wait with the null-message-on-timeout fallback and the
windowHandle-as-number round-trip), the dependency-DLL table, bot-agent deployment notes
(.NET 4.8, Windows desktop session for UI components), and a description of the *real* native
path — a Java Package SDK package wrapping the same bridge via `ProcessBuilder` — framed as an
advanced, do-it-only-if-many-bots option rather than delivered code.

## Execution conventions (per repo practice)

- One worktree + one PR per phase: `.worktrees/<name>`, branch `feature/<name>`, PR via `gh`, worktree removed after merge. Phase A first as its own PR.
- Plan doc copied into `project-docs/plans/2026-09-19-nuget-and-integrations.md` at Phase A start and amended in place as phases progress (repo convention: plan stays ground truth).
- `Package-Release.ps1`/`Package-Documentation.ps1` are unaffected (zip packaging continues unchanged) — verify no interaction during Phase A.

## Verification summary

1. `dotnet build src/AwesomeRpaUtils.sln -c Release` green on windows-latest CI (PR check).
2. All Linux-runnable test suites pass before/after net48 addition.
3. `artifacts/nuget/` contains one nupkg per component; contents verified via `unzip -l` (all three TFMs + README + license).
4. Consumer smoke test installs a package from the folder feed and calls a method.
5. UiPath package loads in Studio (manual checklist), PAD/BluePrism/AA guides verified step-by-step on a Windows machine where available.