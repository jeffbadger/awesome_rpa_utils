# Wait for Text

## Wait for a confirmation message before continuing

```csharp
bool appeared = ocr.WaitForTextToAppear(
    left: 400, top: 300, width: 600, height: 100,
    expectedText: "Submitted successfully",
    timeoutMs: 10000, pollIntervalMs: 500);

if (!appeared)
{
    throw new InvalidOperationException("Confirmation message never appeared.");
}
```
