# AwesomeRpaUtils.Activities (UiPath)

UiPath activities wrapping the [Awesome RPA Utils](https://github.com/jeffbadger/awesome_rpa_utils) component suite for **UiPath Studio modern (Windows) projects**. The package targets `net6.0-windows` — the flavor Studio's modern Windows projects load — and its nuspec depends on the wrapped component packages, so installing it pulls them from the same feed.

## Install

1. In Studio: **Manage Packages → Settings → Custom** and add the feed that hosts the packages (for the local build: the `artifacts/nuget/` folder).
2. Search for **AwesomeRpaUtils.Activities** and install. The component packages (`AwesomeRpaUtils.WindowAutomation`, `...MouseAutomation`, etc.) install as dependencies.

## Activities

Every activity lives under the `Awesome RPA Utils` category, with subcategories `Window`, `Mouse`, `Keyboard`, `Dialog`, and `Json`.

### Window (`Awesome RPA Utils > Window`)

| Activity | Notes |
|---|---|
| Find Window | Regex title/class-name search → `WindowHandle` (IntPtr) |
| Wait For Window | Polls until a matching window appears or `TimeoutMs` elapses |
| Get Window Title | Returns the title text |
| Activate Window | Brings the window to the foreground |
| Close Window | Closes the window |

### Mouse (`Awesome RPA Utils > Mouse`)

| Activity | Notes |
|---|---|
| Get Mouse Position | Outputs current `X`/`Y` |
| Move Mouse | Moves to `X`/`Y` |
| Left Click At / Right Click At | Clicks at `X`/`Y` |
| Scroll Mouse | Positive `Notches` scroll up, negative down |

### Keyboard (`Awesome RPA Utils > Keyboard`)

| Activity | Notes |
|---|---|
| Type Text | Types into the focused control, character by character |
| Paste Text | Clipboard paste (Ctrl+V) — faster for long strings |
| Press Key | `VirtualKey` enum (Enter, Escape, …) |

### Dialog (`Awesome RPA Utils > Dialog`)

| Activity | Notes |
|---|---|
| Wait For Dialog | Title-substring match (exact when `ExactMatch`) → `WindowHandle`; throws on timeout |
| Click Dialog Button | Finds a button by text, waits until enabled, clicks; output = button was enabled |
| Get Dialog Text | Reads the dialog title text |

### Json (`Awesome RPA Utils > Json`)

| Activity | Notes |
|---|---|
| Get Json Value | Reads a string value by path |
| Set Json Value | Sets a value by path → updated document |
| Is Json Valid | Returns whether the text parses |

## Conventions

- **Errors**: component methods return `(bool succeeded, out string message)`. When a call fails, the activity throws an exception whose message is the component's message — wire it into Studio's standard error handling. `Is Json Valid` is the exception: it reports validity as a boolean instead of throwing.
- **Window handles** flow between activities as `IntPtr` (`WindowHandle` arguments), so `Find Window` / `Wait For Dialog` feed directly into `Click Dialog Button`, `Get Dialog Text`, and the window activities.
- **Windows only**: the wrapped components call the Win32 API; run these activities in Studio's modern Windows projects on a Windows machine.

## Manual QA checklist (requires Windows + Studio)

1. Add `artifacts/nuget/` as a local feed and install the package (no restore errors).
2. In a modern Windows project, search "Awesome" — all 17 activities appear.
3. Open Notepad; run `Find Window` with pattern `Notepad`, then `Get Window Title` using the returned handle.
4. Run `Type Text` and `Press Key` (Enter) against the focused Notepad window.
5. Run `Wait For Dialog` + `Click Dialog Button` against an app that shows a dialog (e.g. Notepad's Exit confirmation).
6. Run the three Json activities on a sample document.