# Waiting for the clipboard

An automation that copies from another application has to know when the copy has *happened*.
A fixed sleep is either too short (the copy has not finished) or wastes time. The clipboard keeps
a **sequence number** that changes every time its contents change, so waiting is exact.

## Wait for a copy you caused

**Scenario:** press Ctrl+C in a legacy application, then read what it copied.

```csharp
// 1. Note the number BEFORE the step that changes the clipboard.
clipboard.GetClipboardSequenceNumber(out long before, out _);

// 2. The step that causes the copy.
keyboard.PressKeyWithModifiers(VirtualKey.C, ModifierKeys.Control, out _);

// 3. Wait for the number to move.
if (clipboard.WaitForClipboardChangeSince(before, timeoutMs: 5000, pollIntervalMs: 50,
        out bool timedOut, out string message))
{
    clipboard.GetClipboardText(out string copied, out _, out _);
}
else if (timedOut)
{
    // nothing was copied within 5 seconds (nothing selected? focus in the wrong place?)
}
else
{
    Logger.Error(message);   // an invalid argument, not a timeout
}
```

Reading the number first matters: `WaitForClipboardChangeSince` returns **at once** if the clipboard has
already changed since that number was read, so a fast application that copies before your wait begins
is not missed.

`WaitForClipboardChange(timeoutMs, pollIntervalMs, ...)` is the same wait measured from the moment of
the call. Use it only when nothing you do can change the clipboard before the call.

## Wait for a kind of content

```csharp
// Wait for a file list (after the user, or another process, copies files)
clipboard.WaitForClipboardFormat("CF_HDROP", 30000, 100, out bool timedOut, out _);

clipboard.WaitForClipboardFormat("HTML Format", 10000, 50, out timedOut, out _);   // a registered name
clipboard.WaitForClipboardFormat("DIB", 10000, 50, out timedOut, out _);           // an image
```

It checks first, so it returns at once if the format is already there. A format is named by its
`CF_` name (with or without the prefix, any case), by a number (`15` or `0x0F`), or by the name it was
registered under. `IsFormatAvailable` asks the same question once, without waiting, and
`GetFormatsJson` lists everything on the clipboard.

## Timeouts, and what "changed" means

- A wait that runs out returns `false` with `timedOut` **true** and **no message** - it is a normal outcome,
  not an error. A `false` with a message and `timedOut` false is a problem with the call.
- `timeoutMs` is 0 to 3,600,000 (an hour); 0 checks once. `pollIntervalMs` is 5 to 60,000.
- The sequence number changes on *any* change, including ones your own steps make, so measure from a
  number read just before the step you care about.
- Polling the sequence number needs no open clipboard, so waiting never interferes with the application
  that is copying.
