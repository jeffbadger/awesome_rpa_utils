# Clipboard-Paste Fallback

## Paste text into a field that mangles synthetic keystrokes

```csharp
// Useful for IME-backed fields or apps that read raw input directly instead
// of standard keyboard messages.
keyboard.PasteText("some-value-that-typed-badly");
```

The original clipboard contents are restored automatically afterward when they were plain text (or the clipboard was empty). If the clipboard held something else, such as an image, that content can't be restored — the pasted text is left in place instead. See the main [README](../README.md)'s Notes & Caveats section for details.
