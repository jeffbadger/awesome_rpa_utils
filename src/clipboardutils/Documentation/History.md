# Clipboard history

`StartClipboardHistory` keeps the last N things copied to the clipboard, from a background thread of
its own, so a step that is busy (or blocked in a long wait) does not stop it noticing a copy.

There is **no default for the number of items**: `maxItems` is required (1 to 1000). Pick what you need;
each item costs memory (text only is small; `AllFormats` also keeps images and the data of every format).

## Starting it

```
StartClipboardHistory(maxItems: 20, message)
```

| Argument | Default | Meaning |
|---|---|---|
| `maxItems` | required, 1-1000 | How many items to keep. The oldest is dropped when a new one arrives. |
| `mode` | `TextOnly` | `TextOnly` keeps the text and any file list, and never asks the copying application for anything else. `AllFormats` keeps every format, so an item restores completely (an image comes back as an image). |
| `pollIntervalMs` | 250 | How often to look for a change (25 to 60,000). |
| `captureCurrent` | false | Also record what is on the clipboard right now. |

Starting twice is refused until `StopClipboardHistory`. Stopping keeps the items; `ClearClipboardHistory`
discards them. Counts (recorded, skipped, failed, last error) come from `GetClipboardHistoryStatusJson`.

## Reading it: item 0 is the newest

| To... | Use |
|---|---|
| Count the items | `GetClipboardHistoryCount` |
| List them (index, time, formats, text length, file count, size) | `GetClipboardHistoryJson(maxEntries, ...)` |
| Get one item's text | `GetClipboardHistoryText(index, out text, ...)` |
| Put one back on the clipboard | `RestoreClipboardHistoryItem(index)` |
| Remove one | `DiscardClipboardHistoryItem(index)` |

The list leaves the text out unless `includeText` is true (and then shortens it to 120 characters;
`GetClipboardHistoryText` returns all of it), so logging the list cannot leak what was copied.

## Searching it

`FindClipboardHistoryIndex(searchText, out index, ...)` returns the index of the newest item whose text or file
paths contain `searchText` (`-1` if none does, which is still a successful search). Case is ignored unless
`matchCase` is true. To find the next match, call it again with `startIndex` = the previous index + 1.

`SearchClipboardHistoryJson(searchText, out json, ...)` lists every match (up to `maxEntries`, default 50) in
the same shape as `GetClipboardHistoryJson`; each entry's `index` is its position in the whole history.

The search is a plain "contains", not a pattern. Indexes shift when a new item arrives, so use an index
straight after finding it.

### Example: put back the last thing copied that mentioned an invoice

```
FindClipboardHistoryIndex("invoice", index, message)
if index >= 0:
    RestoreClipboardHistoryItem(index, message)     // or GetClipboardHistoryText(index, text, ...)
```

## What is and is not recorded

- **Everything copied by someone else** (a person, or another application).
- **Never what this component puts on the clipboard itself**: `PasteText`, `SetClipboardText`, `ClearClipboard`,
  `RestoreClipboard`, `SetFileDropList*` and `RestoreClipboardHistoryItem` are not recorded. A copy made just
  before one of them is still recorded first, before it is overwritten.
- **Content that asks to be kept out of clipboard history** (`ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory` = 0, `CanUploadToCloudClipboard` = 0, which is what password managers set) is
  skipped without being read; `skippedExcluded` counts them.
- **The same thing copied twice in a row** is one item.
- Emptying the clipboard is not an item.
- A copy over `MaximumClipboardMegabytes` is skipped (`skippedTooLarge`), and the oldest items are dropped to
  keep the history under that limit (separately from saved snapshots).

## Cautions

- **A history holds whatever was copied, passwords included** (from software that does not mark them). Use
  `TextOnly` unless you need the other formats, call `ClearClipboardHistory` when done; disposing the
  component overwrites the memory it used.
- **It polls rather than being notified.** A copy replaced within one poll interval is missed; lower
  `pollIntervalMs` to catch faster changes.
- **`AllFormats` asks the copying application for every format.** That makes an application that copies lazily
  (Excel, most OLE sources) produce all of them at each copy, which can be slow for big copies. `TextOnly`
  does not.
- **Nothing is written to disk.** The history is gone when the component is disposed or the process ends.
