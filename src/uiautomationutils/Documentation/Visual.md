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
