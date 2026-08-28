# Wait

## Wait for a dialog's OK button to appear, then click it

```csharp
bool found = uia.WaitForElementByAutomationId(dialogWindow, "OkButton", timeoutMs: 5000, pollIntervalMs: 200, out AutomationElement okButton);
if (found)
{
    uia.Invoke(okButton);
}
```

## Wait for a status label's text to change

```csharp
bool appeared = uia.WaitForElementByName(statusPane, "Complete", exactMatch: false, timeoutMs: 30000, pollIntervalMs: 500, out AutomationElement _);
```
