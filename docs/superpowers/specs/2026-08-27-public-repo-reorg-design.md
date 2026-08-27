# Public Repo Reorganization Design

## Overview

Prepare `awesome_rpa_utils` to go public on GitHub as a single repository (not split into per-component repos) with a proper GitHub Releases pipeline. This covers: moving all six components under `src/`, adding a LICENSE, adding CI + release GitHub Actions workflows, adding contributor-facing templates, untracking the internal `docs/superpowers/` planning artifacts, and updating the root README to match.

## Goals

- Single top-level `src/` directory holding all six components (`mouseutils`, `screencaptureutils`, `keyboardutils`, `windowutils`, `ocrutils`, `dialogutils`) plus `AwesomeRpaUtils.sln` and `Directory.Build.props`, moved together so the solution's relative project paths stay valid with no `.sln`/`.csproj` edits needed.
- GitHub Releases become usable via git tags: pushing a `v*` tag builds Release-configuration binaries and attaches them to an auto-created GitHub Release. No literal "releases" folder in the repo — GitHub's Releases feature is tag-driven, not folder-driven.
- CI validates every push/PR by building the whole solution on a real `windows-latest` GitHub-hosted runner — a stronger signal than this repo's dev environment, which only builds via `EnableWindowsTargeting` on Linux.
- `docs/superpowers/` (this session's AI-assisted specs/plans) stops being tracked by git going forward, so it doesn't ship in the public repo, while remaining on disk locally. This spec document itself will also stop being tracked once this reorg's own implementation task performs that untracking — that's expected and consistent, not a bug.
- Public-repo hygiene: MIT `LICENSE`, `CONTRIBUTING.md`, issue templates, PR template, and an updated README (new build paths, License section, link to Releases).

## Non-goals

- Splitting the six components into separate GitHub repositories. Confirmed with the user: stays one repo, one `src/` directory holding all six.
- Rewriting git history to scrub `docs/superpowers/` from past commits. The content isn't sensitive (no secrets), and a rewrite would touch nearly every commit from this session's design phase onward, requiring a force-push and breaking any existing clones. Untracking going forward (`git rm --cached`) is the chosen, non-destructive approach; history will still contain the old content unless the user separately asks for a rewrite later.
- Actually running the new GitHub Actions workflows to completion (this sandbox has no `gh`/GitHub Actions execution access, and `git push` to `origin` fails here — no SSH key configured). The workflows will be written correctly per GitHub Actions/`.NET`/`softprops/action-gh-release` conventions and left for the user to verify on a real push, since they're untestable from this environment.

## New Repository Layout

```
/
├── src/
│   ├── AwesomeRpaUtils.sln
│   ├── Directory.Build.props
│   ├── mouseutils/
│   ├── screencaptureutils/
│   ├── keyboardutils/
│   ├── windowutils/
│   ├── ocrutils/
│   └── dialogutils/
├── .github/
│   ├── workflows/
│   │   ├── build.yml
│   │   └── release.yml
│   ├── ISSUE_TEMPLATE/
│   │   ├── bug_report.md
│   │   └── feature_request.md
│   └── PULL_REQUEST_TEMPLATE.md
├── LICENSE
├── CONTRIBUTING.md
├── README.md
└── .gitignore
```

`docs/` disappears from the tracked tree entirely once `docs/superpowers/` is untracked (it's the only thing currently under `docs/`).

## File Moves

Move (via `git mv`, preserving history for each file) into `src/`:
- `AwesomeRpaUtils.sln`
- `Directory.Build.props`
- `mouseutils/` (all contents)
- `screencaptureutils/` (all contents)
- `keyboardutils/` (all contents)
- `windowutils/` (all contents)
- `ocrutils/` (all contents)
- `dialogutils/` (all contents)

No content changes to any moved file. The `.sln`'s project references are relative (e.g. `mouseutils\MouseAutomation.csproj`) and each `.csproj`'s own relative paths (e.g. `Directory.Build.props` lookup via MSBuild's directory-walk) stay correct because everything moves together as one unit — verified by rebuilding after the move (see Verification below).

**Consequence:** `Directory.Build.props`'s `$(MSBuildThisFileDirectory)bin\`/`obj\` output paths now resolve to `src/bin/`/`src/obj/` instead of the repo-root `bin/`/`obj/`. This is a behavior change worth calling out in the moved README's Building section, but not a problem — `.gitignore`'s `[Bb]in/`/`[Oo]bj/` patterns are unanchored and still match at the new depth.

## `docs/superpowers/` Untracking

```bash
git rm -r --cached docs/superpowers
```
Then add to `.gitignore`:
```
# Internal AI-assisted design docs (kept locally, not shipped publicly)
docs/superpowers/
```
This removes it from the git index (so it no longer appears in `git status`/future commits/the pushed tree) while leaving the files untouched on disk. Past commits that already contain this content are unaffected (see Non-goals).

## `.github/workflows/build.yml`

Triggers: `push` to `main`, `pull_request` targeting `main`.
Runner: `windows-latest` (required — all six projects target Windows-only TFMs).

```yaml
name: Build

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Restore
        run: dotnet restore src/AwesomeRpaUtils.sln
      - name: Build
        run: dotnet build src/AwesomeRpaUtils.sln --configuration Release --no-restore
```

## `.github/workflows/release.yml`

Triggers: `push` of tags matching `v*` (e.g. `v1.0.0`).
Runner: `windows-latest`.

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

jobs:
  release:
    runs-on: windows-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Restore
        run: dotnet restore src/AwesomeRpaUtils.sln
      - name: Build
        run: dotnet build src/AwesomeRpaUtils.sln --configuration Release --no-restore
      - name: Zip build output
        shell: pwsh
        run: |
          Compress-Archive -Path src/bin/Release/* -DestinationPath AwesomeRpaUtils-${{ github.ref_name }}.zip
      - name: Create Release
        uses: softprops/action-gh-release@v2
        with:
          generate_release_notes: true
          files: AwesomeRpaUtils-${{ github.ref_name }}.zip
```

This zips the whole combined `src/bin/Release/` output (all six DLLs + their XML doc files across the `net10.0-windows` and `net10.0-windows10.0.19041.0` subfolders that `Directory.Build.props` produces) into one archive per release, matching the repo's existing "single combined bin folder" convention rather than inventing a new per-component packaging scheme.

## `LICENSE`

Standard MIT license text, `Copyright (c) 2026 Jeff Badger`.

## `CONTRIBUTING.md`

Short, practical:
- How to build (`dotnet build src/AwesomeRpaUtils.sln`, Windows required for full functionality; Linux/macOS can compile via `EnableWindowsTargeting` but can't run/verify Windows APIs)
- No automated test suite by design (documented existing repo convention) — verification is a clean build plus manual smoke-testing on Windows
- Conventions to follow: one component per folder, `XAutomation` assembly/namespace, `XUtils` class, standalone (no cross-component project references), `[Category]`/`[Description]` on every public method, README + `Documentation/` folder per component
- PR expectations: clean build, no new warnings

## `.github/ISSUE_TEMPLATE/bug_report.md` and `feature_request.md`

Minimal standard GitHub issue templates (front-matter `name`/`about`/`title`/`labels`, a few prompting sections: repro steps/expected/actual for bugs; problem/proposed solution for features).

## `.github/PULL_REQUEST_TEMPLATE.md`

Minimal: what changed, why, how it was tested (build clean / manual smoke test on Windows), checklist (follows existing component conventions, docs updated if public API changed).

## README Updates

- `## Building` code block: `dotnet build AwesomeRpaUtils.sln` → `dotnet build src/AwesomeRpaUtils.sln`.
- Component table links: `mouseutils/README.md` → `src/mouseutils/README.md` (and same for all six, plus their `Documentation/` links).
- `Directory.Build.props` link: `Directory.Build.props` → `src/Directory.Build.props`.
- Add a `## License` section near the bottom linking to `LICENSE` (MIT).
- Add a line pointing at the repo's Releases page for downloading prebuilt DLLs, near the top or in Building.

## Verification

- After moving files: `dotnet build src/AwesomeRpaUtils.sln` (use `/usr/bin/dotnet` directly per this session's established practice of bypassing a CLI wrapper that has produced false-clean results) — must show all 6 projects, 0 warnings, 0 errors, same as before the move.
- `git status` clean after each commit; `docs/superpowers/` must no longer appear in `git status` after the untracking step (but must still exist on disk).
- All README links spot-checked to resolve to real files at their new paths.
- YAML workflow files checked for valid syntax (no execution possible in this environment — flagged in Non-goals).
