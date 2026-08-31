# Language

## Check whether a language is available before requesting it

```csharp
var available = ocr.GetAvailableLanguages();
if (available.Contains("de-DE"))
{
    ocr.GetTextFromRegion(0, 0, 800, 200, out string text, out _, languageTag: "de-DE");
}
```

`GetAvailableLanguages` never throws — it returns an empty list if the language list
can't be queried. Use `TryGetAvailableLanguages(out List<string> tags, out string message)`
when you need to distinguish "no languages installed" from a real failure.

## For designers without a `List<string>` proxy

`GetAvailableLanguagesDelimited` returns the same tags joined into one string:

```csharp
if (ocr.GetAvailableLanguagesDelimited(out string tags, out string message))
{
    Logger.Info($"Installed OCR languages: {tags}"); // e.g. "en-US,de-DE,fr-FR"
}
```

Pass `delimiter` to use something other than the default comma, e.g. `delimiter: "; "`.
