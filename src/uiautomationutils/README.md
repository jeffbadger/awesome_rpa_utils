# UIAutomation

A Pega Robot Studio-ready component (`UIAutomationUtils`) that finds and
drives modern (WinUI3/UWP/WPF/browser-hosted) UI via Windows UI Automation
(UIA) - the controls that `WindowUtils`/`DialogUtils` can't see, since those
operate on native Win32 windows/controls by handle and window class.

- Target framework: `net10.0-windows`
- Namespace: `UIAutomation`
- Assembly: `UIAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

**Naming note:** every other component in this repo follows a
`<Name>Utils`/`<Name>Automation` pattern (e.g. `WindowUtils`/
`WindowAutomation`). Applying that literally here would produce the
redundant `UIAutomationUtils`/`UIAutomationAutomation` - so this component's
assembly/namespace is just `UIAutomation` (no trailing "Automation"), a
deliberate, documented one-off exception.

`FromWindowHandle` bridges a handle from `WindowUtils`/`DialogUtils` into
UIA; `FromPoint` bridges screen coordinates from `MouseUtils`. No project
references between components - this is the same duplication-over-coupling
tradeoff the rest of the repo makes.

## Types

### `UiControlType`
A common UI Automation control type, mapped internally to
`System.Windows.Automation.ControlType`: `Button`, `CheckBox`, `ComboBox`,
`Edit`, `Hyperlink`, `Image`, `List`, `ListItem`, `Menu`, `MenuItem`, `Pane`,
`RadioButton`, `Tab`, `TabItem`, `Text`, `Tree`, `TreeItem`, `Window`,
`Custom`.

## Constructors

| Constructor | Description |
|---|---|
| `UIAutomationUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `UIAutomationUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Find

| Method | Signature | Description |
|---|---|---|
| `GetRootElement` | `AutomationElement GetRootElement()` | Gets the desktop root element. |
| `FromWindowHandle` | `AutomationElement FromWindowHandle(IntPtr hWnd)` | Gets the UI Automation element for a window handle, or null if invalid. |
| `FromPoint` | `AutomationElement FromPoint(int x, int y)` | Gets the UI Automation element at a screen point, or null on failure. |
| `FindByAutomationId` | `bool FindByAutomationId(AutomationElement parent, string automationId, out AutomationElement element, out string message, bool descendantsOnly = true)` | Finds a descendant (or child) element by its AutomationId. Returns True if found; never throws. |
| `FindByName` | `bool FindByName(AutomationElement parent, string name, out AutomationElement element, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant (or child) element by its Name (exact or substring match). Returns True if found; never throws. |
| `FindByClassName` | `bool FindByClassName(AutomationElement parent, string className, out AutomationElement element, out string message, bool descendantsOnly = true)` | Finds a descendant (or child) element by its window class name. Returns True if found; never throws. |
| `FindByControlType` | `bool FindByControlType(AutomationElement parent, UiControlType controlType, out AutomationElement element, out string message, bool descendantsOnly = true)` | Finds the first descendant (or child) element of the given control type. Returns True if found; never throws. |
| `FindAllByControlType` | `bool FindAllByControlType(AutomationElement parent, UiControlType controlType, out List<AutomationElement> elements, out string message, bool descendantsOnly = true)` | Finds every descendant (or child) element of the given control type. Returns True on success; never throws. |
| `GetChildren` | `bool GetChildren(AutomationElement parent, out List<AutomationElement> children, out string message)` | Gets all immediate children of an element. Returns True on success; never throws. |

`descendantsOnly = true` (the default) searches the full subtree
(`TreeScope.Descendants`); `false` searches only immediate children
(`TreeScope.Children`).

### Properties

| Method | Signature | Description |
|---|---|---|
| `GetName` | `bool GetName(AutomationElement element, out string name, out string message)` | Gets an element's Name property. Returns True on success; never throws. |
| `GetAutomationId` | `bool GetAutomationId(AutomationElement element, out string automationId, out string message)` | Gets an element's AutomationId property. Returns True on success; never throws. |
| `GetClassName` | `bool GetClassName(AutomationElement element, out string className, out string message)` | Gets an element's window class name. Returns True on success; never throws. |
| `GetControlTypeName` | `bool GetControlTypeName(AutomationElement element, out string controlTypeName, out string message)` | Gets a friendly name for an element's control type (e.g. "Button"); `false` + message if the element reports no ControlType. |
| `GetBoundingRectangle` | `bool GetBoundingRectangle(AutomationElement element, out Rectangle bounds, out string message)` | Gets an element's screen-space bounding rectangle; `false` (+ message) if the element has no on-screen bounding rectangle. |
| `IsEnabled` | `bool IsEnabled(AutomationElement element, out string message)` | Returns True if the element is enabled. Never throws. |
| `IsOffscreen` | `bool IsOffscreen(AutomationElement element, out string message)` | Returns True if the element is offscreen. Never throws. |
| `IsElementAvailable` | `bool IsElementAvailable(AutomationElement element)` | Returns True if the element is still available (its underlying UI hasn't gone away). |

### Actions

| Method | Signature | Description |
|---|---|---|
| `Invoke` | `bool Invoke(AutomationElement element, out string message)` | Invokes an element (click-equivalent for buttons/menu items) via InvokePattern. Returns True on success; never throws. |
| `SetValue` | `bool SetValue(AutomationElement element, string value, out string message)` | Sets an element's value via ValuePattern. Returns True on success; never throws. |
| `GetValue` | `bool GetValue(AutomationElement element, out string value, out string message)` | Gets an element's value via ValuePattern. Returns True on success; never throws. |
| `Toggle` | `bool Toggle(AutomationElement element, out string message)` | Toggles an element (e.g. a checkbox) via TogglePattern. Returns True on success; never throws. |
| `IsToggled` | `bool IsToggled(AutomationElement element, out string message)` | Returns True if a toggleable element is currently On. Never throws. |
| `Expand` | `bool Expand(AutomationElement element, out string message)` | Expands an element (e.g. a combo box or tree node) via ExpandCollapsePattern. Returns True on success; never throws. |
| `Collapse` | `bool Collapse(AutomationElement element, out string message)` | Collapses an element via ExpandCollapsePattern. Returns True on success; never throws. |
| `Select` | `bool Select(AutomationElement element, out string message)` | Selects an element (e.g. a list item) via SelectionItemPattern. Returns True on success; never throws. |
| `IsSelected` | `bool IsSelected(AutomationElement element, out string message)` | Returns True if a selectable element is currently selected. Never throws. |

### Wait

| Method | Signature | Description |
|---|---|---|
| `WaitForElementByAutomationId` | `bool WaitForElementByAutomationId(AutomationElement parent, string automationId, int timeoutMs, int pollIntervalMs, out AutomationElement element, out string message)` | Polls for a descendant element matching the given AutomationId until it appears or the timeout elapses. `message` is only set if a real argument error aborted the poll early. Never throws. |
| `WaitForElementByName` | `bool WaitForElementByName(AutomationElement parent, string name, bool exactMatch, int timeoutMs, int pollIntervalMs, out AutomationElement element, out string message)` | Polls for a descendant element matching the given Name until it appears or the timeout elapses. `message` is only set if a real argument error aborted the poll early. Never throws. |

### Visual

| Method | Signature | Description |
|---|---|---|
| `HighlightElement` | `bool HighlightElement(AutomationElement element, out string message, int flashes = 3, int flashMs = 200, int lineWidth = 3, int colorRef = 0x0000FF)` | Flashes an inverting rectangle around an element to visually confirm which on-screen element it corresponds to. Returns True on success; never throws. |

## Notes & Caveats

- **Every method that could previously throw for a null element or unsupported pattern
  now returns `bool` with an `out string message`** instead — a null `element`/`parent`,
  an unsupported action pattern (e.g. calling `Toggle` on a plain button), and Win32
  failures (`HighlightElement`'s `GetDC`) are all reported this way, with `message` set
  to a human-readable reason whenever the method returns `false`.
- **`FindBy*` methods overload the meaning of a `false` return**: it covers both a normal
  "not found" outcome (`message == null`) and a real argument error that prevented the
  search (`message` set) — check `message` to tell them apart. `FindAllByControlType`/
  `GetChildren` don't have this ambiguity since an empty list is an unambiguous "no
  results," so their `bool` means only "no argument error occurred."
- **`IsEnabled`/`IsOffscreen`/`IsToggled`/`IsSelected` keep their original `bool` meaning**
  (the property's actual value) rather than gaining a separate success flag — a `false`
  return can mean either the genuine state or a real error (null element, unsupported
  pattern); check `message` to tell them apart.
- **`IsElementAvailable` is the one method that accepts `null` without
  needing an `out message`** (returning `false` instead) - its entire purpose is checking
  whether a reference is still good, and a null reference is definitionally
  not available. It was already never-throw and is unchanged.
- **An element reference can go stale at any time** (the underlying UI
  closed, its control was removed, etc.) - property/action/take-lookup
  calls on a stale element throw `ElementNotAvailableException`, so all
  find/property/action methods catch it and report it via `message` (+ a
  `false` return) instead of throwing. Re-find the element (or check
  `IsElementAvailable` first) rather than caching a reference across a
  long-running automation step, the same caution `WindowUtils` already
  gives for raw window handles.
- **`FindByControlType`/`FindAllByControlType` only cover the 19 control
  types in `UiControlType`** - advanced/rare UIA patterns (Grid, Table,
  Scroll, Text range, MultipleView) are out of scope for this component.
- **This component cannot be exercised without a real Windows desktop
  session and a real target application** - unlike some other components
  here, almost no part of this can be exercised on a non-Windows
  machine or without an actual UI to point it at. The `UIAutomation.Tests`
  project covers only the platform-independent input guards and the
  `UiControlType` mapping; live element behavior is covered by the Pega
  Unit Test plan in the repo's TESTING.md.
