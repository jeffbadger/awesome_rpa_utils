# Awesome RPA Utils

A collection of Pega Robot Studio-ready .NET components for Windows desktop
automation. Each component is a self-contained `.csproj` that drops onto a
Robot Studio design surface, with its own README and per-method usage docs.

| Component | Assembly | Description |
|---|---|---|
| [mouseutils](mouseutils/README.md) | `MouseAutomation` | Moves, clicks, drags, and scrolls the mouse via `SendInput`/`SetCursorPos`; controls cursor appearance, visibility, and confinement. |
| [screencaptureutils](screencaptureutils/README.md) | `ScreenCaptureAutomation` | Captures the screen, a region, or a window to a file/clipboard; compares captures against a baseline; annotates or redacts saved screenshots. |

The two components are designed to be used together: MouseUtils owns
cursor/input concerns, ScreenCaptureUtils owns pixels-to-image concerns.

## Requirements

- Windows (both projects target `net10.0-windows`)
- .NET 10 SDK
- Visual Studio 2022 (17.x) or later, or Pega Robot Studio, to consume the
  built components

## Building

```bash
dotnet build AwesomeRpaUtils.sln
```

All projects share build settings via [Directory.Build.props](Directory.Build.props),
which combines every project's output into a single root-level `bin/` folder
(intermediate `obj/` output stays per-project to avoid concurrent-build
collisions).

## Documentation

Each component has its own README with the full method reference, plus a
`Documentation/` folder with real-world usage examples organized by category:

- [mouseutils/README.md](mouseutils/README.md) and [mouseutils/Documentation/](mouseutils/Documentation/README.md)
- [screencaptureutils/README.md](screencaptureutils/README.md)
