# Find

## Bridge a window handle from WindowUtils into UIA

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
AutomationElement settingsWindow = uia.FromWindowHandle(hWnd);
```

## Find a button by its AutomationId

```csharp
AutomationElement saveButton = uia.FindByAutomationId(settingsWindow, "SaveButton");
if (saveButton != null)
{
    uia.Invoke(saveButton);
}
```

## Find a control by name (substring match)

```csharp
AutomationElement darkModeToggle = uia.FindByName(settingsWindow, "Dark mode", exactMatch: false);
```

## Find every checkbox in a window

```csharp
List<AutomationElement> checkboxes = uia.FindAllByControlType(settingsWindow, UiControlType.CheckBox);
```

## Get the element under the mouse cursor

```csharp
AutomationElement hovered = uia.FromPoint(mouse.GetX(), mouse.GetY());
```
