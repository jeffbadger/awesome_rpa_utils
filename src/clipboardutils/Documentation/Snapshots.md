# Snapshots

The Windows clipboard holds one thing at a time, in as many formats as the application that
copied it chose to offer: plain text, formatted text, an image, a list of files, and often
private formats only that application understands. Setting new content throws all of it away.

A **snapshot** is a named copy of everything on the clipboard, kept in this component's memory,
that you can put back later.

## Use the clipboard and give it back

**Scenario:** An automation needs the clipboard to move a value between two applications, but
the person running it may have something valuable on the clipboard (a screenshot they are about
to paste into an email).

```csharp
if (!clipboard.SaveClipboard("before", out string message))
{
    Logger.Warn($"Did not touch the clipboard: {message}");
    return;
}

clipboard.SetClipboardText(orderNumber, out _);
// ... a step that pastes it, or copies something else ...

clipboard.RestoreClipboard("before", out message);   // the screenshot is back, in every format
clipboard.DiscardSnapshot("before", out _);          // and the saved copy is overwritten
```

`SaveClipboard` does not change the clipboard. `RestoreClipboard` replaces it with the copy, put
back format by format in the order the original application offered them, so a program that
picks the "richest" format sees the same choice. The copy is kept until you discard it, so it
can be restored more than once.

An **empty clipboard is a valid snapshot**: restoring it empties the clipboard.

## See what a snapshot holds

```csharp
clipboard.GetSnapshotInfoJson("before", out string json, out _);
// {"name":"before","capturedUtc":"2026-09-19T12:30:13.19Z","formatCount":5,"totalBytes":133,"complete":true,
//  "formats":[{"id":13,"name":"CF_UNICODETEXT","bytes":62},{"id":50066,"name":"Lazy Custom","bytes":5}, ...],
//  "skipped":[{"id":49161,"name":"DataObject","reason":"It describes ... live OLE data object ...","loss":false}]}

clipboard.ListSnapshotsJson(out string all, out _);   // every copy, oldest first, without the format detail
clipboard.HasSnapshot("before", out bool exists, out _);
```

`complete` is `false` only when something on the clipboard could not be copied **and will not come
back** (`"loss": true` in `skipped`).

## What is, and is not, copied

Everything the clipboard hands over as data is copied byte for byte: text in every encoding, HTML,
RTF, DIB and DIBV5 images, PNG, file lists, enhanced metafiles, and every private or registered
format. Formats registered by name are put back by name, because their numbers are not fixed.

Three things are deliberately left out:

| Left out | Why | A loss? |
|---|---|---|
| `CF_BITMAP`, `CF_PALETTE`, `CF_METAFILEPICT` | They hold GDI handles, not data. Windows rebuilds them from the DIB or metafile that is copied. | No, when that source format is present. |
| `DataObject`, `Ole Private Data`, `OleClipboardPersistOnFlush` | OLE bookkeeping that points at the copying application's live object, which is stale once the clipboard changes. The data itself is copied as its real formats. | No. |
| Any other GDI-object format, or a format the owner would not render | Cannot be copied. | **Yes.** |

To refuse rather than carry on when something would be lost:

```csharp
if (!clipboard.SaveClipboard("before", out string message, requireCompleteCopy: true))
{
    // message names what would not come back; nothing was kept, and the clipboard is untouched
}
```

## Limits

- **`MaximumClipboardMegabytes`** (default 128, at most 1024) is the most one read will take *and* the most
  all snapshots together will hold. A clipboard over the limit is refused with the name of the format that
  did not fit, never partly copied. Saving under an existing name replaces it, so its own bytes do not
  count twice. Set the property in the designer or in a step before saving.
- **Names**: not empty, at most 64 characters, unique ignoring case.
- **A snapshot is memory only.** It does not survive the automation ending, and is not shared between
  component instances.

## Secrets

A snapshot holds whatever was on the clipboard, which can be a password copied from a password manager.

- `DiscardSnapshot` and `ClearSnapshots` overwrite the copied bytes in memory. So does disposing the
  component, and so does saving over an existing name.
- Discard a snapshot as soon as you have restored it.

## When the clipboard cannot be read

Applications that copy "lazily" produce their data only when it is asked for, so saving asks them.
Every read gives up after 20 seconds and reports it rather than hanging the automation; if another
program is holding the clipboard open you may see "the clipboard could not be opened; another
application is holding it" straight away. Either way the clipboard is left as it was, and a
`SaveClipboard` that fails keeps any earlier snapshot of that name intact.
