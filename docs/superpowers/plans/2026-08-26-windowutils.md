# WindowUtils Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `WindowUtils` Pega Robot Studio component (assembly `WindowAutomation`) that enumerates, locates, moves/resizes, activates, and closes windows via the Win32 window APIs, matching the conventions of the existing `mouseutils`/`screencaptureutils` components.

**Architecture:** A single `WindowUtils : Component` class in `windowutils/WindowUtils.cs`, using direct `user32.dll` P/Invoke (`EnumWindows`, `GetWindowRect`, `SetWindowPos`, etc.) — the same interop style already used by `mouseutils/MouseUtils.cs`'s window-relative-targeting methods. No project references to other components — fully standalone (its own private `RECT` struct, `GetWindowBounds`, etc., duplicated from MouseUtils' equivalents by design).

**Tech Stack:** .NET 10 (`net10.0-windows`), `System.Runtime.InteropServices` (user32.dll P/Invoke), `System.ComponentModel.Component`, `System.Drawing.Rectangle` (available unqualified via full-name reference the same way `MouseUtils.cs` uses it, with no extra package reference or `UseWindowsForms`).

**Spec:** See `docs/superpowers/specs/2026-08-26-keyboard-window-ocr-dialog-utils-design.md` (WindowUtils section) for the approved design this plan implements.

---

### Task 1: Scaffold the project and add it to the solution

**Files:**
- Create: `windowutils/WindowUtils.csproj`
- Create: `windowutils/WindowUtils.cs`
- Modify: `AwesomeRpaUtils.sln` (via `dotnet sln add`)

- [ ] **Step 1: Create the csproj**

Create `windowutils/WindowUtils.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AssemblyName>WindowAutomation</AssemblyName>
    <RootNamespace>WindowAutomation</RootNamespace>
    <Platforms>AnyCPU;x64</Platforms>
    <!-- Emit WindowAutomation.xml next to the DLL so the XML doc comments are consumable -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Create a minimal compiling stub class**

Create `windowutils/WindowUtils.cs`:

```csharp
using System.ComponentModel;

namespace WindowAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that enumerates, locates, moves/resizes,
    /// activates, and closes windows using the Win32 window APIs.
    /// </summary>
    [Description("Finds, moves, resizes, activates, and closes windows. Drag this " +
                 "component onto a Pega Robot Studio automation to use its methods.")]
    public class WindowUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public WindowUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public WindowUtils(IContainer container)
        {
            container?.Add(this);
        }
    }
}
```

- [ ] **Step 3: Add the project to the solution under its own solution folder**

Run: `dotnet sln AwesomeRpaUtils.sln add windowutils/WindowUtils.csproj --solution-folder windowutils`
Expected: `Project(s) added to the solution.`

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build AwesomeRpaUtils.sln`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add windowutils/WindowUtils.csproj windowutils/WindowUtils.cs AwesomeRpaUtils.sln
git commit -m "Scaffold WindowUtils project"
```

---

### Task 2: Enum and Win32 interop foundation

**Files:**
- Modify: `windowutils/WindowUtils.cs`

- [ ] **Step 1: Replace the top of the file with the full `using` list and the `ShowWindowCommand` enum**

Replace the `using System.ComponentModel;` line and everything up to (not including) the `WindowUtils` class's XML doc comment with:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WindowAutomation
{
    /// <summary>
    /// The window-state command applied by <see cref="WindowUtils.SetWindowState"/>,
    /// wrapping the Win32 <c>SW_*</c> constants used by <c>ShowWindow</c>.
    /// </summary>
    public enum ShowWindowCommand
    {
        /// <summary>Hides the window (SW_HIDE, 0).</summary>
        Hide = 0,
        /// <summary>Shows the window in its normal (restored) state (SW_SHOWNORMAL, 1).</summary>
        Normal = 1,
        /// <summary>Shows the window maximized (SW_SHOWMAXIMIZED, 3).</summary>
        Maximized = 3,
        /// <summary>Minimizes the window without activating another window (SW_MINIMIZE, 6).</summary>
        Minimized = 6,
        /// <summary>Restores a minimized/maximized window to its previous size and position (SW_RESTORE, 9).</summary>
        Restore = 9
    }

```

- [ ] **Step 2: Add the Win32 interop foundation as a new region inside the class, after the constructors**

```csharp
        #region Win32 Interop

        private const uint WM_CLOSE = 0x0010;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowNative(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static extern IntPtr GetForegroundWindowNative();

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindowNative(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "MoveWindow", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveWindowNative(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

        [DllImport("user32.dll", EntryPoint = "ShowWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowNative(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisibleNative(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowNative(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "IsHungAppWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsHungAppWindowNative(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build windowutils/WindowUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add windowutils/WindowUtils.cs
git commit -m "Add ShowWindowCommand enum and Win32 interop layer to WindowUtils"
```

---

### Task 3: Enumeration & Lookup methods

**Files:**
- Modify: `windowutils/WindowUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after the constructors and before Win32 Interop**

```csharp
        #region Enumeration & Lookup

        /// <summary>Gets all top-level windows via <c>EnumWindows</c>.</summary>
        public List<IntPtr> GetTopLevelWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                windows.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        /// <summary>
        /// Finds a top-level window by its title. Returns <see cref="IntPtr.Zero"/> if no
        /// window matches (not found is a normal, checkable outcome, not an error).
        /// </summary>
        /// <param name="title">The title to match.</param>
        /// <param name="exactMatch">
        /// If <c>true</c> (default), requires an exact, case-sensitive title match.
        /// If <c>false</c>, matches any window whose title contains <paramref name="title"/>
        /// (case-insensitive).
        /// </param>
        public IntPtr FindWindowByTitle(string title, bool exactMatch = true)
        {
            foreach (var hWnd in GetTopLevelWindows())
            {
                string windowTitle = GetWindowTitle(hWnd);
                bool matches = exactMatch
                    ? string.Equals(windowTitle, title, StringComparison.Ordinal)
                    : windowTitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0;
                if (matches)
                    return hWnd;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Finds the first top-level window of the given window class. Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        public IntPtr FindWindowByClass(string className)
        {
            return FindWindowNative(className, null);
        }

        /// <summary>
        /// Finds all top-level windows owned by the given process ID (a process can own
        /// more than one top-level window).
        /// </summary>
        public List<IntPtr> FindWindowsByProcessId(int processId)
        {
            var matches = new List<IntPtr>();
            foreach (var hWnd in GetTopLevelWindows())
            {
                if (GetWindowProcessId(hWnd) == processId)
                    matches.Add(hWnd);
            }
            return matches;
        }

        /// <summary>Gets the handle of the current foreground (active) window.</summary>
        public IntPtr GetForegroundWindow()
        {
            return GetForegroundWindowNative();
        }

        #endregion
```

- [ ] **Step 2: Build to verify it compiles**

This will fail to compile until Task 4 adds `GetWindowTitle`/`GetWindowProcessId` (used above). That is expected here — add Task 4's methods before building. If you must verify incrementally, temporarily comment out `FindWindowByTitle`'s `GetWindowTitle` call and `FindWindowsByProcessId`'s `GetWindowProcessId` call, build, then restore them once Task 4 is applied.

- [ ] **Step 3: Commit** (after Task 4 is also applied and the project builds — see Task 4's steps)

---

### Task 4: State & Geometry methods

**Files:**
- Modify: `windowutils/WindowUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after Enumeration & Lookup**

```csharp
        #region State & Geometry

        /// <summary>Gets the screen-space bounding rectangle of a window.</summary>
        /// <exception cref="Win32Exception">GetWindowRect failed.</exception>
        public System.Drawing.Rectangle GetWindowBounds(IntPtr hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT rect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.");
            return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        /// <summary>Moves and/or resizes a window to the given screen-space rectangle.</summary>
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is negative.</exception>
        /// <exception cref="Win32Exception">MoveWindow failed.</exception>
        public void SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height)
        {
            if (width < 0 || height < 0)
                throw new ArgumentException("width and height must be non-negative.");
            if (!MoveWindowNative(hWnd, left, top, width, height, true))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "MoveWindow failed.");
        }

        /// <summary>Moves a window to a new position without changing its size.</summary>
        /// <exception cref="Win32Exception">GetWindowRect or MoveWindow failed.</exception>
        public void MoveWindow(IntPtr hWnd, int left, int top)
        {
            var bounds = GetWindowBounds(hWnd);
            SetWindowBounds(hWnd, left, top, bounds.Width, bounds.Height);
        }

        /// <summary>Resizes a window without changing its position.</summary>
        /// <exception cref="ArgumentException"><paramref name="width"/> or <paramref name="height"/> is negative.</exception>
        /// <exception cref="Win32Exception">GetWindowRect or MoveWindow failed.</exception>
        public void ResizeWindow(IntPtr hWnd, int width, int height)
        {
            var bounds = GetWindowBounds(hWnd);
            SetWindowBounds(hWnd, bounds.Left, bounds.Top, width, height);
        }

        /// <summary>Gets a window's title text (empty string if it has none).</summary>
        public string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Gets a window's window-class name.</summary>
        public string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Gets the process ID that owns a window.</summary>
        public int GetWindowProcessId(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            return (int)processId;
        }

        /// <summary>Returns <c>true</c> if the window is visible.</summary>
        public bool IsWindowVisible(IntPtr hWnd)
        {
            return IsWindowVisibleNative(hWnd);
        }

        /// <summary>
        /// Returns <c>true</c> if the window is responding to messages (the inverse of
        /// <c>IsHungAppWindow</c>).
        /// </summary>
        public bool IsWindowResponding(IntPtr hWnd)
        {
            return !IsHungAppWindowNative(hWnd);
        }

        /// <summary>
        /// Applies a show/hide/minimize/maximize/restore state to a window.
        /// </summary>
        /// <remarks>
        /// <c>ShowWindow</c>'s return value reports whether the window was PREVIOUSLY
        /// visible, not whether this call succeeded — there is nothing meaningful to
        /// check or throw on, so this method never throws.
        /// </remarks>
        public void SetWindowState(IntPtr hWnd, ShowWindowCommand command)
        {
            ShowWindowNative(hWnd, (int)command);
        }

        /// <summary>Asks a window to close by posting <c>WM_CLOSE</c> to it.</summary>
        /// <exception cref="Win32Exception">PostMessage failed.</exception>
        public void CloseWindow(IntPtr hWnd)
        {
            if (!PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PostMessage(WM_CLOSE) failed.");
        }

        #endregion
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build windowutils/WindowUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual smoke test**

Open Notepad, then call `FindWindowByTitle("Notepad", exactMatch: false)` to get its handle, `GetWindowBounds(hWnd)` to read its rectangle, and `SetWindowBounds(hWnd, 100, 100, 800, 600)` to reposition/resize it. Confirm the window moved.

- [ ] **Step 4: Commit** (this also covers Task 3, now that it compiles)

```bash
git add windowutils/WindowUtils.cs
git commit -m "Implement WindowUtils enumeration/lookup and state/geometry methods"
```

---

### Task 5: Activation & Z-Order methods

**Files:**
- Modify: `windowutils/WindowUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after State & Geometry**

```csharp
        #region Activation & Z-Order

        /// <summary>Brings a window to the foreground and gives it input focus.</summary>
        /// <exception cref="Win32Exception">
        /// SetForegroundWindow failed. Windows' foreground-lock rules can block activation
        /// requested from a background process that isn't the user's currently active app.
        /// </exception>
        public void ActivateWindow(IntPtr hWnd)
        {
            if (!SetForegroundWindowNative(hWnd))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "SetForegroundWindow failed. (Windows' foreground-lock rules can block activation from a background process.)");
        }

        /// <summary>
        /// Makes a window always-on-top (or removes that state), system-wide and
        /// session-persistent until changed again.
        /// </summary>
        /// <exception cref="Win32Exception">SetWindowPos failed.</exception>
        public void SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop)
        {
            IntPtr insertAfter = alwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST;
            if (!SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos failed.");
        }

        /// <summary>
        /// Polls for a top-level window matching <paramref name="title"/> (substring,
        /// case-insensitive) until it appears or the timeout elapses.
        /// </summary>
        /// <param name="title">The window title substring to match (case-insensitive).</param>
        /// <param name="timeoutMs">Maximum time to poll, in milliseconds.</param>
        /// <param name="pollIntervalMs">Time to sleep between polls, in milliseconds.</param>
        /// <param name="hWnd">The matching window's handle, or <see cref="IntPtr.Zero"/> if not found in time.</param>
        /// <returns><c>true</c> if a matching window was found before the timeout.</returns>
        public bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (true)
            {
                IntPtr found = FindWindowByTitle(title, exactMatch: false);
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

        /// <summary>Polls until a window handle is no longer valid (the window closed), or the timeout elapses.</summary>
        public bool WaitForWindowToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)
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

        /// <summary>Polls until the given window becomes the foreground window, or the timeout elapses.</summary>
        public bool WaitForWindowActive(IntPtr hWnd, int timeoutMs, int pollIntervalMs)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (GetForegroundWindow() != hWnd)
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

Run: `dotnet build windowutils/WindowUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual smoke test**

Launch Notepad from code (or manually) right before calling `WaitForWindow("Notepad", 5000, 100, out var hWnd)`; confirm it returns `true` with a valid handle. Call `ActivateWindow(hWnd)` on a background copy of Notepad and confirm it comes to the front.

- [ ] **Step 4: Commit**

```bash
git add windowutils/WindowUtils.cs
git commit -m "Implement WindowUtils activation and z-order methods"
```

---

### Task 6: Child / Multi-Window Enumeration methods

**Files:**
- Modify: `windowutils/WindowUtils.cs`

- [ ] **Step 1: Add the methods as a new region, after Activation & Z-Order**

```csharp
        #region Child / Multi-Window Enumeration

        /// <summary>Gets all direct child windows/controls of a parent window via <c>EnumChildWindows</c>.</summary>
        public List<IntPtr> GetChildWindows(IntPtr hWndParent)
        {
            var children = new List<IntPtr>();
            EnumChildWindows(hWndParent, (hWnd, lParam) =>
            {
                children.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return children;
        }

        /// <summary>
        /// Finds a child window under <paramref name="hWndParent"/> matching the given title
        /// and/or class name (pass <c>null</c> for either to not filter on it). Returns
        /// <see cref="IntPtr.Zero"/> if none matches.
        /// </summary>
        public IntPtr FindChildWindow(IntPtr hWndParent, string title, string className)
        {
            foreach (var child in GetChildWindows(hWndParent))
            {
                bool titleMatches = title == null || GetWindowTitle(child) == title;
                bool classMatches = className == null || GetWindowClassName(child) == className;
                if (titleMatches && classMatches)
                    return child;
            }
            return IntPtr.Zero;
        }

        #endregion
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build windowutils/WindowUtils.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual smoke test**

Find Notepad's edit control: `FindChildWindow(notepadHandle, null, "Edit")` (or `"RichEditD2DPT"` on newer Notepad builds) and confirm a non-zero handle is returned.

- [ ] **Step 4: Commit**

```bash
git add windowutils/WindowUtils.cs
git commit -m "Implement WindowUtils child window enumeration"
```

---

### Task 7: Component README

**Files:**
- Create: `windowutils/README.md`

- [ ] **Step 1: Write the README**

Create `windowutils/README.md`:

```markdown
# WindowAutomation

A Pega Robot Studio-ready component (`WindowUtils`) that enumerates, locates,
moves/resizes, activates, and closes windows using the Win32 window APIs
(`EnumWindows`, `GetWindowRect`, `SetWindowPos`, and friends).

- Target framework: `net10.0-windows`
- Namespace: `WindowAutomation`
- Assembly: `WindowAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

Designed to be used alongside [MouseUtils](../mouseutils/MouseUtils.cs)
(`MouseAutomation`), which already exposes a `GetWindowBounds`/`GetWindowAtPoint`
pair scoped to window-relative *clicking*: this component owns general window
management (finding, moving, activating, closing), independent of any input
component. A small amount of overlap (both components can get a window's
bounds) is intentional — each component is fully standalone with no shared
project reference.

## Enums

### `ShowWindowCommand`
The window-state command applied by `SetWindowState`, wrapping the Win32
`SW_*` constants: `Hide`, `Normal`, `Maximized`, `Minimized`, `Restore`.

## Constructors

| Constructor | Description |
|---|---|
| `WindowUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `WindowUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Enumeration & Lookup

| Method | Description |
|---|---|
| `List<IntPtr> GetTopLevelWindows()` | Gets all top-level windows via `EnumWindows`. |
| `IntPtr FindWindowByTitle(string title, bool exactMatch = true)` | Finds a top-level window by title (exact or substring match). Returns `IntPtr.Zero` if none matches. |
| `IntPtr FindWindowByClass(string className)` | Finds the first top-level window of the given window class. |
| `List<IntPtr> FindWindowsByProcessId(int processId)` | Finds all top-level windows owned by the given process ID. |
| `IntPtr GetForegroundWindow()` | Gets the handle of the current foreground (active) window. |

### State & Geometry

| Method | Description |
|---|---|
| `Rectangle GetWindowBounds(IntPtr hWnd)` | Gets the screen-space bounding rectangle of a window. |
| `void SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height)` | Moves and/or resizes a window to the given rectangle. |
| `void MoveWindow(IntPtr hWnd, int left, int top)` | Moves a window without changing its size. |
| `void ResizeWindow(IntPtr hWnd, int width, int height)` | Resizes a window without changing its position. |
| `string GetWindowTitle(IntPtr hWnd)` | Gets a window's title text. |
| `string GetWindowClassName(IntPtr hWnd)` | Gets a window's window-class name. |
| `int GetWindowProcessId(IntPtr hWnd)` | Gets the process ID that owns a window. |
| `bool IsWindowVisible(IntPtr hWnd)` | Returns `true` if the window is visible. |
| `bool IsWindowResponding(IntPtr hWnd)` | Returns `true` if the window is responding to messages (inverse of `IsHungAppWindow`). |
| `void SetWindowState(IntPtr hWnd, ShowWindowCommand command)` | Applies a show/hide/minimize/maximize/restore state to a window. |
| `void CloseWindow(IntPtr hWnd)` | Asks a window to close by posting `WM_CLOSE`. |

### Activation & Z-Order

| Method | Description |
|---|---|
| `void ActivateWindow(IntPtr hWnd)` | Brings a window to the foreground and gives it input focus. |
| `void SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop)` | Makes a window always-on-top (or removes that state). |
| `bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)` | Polls for a window matching the title until it appears or the timeout elapses. |
| `bool WaitForWindowToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until a window handle is no longer valid, or the timeout elapses. |
| `bool WaitForWindowActive(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until the given window becomes the foreground window, or the timeout elapses. |

### Child / Multi-Window Enumeration

| Method | Description |
|---|---|
| `List<IntPtr> GetChildWindows(IntPtr hWndParent)` | Gets all direct child windows/controls of a parent window. |
| `IntPtr FindChildWindow(IntPtr hWndParent, string title, string className)` | Finds a child window matching the given title and/or class name. |

## Notes & Caveats

- **Window handles (`IntPtr`) become invalid once a window closes.** No method here holds a
  handle open; always re-find a window (or use `WaitForWindow`) rather than caching a handle
  across a long-running automation step.
- **`SetWindowState`** never throws: `ShowWindow`'s return value reports the window's
  *previous* visibility state, not whether the call succeeded, so there is nothing
  meaningful to check.
- **`ActivateWindow`** can fail due to Windows' foreground-lock timeout rules, which block a
  background process from stealing focus from the user's currently active app — this is a
  deliberate OS security behavior, not a bug.
- **`SetAlwaysOnTop`** is system-wide and session-persistent for that window until changed
  again or the window closes.
- **`CloseWindow`** posts `WM_CLOSE` (a polite request); an application with unsaved changes
  may show a "Save changes?" prompt instead of closing immediately — pair with
  `WaitForWindowToClose` and handle that dialog if it can appear (e.g. via DialogUtils).
```

- [ ] **Step 2: Commit**

```bash
git add windowutils/README.md
git commit -m "Add WindowUtils README"
```

---

### Task 8: Per-category usage documentation

**Files:**
- Create: `windowutils/Documentation/README.md`
- Create: `windowutils/Documentation/EnumerationAndLookup.md`
- Create: `windowutils/Documentation/StateAndGeometry.md`
- Create: `windowutils/Documentation/ActivationAndZOrder.md`
- Create: `windowutils/Documentation/ChildWindows.md`

- [ ] **Step 1: Write the documentation index**

Create `windowutils/Documentation/README.md`:

```markdown
# WindowUtils Documentation

Real-world usage examples for every method on the `WindowUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Enumeration & Lookup](EnumerationAndLookup.md) — finding windows by title/class/process
- [State & Geometry](StateAndGeometry.md) — bounds, title, visibility, show/hide/close
- [Activation & Z-Order](ActivationAndZOrder.md) — focus, always-on-top, wait-for helpers
- [Child Windows](ChildWindows.md) — enumerating and finding controls within a window

All examples assume a `WindowUtils` instance named `window`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var window = new WindowUtils();`).
```

- [ ] **Step 2: Write `EnumerationAndLookup.md`**

Create `windowutils/Documentation/EnumerationAndLookup.md`:

```markdown
# Enumeration & Lookup

## Find a window by its title (substring match)

```csharp
IntPtr notepad = window.FindWindowByTitle("Notepad", exactMatch: false);
```

## Find a window by exact title

```csharp
IntPtr exact = window.FindWindowByTitle("Untitled - Notepad");
```

## Find all windows belonging to a process

```csharp
var process = System.Diagnostics.Process.Start("notepad.exe");
var handles = window.FindWindowsByProcessId(process.Id);
```

## List every top-level window currently open

```csharp
foreach (var hWnd in window.GetTopLevelWindows())
{
    Console.WriteLine(window.GetWindowTitle(hWnd));
}
```

## Get the currently active window

```csharp
IntPtr active = window.GetForegroundWindow();
```
```

- [ ] **Step 3: Write `StateAndGeometry.md`**

Create `windowutils/Documentation/StateAndGeometry.md`:

```markdown
# State & Geometry

## Read a window's position and size

```csharp
System.Drawing.Rectangle bounds = window.GetWindowBounds(hWnd);
```

## Move and resize a window in one call

```csharp
window.SetWindowBounds(hWnd, left: 0, top: 0, width: 1024, height: 768);
```

## Move only

```csharp
window.MoveWindow(hWnd, left: 100, top: 100);
```

## Resize only

```csharp
window.ResizeWindow(hWnd, width: 800, height: 600);
```

## Read a window's title, class, and owning process

```csharp
string title = window.GetWindowTitle(hWnd);
string className = window.GetWindowClassName(hWnd);
int pid = window.GetWindowProcessId(hWnd);
```

## Minimize, maximize, and restore

```csharp
window.SetWindowState(hWnd, ShowWindowCommand.Minimized);
window.SetWindowState(hWnd, ShowWindowCommand.Maximized);
window.SetWindowState(hWnd, ShowWindowCommand.Restore);
```

## Check responsiveness before interacting

```csharp
if (!window.IsWindowResponding(hWnd))
{
    // the app is hung - skip it or escalate rather than clicking into it
}
```

## Close a window

```csharp
window.CloseWindow(hWnd);
```
```

- [ ] **Step 4: Write `ActivationAndZOrder.md`**

Create `windowutils/Documentation/ActivationAndZOrder.md`:

```markdown
# Activation & Z-Order

## Bring a window to the front before interacting with it

```csharp
window.ActivateWindow(hWnd);
```

## Pin a window on top of everything else

```csharp
window.SetAlwaysOnTop(hWnd, alwaysOnTop: true);
// ... later ...
window.SetAlwaysOnTop(hWnd, alwaysOnTop: false);
```

## Wait for an application to launch and its window to appear

```csharp
System.Diagnostics.Process.Start("notepad.exe");
if (window.WaitForWindow("Notepad", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd))
{
    window.ActivateWindow(hWnd);
}
```

## Wait for a window to close before continuing

```csharp
window.CloseWindow(hWnd);
window.WaitForWindowToClose(hWnd, timeoutMs: 5000, pollIntervalMs: 100);
```

## Wait for a window to actually become active after requesting activation

```csharp
window.ActivateWindow(hWnd);
window.WaitForWindowActive(hWnd, timeoutMs: 2000, pollIntervalMs: 50);
```
```

- [ ] **Step 5: Write `ChildWindows.md`**

Create `windowutils/Documentation/ChildWindows.md`:

```markdown
# Child Windows

## List every control inside a window

```csharp
foreach (var child in window.GetChildWindows(hWnd))
{
    Console.WriteLine(window.GetWindowClassName(child));
}
```

## Find Notepad's text-edit control

```csharp
IntPtr editControl = window.FindChildWindow(hWnd, title: null, className: "Edit");
```

## Find a specific labeled button

```csharp
IntPtr okButton = window.FindChildWindow(hWnd, title: "OK", className: "Button");
```
```

- [ ] **Step 6: Commit**

```bash
git add windowutils/Documentation
git commit -m "Add WindowUtils per-category usage documentation"
```

---

### Task 9: Update the root README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add a row to the components table**

In the root `README.md`'s components table (the same table Task 9 of the KeyboardUtils plan already added a `keyboardutils` row to — if that plan has not run yet, add both rows together), add:

```markdown
| [windowutils](windowutils/README.md) | `WindowAutomation` | Enumerates, locates, moves/resizes, activates, and closes windows via the Win32 window APIs. |
```

as a new row in the `| Component | Assembly | Description |` table.

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "List WindowUtils in the root README"
```

---

### Task 10: Final solution build

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build AwesomeRpaUtils.sln`
Expected: `Build succeeded.` with all projects compiling cleanly.

- [ ] **Step 2: Confirm nothing was left uncommitted**

Run: `git status`
Expected: `nothing to commit, working tree clean`
