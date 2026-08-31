# Visual

`HighlightElement` returns `bool` (success) with an `out string message` —
never throws, including for a null element.

## Confirm which on-screen control a found element corresponds to

```csharp
if (uia.FindByAutomationId(window, "SubmitButton", out AutomationElement element, out _))
{
    uia.HighlightElement(element, out _);
}
```

## Choose a highlight color without hexadecimal colorRef entry

`HighlightElement` also has an RGB-component overload and a
`System.Drawing.Color` overload, alongside the original `colorRef` overload:

```csharp
uia.HighlightElement(element, red: 0, green: 255, blue: 0, out _);       // RGB components
uia.HighlightElement(element, System.Drawing.Color.Lime, out _);        // named/system color
```
