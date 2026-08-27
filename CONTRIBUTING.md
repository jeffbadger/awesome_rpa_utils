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
