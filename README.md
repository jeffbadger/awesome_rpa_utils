# Awesome RPA Utils

A collection of Pega Robot Studio-ready .NET components for Windows desktop
automation. Each component is a self-contained `.csproj` that drops onto a
Robot Studio design surface, with its own README and per-method usage docs.

| Component | Assembly | Description |
|---|---|---|
| [mouseutils](src/mouseutils/README.md) | `MouseAutomation` | Moves, clicks, drags, and scrolls the mouse via `SendInput`/`SetCursorPos`; controls cursor appearance, visibility, and confinement. |
| [screencaptureutils](src/screencaptureutils/README.md) | `ScreenCaptureAutomation` | Captures the screen, a region, or a window to a file/clipboard; compares captures against a baseline; annotates or redacts saved screenshots. |
| [keyboardutils](src/keyboardutils/README.md) | `KeyboardAutomation` | Injects keyboard input via `SendInput`: key presses, combos, typed text, and a clipboard-paste fallback; queries key/modifier state. |
| [windowutils](src/windowutils/README.md) | `WindowAutomation` | Enumerates, locates, moves/resizes, activates, and closes windows via the Win32 window APIs. |
| [ocrutils](src/ocrutils/README.md) | `OcrAutomation` | Recognizes text from the screen or an image file via `Windows.Media.Ocr`, with plain-text and positioned-result options. |
| [dialogutils](src/dialogutils/README.md) | `DialogAutomation` | Finds and dismisses native dialogs by button text/control ID via `BM_CLICK`, without moving the cursor. |
| [commandlineutils](src/commandlineutils/README.md) | `CommandLineAutomation` | Runs external commands/processes and captures their exit code, stdout, and stderr, including elevated and fire-and-forget launches. |

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
dotnet build src/AwesomeRpaUtils.sln
```

All projects share build settings via [Directory.Build.props](src/Directory.Build.props),
which combines every project's output into a single `src/bin/` folder
(intermediate `obj/` output stays per-project to avoid concurrent-build
collisions).

Prebuilt DLLs for each tagged version are available on the
[Releases](../../releases) page.

## Documentation

Each component has its own README with the full method reference, plus a
`Documentation/` folder with real-world usage examples organized by category:

- [mouseutils/README.md](src/mouseutils/README.md) and [mouseutils/Documentation/](src/mouseutils/Documentation/README.md)
- [screencaptureutils/README.md](src/screencaptureutils/README.md)
- [keyboardutils/README.md](src/keyboardutils/README.md) and [keyboardutils/Documentation/](src/keyboardutils/Documentation/README.md)
- [windowutils/README.md](src/windowutils/README.md) and [windowutils/Documentation/](src/windowutils/Documentation/README.md)
- [ocrutils/README.md](src/ocrutils/README.md) and [ocrutils/Documentation/](src/ocrutils/Documentation/README.md)
- [dialogutils/README.md](src/dialogutils/README.md) and [dialogutils/Documentation/](src/dialogutils/Documentation/README.md)
- [commandlineutils/README.md](src/commandlineutils/README.md) and [commandlineutils/Documentation/](src/commandlineutils/Documentation/README.md)

## Testing

See [TESTING.md](TESTING.md) for a step-by-step plan to test every component
using Pega Robot Studio's Unit Testing framework.

## License

MIT — see [LICENSE](LICENSE).
