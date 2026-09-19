# ClipboardAutomation

A Pega Robot Studio-ready component (`ClipboardUtils`) for the Windows clipboard:

- **Save and restore everything on it** - every format, not just text - so an automation
  can use the clipboard without destroying what the user (or an earlier step) put there.
- **Paste text and put the clipboard back**, all of it. `KeyboardUtils.PasteText` can only
  restore plain text; anything else (an image, files, formatted text) is gone once it runs.
- **Wait for the clipboard to change**, or for a format to appear, instead of sleeping and hoping.
- **Read and set a list of files**, the way Explorer's Copy and Cut do.
- **Keep a history of the last N things copied**, search it, and put an item back.

It uses the raw Win32 clipboard functions (no OLE), so it works from any thread, and nothing in
it needs a window or a message loop.

- Target framework: `net8.0-windows` and `net10.0-windows`
- Namespace: `ClipboardAutomation`
- Assembly: `ClipboardAutomation`

See the [Documentation](Documentation/README.md) folder for worked scenarios.

This component is fully standalone: it carries its own small `SendInput` code for the paste
keystroke rather than referencing [KeyboardUtils](../keyboardutils/README.md), like every
component in this repo.

## Enums

### `FileDropEffect`
What pasting a file list is asked to do, as the clipboard's `Preferred DropEffect` says:
`Copy` (1), `Move` (2, what Cut leaves), `Link` (4).

### `ClipboardHistoryMode`
What the history keeps of each copy: `TextOnly` (0, the default: text and file lists) or `AllFormats` (1: every
format, so an item restores completely).

## Constructors

| Constructor | Description |
|---|---|
| `ClipboardUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `ClipboardUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Properties

| Property | Type | Description |
|---|---|---|
| `MaximumClipboardMegabytes` | `int` | The most clipboard data one read will take, and that all saved snapshots together will hold (1 to 1024; default 128). A clipboard bigger than this is refused, never partly copied. |

## Methods

Every method returns `bool` (success) with an `out string message` explaining why on failure,
and never throws. A timeout in a wait is a normal outcome, not an error: it returns `false`
with `timedOut` set and no message.

### Snapshots

A snapshot is a named copy of everything on the clipboard, held in this component's memory.
See [Snapshots](Documentation/Snapshots.md).

| Method | Signature | Description |
|---|---|---|
| `SaveClipboard` | `bool SaveClipboard(string snapshotName, out string message, bool requireCompleteCopy = false)` | Keeps a copy of every format on the clipboard under a name. Does not change the clipboard. |
| `RestoreClipboard` | `bool RestoreClipboard(string snapshotName, out string message)` | Puts a saved copy back, replacing the clipboard's contents in every format, in the original order. The copy is kept. |
| `HasSnapshot` | `bool HasSnapshot(string snapshotName, out bool exists, out string message)` | Whether a copy with this name is being kept. |
| `DiscardSnapshot` | `bool DiscardSnapshot(string snapshotName, out string message)` | Discards a copy, overwriting its bytes in memory. |
| `ClearSnapshots` | `bool ClearSnapshots(out string message)` | Discards every copy. |
| `GetSnapshotInfoJson` | `bool GetSnapshotInfoJson(string snapshotName, out string infoJson, out string message)` | Describes a copy as JSON: its formats and sizes, and anything left out and why. |
| `ListSnapshotsJson` | `bool ListSnapshotsJson(out string snapshotsJson, out string message)` | Lists the copies, oldest first, as JSON. |

### Text and paste

| Method | Signature | Description |
|---|---|---|
| `PasteText` | `bool PasteText(string text, out string message, int postPasteDelayMilliseconds = 100, bool excludeFromHistory = true, bool requireCompleteRestore = false)` | Puts the text on the clipboard, sends Ctrl+V, then restores **everything** that was on the clipboard. See [Paste](Documentation/Paste.md). |
| `GetClipboardText` | `bool GetClipboardText(out string text, out bool textAvailable, out string message)` | Reads the clipboard's text. An empty clipboard is a success with `textAvailable` false. |
| `SetClipboardText` | `bool SetClipboardText(string text, out string message, bool excludeFromHistory = false)` | Replaces the clipboard with the text alone. Optionally keeps it out of clipboard history. |
| `ClearClipboard` | `bool ClearClipboard(out string message)` | Empties the clipboard. |

### Formats and change detection

| Method | Signature | Description |
|---|---|---|
| `GetClipboardSequenceNumber` | `bool GetClipboardSequenceNumber(out long sequenceNumber, out string message)` | The clipboard's sequence number, which changes whenever its contents change. |
| `GetFormatsJson` | `bool GetFormatsJson(out string formatsJson, out string message)` | The formats on the clipboard, in order, as JSON. Does not ask the clipboard's owner to produce any data. |
| `IsFormatAvailable` | `bool IsFormatAvailable(string formatName, out bool available, out string message)` | Whether a format (`CF_HDROP`, `DIB`, `15`, `HTML Format`...) is on the clipboard. |
| `WaitForClipboardChange` | `bool WaitForClipboardChange(int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Waits for the clipboard to change, measured from the call. |
| `WaitForClipboardChangeSince` | `bool WaitForClipboardChangeSince(long sequenceNumber, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Waits until the sequence number differs from one read earlier, so a change made before the wait began is not missed. |
| `WaitForClipboardFormat` | `bool WaitForClipboardFormat(string formatName, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Waits until a format is on the clipboard (returns at once if it already is). |

### File lists

See [File lists](Documentation/FileLists.md).

| Method | Signature | Description |
|---|---|---|
| `GetFileDropListJson` | `bool GetFileDropListJson(out string pathsJson, out int count, out FileDropEffect effect, out string message)` | The files on the clipboard as a JSON array, with the copy/move effect. No file list is a success with count 0. |
| `GetFileDropListText` | `bool GetFileDropListText(out string paths, out int count, out FileDropEffect effect, out string message)` | The same, one path per line. |
| `SetFileDropList` | `bool SetFileDropList(string paths, FileDropEffect effect, out string message, bool requireExisting = true)` | Puts a list of files (one path per line) on the clipboard for a copy or move paste. |
| `SetFileDropListJson` | `bool SetFileDropListJson(string pathsJson, FileDropEffect effect, out string message, bool requireExisting = true)` | The same, from a JSON array of paths. |

### History

Keeps the last N things copied, from a background thread. See [History](Documentation/History.md).
Item 0 is always the newest. `maxItems` has no default: it is required (1 to 1000).

| Method | Signature | Description |
|---|---|---|
| `StartClipboardHistory` | `bool StartClipboardHistory(int maxItems, out string message, ClipboardHistoryMode mode = TextOnly, int pollIntervalMs = 250, bool captureCurrent = false)` | Starts recording every new copy, keeping the last `maxItems`. |
| `StopClipboardHistory` | `bool StopClipboardHistory(out string message)` | Stops recording. The items are kept. |
| `GetClipboardHistoryCount` | `bool GetClipboardHistoryCount(out int count, out string message)` | How many items are kept. |
| `GetClipboardHistoryStatusJson` | `bool GetClipboardHistoryStatusJson(out string statusJson, out string message)` | Whether it is running, its settings, and counts (recorded, skipped, failed, last error). |
| `GetClipboardHistoryJson` | `bool GetClipboardHistoryJson(int maxEntries, out string historyJson, out string message, bool includeText = false)` | Lists the newest items as JSON. Text is left out unless asked for. |
| `GetClipboardHistoryText` | `bool GetClipboardHistoryText(int index, out string text, out bool textAvailable, out string message)` | The text of one item. |
| `FindClipboardHistoryIndex` | `bool FindClipboardHistoryIndex(string searchText, out int index, out string message, bool matchCase = false, int startIndex = 0)` | The index of the newest item whose text or file paths contain the text (-1 if none). |
| `SearchClipboardHistoryJson` | `bool SearchClipboardHistoryJson(string searchText, out string historyJson, out string message, bool matchCase = false, int maxEntries = 50, bool includeText = false)` | Lists every matching item as JSON, each with its index. |
| `RestoreClipboardHistoryItem` | `bool RestoreClipboardHistoryItem(int index, out string message, bool requireCompleteRestore = false)` | Puts an item back on the clipboard (complete in `AllFormats` mode unless a format could not be copied; optionally refuses such an item). Not recorded again. |
| `DiscardClipboardHistoryItem` | `bool DiscardClipboardHistoryItem(int index, out string message)` | Removes one item, overwriting the bytes it kept (its text and file paths are dropped, not overwritten; see the notes). |
| `ClearClipboardHistory` | `bool ClearClipboardHistory(out string message)` | Removes every item. |

## Notes & Caveats

- **Setting anything replaces the clipboard.** `SetClipboardText` and `SetFileDropList` discard every
  other format (that is how the Windows clipboard works). Use `SaveClipboard` first, or `PasteText`,
  if the content must come back.
- **A snapshot is byte for byte.** Verified against the real clipboard, with a text, HTML, RTF, image,
  file-list and custom-format clipboard: after save, destroy and restore, every copied format had
  identical bytes, in the same order. Formats registered by name are put back by name.
- **What a snapshot leaves out.** Three kinds of format are not copied, and `GetSnapshotInfoJson` lists
  each with the reason:
  - `CF_BITMAP` / `CF_PALETTE` / `CF_METAFILEPICT` hold GDI handles rather than data. Windows rebuilds
    the usual ones from the DIB or enhanced metafile that *is* copied, so this is not a loss - a reader
    that prefers `CF_BITMAP` is handed a bitmap Windows builds from the DIB.
  - OLE's own bookkeeping formats (`DataObject`, `Ole Private Data`, `OleClipboardPersistOnFlush`)
    describe the setting application's live OLE object, which is stale once the clipboard changes.
    Their data is copied as the real formats; the bookkeeping is not, and that is not a loss.
  - Anything else that cannot be copied (a GDI-object format, or one the owner would not render) **is**
    a loss. `requireCompleteCopy` (on `SaveClipboard`) and `requireCompleteRestore` (on `PasteText`) make
    that a refusal instead.
- **Formats that render on demand are rendered at save time.** An application that copies "lazily"
  (Excel, most OLE sources) produces the data when it is read, so a save of a large lazy copy can take a
  moment, and it needs that application to be running and answering.
- **A clipboard owner that stops answering cannot hang the automation.** Every operation that reads
  the clipboard gives up after 20 seconds and reports it; the abandoned operation finishes on its own if the
  owner ever answers, and until it has, other clipboard operations are refused ("an earlier clipboard operation
  that timed out is still running") so they cannot race it. In practice Windows itself (its clipboard-history
  service) often reads a newly set
  format first and is then stuck holding the clipboard open, so the more common result is that opening the
  clipboard fails quickly with "another application is holding it". Both are reported, neither hangs.
- **`PasteText` sends a keystroke to whatever has the focus.** It does not find the field or take the
  focus; put the focus where the text should go first. The text is on the system clipboard for the length
  of the paste, so for a secret prefer typing it (`KeyboardUtils.TypeText`).
- **`excludeFromHistory` is a request, not a guarantee.** It marks the content with the formats Windows'
  clipboard history and cloud sync, and well-behaved clipboard monitors, check. Software that ignores them
  can still read the clipboard.
- **Saved snapshots can contain secrets.** They hold whatever was on the clipboard. `DiscardSnapshot`
  and `ClearSnapshots` overwrite the bytes in memory; disposing the component does the same for all of them.
- **A wait reads the sequence number, not the content.** Any change counts, including one your own steps
  make. To wait for a change caused by a step, read `GetClipboardSequenceNumber` *before* the step and
  use `WaitForClipboardChangeSince`.
- **The clipboard is machine-wide state.** Two automations, or an automation and a person, using it at
  once will interfere, exactly as with any clipboard use.
- **Clipboard history holds whatever was copied.** That can include passwords from software that does not mark
  them. Content marked "exclude from history" (what password managers set) is skipped unread - and if such a
  marker is there but cannot be read, or is not a readable non-zero number, the copy is skipped too, never
  kept - and what this component itself puts on the clipboard is never recorded. It is kept in memory only.
  A copy replaced within one poll interval is missed.
- **Known limits of the memory wipe.** Discarding, clearing and disposing overwrite the *bytes* the component
  copied (a snapshot's formats, and an all-formats history item's formats). A history item's **text and file
  paths are .NET strings, which cannot be overwritten in place**: they are dropped, and stay in memory until the
  garbage collector reuses it. The text you read out of the component (`GetClipboardText`,
  `GetClipboardHistoryText`, the JSON) is a string in your own automation too. If a secret must not linger,
  keep it out of the history (`excludeFromHistory`, or do not run the history around it) and prefer typing it.
- **A timed-out operation blocks the next ones, by design.** When the clipboard's owner does not answer, the
  operation is abandoned after 20 seconds and further clipboard operations are refused until the abandoned one
  has finished, so it can never race them. If the owner never answers, that lasts until it is closed. A snapshot
  or history item that is discarded while an abandoned restore is still reading it is the one case not
  guarded; the abandoned restore can then put back blanked data.
- **A write that lands late is not recorded, and can hide a copy made in that instant.** If the component's own
  write is abandoned by a timeout and lands afterwards, the history skips it. A person's copy made in the very
  same moment as that late write can be skipped with it.
- **An unreadable file effect means Copy.** If the clipboard's `Preferred DropEffect` cannot be read, the file
  list is reported as a Copy, the safe choice (a wrong Move would delete the source files).
- **Empty formats come back one byte long.** A format that was on the clipboard with no data is put back as a
  single zero byte, because the clipboard cannot be handed a zero-length block.
- **Lowering `MaximumClipboardMegabytes` does not evict.** Snapshots already held stay; the new limit applies
  to the next save, and a save that would go over it is refused.
