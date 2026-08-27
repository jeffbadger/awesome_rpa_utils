# Public Repo Reorganization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move all six components under `src/`, untrack `docs/superpowers/`, and add the GitHub public-repo scaffolding (LICENSE, CI + release workflows, contributor templates, updated README) needed before this repo goes public.

**Architecture:** Pure file reorganization plus new static files — no C#/logic changes to any component. Each task is independently verifiable via a clean `dotnet build` and `git status`.

**Tech Stack:** git, GitHub Actions (YAML), Markdown, MIT license text.

**Spec:** See `docs/superpowers/specs/2026-08-27-public-repo-reorg-design.md` for the approved design this plan implements.

---

### Task 1: Move all source files into `src/`

**Files:**
- Move: `AwesomeRpaUtils.sln` → `src/AwesomeRpaUtils.sln`
- Move: `Directory.Build.props` → `src/Directory.Build.props`
- Move: `mouseutils/` → `src/mouseutils/`
- Move: `screencaptureutils/` → `src/screencaptureutils/`
- Move: `keyboardutils/` → `src/keyboardutils/`
- Move: `windowutils/` → `src/windowutils/`
- Move: `ocrutils/` → `src/ocrutils/`
- Move: `dialogutils/` → `src/dialogutils/`

- [ ] **Step 1: Create `src/` and move everything with `git mv`**

```bash
mkdir -p src
git mv AwesomeRpaUtils.sln src/AwesomeRpaUtils.sln
git mv Directory.Build.props src/Directory.Build.props
git mv mouseutils src/mouseutils
git mv screencaptureutils src/screencaptureutils
git mv keyboardutils src/keyboardutils
git mv windowutils src/windowutils
git mv ocrutils src/ocrutils
git mv dialogutils src/dialogutils
```

- [ ] **Step 2: Build to verify the move didn't break anything**

Run (delete stale `bin`/`obj` first, use the real binary directly to avoid a CLI wrapper on this host that has produced false-clean results in the past):

```bash
rm -rf src/bin src/obj src/*/bin src/*/obj
/usr/bin/dotnet build src/AwesomeRpaUtils.sln --no-incremental
```

Expected: `Build succeeded.` — all 6 projects (MouseAutomation, ScreenCaptureAutomation, KeyboardAutomation, WindowAutomation, OcrAutomation, DialogAutomation), 0 Warning(s), 0 Error(s). Output now lands at `src/bin/Debug/...` instead of the old repo-root `bin/`.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Move all components and solution files under src/"
```

---

### Task 2: Untrack `docs/superpowers/` and update `.gitignore`

**Files:**
- Modify: `.gitignore`
- Untrack (keep on disk): `docs/superpowers/` (all contents)

- [ ] **Step 1: Remove `docs/superpowers/` from git's index, keeping the files on disk**

```bash
git rm -r --cached docs/superpowers
```

- [ ] **Step 2: Add it to `.gitignore`**

Add this block to the end of `.gitignore`:

```
# Internal AI-assisted design docs (kept locally, not shipped publicly)
docs/superpowers/
```

- [ ] **Step 3: Verify**

```bash
git status
```

Expected: `docs/superpowers/` no longer appears as tracked or untracked (it's now ignored); the files still exist on disk (`ls docs/superpowers/specs docs/superpowers/plans` should still list them).

- [ ] **Step 4: Commit**

```bash
git add .gitignore
git commit -m "Untrack docs/superpowers/ (internal design docs, kept local-only)"
```

---

### Task 3: Add `LICENSE`

**Files:**
- Create: `LICENSE`

- [ ] **Step 1: Create the file**

Create `LICENSE` at the repo root:

```
MIT License

Copyright (c) 2026 Jeff Badger

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: Commit**

```bash
git add LICENSE
git commit -m "Add MIT LICENSE"
```

---

### Task 4: Add CI workflow

**Files:**
- Create: `.github/workflows/build.yml`

- [ ] **Step 1: Create the workflow**

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

- [ ] **Step 2: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/build.yml'))" && echo "valid YAML"
```

(This cannot be executed as a real GitHub Actions run in this environment — no Actions runner access here. This step only checks the YAML parses; actual behavior must be verified on a real push.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "Add CI build workflow"
```

---

### Task 5: Add release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Create the workflow**

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

- [ ] **Step 2: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))" && echo "valid YAML"
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add release workflow (tag push -> GitHub Release with build artifacts)"
```

---

### Task 6: Add `CONTRIBUTING.md`

**Files:**
- Create: `CONTRIBUTING.md`

- [ ] **Step 1: Create the file**

```markdown
# Contributing to Awesome RPA Utils

Thanks for your interest in contributing!

## Building

```bash
dotnet build src/AwesomeRpaUtils.sln
```

Requires the .NET 10 SDK. All components target Windows-only TFMs
(`net10.0-windows`, or `net10.0-windows10.0.19041.0` for `ocrutils`, which
consumes WinRT's `Windows.Media.Ocr`). The solution *compiles* on macOS/Linux
via `EnableWindowsTargeting` in the projects that need it, but running or
manually verifying any component's actual behavior requires Windows.

## No automated test suite

This is a deliberate choice: every component is a thin wrapper around Win32/
WinRT APIs, which are impractical to unit test meaningfully without a real
Windows desktop session. Verification is a clean build plus manual
smoke-testing on Windows (ideally by dropping the component onto a Pega Robot
Studio design surface, or exercising it from a small throwaway console app).

## Conventions

Each component is a fully standalone `.csproj` under `src/` — no project
references between components, even where two components' functionality
overlaps slightly (e.g. both `MouseUtils` and `WindowUtils` can read a
window's bounds). Please preserve this: don't add a `ProjectReference`
between components.

- Namespace/assembly: `<Name>Automation` (e.g. `KeyboardAutomation`)
- Class: `<Name>Utils` (e.g. `KeyboardUtils`), inheriting `System.ComponentModel.Component`
- Every public method: `[Category("<Component> - <Subcategory>")]` +
  `[Description("...")]` attributes (drives the Pega Robot Studio designer's
  method browser), plus full XML doc comments (`<summary>`, `<param>` for
  every parameter, `<returns>` where applicable, `<exception>` for anything
  it can throw)
- Failed Win32 calls: throw `Win32Exception` with `Marshal.GetLastWin32Error()`
- Invalid arguments: throw `ArgumentException`
- "Not found" is a normal outcome, not an error: return a sentinel
  (`IntPtr.Zero`, `Rectangle.Empty`, `false`) rather than throwing
- Each component ships a `README.md` (method reference tables + a "Notes &
  Caveats" section) and a `Documentation/` folder with one usage-examples
  file per method category, matching the existing components' shape

## Pull requests

- Keep the build clean — no new compiler warnings (`GenerateDocumentationFile`
  is on for every project, so incomplete XML doc comments will surface as
  `CS1573` warnings; fix these before opening a PR)
- If you're adding a new component, follow the conventions above rather than
  introducing a new shape
- Describe what you tested and how, since there's no CI test suite to lean on
  beyond the build itself
```

- [ ] **Step 2: Commit**

```bash
git add CONTRIBUTING.md
git commit -m "Add CONTRIBUTING.md"
```

---

### Task 7: Add issue and PR templates

**Files:**
- Create: `.github/ISSUE_TEMPLATE/bug_report.md`
- Create: `.github/ISSUE_TEMPLATE/feature_request.md`
- Create: `.github/PULL_REQUEST_TEMPLATE.md`

- [ ] **Step 1: Create `bug_report.md`**

```markdown
---
name: Bug report
about: Report something that isn't working as expected
title: "[Bug] "
labels: bug
---

**Which component?**
(e.g. KeyboardUtils, WindowUtils, OcrUtils, DialogUtils, MouseUtils, ScreenCaptureUtils)

**What happened?**
A clear description of the incorrect behavior.

**What did you expect to happen?**

**Steps to reproduce**
1. ...
2. ...

**Environment**
- Windows version:
- .NET version:
- Pega Robot Studio version (if applicable):

**Additional context**
Any error messages, stack traces, or screenshots.
```

- [ ] **Step 2: Create `feature_request.md`**

```markdown
---
name: Feature request
about: Suggest a new method, component, or capability
title: "[Feature] "
labels: enhancement
---

**Which component does this affect?**
(e.g. an existing component, or a new one entirely)

**What problem are you trying to solve?**

**Proposed solution**
What you'd like to see added or changed.

**Alternatives considered**
Any other approaches you thought about.
```

- [ ] **Step 3: Create `PULL_REQUEST_TEMPLATE.md`**

```markdown
## What changed

## Why

## How was this tested?
- [ ] `dotnet build src/AwesomeRpaUtils.sln` succeeds with 0 warnings
- [ ] Manually verified on Windows (describe how, since there's no automated test suite)

## Checklist
- [ ] Follows the conventions in [CONTRIBUTING.md](../CONTRIBUTING.md) (naming, `[Category]`/`[Description]`, standalone components, XML docs)
- [ ] Updated the affected component's `README.md` / `Documentation/` if the public API changed
```

- [ ] **Step 4: Commit**

```bash
git add .github/ISSUE_TEMPLATE .github/PULL_REQUEST_TEMPLATE.md
git commit -m "Add issue and pull request templates"
```

---

### Task 8: Update root README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the component table links to point at `src/`**

Change every `[componentname](componentname/README.md)` link to
`[componentname](src/componentname/README.md)`, for all six components. E.g.:

```markdown
| [mouseutils](src/mouseutils/README.md) | `MouseAutomation` | Moves, clicks, drags, and scrolls the mouse via `SendInput`/`SetCursorPos`; controls cursor appearance, visibility, and confinement. |
```

Apply the same `src/` prefix to all six table rows and to every link in the
"## Documentation" section at the bottom of the file (both the `README.md`
links and the `Documentation/` links).

- [ ] **Step 2: Update the Building section**

Change:
```markdown
​```bash
dotnet build AwesomeRpaUtils.sln
​```
```
to:
```markdown
​```bash
dotnet build src/AwesomeRpaUtils.sln
​```
```

And update the `Directory.Build.props` link from `[Directory.Build.props](Directory.Build.props)` to `[Directory.Build.props](src/Directory.Build.props)`.

- [ ] **Step 3: Add a License section**

Add near the end of the file, after the Documentation section:

```markdown
## License

MIT — see [LICENSE](LICENSE).
```

- [ ] **Step 4: Mention Releases**

In the Requirements or Building section, add a short line:

```markdown
Prebuilt DLLs for each tagged version are available on the
[Releases](../../releases) page.
```

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "Update README for src/ layout, add License section and Releases link"
```

---

### Task 9: Final verification

**Files:** none (verification only)

- [ ] **Step 1: Full clean build**

```bash
rm -rf src/bin src/obj src/*/bin src/*/obj
/usr/bin/dotnet build src/AwesomeRpaUtils.sln --no-incremental
```

Expected: `Build succeeded.`, all 6 projects, 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Confirm repo layout matches the design**

```bash
ls -la
ls -la .github/workflows .github/ISSUE_TEMPLATE
```

Expected top level: `src/`, `.github/`, `LICENSE`, `CONTRIBUTING.md`, `README.md`, `.gitignore`, plus whatever `docs/` remnants exist (should be empty/gone since `docs/superpowers/` was the only thing in it and is now ignored — if `docs/` is now an empty tracked directory, that's fine, git doesn't track empty directories so it will simply not appear).

- [ ] **Step 3: Confirm `docs/superpowers/` is ignored, not tracked, and still on disk**

```bash
git status --ignored
ls docs/superpowers/specs docs/superpowers/plans
```

- [ ] **Step 4: Confirm nothing left uncommitted**

```bash
git status
```

Expected: `nothing to commit, working tree clean`.

- [ ] **Step 5: Spot-check README links resolve**

```bash
for f in src/mouseutils/README.md src/screencaptureutils/README.md src/keyboardutils/README.md src/windowutils/README.md src/ocrutils/README.md src/dialogutils/README.md LICENSE CONTRIBUTING.md; do
  [ -f "$f" ] && echo "OK: $f" || echo "MISSING: $f"
done
```

Expected: `OK:` for every line.
