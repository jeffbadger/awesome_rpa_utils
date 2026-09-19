# ClipboardUtils Pega usability review

`ClipboardUtils` exists because `KeyboardUtils.PasteText` can only restore plain text: it must empty
the clipboard to set its own text, so an image, a copied file or formatted text on the clipboard is
destroyed. Robot Studio cannot hold a clipboard's contents in a variable (they are binary and
multi-format), so the component keeps them itself, under names, and the API only ever passes names,
strings and numbers.

## Findings applied

- **Every port is scalar.** Snapshots are addressed by a name string; nothing binary crosses the API.
  Results that are collections (`GetFormatsJson`, `GetSnapshotInfoJson`, `ListSnapshotsJson`,
  `GetFileDropListJson`) come as JSON, and file lists also come and go as newline-separated text, so no
  collection proxy is needed. Every method has a unique name, returns `bool`, initializes its `out`
  values and puts them after the inputs, so the
  [Signature Uniqueness Standard](../coding-standards/signature-uniqueness-standard.md) holds.
- **Timeouts are a normal outcome.** Waits return `false` with `timedOut` set and no message, so a step
  can branch on a timeout without treating it as a failure; an invalid argument is `false` with a message
  and `timedOut` false.
- **Empty is not an error.** An empty clipboard is a valid snapshot; no text or no file list is a success
  with `textAvailable` false or `count` 0. Polling loops and "is there anything to paste?" branches do not
  have to handle exceptions.
- **The race between "cause a copy" and "wait for it" is closed by the API.** `GetClipboardSequenceNumber`
  then `WaitForClipboardChangeSince` needs no window, message loop or event, and returns at once if the
  copy already happened.
- **The dangerous defaults are opt-in or refusable.** Something that cannot be put back is reported per
  format in `GetSnapshotInfoJson`, and `requireCompleteCopy` / `requireCompleteRestore` turn it into a
  refusal that leaves the clipboard untouched.
- **Designer property with a visible default.** `MaximumClipboardMegabytes` (default 128) is set in the
  Property Grid, like `StackUtils.MaximumItems`.

## Decisions

1. **Decision: a standalone component with its own `PasteText`, not a change to `KeyboardUtils`.** The
   repository forbids project references between components. `ClipboardUtils.PasteText` carries its own
   small `SendInput` code and does the whole save, paste, restore, and `KeyboardUtils.PasteText` is left as
   it is (its description now points here). The alternative - copying the snapshot engine into
   `KeyboardUtils` - would have duplicated roughly 250 lines to protect callers who never asked for it.
2. **Decision: raw Win32 clipboard, no OLE.** It needs no STA thread, window or message loop and cannot be
   left holding an OLE object. The cost: formats that are pure GDI handles cannot be copied as bytes.
   `CF_BITMAP`, `CF_PALETTE` and `CF_METAFILEPICT` are skipped as not a loss when Windows rebuilds them
   from the DIB or enhanced metafile that is copied; anything else is reported as a loss.
3. **Decision: OLE's bookkeeping formats are not restored.** `DataObject`, `Ole Private Data` and
   `OleClipboardPersistOnFlush` describe the *setting application's live OLE object*. Once the clipboard
   has changed they are stale, and restoring them would leave readers pointing at an object that is gone or
   serving something other than what was saved. The data they wrap is copied as its real formats.
4. **Decision: every read runs on a thread the caller can walk away from, for 20 seconds.** A format that
   its owner renders on demand asks that application, and a hung owner would otherwise hang the automation.
   The abandoned read finishes (and closes the clipboard) on its own if the owner ever answers. Verified:
   in practice Windows' clipboard-history service reads a newly set format first and is then blocked
   holding the clipboard open, so the usual symptom is a fast "another application is holding it" failure.
5. **Decision: snapshots are in memory only, capped, and wiped.** They may hold secrets. Discarding,
   replacing and disposing overwrite the bytes; the cap (`MaximumClipboardMegabytes`) covers all snapshots
   together, so a step cannot quietly hold gigabytes.
6. **Decision: no event when the clipboard changes.** Robot Studio's support for component events is
   unproven and a listener needs a window on its own thread; polling the sequence number is exact, cheap,
   needs no open clipboard, and works from a blocked step.

## Accepted caveats

A snapshot restores every copied format byte for byte (verified against the real clipboard), but a reader
that asks for `CF_BITMAP` is handed a bitmap Windows builds from the DIB. `excludeFromHistory` is a request
that clipboard history, cloud sync and well-behaved monitors check; other software can still read the
clipboard. `PasteText` sends a keystroke to whatever has the focus and cannot find the field. The clipboard
is machine-wide state, so another process changing it during a save or paste is not prevented.
