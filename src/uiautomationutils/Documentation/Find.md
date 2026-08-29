# Find

`FindByAutomationId`/`FindByName`/`FindByClassName`/`FindByControlType`/
`FindAllByControlType`/`GetChildren` return `bool` with an `out` result and
`out string message` — never throw. `FindBy*`'s `bool` means "found";
`false` covers both a normal not-found (`message == null`) and a real
argument error (`message` set) — check `message` to tell them apart.

## Bridge a window handle from WindowUtils into UIA

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
AutomationElement settingsWindow = uia.FromWindowHandle(hWnd);
```

## Find a button by its AutomationId

```csharp
if (uia.FindByAutomationId(settingsWindow, "SaveButton", out AutomationElement saveButton, out _))
{
    uia.Invoke(saveButton, out _);
}
```

## Find a control by name (substring match)

```csharp
uia.FindByName(settingsWindow, "Dark mode", out AutomationElement darkModeToggle, out _, exactMatch: false);
```

## Find every checkbox in a window

```csharp
uia.FindAllByControlType(settingsWindow, UiControlType.CheckBox, out List<AutomationElement> checkboxes, out _);
```

## Get the element under the mouse cursor

```csharp
mouse.GetX(out int x, out _);
mouse.GetY(out int y, out _);
AutomationElement hovered = uia.FromPoint(x, y);
```

## Checking why a search failed

```csharp
if (!uia.FindByAutomationId(settingsWindow, "SaveButton", out AutomationElement saveButton, out string message))
{
    if (message != null)
        Logger.Warn($"Search failed: {message}");
    // else: genuinely not found
}
```
