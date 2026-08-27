# Clipboard-Paste Fallback

## Paste text into a field that mangles synthetic keystrokes

```csharp
// Useful for IME-backed fields or apps that read raw input directly instead
// of standard keyboard messages.
keyboard.PasteText("some-value-that-typed-badly");
```

The original clipboard contents are restored automatically afterward, even
if the paste itself throws.
