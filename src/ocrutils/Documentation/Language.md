# Language

## Check whether a language is available before requesting it

```csharp
var available = ocr.GetAvailableLanguages();
if (available.Contains("de-DE"))
{
    ocr.GetTextFromRegion(0, 0, 800, 200, out string text, out _, languageTag: "de-DE");
}
```
