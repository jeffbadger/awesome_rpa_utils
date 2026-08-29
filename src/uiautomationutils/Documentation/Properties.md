# Properties

## Read a control's name and type before deciding what to do with it

```csharp
string name = uia.GetName(element);
string type = uia.GetControlTypeName(element);
if (type == "Button" && uia.IsEnabled(element))
{
    uia.Invoke(element);
}
```

## Get an element's on-screen position

```csharp
Rectangle bounds = uia.GetBoundingRectangle(element);
mouse.MoveTo(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, out _);
```

## Check whether a cached element reference is still good

```csharp
if (!uia.IsElementAvailable(cachedElement))
{
    // The underlying UI closed or changed - re-find it instead of using
    // the stale reference.
    cachedElement = uia.FindByAutomationId(parentWindow, "SomeControl");
}
```
