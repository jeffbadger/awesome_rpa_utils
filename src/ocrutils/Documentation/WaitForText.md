# Wait for Text

`WaitForTextToAppear` returns `bool` (found in time vs. not) with an
`out string message` — never throws. `message` is only set if a real failure
(bad dimensions, missing OCR language pack, negative timeout) aborted the poll
early; it stays `null` for a genuine timeout.

## Wait for a confirmation message before continuing

```csharp
bool appeared = ocr.WaitForTextToAppear(
    left: 400, top: 300, width: 600, height: 100,
    expectedText: "Submitted successfully",
    timeoutMs: 10000, pollIntervalMs: 500,
    out string message);

if (!appeared)
{
    if (message != null)
        throw new InvalidOperationException($"OCR poll aborted: {message}");
    throw new InvalidOperationException("Confirmation message never appeared.");
}
```
