# Awesome RPA Utils for Power Automate for desktop

Power Automate for desktop (PAD) consumes the suite's **net48** assemblies
directly — no wrapper package is required. Two paths, from quickest to most
integrated:

1. **Run .NET script** — call the components from PAD's built-in .NET
   scripting action. No new projects, no signing. Good for occasional use and
   for evaluating the components.
2. **Native custom actions** (Actions SDK) — wrap the components as
   first-class actions that appear in PAD's actions pane, with typed input/
   output arguments and search. More setup (code-signing, tenant upload), but
   reusable across every flow in the tenant.

Both paths run on a Windows machine only — the components call the Win32 API
(window, mouse, keyboard, dialog) or Windows-only services.

## Getting the net48 assemblies

Either source works:

- **NuGet packages** (recommended): add the feed as a
  [local folder feed](../README.md#consuming-the-nuget-packages), restore
  e.g. `AwesomeRpaUtils.WindowAutomation`, and collect the DLLs from
  `lib/net48` of each extracted package.
- **Release zip**: each component's release archive contains the net48
  assembly in its `net48` folder (see the README's Packaging-a-release
  section).

**Dependency DLLs are needed too** — a component's NuGet dependencies are
not part of the component's own zip. Per component (net48):

| Component | Extra DLLs required beside it |
|---|---|
| WindowAutomation | System.Text.Json 8.0.5 (+ its dependencies: System.Memory, System.Buffers, System.Runtime.CompilerServices.Unsafe, System.Text.Encodings.Web, System.Threading.Tasks.Extensions, System.ValueTuple, Microsoft.Bcl.AsyncInterfaces, System.Numerics.Vectors) |
| JsonAutomation | Newtonsoft.Json 13.0.3 |
| KeyboardAutomation, MouseAutomation | none |

The simplest way to get a complete folder: restore the packages with NuGet
and copy the whole `lib/net48` output *plus* the packages' `lib/net46*`-era
dependency DLLs from the global packages folder (`%USERPROFILE%\.nuget\packages\…`)
into one directory — everything the flow needs in a single folder.

## Path 1 — Run .NET script (no setup beyond DLLs)

PAD's **Run .NET script** action loads assemblies from a folder:

1. Put the net48 DLLs (component + dependencies, per the table above) in one
   folder, e.g. `C:\RPA\AwesomeRpaUtils\net48`.
2. In the action: set **References to be loaded** to that folder and
   **.NET script imports** to the component namespace(s)
   (`WindowAutomation`, `MouseAutomation`, `KeyboardAutomation`,
   `JsonAutomation`, …).
3. Declare typed inputs/outputs in the **Script Parameters** window
   (direction **In** / **Out**). An **Out** parameter must be assigned inside
   the script, or the action errors.

The script body runs as **C# 5.0** — no string interpolation, no
`?.`, no inline `out var` declarations. The examples below follow that.

### Example: validate and read JSON (JsonAutomation)

Script parameters: `JsonText` (In, string), `Value` (Out, string).
Imports: `JsonAutomation`.

```csharp
var utils = new JsonUtils();
string message;
string value;
if (!utils.TryGetStringValue(JsonText, "customer.name", out value, out message))
{
    throw new Exception(message);
}
Value = value;
```

### Example: wait for and use a window (WindowAutomation)

Script parameters: `Title` (In, string), `WindowHandle` (Out, int —
`IntPtr` marshals as its integer value), `WindowTitle` (Out, string).
Imports: `WindowAutomation`.

```csharp
var utils = new WindowUtils();
IntPtr hWnd;
string message;
if (!utils.WaitForWindow(Title, 10000, 250, out hWnd, out message))
{
    throw new Exception(message ?? "window did not appear within 10 s");
}
string title;
if (!utils.TryGetWindowTitle(hWnd, out title, out message))
{
    throw new Exception(message);
}
WindowHandle = (int)hWnd.ToInt64();
WindowTitle = title;
```

### Example: type into the focused control (KeyboardAutomation)

Script parameters: `Text` (In, string). Imports: `KeyboardAutomation`.

```csharp
var utils = new KeyboardUtils();
string message;
if (!utils.TypeText(Text, out message))
{
    throw new Exception(message);
}
```

The same pattern covers `MouseAutomation` (`GetPosition`, `MoveTo`,
`LeftClickAt`, `RightClickAt`, `ScrollUp`/`ScrollDown`) and every other
component: instantiate, call the `(bool, out string message)` overload,
throw the message on failure.

## Path 2 — native custom actions (Actions SDK)

For actions that appear in PAD's designer with typed arguments, wrap the
components with the official SDK
([Create custom actions](https://learn.microsoft.com/en-us/power-automate/desktop-flows/create-custom-actions),
[build/deploy walkthrough](https://learn.microsoft.com/en-us/power-automate/guidance/how-to/build-custom-action/buildcustomaction)).

1. **Project**: Visual Studio 2022, *Class Library (.NET Framework)*,
   target **.NET Framework 4.7.2** (or 4.8), assembly name
   `Modules.AwesomeRpaUtils` (custom-action modules must be named
   `Modules.<name>.dll`).
2. **NuGet**: `Microsoft.PowerPlatform.PowerAutomate.Desktop.Actions.SDK`
   (namespace renamed from the older `Microsoft.PowerAutomate.Desktop…`).
3. **Component references**: NuGet `AwesomeRpaUtils.*` packages (net48) or
   direct DLL references to the net48 builds.
4. **Wrapper classes**: inherit `ActionBase`, decorate with `[Action]`,
   expose `[InputArgument]`/`[OutputArgument]` properties, and call the
   component in `Execute(ActionContext)`:

```csharp
using Microsoft.PowerPlatform.PowerAutomate.Desktop.Actions.SDK;
using Microsoft.PowerPlatform.PowerAutomate.Desktop.Actions.SDK.Attributes;
using System;
using System.ComponentModel;
using JsonAutomation;

namespace Modules.AwesomeRpaUtils
{
    [Action(Id = "GetJsonValue")]
    public class GetJsonValue : ActionBase
    {
        [InputArgument]
        public string Json { get; set; }

        [InputArgument]
        public string Path { get; set; }

        [OutputArgument]
        public string Value { get; set; }

        public override void Execute(ActionContext context)
        {
            var utils = new JsonUtils();
            string message;
            if (!utils.TryGetStringValue(Json, Path, out Value, out message))
            {
                throw new ActionException("GetJsonError", message, null);
            }
        }
    }

    [Action(Id = "WaitForWindow")]
    public class WaitForWindow : ActionBase
    {
        [InputArgument]
        public string Title { get; set; }

        [InputArgument, DefaultValue(10000)]
        public int TimeoutMs { get; set; }

        [OutputArgument]
        public int WindowHandle { get; set; }

        public override void Execute(ActionContext context)
        {
            var utils = new WindowUtils();
            IntPtr hWnd;
            string message;
            if (!utils.WaitForWindow(Title, TimeoutMs, 250, out hWnd, out message))
            {
                throw new ActionException("WindowTimeout",
                    message ?? "window did not appear within the timeout", null);
            }
            WindowHandle = (int)hWnd.ToInt64();
        }
    }
}
```

5. **Sign every DLL** — the wrapper *and* all dependency DLLs (Newtonsoft.Json,
   System.Text.Json, …) must be signed with a certificate PAD trusts
   (self-signed for testing, CA-issued for production). Untrusted
   dependencies are a common deployment failure.
6. **Package and upload**: bundle into a `.cab`, upload through the custom
   actions section of make.powerautomate.com (requires the Desktop Flow
   Module Developer role), then add from the designer's Assets Library.
   The exact certificate/signing/cab commands are in the Microsoft
   walkthrough above; they are environment-specific (certificate thumbprint,
   cab tool) so they are not duplicated here.

## Notes

- **Bitness**: PAD runs 64-bit; the components are AnyCPU, so both work —
  but all DLLs in a given flow's scripts/custom action must share one
  architecture for native interop consistency (the managed-only net48
  assemblies are AnyCPU, so this is automatic).
- **Elevated rights**: flows that touch protected resources (UIA of
  elevated apps, event log, services) need PAD running as administrator.
- **Never-throw convention**: component methods return `(bool succeeded,
  out string message)` and never throw themselves. The examples above turn
  `false` into an exception so PAD's on-error handling sees a real error;
  wrap differently if you prefer branching on success instead.