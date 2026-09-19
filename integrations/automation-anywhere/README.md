# Awesome RPA Utils for Automation 360

Automation 360 (A360) bots cannot load .NET assemblies as first-class
actions: **custom packages are Java-based** (Package SDK → JDK 11 + Gradle →
a JAR of `@BotCommand`-annotated actions that the Bot Agent loads). There is
no supported C# custom-package manifest, so a native package wrapping this
suite would have to be a Java shell that shells out again — two hops for
little benefit.

The practical path is the same one many integrations use: a bot runs a
short **PowerShell bridge** with the PowerShell package's *Run script*
action (or *Run DOS command* invoking `powershell.exe`), and the script
loads the suite's **net48** assemblies with `[Reflection.Assembly]::LoadFrom`
and calls the components directly. No wrapper project, no signing, no
package upload.

Requires: Windows bot agent with .NET Framework 4.8 (the net48 assemblies).

## Getting the net48 assemblies

- **NuGet packages** (recommended): add the feed as a
  [local folder feed](../../README.md#consuming-the-nuget-packages), restore
  the components you need, and collect the DLLs from `lib/net48` of each
  extracted package.
- **Release zip**: each component's release archive contains the net48
  assembly in its `net48` folder.

**Dependency DLLs are needed too**:

| Component | Extra DLLs required beside it |
|---|---|
| WindowAutomation | System.Text.Json 8.0.5 (+ System.Memory, System.Buffers, System.Runtime.CompilerServices.Unsafe, System.Text.Encodings.Web, System.Threading.Tasks.Extensions, Microsoft.Bcl.AsyncInterfaces) |
| JsonAutomation | Newtonsoft.Json 13.0.3 |
| KeyboardAutomation, MouseAutomation | none |

Stage the component + dependency DLLs in one folder on each bot agent (an
A360 file action or deployment tooling can push them; the folder must be
readable by the Bot Agent's user).

## PowerShell bridge

The pattern: `Add-Type` every staged DLL (component + its dependency DLLs
from the table), instantiate the component, call the method, and print a
JSON envelope on stdout so the bot gets one parseable result. The shared
loader goes at the top of every bridge script:

```powershell
# Load every DLL staged in the folder - components and dependencies alike.
# Loading all of them up front avoids FileNotFound exceptions when a call
# touches a dependency (e.g. a JSON-serializing window method).
$ErrorActionPreference = 'Stop'
Get-ChildItem 'C:\RPA\AwesomeRpaUtils\net48\*.dll' |
    ForEach-Object { Add-Type -Path $_.FullName }
```

(The explicit per-DLL `Add-Type` in the examples below works too, but only
because those calls never touch System.Text.Json — load everything instead.
Note System.Text.Json 8.0.5 on PowerShell 5.1 can need binding redirects if
you ever see `FileLoadException` on System.Memory or its siblings.)

Example — read a JSON value (inputs wired to bot variables):

```powershell
$utils = New-Object JsonAutomation.JsonUtils
# Pre-typed to match the method's out parameters - [ref] needs the right type
$value = ''
$message = ''
$ok = $utils.TryGetStringValue($env:RPA_JSON, $env:RPA_PATH, [ref]$value, [ref]$message)

if (-not $ok) {
    # Non-zero exit code + message on stderr → the bot's error branch
    [Console]::Error.WriteLine($message)
    exit 1
}
ConvertTo-Json @{ ok = $true; value = $value } -Compress
```

Pass bot variables into the script through environment variables
(`$env:` names) or the PowerShell action's parameters — both avoid
quoting problems with inline script text. The action's output variable
(or the script's stdout when using Run DOS command + `powershell.exe`)
is what the bot captures; `ConvertTo-Json` gives it one string to parse.

Example — wait for a window and hand the handle back to the bot:

```powershell
$utils = New-Object WindowAutomation.WindowUtils
$hWnd = [IntPtr]::Zero
$message = ''
$ok = $utils.WaitForWindow($env:RPA_TITLE, [int]$env:RPA_TIMEOUT_MS, 250, [ref]$hWnd, [ref]$message)

if (-not $ok) {
    if ($message) { [Console]::Error.WriteLine($message) }
    else { [Console]::Error.WriteLine('window did not appear within the timeout') }
    exit 1
}
ConvertTo-Json @{ ok = $true; windowHandle = $hWnd.ToInt64() } -Compress
```

The bot then uses `windowHandle` (a number) in later calls, e.g. passing it
back as `$env:RPA_HANDLE` to a script that pre-types its out variables
(`$title = ''`; `$message = ''`) and calls
`TryGetWindowTitle([IntPtr]$env:RPA_HANDLE, [ref]$title, [ref]$message)`.

## Advanced: a real Java custom package around the bridge

If you want these calls to appear as actions in the Control Room Action
Panel, the supported route is the Java Package SDK
([creating custom packages](https://community.automationanywhere.com/pathfinder-blog-85009/creating-custom-packages-85010),
[tutorial](https://community.automationanywhere.com/pathfinder-blog-85009/tutorial-building-an-automation-360-package-85087)):
JDK 11 + Gradle, actions annotated `@BotCommand`/`@CommandPkg`, built to a
JAR with `gradlew clean build shadowJar`, uploaded to the Control Room. A
Java action would wrap the same PowerShell bridge via `ProcessBuilder`
(arguments + environment), reading stdout/stderr exactly like the bot's
error/success branches above. That is an integration project of its own —
start with the plain PowerShell bridge and graduate only if dozens of bots
need the same calls.

## Notes

- **Never-throw convention**: the components return `(bool succeeded,
  out string message)` and never throw. The bridge scripts translate a
  `false` return into a non-zero exit code + stderr message, which is what
  A360's error handling acts on; adjust to your process convention.
- **Bitness**: A360 Bot Agents run 64-bit; the assemblies are AnyCPU, so no
  bitness handling is needed.
- **Windows only**: the bridge is PowerShell + .NET Framework 4.8, so it runs
  on Windows Bot Agents with a desktop session for the UI components (the
  non-UI components like JSON work headlessly too). Linux Bot Agents are out
  of scope for this bridge; they would need a different one (the assemblies
  are net48).
- PowerShell 5.1 ships with .NET Framework 4.8 and loads net48 assemblies
  directly; no newer PowerShell is required.