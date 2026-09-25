# Contributing to Awesome RPA Utils

Thanks for your interest in contributing! Please read our
[Code of Conduct](CODE_OF_CONDUCT.md). To report a security problem, follow the
[Security Policy](SECURITY.md) rather than opening a public issue.

## Building

```bash
dotnet build src/AwesomeRpaUtils.sln
```

Requires the .NET 8 SDK and the .NET 10 SDK, both installed side by side. All
components multi-target Windows-only TFMs (`net8.0-windows` and
`net10.0-windows`, or `net8.0-windows10.0.19041.0`/`net10.0-windows10.0.19041.0`
for `ocrutils`, which consumes WinRT's `Windows.Media.Ocr`). A single
`dotnet build` builds both target frameworks. The solution *compiles* on
macOS/Linux via `EnableWindowsTargeting` in the projects that need it, but
running or manually verifying any component's real Windows behavior requires
Windows.

## Automated tests are required

**Every change that adds or alters behavior must come with automated tests.**
A pull request without them will not be merged, except for documentation-only
changes and changes that genuinely cannot be tested without a desktop (see
"Behavior that needs a real desktop" below, and say so in the PR).

- **New method or new behavior:** tests for the normal outcome, for each edge
  case you can think of, and for the [never-throws
  contract](project-docs/coding-standards/never-throws-standard.md): bad input
  must return `false` with a message and must never throw.
- **Bug fix:** add a regression test that **fails without your fix** and passes
  with it, and say in the PR that you saw it fail. A fix without one tends to
  come back.
- **Changed behavior:** update the tests that described the old behavior; do
  not delete a test to make a change pass.

### How tests are organized

Each component has a test project in a subfolder of its own folder, using xunit:

```
src/stackutils/
  StackUtils.csproj
  StackUtils.Tests/
    StackUtils.Tests.csproj
    StackUtilsTests.cs
```

- The component's `.csproj` must contain `<Compile Remove="<Name>.Tests/**/*.cs" />`,
  or the default glob compiles your tests into the shipped DLL.
- Register the test project in `src/AwesomeRpaUtils.sln` (it is a normal project
  in the solution, next to the component).
- Copy the shape of an existing test project (`src/stackutils/StackUtils.Tests` is
  a small one); do not invent a new layout.

Run one component's tests, or all of them where your platform allows:

```bash
dotnet test src/stackutils/StackUtils.Tests/StackUtils.Tests.csproj
dotnet test src/AwesomeRpaUtils.sln
```

`UIAutomationUtils` pulls in the Windows desktop runtime, so its tests run only on
Windows and abort a solution-wide `dotnet test` on Linux/macOS; run the other test
projects individually there.

**Continuous integration runs the tests** on Windows after building: every test
project in `src/AwesomeRpaUtils.sln` (one project at a time, so timing-sensitive
tests are not starved by their neighbors), plus the two test projects that live
outside the solution, `tools/RestCodeGenerator.Tests` and
`component-browser/ComponentBrowser.Tests`. A failing test fails the build. A new
test project must be added to the solution (or to the workflow, if it cannot live
there), or CI will not run it. Run the tests locally before you push
and paste the result (the `Passed!` line for each project you touched) into the pull
request. The CI test results are also attached to each run as a `test-results`
artifact.

If a test of yours only fails on the CI runner, do not skip it: find out why. The
usual causes are timing assumptions (a background task that may start late, a clock
resolution finer than the platform's) and state shared with another test.

### Writing good tests

- **Make the logic testable without a desktop.** Keep the Win32/WinRT P/Invoke
  layer thin and put parsing, validation, state handling and decisions in code a
  test can call directly. Tests that do not need a real window run on Linux and
  macOS as well as Windows, which is where most reviewers and contributors work.
- **Be deterministic.** Do not use sleeps to wait for another thread; wait on a
  condition with a timeout. Inject time (a clock) instead of reading the wall
  clock when the behavior depends on it. Do not share static state between tests;
  use a unique name or temp folder per test and clean it up.
- **Assert the outcome, not just "no exception".** Check the returned value, the
  `out` values and the message. A test that would still pass if the method did
  nothing is not a test.
- **Prove the test can fail.** Before you finish, break the code on purpose (for
  example remove the check you added), confirm the test fails, and restore it.
- **Keep the documentation honest.** If a README or `Documentation/` page shows
  an example, prefer a test that runs it (see `StateMachineUtils.Tests` for
  worked-example tests that read their definitions out of the docs).

### Behavior that needs a real desktop

Some behavior (clicking a dialog button, moving the cursor, reading a live screen)
can only be verified on a Windows desktop. Cover everything around it with
automated tests (argument guards, parsing, result shaping), and document the rest
as a manual step in [TESTING.md](TESTING.md), using the
[`test-harness/`](test-harness/README.md) where it helps. In the PR, state which
parts were verified manually and how.

## Conventions

Each component is a fully standalone `.csproj` under `src/` - no project
references between components, even where two components' functionality
overlaps slightly (for example both `MouseUtils` and `WindowUtils` can read a
window's bounds). Please preserve this: don't add a `ProjectReference`
between components.

- Namespace/assembly: `<Name>Automation` (e.g. `KeyboardAutomation`)
- Class: `<Name>Utils` (e.g. `KeyboardUtils`), inheriting `System.ComponentModel.Component`
- **Never throws.** Public methods return `bool` and report why in a trailing
  `out string message`; invalid input or a failed operation returns `false` with
  a message and never throws. Each component has its own internal
  `NeverThrowsGuard`. Read the
  [Never-Throws Standard](project-docs/coding-standards/never-throws-standard.md).
- **"Nothing to report" is a normal outcome, not an error:** return `true` and
  say so through an `out` value (`found`, `exists`, `itemAvailable`, ...) rather
  than failing.
- **Unique signatures.** Two public overloads with the same name and the same
  non-`out` parameter types are ambiguous in Robot Studio; give the second one a
  different name. See the
  [Signature Uniqueness Standard](project-docs/coding-standards/signature-uniqueness-standard.md).
- **Designer-friendly parameters:** strings, `bool`, `int`, `double`, or an enum
  (shown as a drop-down). No generics and no types that need a proxy object.
- Every public method: `[Category("<Component> - <Subcategory>")]` +
  `[Description("...")]` attributes (drives the Pega Robot Studio designer's
  method browser), plus full XML doc comments (`<summary>`, `<param>` for
  every parameter, `<returns>` where applicable)
- Each component ships a `README.md` (method reference tables + a "Notes &
  Caveats" section) and a `Documentation/` folder with usage examples, matching
  the existing components' shape

## Pull requests

- Keep the build clean: no new compiler warnings (`GenerateDocumentationFile`
  is on for every project, so incomplete XML doc comments will surface as
  `CS1573` warnings; fix these before opening a PR)
- Include the tests (see above) and the result of running them
- Update the affected component's `README.md` / `Documentation/` and, if you
  changed the public API, [CrossReference.md](CrossReference.md)
- If you're adding a new component, follow the conventions above rather than
  introducing a new shape, and register it everywhere it must appear:
  - a `README.md` in its folder and the
    `<None Include="README.md" Pack="true" PackagePath="\" />` item in its csproj
    (copy it from any existing component), or `scripts/Pack-NuGet.ps1` and the
    Build workflow's pack step will fail
  - the component and its test project in `src/AwesomeRpaUtils.sln`
  - its DLL in the `$releaseAssemblies` list in `scripts/Package-Release.ps1`
    (this list is hardcoded, so it is easy to forget)
  - a row in the root `README.md`, entries in `CrossReference.md`, and a section in
    `TESTING.md` for anything that needs manual verification
- Describe what you tested and how
