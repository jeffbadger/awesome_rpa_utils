# Awesome RPA Utils for Blue Prism

Blue Prism consumes the suite's **net48** assemblies from code stages —
Blue Prism compiles code stages with the .NET Framework, so the net48 builds
are the ones to reference (Blue Prism 7.x requires .NET Framework 4.8 on the
machine; the assemblies target net48 and load fine there).

There is no wrapper package to install: a business object (VBO) references
the DLLs directly in its **Code Options**, and each component method is
exposed as a code stage. This guide shows the one-time setup and the
wrapping pattern for a starter set of methods. Everything runs on Windows
only — the components call the Win32 API.

## Getting the net48 assemblies

- **NuGet packages** (recommended): add the feed as a
  [local folder feed](../../README.md#consuming-the-nuget-packages), restore
  the components you need (e.g. `AwesomeRpaUtils.WindowAutomation`), and
  collect the DLLs from `lib/net48` of each extracted package.
- **Release zip**: each component's release archive contains the net48
  assembly in its `net48` folder.

**Dependency DLLs are needed too** (the component's NuGet dependencies are
not in its own zip):

| Component | Extra DLLs required beside it |
|---|---|
| WindowAutomation | System.Text.Json 8.0.5 (+ its dependencies: System.Memory, System.Buffers, System.Runtime.CompilerServices.Unsafe, System.Text.Encodings.Web, System.Threading.Tasks.Extensions, Microsoft.Bcl.AsyncInterfaces — the ValueTuple and Numerics.Vectors types already live in .NET 4.8's mscorlib) |
| JsonAutomation | Newtonsoft.Json 13.0.3 |
| KeyboardAutomation, MouseAutomation | none |

## Deployment

The DLLs must be present on **every machine that runs the object** (dev
studio and each runtime resource). Two placement options:

1. **Beside `Automate.exe`** (Blue Prism's install folder) with no path in
   the reference — the community-recommended default; full-path references
   compile but some assemblies fail to *load* at runtime from other folders.
2. **A dedicated folder** referenced by full path — works for most managed
   assemblies, but validate each component with a live run on a runtime
   resource before relying on it (the dependency DLLs must load too).

Put the component DLL *and* every dependency DLL from the table above in the
chosen folder.

## One-time object setup

1. Create a business object (e.g. `AwesomeRPA - Window`).
2. On the Initialise page, double-click the object name box → **Code
   Options** tab:
   - **Language**: C# (all code stages in the object must use the same one).
   - **External References**: add a row for the component DLL (and each
     dependency DLL from the table).
   - **Namespace Imports**: add the component's namespace (`WindowAutomation`,
     `JsonAutomation`, `KeyboardAutomation`, `MouseAutomation`, …) — for .NET
     class libraries both the reference *and* the namespace import are
     required.
3. Use **Check Code** to validate before publishing.

## Wrapping pattern

Put a shared instantiation in the object's **Global Code** tab so code stages
stay one-liners:

```csharp
// Global Code — available to every code stage in the object
static readonly WindowAutomation.WindowUtils Window =
    new WindowAutomation.WindowUtils();
```

Then one code stage per component method, with the standard
`(bool succeeded, out string message)` contract mapped onto Blue Prism data
items. Example — **Wait For Window** (inputs: `Title` text, `TimeoutMs`
number, `PollMs` number; outputs: `WindowHandle` number, `ErrorMessage`
text):

```csharp
IntPtr hWnd;
string message;
if (!Window.WaitForWindow(Title, (int)TimeoutMs, (int)PollMs, out hWnd, out message))
{
    ErrorMessage = message ?? "window did not appear within the timeout";
}
WindowHandle = hWnd.ToInt64();
```

Branch on `ErrorMessage = ""` downstream, or surface `ErrorMessage` in the
process the same way as any other failure. Note `WindowUtils.WaitForWindow`
returns `false` with a **null** message on timeout — the `??` fallback is
required.

Example — **JSON value read** (inputs: `Json` text, `Path` text; outputs:
`Value` text, `ErrorMessage` text):

```csharp
static readonly JsonAutomation.JsonUtils Json =
    new JsonAutomation.JsonUtils();

// code stage:
string value;
string message;
if (!Json.TryGetStringValue(Json, Path, out value, out message))
{
    ErrorMessage = message;
}
Value = value;
```

(For the Json object, instantiate `JsonUtils` in *its* Global Code — the
snippet above shows the Global Code line in place of the Window one.)

Other starters, same shape:

- Keyboard: `TypeText(text, out message)`, `PressKey(key, out message)`,
  `PasteText(text, out message)`
- Mouse: `GetPosition(out x, out y, out message)`, `MoveTo(x, y, out message)`,
  `LeftClickAt(x, y, out message)`, `RightClickAt(x, y, out message)`,
  `ScrollUp(n, out message)` / `ScrollDown(n, out message)`
- Window: `TryGetWindowTitle(hWnd, out title, out message)`,
  `ActivateWindow(hWnd, out message)`, `CloseWindow(hWnd, out message)`

## Why no importable VBO file

A `.bprelease`/VBO XML is a binary-ish export format that is fragile to
hand-author, and a broken import is worse than a five-minute manual setup
(the Code Options steps above take about two minutes). Instead of shipping
an importable object, this guide gives the exact Global Code + code-stage
snippets to paste. If demand warrants it, a release-exported VBO can be
added later — exported from a real Blue Prism install, not hand-written.

## Notes

- **Never-throw convention**: the components return `(bool succeeded,
  out string message)` and never throw. The snippets translate `false`
  into an `ErrorMessage` output so processes can branch on it; adjust to
  your process's error convention.
- **Windows handles** flow as `IntPtr` — carried in Blue Prism as a number
  (`hWnd.ToInt64()` into a number data item), and back with
  `new IntPtr(WindowHandle)`.
- **Runtime resources**: install .NET Framework 4.8 and the DLL set on every
  runtime resource that runs objects using these components.
- Blue Prism code stages also support Python — irrelevant here; the net48
  assemblies are .NET only.