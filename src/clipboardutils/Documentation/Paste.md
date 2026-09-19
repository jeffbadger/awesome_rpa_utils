# Paste and text

## Paste text without destroying the clipboard

Some fields ignore typed keystrokes (IME-backed fields, rich editors, anything that mangles
`SendInput` characters), and pasting is the reliable alternative. But pasting means putting the text
on the clipboard, and that throws away whatever was there.

`PasteText` does the whole job and puts everything back:

```csharp
// The focus must already be in the field.
if (!clipboard.PasteText("Ünïcode text, 日本語, and a very long paragraph...", out string message))
{
    Logger.Warn($"Paste failed: {message}");
}
```

In order, it: saves **every format** on the clipboard, puts the text on the clipboard, sends
Ctrl+V, waits `postPasteDelayMilliseconds` (default 100) for the target to read it, and restores
what was there. If the keystroke fails the clipboard is still restored; if the restore fails the
message says so even though the paste worked; if both fail, both are reported.

Compare `KeyboardUtils.PasteText`, which restores only plain text - a screenshot, a copied file, or
formatted text on the clipboard is destroyed. Use this one whenever the clipboard might hold anything else.

### Options

```csharp
clipboard.PasteText(text, out message,
    postPasteDelayMilliseconds: 250,   // slower target: give it longer to read the clipboard (0 to 10000)
    excludeFromHistory: true,          // the default: keep the text out of clipboard history
    requireCompleteRestore: true);     // refuse if something on the clipboard could not be put back
```

- **`postPasteDelayMilliseconds`** - a target reads the clipboard at its own pace, so a delay that is too
  short can make it paste the *restored* content. Raise it for slow or remote targets.
- **`excludeFromHistory`** - marks the pasted text so Windows' clipboard history and cloud sync, and
  well-behaved clipboard monitors, do not record it. It is a request that those check for, not a guarantee.
- **`requireCompleteRestore`** - if part of the clipboard cannot be copied (a GDI-object format, or a
  format its owner would not render), the default is to paste anyway and put back what could be copied.
  With this set, `PasteText` refuses instead, **before touching the clipboard**, and names what would be lost.

`PasteText` does not find the field or take the focus - it sends Ctrl+V to whatever has it.
Put the focus in the field first (for example with WindowUtils or UIAutomationUtils).

### A secret is better typed

The text is on the system clipboard for the length of the paste, where any process watching the clipboard
can see it. For a password, type it (`KeyboardUtils.TypeText`) instead.

## Put text on the clipboard

```csharp
clipboard.SetClipboardText("value to hand to another application", out _);
clipboard.SetClipboardText(secret, out _, excludeFromHistory: true);
```

`SetClipboardText` **replaces** the clipboard with the text alone; every other format is discarded.
Save first if it must come back (see [Snapshots](Snapshots.md)).

## Read the clipboard's text

```csharp
if (clipboard.GetClipboardText(out string text, out bool textAvailable, out string message))
{
    if (textAvailable) { /* text may still be an empty string */ }
    else { /* the clipboard holds no text: an image, files, or nothing */ }
}
```

An empty clipboard, or one holding only an image, is a **success** with `textAvailable` false - not an
error. Unicode text is preferred; a clipboard with only ANSI text is read in the system's ANSI code page.

## Clear it

```csharp
clipboard.ClearClipboard(out _);
```
