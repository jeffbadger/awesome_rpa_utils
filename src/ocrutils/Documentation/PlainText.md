# Plain Text

Both methods return `bool` (success) with an `out string text` and
`out string message` — never throws, including for bad dimensions, a missing
image file, or a missing OCR language pack.

## Read text from a screen region

```csharp
ocr.GetTextFromRegion(left: 100, top: 200, width: 400, height: 80, out string text, out _);
```

## Read text from a region in a specific language

```csharp
ocr.GetTextFromRegion(100, 200, 400, 80, out string text, out _, languageTag: "fr-FR");
```

## Read text from a saved screenshot

```csharp
if (!ocr.GetTextFromImageFile(@"C:\evidence\001_ConfirmationScreen.png", out string text, out string message))
{
    Logger.Warn($"OCR failed: {message}");
}
```
