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

## Set and read a text field by AutomationId without a separate find step

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.SetValueByAutomationId(hWnd, "UsernameField", "jbadger", out string message);
uia.GetValueByAutomationId(hWnd, "UsernameField", out string current, out message);
```

## Checking why a one-shot call failed

```csharp
if (!uia.InvokeByAutomationId(hWnd, "SaveButton", out string message))
{
    Logger.Warn($"Invoke failed: {message}");
}
```
