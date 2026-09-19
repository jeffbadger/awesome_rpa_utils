# Plan: NuGet Packaging + RPA Platform Integrations

## Context

`awesome_rpa_utils` is a suite of ~22 self-contained .NET components (net8.0-windows / net10.0-windows, MIT) built primarily for Pega Robot Studio, released today as zip archives via `Package-Release.ps1`. Goal: distribute the same components as **NuGet packages** (published **locally** for now) and make them consumable from **UiPath**, **Power Automate Desktop**, **Blue Prism**, and **Automation Anywhere** — phased: NuGet first, then UiPath (modern SDK), then the rest.

Platform TFM reality:
- UiPath Studio 2024.10+ runs modern projects on **.NET 8** → activity assemblies can target `net8.0-windows` and directly consume the existing `net8.0-windows` component DLLs.
- **Blue Prism** (VBOs) and **Power Automate Desktop** custom actions require **.NET Framework 4.8-class** assemblies → need a new `net48` TFM.
- Automation Anywhere 360 custom packages are .NET Framework/SDK-manifest based → also `net48`-class.

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

### Phase B verification
- Package builds + packs locally.
- Manual QA checklist in `integrations/uipath/README.md` (Studio load, activity appears in search, executes against a real window).

## Phase C — Power Automate Desktop

Deliverable: consumption guide `integrations/power-automate/README.md` — reference the **net48** assemblies (from the same NuGet packages) as custom actions / .NET script references, with worked examples for 2–3 components. No new build output beyond Phase A's net48 TFM.

## Phase D — Blue Prism

Deliverable: `integrations/blue-prism/README.md` (add the net48 DLLs as VBO object references, .NET 4.8 requirement) + an importable **VBO XML** wrapping a small starter set of methods (e.g. Window + Keyboard basics). VBO XML is hand-authored — treat as stretch/optional; docs-only fallback is acceptable if the XML proves brittle.

## Phase E — Automation Anywhere

Deliverable: A360 custom-package skeleton in `integrations/automation-anywhere/` — `packages.json` manifest + wrapper assembly referencing the net48 DLLs, plus a build/pack script and README. This is the most platform-specific; design it against the AA360 SDK manifest format during implementation and timebox — if the manifest contract turns out to require the full AA Visual Studio tooling, fall back to a documented "invoke via child process/REST" guide.

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