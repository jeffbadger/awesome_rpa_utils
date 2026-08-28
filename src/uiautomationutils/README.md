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

| Method | Description |
|---|---|
| `AutomationElement GetRootElement()` | Gets the desktop root element. |
| `AutomationElement FromWindowHandle(IntPtr hWnd)` | Gets the UI Automation element for a window handle, or null if invalid. |
| `AutomationElement FromPoint(int x, int y)` | Gets the UI Automation element at a screen point, or null on failure. |
| `AutomationElement FindByAutomationId(AutomationElement parent, string automationId, bool descendantsOnly = true)` | Finds a descendant (or child) element by its AutomationId, or null if none matches. |
| `AutomationElement FindByName(AutomationElement parent, string name, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant (or child) element by its Name (exact or substring match), or null if none matches. |
| `AutomationElement FindByClassName(AutomationElement parent, string className, bool descendantsOnly = true)` | Finds a descendant (or child) element by its window class name, or null if none matches. |
| `AutomationElement FindByControlType(AutomationElement parent, UiControlType controlType, bool descendantsOnly = true)` | Finds the first descendant (or child) element of the given control type, or null if none matches. |
| `List<AutomationElement> FindAllByControlType(AutomationElement parent, UiControlType controlType, bool descendantsOnly = true)` | Finds every descendant (or child) element of the given control type. |
| `List<AutomationElement> GetChildren(AutomationElement parent)` | Gets all immediate children of an element. |

`descendantsOnly = true` (the default) searches the full subtree
(`TreeScope.Descendants`); `false` searches only immediate children
(`TreeScope.Children`).

### Properties

| Method | Description |
|---|---|
| `string GetName(AutomationElement element)` | Gets an element's Name property. |
| `string GetAutomationId(AutomationElement element)` | Gets an element's AutomationId property. |
| `string GetClassName(AutomationElement element)` | Gets an element's window class name. |
| `string GetControlTypeName(AutomationElement element)` | Gets a friendly name for an element's control type (e.g. "Button"). |
| `Rectangle GetBoundingRectangle(AutomationElement element)` | Gets an element's screen-space bounding rectangle. |
| `bool IsEnabled(AutomationElement element)` | Returns True if the element is enabled. |
| `bool IsOffscreen(AutomationElement element)` | Returns True if the element is offscreen. |
| `bool IsElementAvailable(AutomationElement element)` | Returns True if the element is still available (its underlying UI hasn't gone away). |

### Actions

| Method | Description |
|---|---|
| `void Invoke(AutomationElement element)` | Invokes an element (click-equivalent for buttons/menu items) via InvokePattern. |
| `void SetValue(AutomationElement element, string value)` | Sets an element's value via ValuePattern. |
| `string GetValue(AutomationElement element)` | Gets an element's value via ValuePattern. |
| `void Toggle(AutomationElement element)` | Toggles an element (e.g. a checkbox) via TogglePattern. |
| `bool IsToggled(AutomationElement element)` | Returns True if a toggleable element is currently On. |
| `void Expand(AutomationElement element)` | Expands an element (e.g. a combo box or tree node) via ExpandCollapsePattern. |
| `void Collapse(AutomationElement element)` | Collapses an element via ExpandCollapsePattern. |
| `void Select(AutomationElement element)` | Selects an element (e.g. a list item) via SelectionItemPattern. |
| `bool IsSelected(AutomationElement element)` | Returns True if a selectable element is currently selected. |

### Wait

| Method | Description |
|---|---|
| `bool WaitForElementByAutomationId(AutomationElement parent, string automationId, int timeoutMs, int pollIntervalMs, out AutomationElement element)` | Polls for a descendant element matching the given AutomationId until it appears or the timeout elapses. |
| `bool WaitForElementByName(AutomationElement parent, string name, bool exactMatch, int timeoutMs, int pollIntervalMs, out AutomationElement element)` | Polls for a descendant element matching the given Name until it appears or the timeout elapses. |

### Visual

| Method | Description |
|---|---|
| `void HighlightElement(AutomationElement element, int flashes = 3, int flashMs = 200, int lineWidth = 3, int colorRef = 0x0000FF)` | Flashes an inverting rectangle around an element to visually confirm which on-screen element it corresponds to. |

## Notes & Caveats

- **Action methods throw `InvalidOperationException` if the element doesn't
  support the required pattern** (e.g. calling `Toggle` on a plain button) -
  this is a caller mistake, not a normal/expected outcome, so it's a real
  exception rather than a sentinel return.
- **`IsElementAvailable` is the one method that accepts `null` without
  throwing** (returning `false` instead) - its entire purpose is checking
  whether a reference is still good, and a null reference is definitionally
  not available.
- **An element reference can go stale at any time** (the underlying UI
  closed, its control was removed, etc.) - any property/action method can
  throw `ElementNotAvailableException` for a stale element; re-find it
  (or check `IsElementAvailable` first) rather than caching a reference
  across a long-running automation step, the same caution `WindowUtils`
  already gives for raw window handles.
- **`FindByControlType`/`FindAllByControlType` only cover the 19 control
  types in `UiControlType`** - advanced/rare UIA patterns (Grid, Table,
  Scroll, Text range, MultipleView) are out of scope for this component.
- **This component cannot be exercised without a real Windows desktop
  session and a real target application** - unlike some other components
  here, there is no way to smoke-test any part of this on a non-Windows
  machine or without an actual UI to point it at.
