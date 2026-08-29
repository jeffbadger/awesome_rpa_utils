# Properties

`GetName`/`GetAutomationId`/`GetClassName`/`GetControlTypeName`/
`GetBoundingRectangle`/`IsEnabled`/`IsOffscreen` return `bool` with an `out`
result and `out string message` — never throw. `IsEnabled`/`IsOffscreen` keep
their `bool` as the actual property value; `message` is only set for a real
error (null element). `IsElementAvailable` is unchanged.

## Read a control's name and type before deciding what to do with it

```csharp
uia.GetName(element, out string name, out _);
uia.GetControlTypeName(element, out string type, out _);
if (type == "Button" && uia.IsEnabled(element, out _))
{
    uia.Invoke(element, out _);
}
```

## Get an element's on-screen position

```csharp
uia.GetBoundingRectangle(element, out Rectangle bounds, out _);
mouse.MoveTo(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, out _);
```

## Check whether a cached element reference is still good

```csharp
if (!uia.IsElementAvailable(cachedElement))
{
    // The underlying UI closed or changed - re-find it instead of using
    // the stale reference.
    uia.FindByAutomationId(parentWindow, "SomeControl", out cachedElement, out _);
}
```

## Checking why a property read failed

```csharp
if (!uia.GetName(element, out string name, out string message))
{
    Logger.Warn($"Could not read Name: {message}");
}
```
