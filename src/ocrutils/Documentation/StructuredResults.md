# Structured Results

Both methods return `bool` (success) with an `out string message` — never
throws, including for bad dimensions or a missing OCR language pack.

## Get positioned lines and words

```csharp
if (ocr.GetStructuredTextFromRegion(0, 0, 1920, 1080, out OcrResult result, out _))
{
    foreach (OcrLine line in result.Lines)
    {
        Console.WriteLine($"{line.Text} @ {line.Bounds}");
    }
}
```

## Find where a specific label sits on screen, then click it

`FindTextLocation` returns `true` only when a match is found; `false` covers
both "not found" and a real failure — check `out string message` to tell them
apart (it's `null` for a normal not-found).

```csharp
if (ocr.FindTextLocation("Submit", 0, 0, 1920, 1080, out System.Drawing.Rectangle location, out string message))
{
    int centerX = location.X + location.Width / 2;
    int centerY = location.Y + location.Height / 2;
    mouse.ClickAt(centerX, centerY, MouseButton.Left, out _);
}
else if (message != null)
{
    Logger.Warn($"OCR search failed: {message}");
}
```
