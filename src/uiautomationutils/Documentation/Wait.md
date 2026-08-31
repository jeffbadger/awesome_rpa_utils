# Wait

Both `Simple`-suffixed methods return `bool` (found in time vs. not) with an
`out string message` — never throw. `message` is only set if a real argument
error aborted the poll early; it stays `null` for a genuine timeout.

## Wait for a dialog's OK button to appear, then click it

```csharp
bool found = uia.WaitForElementByAutomationIdSimple(dialogWindow, "OkButton", timeoutMs: 5000, pollIntervalMs: 200, out AutomationElement okButton, out _);
if (found)
{
    uia.Invoke(okButton, out _);
}
```

## Wait for a status label's text to change

```csharp
bool appeared = uia.WaitForElementByNameSimple(statusPane, "Complete", exactMatch: false, timeoutMs: 30000, pollIntervalMs: 500, out AutomationElement _, out _);
```

## Checking why a wait was aborted early

```csharp
if (!uia.WaitForElementByAutomationIdSimple(dialogWindow, "OkButton", timeoutMs: 5000, pollIntervalMs: 200, out AutomationElement okButton, out string message))
{
    if (message != null)
        throw new InvalidOperationException($"Wait aborted: {message}");
    Logger.Warn("OK button never appeared.");
}
```

## Branching on timeout vs. failure without a null-message test

The `timedOut`-output overloads (`WaitForElementByAutomationId`,
`WaitForElementByName`) report which case caused a `false` return directly,
for designers that would rather branch on a Boolean than test `message` for
`null`:

```csharp
bool found = uia.WaitForElementByAutomationId(dialogWindow, "OkButton",
    timeoutMs: 5000, pollIntervalMs: 200,
    out AutomationElement okButton, out bool timedOut, out string message);

if (!found)
{
    if (timedOut)
        Logger.Warn("OK button never appeared within 5s.");
    else
        Logger.Error($"Wait aborted: {message}");
}
```
