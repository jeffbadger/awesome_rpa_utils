# Awesome RPA Utils

A collection of Pega Robot Studio-ready .NET components for Windows desktop
automation. Each component is a self-contained `.csproj` that drops onto a
Robot Studio design surface, with its own README and per-method usage docs.

| Component | Assembly | Description |
|---|---|---|
| [archiveutils](src/archiveutils/README.md) | `ArchiveAutomation` | Creates, extracts, inspects, and validates ZIP archives, with zip-slip and zip-bomb protection and independent CRC-32 verification built in. |
| [commandlineutils](src/commandlineutils/README.md) | `CommandLineAutomation` | Runs external commands/processes and captures their exit code, stdout, and stderr, including elevated and fire-and-forget launches. |
| [databagutils](src/databagutils/README.md) | `DataBagAutomation` | Defines a typed named-value contract during initialization, then provides strict scalar getters/setters and atomic JSON/DataTable updates at runtime. |
| [dialogutils](src/dialogutils/README.md) | `DialogAutomation` | Finds and dismisses native dialogs by button text/control ID via `BM_CLICK`, without moving the cursor. |
| [eventlogutils](src/eventlogutils/README.md) | `EventLogAutomation` | Reads, queries, waits for, writes, and exports/imports Windows Event Log entries via `EventLogReader`/`EventLog`. |
| [eventutils](src/eventutils/README.md) | `EventAutomation` | Watches Windows UI events via `SetWinEventHook` and delivers them the moment they happen: synchronous `WaitForX` calls or background subscriptions polled with `GetNextEvent`. |
| [filewatchutils](src/filewatchutils/README.md) | `FileWatchAutomation` | Coordinates with files produced by other applications: wait for existence/deletion/change/stability/unlock, watch for filesystem events, atomically move/replace/claim files, hash files, and read file metadata. |
| [keyboardutils](src/keyboardutils/README.md) | `KeyboardAutomation` | Injects keyboard input via `SendInput`: key presses, combos, typed text, and a clipboard-paste fallback; queries key/modifier state. |
| [localqueueutils](src/localqueueutils/README.md) | `LocalQueueAutomation` | Persists variable-count local JSON, text, or file work and exposes a designer-friendly lease/process/complete loop without a Pega collection proxy. |
| [mouseutils](src/mouseutils/README.md) | `MouseAutomation` | Moves, clicks, drags, and scrolls the mouse via `SendInput`/`SetCursorPos`; controls cursor appearance, visibility, and confinement. |
| [ocrutils](src/ocrutils/README.md) | `OcrAutomation` | Recognizes text from the screen or an image file via `Windows.Media.Ocr`, with plain-text and positioned-result options. |
| [screencaptureutils](src/screencaptureutils/README.md) | `ScreenCaptureAutomation` | Captures the screen, a region, or a window to a file/clipboard; compares captures against a baseline; annotates or redacts saved screenshots. |
| [serviceutils](src/serviceutils/README.md) | `ServiceAutomation` | Queries, starts, stops, restarts, and configures the startup type of Windows services. |
| [sessionutils](src/sessionutils/README.md) | `SessionAutomation` | Reports on and acts on Windows session/workstation state: session identity/kind, enumeration, connect state, lock/desktop availability, idle time, and deliberate lock/disconnect actions. |
| [stackutils](src/stackutils/README.md) | `StackAutomation` | Holds variable-count text, JSON, or file-reference work in an instance-local, in-memory LIFO stack without a collection proxy. |
| [terminalutils](src/terminalutils/README.md) | `TerminalAutomation` | Reads and interacts with a target process's live console screen buffer: visible-screen capture, cursor position, waiting for a prompt or a screen change, keystroke injection, and starting/attaching to a console process. |
| [uiautomationutils](src/uiautomationutils/README.md) | `UIAutomation` | Finds and drives modern (WinUI3/UWP/WPF/browser-hosted) UI via Windows UI Automation, for controls WindowUtils/DialogUtils can't see. |
| [windowutils](src/windowutils/README.md) | `WindowAutomation` | Enumerates, locates, moves/resizes, activates, and closes windows via the Win32 window APIs. |

Each component is fully standalone (no project references between them), but
they're designed to complement each other: MouseUtils and KeyboardUtils own
input (cursor/click and keyboard, respectively), ScreenCaptureUtils and
OcrUtils own pixels-to-information (images and recognized text), and
WindowUtils and DialogUtils own window management (general windows and
native dialogs, respectively).

## Requirements

- Windows (most projects multi-target `net8.0-windows` and `net10.0-windows`;
  `ocrutils` targets the versioned `net8.0-windows10.0.19041.0`/
  `net10.0-windows10.0.19041.0` to consume WinRT's `Windows.Media.Ocr`)
- .NET 8 SDK and .NET 10 SDK, both installed side by side
- Visual Studio 2022 (17.x) or later, or Pega Robot Studio, to consume the
  built components

## Building

```bash
dotnet build src/AwesomeRpaUtils.sln
```

All projects share build settings via [Directory.Build.props](src/Directory.Build.props),
which combines every project's output into a single `src/bin/` folder
(intermediate `obj/` output stays per-project to avoid concurrent-build
collisions). Each multi-targeted project builds both `net8.0-windows` and
`net10.0-windows` outputs side by side under `src/bin/<Configuration>/<tfm>/`
in a single `dotnet build` invocation — no extra flags needed.

Prebuilt DLLs for each tagged version are available on the
[Releases](../../releases) page as `net8.0`/`net10.0` archives; pick the one
matching the .NET runtime your Pega Robot Runtime or consuming app uses.
Each archive is self-contained — it also bundles the support libraries, the
REST code generator, and a documentation archive (see below) — so the method
reference is available without needing the repository.

## Packaging a release

To build and create everything this repository produces, run from PowerShell:

```powershell
./scripts/Package-Release.ps1
```

The command creates two self-contained archives — one per target framework,
each holding the complete release:

- `artifacts/AwesomeRpaUtils-net8.0.zip` contains the eighteen project DLLs built
  for `net8.0-windows` plus three bundled archives:
  `AwesomeRpaUtils-SupportLibraries.zip` (the three NuGet runtime DLLs needed
  by ServiceUtils, packaged in the flavor matching the enclosing archive's
  target framework), `AwesomeRpaUtils-RestCodeGenerator.zip` (the design-time
  [REST code generator](tools/README.md)), and
  `AwesomeRpaUtils-Documentation.zip` (the documentation bundle described
  below).
- `artifacts/AwesomeRpaUtils-net10.0.zip` contains the same eighteen DLLs built
  for `net10.0-windows` with the same three bundled archives (the support
  DLLs in the newest flavor the packages ship, which the .NET 10 runtime
  loads; the .NET 8 runtime only loads the `net8.0` flavor).

Everything excludes test infrastructure, PDBs, and XML documentation. The
`RestCodeGenerator` targets .NET 10, so it is runnable via
`dotnet RestCodeGenerator.dll <input> <apiName> <outputDirectory> [--format ...] [--component]`
wherever a .NET 10 runtime is installed, regardless of which archive it came
from. To package an existing Release build or choose another output path,
use:

```powershell
./scripts/Package-Release.ps1 -NoBuild `
  -ArchivePath "artifacts/AwesomeRpaUtils-{tfm}-v1.0.0.zip"
```

With `-NoBuild`, the generator archive is staged from the tool's existing
`tools/RestCodeGenerator/bin/<Configuration>/net10.0` output rather than
re-published, since the generator lives outside `src/AwesomeRpaUtils.sln`.

### Packaging documentation

To create a self-contained documentation archive — the top-level README,
every component's README and `Documentation/*.md` pages, and
[`project-docs/`](project-docs/README.md) — run:

```powershell
./scripts/Package-Documentation.ps1
```

This creates `artifacts/AwesomeRpaUtils-Documentation.zip`, preserving the
same relative folder layout as the repository so every link between bundled
pages keeps working once extracted. Any link that points outside the bundle
(a `.cs` source file, `LICENSE`, `TESTING.md`, `Directory.Build.props`, or a
GitHub-relative URL like the Releases link above) is rewritten to plain text
rather than shipped as a dangling reference — the bundle only ever links to
other pages inside itself. Choose a different output path with
`-ArchivePath`, e.g. `-ArchivePath artifacts/AwesomeRpaUtils-Documentation-v1.0.0.zip`.

`-ArchivePath` must contain a literal `{tfm}` placeholder — the script
substitutes it with `net8.0` and `net10.0` to produce one archive per target
framework.

## REST component code generation

Rather than hand-writing a REST component per API, the repository includes a
generator that reads an OpenAPI/Swagger file, a Postman collection, a Bruno
collection, or a raw curl command, and emits a ready-to-use Robot Studio
component with one never-throw method per operation — a single self-contained
`.cs` file you paste into a Script component (or compile with the emitted
`.csproj`):

```powershell
./scripts/Generate-RestComponent.ps1 -InputPath petstore.json -ApiName pet-store
```

Generated methods take designer-friendly primitive parameters, return raw
JSON the automation parses with Robot Studio's built-in JSON methods, and
need no NuGet packages; pass `-Component` to the wrapper (or `--component` to
the CLI) to also emit the class as deriving from `System.ComponentModel.Component`
with a per-instance HttpClient and Dispose pattern, for the DLL-fallback
component tray. See [tools/README.md](tools/README.md).

## Documentation

Each component has its own README with the full method reference, plus a
`Documentation/` folder with real-world usage examples organized by category:

Public methods documented as never throwing follow the repository-wide
[Never-Throws Standard](project-docs/coding-standards/never-throws-standard.md). The current rollout and
platform-verification status is recorded in
[Never-Throws Implementation Record](project-docs/coding-standards/never-throws-implementation.md). Overload
signatures must also satisfy the
[Signature Uniqueness Standard](project-docs/coding-standards/signature-uniqueness-standard.md), so every
public method stays selectable on the Pega Robot Studio designer surface.

- [mouseutils/README.md](src/mouseutils/README.md) and [mouseutils/Documentation/](src/mouseutils/Documentation/README.md)
- [screencaptureutils/README.md](src/screencaptureutils/README.md)
- [keyboardutils/README.md](src/keyboardutils/README.md) and [keyboardutils/Documentation/](src/keyboardutils/Documentation/README.md)
- [windowutils/README.md](src/windowutils/README.md) and [windowutils/Documentation/](src/windowutils/Documentation/README.md)
- [ocrutils/README.md](src/ocrutils/README.md) and [ocrutils/Documentation/](src/ocrutils/Documentation/README.md)
- [dialogutils/README.md](src/dialogutils/README.md) and [dialogutils/Documentation/](src/dialogutils/Documentation/README.md)
- [uiautomationutils/README.md](src/uiautomationutils/README.md) and [uiautomationutils/Documentation/](src/uiautomationutils/Documentation/README.md)
- [commandlineutils/README.md](src/commandlineutils/README.md) and [commandlineutils/Documentation/](src/commandlineutils/Documentation/README.md)
- [serviceutils/README.md](src/serviceutils/README.md) and [serviceutils/Documentation/](src/serviceutils/Documentation/README.md)
- [eventutils/README.md](src/eventutils/README.md)
- [eventlogutils/README.md](src/eventlogutils/README.md) and [eventlogutils/Documentation/](src/eventlogutils/Documentation/README.md)
- [sessionutils/README.md](src/sessionutils/README.md) and [sessionutils/Documentation/](src/sessionutils/Documentation/README.md)
- [terminalutils/README.md](src/terminalutils/README.md) and [terminalutils/Documentation/](src/terminalutils/Documentation/README.md)
- [filewatchutils/README.md](src/filewatchutils/README.md) and [filewatchutils/Documentation/](src/filewatchutils/Documentation/README.md)
- [archiveutils/README.md](src/archiveutils/README.md) and [archiveutils/Documentation/](src/archiveutils/Documentation/README.md)
- [localqueueutils/README.md](src/localqueueutils/README.md) and [localqueueutils/Documentation/](src/localqueueutils/Documentation/README.md)
- [stackutils/README.md](src/stackutils/README.md) and [stackutils/Documentation/](src/stackutils/Documentation/README.md)
- [databagutils/README.md](src/databagutils/README.md) and [databagutils/Documentation/](src/databagutils/Documentation/README.md)

## Developer tools

This repository also includes three standalone tools for developing, inspecting,
and testing the components. They are kept outside `src/` and
`AwesomeRpaUtils.sln` because they are not shipped as Robot Studio components.

### Component Browser

[Component Browser](component-browser/README.md) is a WPF desktop application
for inspecting a built release archive. Point it at an
`AwesomeRpaUtils-{tfm}-vX.Y.Z.zip` file to browse the components it contains,
their Properties/Methods/Events (PMEs), reflected signatures, categories,
descriptions, notes and caveats, and links to matching worked examples. It
reads metadata without executing component assemblies and combines the DLL
metadata with the documentation archive bundled in the release.

Build it on any host with the .NET 10 SDK; run it on Windows:

```bash
dotnet build component-browser/ComponentBrowser.csproj
dotnet run --project component-browser/ComponentBrowser.csproj
```

Its platform-independent core and tests can be run on any OS:

```bash
dotnet test component-browser/ComponentBrowser.Tests/ComponentBrowser.Tests.csproj
```

### REST Code Generator

[RestCodeGenerator](tools/README.md) is the design-time CLI described in
[REST component code generation](#rest-component-code-generation). It reads
OpenAPI/Swagger files, Postman collections, Bruno collections, or a curl
command, and emits Robot Studio-ready source for each API operation. The
detailed section above covers the repository wrapper, direct CLI invocation,
release-archive usage, and generated component behavior.

### Test Harness

[Test Harness](test-harness/README.md) is a small WinForms application used by
the manual Pega Robot Studio tests described in [TESTING.md](TESTING.md). It
provides stable, known controls for testing `DialogUtils`, `KeyboardUtils`,
`MouseUtils`, `WindowUtils`, and `UIAutomationUtils`, including a click target,
text box, checkbox, multi-select list, tree view, message box, and child window.
This avoids depending on Windows applications whose controls can change across
OS versions.

Build it on any host with the .NET 10 SDK; run it on Windows:

```bash
dotnet build test-harness/TestHarness.csproj
dotnet run --project test-harness/TestHarness.csproj
```

## Testing

See [TESTING.md](TESTING.md) for a step-by-step plan to test every component
using Pega Robot Studio's Unit Testing framework. `DialogUtils`,
`CommandLineUtils`, `KeyboardUtils`, `EventUtils`, `ServiceUtils`,
`EventLogUtils`, `SessionUtils`, `FileWatchUtils`, `ArchiveUtils`,
`TerminalUtils`, `LocalQueueUtils`, `StackUtils`, and `DataBagUtils` additionally have plain xunit projects —
`dotnet test src/dialogutils/DialogUtils.Tests/DialogUtils.Tests.csproj`,
`dotnet test src/commandlineutils/CommandLineUtils.Tests/CommandLineUtils.Tests.csproj`,
`dotnet test src/keyboardutils/KeyboardUtils.Tests/KeyboardUtils.Tests.csproj`,
`dotnet test src/eventutils/EventUtils.Tests/EventUtils.Tests.csproj`,
`dotnet test src/serviceutils/ServiceUtils.Tests/ServiceUtils.Tests.csproj`,
`dotnet test src/eventlogutils/EventLogUtils.Tests/EventLogUtils.Tests.csproj`,
`dotnet test src/sessionutils/SessionUtils.Tests/SessionUtils.Tests.csproj`,
`dotnet test src/filewatchutils/FileWatchUtils.Tests/FileWatchUtils.Tests.csproj`,
`dotnet test src/archiveutils/ArchiveUtils.Tests/ArchiveUtils.Tests.csproj`,
and
`dotnet test src/terminalutils/TerminalUtils.Tests/TerminalUtils.Tests.csproj`,
and
`dotnet test src/localqueueutils/LocalQueueUtils.Tests/LocalQueueUtils.Tests.csproj`,
and
`dotnet test src/stackutils/StackUtils.Tests/StackUtils.Tests.csproj`,
and
`dotnet test src/databagutils/DataBagUtils.Tests/DataBagUtils.Tests.csproj` —
covering their pure logic (mnemonic stripping, the `DialogButton` Win32 IDs,
the shell-command allowlist tokenizer, the `VirtualKey`/`ModifierKeys` values,
key-down/release batch ordering, the event filter/JSON parsing, category
map, debounce, queue-overflow, and waiter logic, service enum-to-Win32
mapping, the Event Log filter-parsing/XPath-building logic, the session
connect-state/kind enum-mapping logic, and TerminalUtils' ANSI-reconstruction
and heuristic field-splitting logic), argument validation, and the
never-throw contract (Windows-only interop/process cases self-skip on
non-Windows machines). `FileWatchUtils` and `ArchiveUtils` are the exception
to that last point: since every one of their operations is plain
cross-platform BCL (file I/O, `System.IO.Compression`) with zero P/Invoke,
their test projects exercise real functional behavior on any OS — real
temp-directory files, real exclusive-lock detection, real
`FileSystemWatcher` events, real concurrent `ClaimFile` races, real hashing,
a real reproduced zip-slip attempt, and a real corrupted-CRC entry — rather
than guard clauses with Windows-only paths self-skipping. `TerminalUtils` is
the opposite end of that spectrum: it is fundamentally Win32 console-API/
P/Invoke-heavy (`AttachConsole`, `ReadConsoleOutputW`, `WriteConsoleInputW`),
so beyond its pure ANSI-reconstruction/field-splitting logic and argument
guards, its actual screen-buffer/attach/write behavior needs a real Windows
console and is covered only by the manual test plan in
[TESTING.md](TESTING.md), not by this xunit project.

## License

MIT — see [LICENSE](LICENSE).
