# DialogUtils Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `DialogUtils` Pega Robot Studio component (assembly `DialogAutomation`) that finds and dismisses native dialogs (message boxes, common dialogs) by button text/control ID via `BM_CLICK`, without moving the cursor or stealing focus, matching the conventions of the existing components.

**Architecture:** A single `DialogUtils : Component` class in `dialogutils/DialogUtils.cs`, implementing its own minimal internal window/child-window enumeration (same `EnumWindows`/`EnumChildWindows` P/Invoke technique as `windowutils/WindowUtils.cs`) rather than referencing the WindowUtils project — fully standalone, per the repo's no-cross-project-references rule.

**Tech Stack:** .NET 10 (`net10.0-windows`), `System.Runtime.InteropServices` (user32.dll P/Invoke), `System.ComponentModel.Component`.

**Spec:** See `docs/superpowers/specs/2026-08-26-keyboard-window-ocr-dialog-utils-design.md` (DialogUtils section) for the approved design this plan implements.

---

### Task 1: Scaffold the project and add it to the solution

**Files:**
- Create: `dialogutils/DialogUtils.csproj`
- Create: `dialogutils/DialogUtils.cs`
- Modify: `AwesomeRpaUtils.sln` (via `dotnet sln add`)

- [ ] **Step 1: Create the csproj**

Create `dialogutils/DialogUtils.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AssemblyName>DialogAutomation</AssemblyName>
    <RootNamespace>DialogAutomation</RootNamespace>
    <Platforms>AnyCPU;x64</Platforms>
    <!-- Emit DialogAutomation.xml next to the DLL so the XML doc comments are consumable -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Create a minimal compiling stub class**

Create `dialogutils/DialogUtils.cs`:

```csharp
using System.ComponentModel;

namespace DialogAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that finds and dismisses native dialogs
    /// (message boxes, common dialogs) by button text or control ID, via <c>BM_CLICK</c>
    /// — no cursor movement required, and it works even if the dialog is behind other
    /// windows.
    /// </summary>
    [Description("Finds and dismisses native dialogs by button text/control ID. Drag " +
                 "this component onto a Pega Robot Studio automation to use its methods.")]
    public class DialogUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public DialogUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public DialogUtils(IContainer container)
        {
            container?.Add(this);
        }
    }
}
```

- [ ] **Step 3: Add the project to the solution under its own solution folder**

Run: `dotnet sln AwesomeRpaUtils.sln add dialogutils/DialogUtils.csproj --solution-folder dialogutils`
Expected: `Project(s) added to the solution.`

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build AwesomeRpaUtils.sln`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add dialogutils/DialogUtils.csproj dialogutils/DialogUtils.cs AwesomeRpaUtils.sln
git commit -m "Scaffold DialogUtils project"
```

---

### Task 2: Enum and Win32 interop foundation

**Files:**
- Modify: `dialogutils/DialogUtils.cs`

- [ ] **Step 1: Replace the top of the file with the full `using` list and the `DialogButton` enum**

Replace the `using System.ComponentModel;` line and everything up to (not including) the `DialogUtils` class's XML doc comment with:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DialogAutomation
{
    /// <summary>
    /// A standard Windows MessageBox button, identified by its well-known control ID
    /// (<c>IDOK</c>, <c>IDCANCEL</c>, etc.), for use with
    /// <see cref="DialogUtils.ClickDialogButton"/>.
    /// </summary>
    public enum DialogButton
    {
        /// <summary>OK (IDOK, 1).</summary>
        Ok = 1,
        /// <summary>Cancel (IDCANCEL, 2).</summary>
        Cancel = 2,
        /// <summary>Abort (IDABORT, 3).</summary>
        Abort = 3,
        /// <summary>Retry (IDRETRY, 4).</summary>
        Retry = 4,
        /// <summary>Ignore (IDIGNORE, 5).</summary>
        Ignore = 5,
        /// <summary>Yes (IDYES, 6).</summary>
        Yes = 6,
        /// <summary>No (IDNO, 7).</summary>
        No = 7
    }

```

- [ ] **Step 2: Add the Win32 interop foundation as a new region inside the class, after the constructors**

```csharp
        #region Win32 Interop

        private const uint BM_CLICK = 0x00F5;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowNative(IntPtr hWnd);

        private static List<IntPtr> GetTopLevelWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                windows.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static List<IntPtr> GetChildWindows(IntPtr hWndParent)
        {
            var children = new List<IntPtr>();
            EnumChildWindows(hWndParent, (hWnd, lParam) =>
            {
                children.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return children;
        }

        private static string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        #endregion
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build dialogutils/DialogUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add dialogutils/DialogUtils.cs
git commit -m "Add DialogButton enum and Win32 interop layer to DialogUtils"
```

---

### Task 3: Find & Click methods

**Files:**
- Modify: `dialogutils/DialogUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after the constructors and before Win32 Interop**

```csharp
        #region Find & Click

        /// <summary>
        /// Finds a top-level dialog window by its title. Returns <see cref="IntPtr.Zero"/>
        /// if none matches (not found is a normal, checkable outcome, not an error).
        /// </summary>
        /// <param name="titlePattern">The title to match.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact, case-sensitive title match.
        /// If <c>false</c>, matches any window whose title contains
        /// <paramref name="titlePattern"/> (case-insensitive).
        /// </param>
        public IntPtr FindDialog(string titlePattern, bool exactMatch = true)
        {
            foreach (var hWnd in GetTopLevelWindows())
            {
                string title = GetControlText(hWnd);
                bool matches = exactMatch
                    ? string.Equals(title, titlePattern, StringComparison.Ordinal)
                    : title.IndexOf(titlePattern, StringComparison.OrdinalIgnoreCase) >= 0;
                if (matches)
                    return hWnd;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Finds a button on a dialog by its visible text (case-insensitive). Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        public IntPtr FindButtonByText(IntPtr hDialog, string buttonText)
        {
            foreach (var child in GetChildWindows(hDialog))
            {
                if (GetWindowClassName(child) == "Button" &&
                    string.Equals(GetControlText(child), buttonText, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Finds a control on a dialog by its control ID (<c>GetDlgItem</c>). Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        public IntPtr FindButtonById(IntPtr hDialog, int controlId)
        {
            return GetDlgItem(hDialog, controlId);
        }

        /// <summary>
        /// Invokes a button by sending it <c>BM_CLICK</c> — no cursor movement is involved,
        /// and it works even if the dialog is behind other windows.
        /// </summary>
        /// <remarks>
        /// <c>SendMessage</c>'s return value for <c>BM_CLICK</c> carries no useful
        /// success/failure signal, so this method never throws based on it.
        /// </remarks>
        public void ClickButton(IntPtr hButton)
        {
            SendMessage(hButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Invokes a standard dialog button by its well-known control ID.</summary>
        /// <exception cref="InvalidOperationException">The dialog has no control with that ID.</exception>
        public void ClickDialogButton(IntPtr hDialog, DialogButton button)
        {
            IntPtr hButton = GetDlgItem(hDialog, (int)button);
            if (hButton == IntPtr.Zero)
                throw new InvalidOperationException($"Dialog has no control with ID {(int)button} ({button}).");
            ClickButton(hButton);
        }

        /// <summary>Finds a button by its visible text and invokes it.</summary>
        /// <exception cref="InvalidOperationException">The dialog has no button with that text.</exception>
        public void ClickDialogButtonByText(IntPtr hDialog, string buttonText)
        {
            IntPtr hButton = FindButtonByText(hDialog, buttonText);
            if (hButton == IntPtr.Zero)
                throw new InvalidOperationException($"Dialog has no button labeled '{buttonText}'.");
            ClickButton(hButton);
        }

        #endregion
```

- [ ] **Step 2: Build to verify it compiles**

This will fail to compile until Task 4 adds `GetControlText` (used above by `FindDialog`/`FindButtonByText`). That is expected here — add Task 4's methods before building. If you must verify incrementally, temporarily stub `GetControlText` to return `string.Empty`, build, then remove the stub once Task 4 is applied.

- [ ] **Step 3: Commit** (after Task 4 is also applied and the project builds — see Task 4's steps)

---

### Task 4: Read Text methods

**Files:**
- Modify: `dialogutils/DialogUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after Find & Click**

```csharp
        #region Read Text

        /// <summary>
        /// Gets a dialog's message body: the text of its first child control of class
        /// <c>Static</c> (the standard control class for a MessageBox's message text).
        /// Returns an empty string if the dialog has no <c>Static</c> child.
        /// </summary>
        public string GetDialogText(IntPtr hDialog)
        {
            foreach (var child in GetChildWindows(hDialog))
            {
                if (GetWindowClassName(child) == "Static")
                {
                    string text = GetControlText(child);
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
            }
            return string.Empty;
        }

        /// <summary>Gets any control's text via <c>GetWindowText</c> (buttons, static labels, edit fields, and the dialog's own title bar).</summary>
        public string GetControlText(IntPtr hControl)
        {
            int length = GetWindowTextLength(hControl);
            var sb = new StringBuilder(length + 1);
            GetWindowText(hControl, sb, sb.Capacity);
            return sb.ToString();
        }

        #endregion
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build dialogutils/DialogUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual smoke test**

From a throwaway console app, call `System.Windows.Forms.MessageBox.Show("Are you sure?", "Confirm", MessageBoxButtons.YesNo)` on a background thread (or trigger any real Windows message box), then use `dialog.FindDialog("Confirm", exactMatch: false)` to get its handle, `dialog.GetDialogText(hWnd)` to confirm it reads back `"Are you sure?"`, and `dialog.ClickDialogButton(hWnd, DialogButton.Yes)` to dismiss it. Confirm the message box closes and the `MessageBox.Show` call returns `DialogResult.Yes`.

- [ ] **Step 4: Commit** (this also covers Task 3, now that it compiles)

```bash
git add dialogutils/DialogUtils.cs
git commit -m "Implement DialogUtils find/click and read-text methods"
```

---

### Task 5: Wait-for-Dialog Polling methods

**Files:**
- Modify: `dialogutils/DialogUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after Read Text**

```csharp
        #region Wait-for-Dialog Polling

        /// <summary>Polls for a top-level dialog matching <paramref name="titlePattern"/> (substring, case-insensitive) until it appears or the timeout elapses.</summary>
        /// <param name="titlePattern">The title to match.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="hWnd">The matching dialog's handle, or <see cref="IntPtr.Zero"/> if not found in time.</param>
        /// <returns><c>true</c> if a matching dialog was found before the timeout.</returns>
        public bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (true)
            {
                IntPtr found = FindDialog(titlePattern, exactMatch: false);
                if (found != IntPtr.Zero)
                {
                    hWnd = found;
                    return true;
                }
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                {
                    hWnd = IntPtr.Zero;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
            }
        }

        /// <summary>Polls until a dialog handle is no longer valid (the dialog closed), or the timeout elapses.</summary>
        /// <param name="hWnd">The dialog handle to watch.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <returns><c>true</c> if the handle became invalid before the timeout; <c>false</c> if the timeout elapsed first.</returns>
        public bool WaitForDialogToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (IsWindowNative(hWnd))
            {
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    return false;
                Thread.Sleep(pollIntervalMs);
            }
            return true;
        }

        #endregion
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build dialogutils/DialogUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual smoke test**

Trigger a message box after a short delay (e.g. a background `Task.Delay(1000)` then `MessageBox.Show(...)`), call `dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd)` immediately before it appears, and confirm it returns `true` once the dialog shows up. Then click a button on it and confirm `dialog.WaitForDialogToClose(hWnd, 5000, 100)` returns `true` once it closes.

- [ ] **Step 4: Commit**

```bash
git add dialogutils/DialogUtils.cs
git commit -m "Implement DialogUtils wait-for-dialog polling"
```

---

### Task 6: Component README

**Files:**
- Create: `dialogutils/README.md`

- [ ] **Step 1: Write the README**

Create `dialogutils/README.md`:

```markdown
# DialogAutomation

A Pega Robot Studio-ready component (`DialogUtils`) that finds and dismisses
native dialogs (message boxes, common dialogs) by button text or control ID,
via `BM_CLICK` — no cursor movement required, and it works even if the
dialog is behind other windows.

- Target framework: `net10.0-windows`
- Namespace: `DialogAutomation`
- Assembly: `DialogAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

This component implements its own minimal internal window/child-window
enumeration (the same `EnumWindows`/`EnumChildWindows` technique
[WindowUtils](../windowutils/WindowUtils.cs) uses) rather than referencing
the WindowUtils project — fully standalone, like every other component in
this repo.

## Enums

### `DialogButton`
A standard Windows MessageBox button, identified by its well-known control
ID, for use with `ClickDialogButton`: `Ok`, `Cancel`, `Abort`, `Retry`,
`Ignore`, `Yes`, `No`.

## Constructors

| Constructor | Description |
|---|---|
| `DialogUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `DialogUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Find & Click

| Method | Description |
|---|---|
| `IntPtr FindDialog(string titlePattern, bool exactMatch = true)` | Finds a top-level dialog window by its title (exact or substring match). |
| `IntPtr FindButtonByText(IntPtr hDialog, string buttonText)` | Finds a button on a dialog by its visible text. |
| `IntPtr FindButtonById(IntPtr hDialog, int controlId)` | Finds a control on a dialog by its control ID. |
| `void ClickButton(IntPtr hButton)` | Invokes a button by sending it `BM_CLICK`. |
| `void ClickDialogButton(IntPtr hDialog, DialogButton button)` | Invokes a standard dialog button by its well-known control ID. |
| `void ClickDialogButtonByText(IntPtr hDialog, string buttonText)` | Finds a button by its visible text and invokes it. |

### Read Text

| Method | Description |
|---|---|
| `string GetDialogText(IntPtr hDialog)` | Gets a dialog's message body (its first `Static`-class child control's text). |
| `string GetControlText(IntPtr hControl)` | Gets any control's text (buttons, labels, edit fields, title bars). |

### Wait-for-Dialog Polling

| Method | Description |
|---|---|
| `bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)` | Polls for a dialog matching the title until it appears or the timeout elapses. |
| `bool WaitForDialogToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until a dialog handle is no longer valid, or the timeout elapses. |

## Notes & Caveats

- **`ClickButton`/`ClickDialogButton`/`ClickDialogButtonByText`** only work against standard
  Win32 dialogs built from real `Button`/`Static`/`Edit` controls; owner-drawn or non-standard
  dialogs (some modern WPF/Electron/browser-rendered "dialogs" that are really just styled
  windows) may not respond to `BM_CLICK` at all — for those, use
  [KeyboardUtils](../keyboardutils/README.md) or
  [MouseUtils](../mouseutils/MouseUtils.cs)'s window-relative click methods instead.
- **`SendMessage`'s return value for `BM_CLICK`** carries no reliable success/failure signal,
  so `ClickButton` never throws based on it — a click that had no visible effect usually means
  the target wasn't actually a clickable `Button` control, not a Win32-level failure.
- **`FindDialog`/`FindButtonByText`** do a linear scan of top-level windows / child controls
  each call; on a system with many open windows this is a few milliseconds, not a concern for
  interactive automation use.
- **Dialog handles (`IntPtr`) become invalid once the dialog closes.** Re-find the dialog
  (or use `WaitForDialog`) rather than caching a handle across a long-running step.
```

- [ ] **Step 2: Commit**

```bash
git add dialogutils/README.md
git commit -m "Add DialogUtils README"
```

---

### Task 7: Per-category usage documentation

**Files:**
- Create: `dialogutils/Documentation/README.md`
- Create: `dialogutils/Documentation/FindAndClick.md`
- Create: `dialogutils/Documentation/ReadText.md`
- Create: `dialogutils/Documentation/WaitForDialog.md`

- [ ] **Step 1: Write the documentation index**

Create `dialogutils/Documentation/README.md`:

```markdown
# DialogUtils Documentation

Real-world usage examples for every method on the `DialogUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Find & Click](FindAndClick.md) — locating a dialog and invoking its buttons
- [Read Text](ReadText.md) — reading a dialog's message and control text
- [Wait for Dialog](WaitForDialog.md) — polling for a dialog to appear/close

All examples assume a `DialogUtils` instance named `dialog`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var dialog = new DialogUtils();`).
```

- [ ] **Step 2: Write `FindAndClick.md`**

Create `dialogutils/Documentation/FindAndClick.md`:

```markdown
# Find & Click

## Dismiss a Yes/No confirmation

```csharp
if (dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd))
{
    dialog.ClickDialogButton(hWnd, DialogButton.Yes);
}
```

## Click a button by its visible label instead of a standard control ID

```csharp
dialog.ClickDialogButtonByText(hWnd, "Don't Save");
```

## Lower-level: find then click

```csharp
IntPtr okButton = dialog.FindButtonByText(hWnd, "OK");
if (okButton != IntPtr.Zero)
{
    dialog.ClickButton(okButton);
}
```

## Click a button by a known custom control ID

```csharp
IntPtr detailsButton = dialog.FindButtonById(hWnd, 1001);
if (detailsButton != IntPtr.Zero)
{
    dialog.ClickButton(detailsButton);
}
```
```

- [ ] **Step 3: Write `ReadText.md`**

Create `dialogutils/Documentation/ReadText.md`:

```markdown
# Read Text

## Log an error message before dismissing it

```csharp
string message = dialog.GetDialogText(hWnd);
Console.WriteLine($"Dialog said: {message}");
dialog.ClickDialogButton(hWnd, DialogButton.Ok);
```

## Read the dialog's own title bar text

```csharp
string title = dialog.GetControlText(hWnd);
```
```

- [ ] **Step 4: Write `WaitForDialog.md`**

Create `dialogutils/Documentation/WaitForDialog.md`:

```markdown
# Wait for Dialog

## Wait for a save-confirmation dialog that may or may not appear

```csharp
if (dialog.WaitForDialog("Save changes", timeoutMs: 3000, pollIntervalMs: 100, out IntPtr hWnd))
{
    dialog.ClickDialogButton(hWnd, DialogButton.No);
}
// else: the app closed without prompting - nothing to do
```

## Wait for a dialog to close after clicking its button

```csharp
dialog.ClickDialogButton(hWnd, DialogButton.Ok);
dialog.WaitForDialogToClose(hWnd, timeoutMs: 5000, pollIntervalMs: 100);
```
```

- [ ] **Step 5: Commit**

```bash
git add dialogutils/Documentation
git commit -m "Add DialogUtils per-category usage documentation"
```

---

### Task 8: Update the root README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add a row to the components table**

Add to the `| Component | Assembly | Description |` table in the root `README.md`:

```markdown
| [dialogutils](dialogutils/README.md) | `DialogAutomation` | Finds and dismisses native dialogs by button text/control ID via `BM_CLICK`, without moving the cursor. |
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "List DialogUtils in the root README"
```

---

### Task 9: Final solution build

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build AwesomeRpaUtils.sln`
Expected: `Build succeeded.` with all projects — `MouseAutomation`, `ScreenCaptureAutomation`, `KeyboardAutomation`, `WindowAutomation`, `OcrAutomation`, `DialogAutomation` — compiling cleanly.

- [ ] **Step 2: Confirm nothing was left uncommitted**

Run: `git status`
Expected: `nothing to commit, working tree clean`
