# Awesome RPA Utils

A collection of Pega Robot Studio-ready .NET components for Windows desktop
automation. Each component is a self-contained `.csproj` that drops onto a
Robot Studio design surface, with its own README and per-method usage docs.

| Component | Assembly | Description |
|---|---|---|
| [mouseutils](mouseutils/README.md) | `MouseAutomation` | Moves, clicks, drags, and scrolls the mouse via `SendInput`/`SetCursorPos`; controls cursor appearance, visibility, and confinement. |
| [screencaptureutils](screencaptureutils/README.md) | `ScreenCaptureAutomation` | Captures the screen, a region, or a window to a file/clipboard; compares captures against a baseline; annotates or redacts saved screenshots. |
| [keyboardutils](keyboardutils/README.md) | `KeyboardAutomation` | Injects keyboard input via `SendInput`: key presses, combos, typed text, and a clipboard-paste fallback; queries key/modifier state. |
| [windowutils](windowutils/README.md) | `WindowAutomation` | Enumerates, locates, moves/resizes, activates, and closes windows via the Win32 window APIs. |
| [ocrutils](ocrutils/README.md) | `OcrAutomation` | Recognizes text from the screen or an image file via `Windows.Media.Ocr`, with plain-text and positioned-result options. |
| [dialogutils](dialogutils/README.md) | `DialogAutomation` | Finds and dismisses native dialogs by button text/control ID via `BM_CLICK`, without moving the cursor. |

> **Status:** `ocrutils` and `dialogutils` are designed and planned (see
> `docs/superpowers/specs/` and `docs/superpowers/plans/`) but not yet
> implemented — their linked READMEs don't exist until their implementation
> plans are executed. `keyboardutils` and `windowutils` are fully implemented.

Each component is fully standalone (no project references between them), but
they're designed to complement each other: MouseUtils and KeyboardUtils own
input (cursor/click and keyboard, respectively), ScreenCaptureUtils and
OcrUtils own pixels-to-information (images and recognized text), and
WindowUtils and DialogUtils own window management (general windows and
native dialogs, respectively).

## Requirements

- Windows (most projects target `net10.0-windows`; `ocrutils` targets the
  versioned `net10.0-windows10.0.19041.0` to consume WinRT's `Windows.Media.Ocr`)
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
- [keyboardutils/README.md](keyboardutils/README.md) and [keyboardutils/Documentation/](keyboardutils/Documentation/README.md)
- [windowutils/README.md](windowutils/README.md) and [windowutils/Documentation/](windowutils/Documentation/README.md)
- ocrutils/README.md, dialogutils/README.md (planned — see the Status note above)
