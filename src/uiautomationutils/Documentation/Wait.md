# Wait

Both methods return `bool` (found in time vs. not) with an `out string message`
— never throw. `message` is only set if a real argument error aborted the
poll early; it stays `null` for a genuine timeout.

## Wait for a dialog's OK button to appear, then click it

```csharp
bool found = uia.WaitForElementByAutomationId(dialogWindow, "OkButton", timeoutMs: 5000, pollIntervalMs: 200, out AutomationElement okButton, out _);
if (found)
{
    uia.Invoke(okButton, out _);
}
```

## Wait for a status label's text to change

```csharp
bool appeared = uia.WaitForElementByName(statusPane, "Complete", exactMatch: false, timeoutMs: 30000, pollIntervalMs: 500, out AutomationElement _, out _);
```

## Checking why a wait was aborted early

```csharp
if (!uia.WaitForElementByAutomationId(dialogWindow, "OkButton", timeoutMs: 5000, pollIntervalMs: 200, out AutomationElement okButton, out string message))
{
    if (message != null)
        throw new InvalidOperationException($"Wait aborted: {message}");
    Logger.Warn("OK button never appeared.");
}
```
