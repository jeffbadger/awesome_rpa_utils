# Clipboard-Paste Fallback

> **⚠ Destructive:** `PasteText` permanently destroys any non-text clipboard content
> (an image, files) — see Restore behavior below. Snapshot it yourself first if the
> automation might run while the clipboard holds something other than text.

## Paste text into a field that mangles synthetic keystrokes

```csharp
// Useful for IME-backed fields or apps that read raw input directly instead
// of standard keyboard messages.
if (!keyboard.PasteText("some-value-that-typed-badly", out string message))
{
    Console.WriteLine($"Paste failed: {message}");
}
```

Restore behavior depends on what the clipboard held before the paste:

- **Empty** — restored empty.
- **Plain text** — the original text is restored afterward.
- **Anything else (an image, files)** — destroyed when the paste text is set and cannot
  be restored; the clipboard is left holding the pasted text. If you rely on the
  clipboard holding an image or file list, snapshot it before calling `PasteText`.

`PasteText` never throws, including if restoring the original clipboard afterward itself
fails. Two more caveats:

- **Security**: the pasted text briefly sits on the system clipboard, where any process
  monitoring the clipboard can observe it. Prefer `TypeText` for sensitive values.
- **Pace**: paste delivery is asynchronous at the target's pace. `PasteText` waits
  `postPasteDelayMilliseconds` (default 50 ms) after sending Ctrl+V before restoring the
  original clipboard; if a target reads the clipboard slowly it can race the restore and
  receive the *original* text — raise the delay for such targets:

```csharp
// Give a slow target 250 ms to fetch the clipboard before the original is restored.
if (!keyboard.PasteText("some-value", out string message, 250))
{
    Console.WriteLine($"Paste failed: {message}");
}
```

See the main [README](../README.md)'s Notes & Caveats section for details.
