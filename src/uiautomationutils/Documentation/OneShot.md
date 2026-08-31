# One-Shot (Window-Handle-Scoped)

These methods take a window handle directly and do the
`FromWindowHandle` → find/act step internally, so a Pega automation does not
need to retain an intermediate `AutomationElement` proxy between steps for
the common single-step case. They complement, not replace, the composable
`Find`/`Properties`/`Actions` API - reach for the composable API when you
need to search more than once against the same element, or need the element
itself for a follow-up call these one-shots don't cover.

Every method here returns `bool` with an `out string message` - never
throws, including for a zero/invalid window handle.

## Find a top-level window and list all its child elements, in one call

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.GetChildrenFromWindowHandle(hWnd, out List<AutomationElement> children, out string message);
```

For designers without an `AutomationElement` collection proxy, the JSON
overload returns the same children summarized as a string:

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
if (uia.GetChildrenSummaryJsonFromWindowHandle(hWnd, out string json, out string message))
{
    Logger.Info(json); // [{"name":"...","automationId":"...","className":"...","controlType":"...","bounds":{...}}]
}
```

## Click a button by AutomationId without a separate find step

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.InvokeByAutomationId(hWnd, "SaveButton", out string message);
```

For designers without an AutomationId to hand — only the button's visible
text — `InvokeByName` does the same thing, matched by `Name` instead:

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.InvokeByName(hWnd, "Save", out string message);
```

## Set and read a text field by AutomationId without a separate find step

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.SetValueByAutomationId(hWnd, "UsernameField", "jbadger", out string message);
uia.GetValueByAutomationId(hWnd, "UsernameField", out string current, out message);
```

The `ByName` overloads match by visible name instead, for the same reason
as `InvokeByName` above:

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.SetValueByName(hWnd, "Username", "jbadger", out string message);
uia.GetValueByName(hWnd, "Username", out string current, out message);
```

## Select an item in a combo box, list box, or tree by name, in one call

`SelectListItemByName` finds the container by name, expands it if the
control needs that before its items are reachable, then finds and selects
the item within it by name:

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.SelectListItemByName(hWnd, "Country", "United States", out string message);
```

Equivalent to `FromWindowHandle` → `FindByName` (container) → `Expand` →
`FindByName` (item) → `Select` via the composable API — see
[Actions](Actions.md) for those individual methods if you need to act on the
container or item again afterward instead of just selecting once.

## Check/uncheck a checkbox by name without a separate find step

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.ToggleByName(hWnd, "Remember me", out string message);
```

## Switch to a tab or pick a radio button by name, in one call

`SelectByName` selects a target found directly by its own name, without
needing a separate container name first - use it instead of
`SelectListItemByName` when the target (a tab, a radio button, a list item
you can already name uniquely) doesn't need a container lookup:

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.SelectByName(hWnd, "Advanced", out string message); // switches to the "Advanced" tab
```

## Check a checkbox/tab/button's state by name, in one call

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
bool isChecked = uia.IsToggledByName(hWnd, "Remember me", out string message);
bool isOnAdvancedTab = uia.IsSelectedByName(hWnd, "Advanced", out message);
bool canClickSave = uia.IsEnabledByName(hWnd, "Save", out message);
```

## Checking why a one-shot call failed

```csharp
if (!uia.InvokeByAutomationId(hWnd, "SaveButton", out string message))
{
    Logger.Warn($"Invoke failed: {message}");
}
```
