# Structured Results

## Get positioned lines and words

```csharp
OcrResult result = ocr.GetStructuredTextFromRegion(0, 0, 1920, 1080);
foreach (OcrLine line in result.Lines)
{
    Console.WriteLine($"{line.Text} @ {line.Bounds}");
}
```

## Find where a specific label sits on screen, then click it

```csharp
System.Drawing.Rectangle location = ocr.FindTextLocation("Submit", 0, 0, 1920, 1080);
if (location != System.Drawing.Rectangle.Empty)
{
    int centerX = location.X + location.Width / 2;
    int centerY = location.Y + location.Height / 2;
    mouse.ClickAt(centerX, centerY, MouseButton.Left);
}
```
