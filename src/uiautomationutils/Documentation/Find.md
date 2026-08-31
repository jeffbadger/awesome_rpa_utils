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

## List all the child elements of a window (or any element)

`GetChildren` returns every immediate child of an element - combined with
`FromWindowHandle`, this finds a top-level window and lists everything
directly inside it:

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
AutomationElement settingsWindow = uia.FromWindowHandle(hWnd);
uia.GetChildren(settingsWindow, out List<AutomationElement> children, out _);
```

`UIAutomationUtils - One-Shot` category methods do this in a single call - see
[OneShot.md](OneShot.md).

## Loop over a found list by scalar index instead of a collection proxy

`GetElementCount`/`GetElementAt` work on any `List<AutomationElement>` from
`GetChildren` or `FindAllByControlType`, for designers who would rather drive
a counted loop than iterate a collection proxy directly:

```csharp
uia.GetChildren(settingsWindow, out List<AutomationElement> children, out _);
uia.GetElementCount(children, out int count, out _);
for (int i = 0; i < count; i++)
{
    uia.GetElementAt(children, i, out AutomationElement child, out _);
    uia.GetName(child, out string name, out _);
    Logger.Info(name);
}
```

## List all the child elements as JSON, without an `AutomationElement` proxy

`GetChildrenSummaryJson` returns each child's Name, AutomationId, ClassName,
control type, and bounds as a single JSON string - useful when a designer
cannot construct an `AutomationElement`/collection proxy at all:

```csharp
uia.GetChildrenSummaryJson(settingsWindow, out string json, out _);
Logger.Info(json); // [{"name":"...","automationId":"...","className":"...","controlType":"Button","bounds":{"left":0,"top":0,"width":0,"height":0}}]
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
