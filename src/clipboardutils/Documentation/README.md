# ClipboardUtils Documentation

Worked examples for the clipboard component. Every method returns `bool` with an
`out string message` and never throws; the examples check `message` only where it helps.

| Page | What it covers |
|---|---|
| [Snapshots](Snapshots.md) | Keeping a copy of everything on the clipboard and putting it back; what a snapshot can and cannot hold; limits; secrets. |
| [Paste](Paste.md) | Pasting text without destroying the clipboard; putting text on the clipboard; keeping it out of clipboard history. |
| [Waiting](Waiting.md) | Waiting for a copy to happen: the sequence number, `WaitForClipboardChangeSince`, waiting for a format. |
| [File lists](FileLists.md) | Reading what Explorer copied; putting files on the clipboard to be pasted (copy or move). |
| [History](History.md) | Keeping the last N things copied, reading and searching them, putting one back; what is skipped; cautions. |

See the component [README](../README.md) for the full method list and caveats.
