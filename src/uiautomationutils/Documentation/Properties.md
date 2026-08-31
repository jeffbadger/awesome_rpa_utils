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

For designers without a `Rectangle` proxy, the scalar overload returns the
same bounds as `left`/`top`/`width`/`height`:

```csharp
uia.GetBoundingRectangle(element, out int left, out int top, out int width, out int height, out _);
mouse.MoveTo(left + width / 2, top + height / 2, out _);
```

## Check enabled/offscreen state without a null-message test

`IsEnabled`/`IsOffscreen` have a `querySucceeded`-output overload that makes
the existing null-message convention explicit as a Boolean:

```csharp
if (!uia.IsEnabled(element, out bool querySucceeded, out string message))
{
    if (!querySucceeded)
        Logger.Error($"Could not check whether the element is enabled: {message}");
    // else: querySucceeded is true and the element is genuinely disabled.
}
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
