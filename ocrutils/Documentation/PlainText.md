# Plain Text

## Read text from a screen region

```csharp
string text = ocr.GetTextFromRegion(left: 100, top: 200, width: 400, height: 80);
```

## Read text from a region in a specific language

```csharp
string text = ocr.GetTextFromRegion(100, 200, 400, 80, languageTag: "fr-FR");
```

## Read text from a saved screenshot

```csharp
string text = ocr.GetTextFromImageFile(@"C:\evidence\001_ConfirmationScreen.png");
```
