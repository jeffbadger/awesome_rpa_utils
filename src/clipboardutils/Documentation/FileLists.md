# File lists

When you select files in Explorer and press Copy or Cut, the clipboard receives a **file list**
(`CF_HDROP`) and a note of whether a paste should copy or move them. These methods read and set that.

## Read what Explorer (or another application) copied

```csharp
if (clipboard.GetFileDropListJson(out string json, out int count, out FileDropEffect effect, out string message))
{
    if (count == 0) { /* the clipboard holds no file list */ }
    else { /* json: ["C:\\Reports\\a.csv","C:\\Reports\\b.csv"]; effect: Copy or Move */ }
}

clipboard.GetFileDropListText(out string paths, out count, out effect, out _);   // one path per line
```

No file list is a **success** with `count` 0. The effect is what a paste is asked to do: **Move** after Cut, **Copy**
after Copy, and Copy when the clipboard does not say (which is what a paste does). Combine with
`WaitForClipboardFormat("CF_HDROP", ...)` to wait for the copy first.

## Put files on the clipboard for a paste

**Scenario:** an upload dialog in a legacy application accepts files pasted into it, or Explorer should
copy a report into a network share.

```csharp
clipboard.SetFileDropList(
    @"C:\Reports\claims-2026-09.csv" + "\r\n" + @"C:\Reports\claims-2026-09.pdf",
    FileDropEffect.Copy, out string message);
// ...focus the target folder or window and paste (Ctrl+V, or KeyboardUtils)...
```

`SetFileDropListJson` takes a JSON array of path strings instead, for a list held in a JSON value:
`["C:\\Reports\\a.csv","C:\\Reports\\b.csv"]`. It must be an array of strings; anything else is refused.

- **`FileDropEffect.Copy`** - a paste copies the files. **`Move`** - a paste moves them, as after Cut
  (the files stay where they are until something pastes). **`Link`** - a paste makes shortcuts.
- The paths may be files or folders, one per line (either line break; blank lines and surrounding
  quotes are ignored). Relative paths are made absolute.
- **`requireExisting`** (default `true`) refuses a path that does not exist, since pasting it would fail
  later and confusingly; the message lists up to five missing paths. Pass `false` to put paths on the
  clipboard for files that will exist by the time something pastes them.
- Wildcards, invalid characters, an empty list, and more than 100,000 paths are refused.
- Everything else on the clipboard is discarded (`SetFileDropList` replaces the clipboard). Nothing is copied,
  moved or deleted by the call itself.

Verified against real Explorer: with a Copy list, pasting into a folder copies the files and leaves the
originals; with a Move list, pasting moves them.

## Restoring a file list

A snapshot ([Snapshots](Snapshots.md)) holds a file list like any other format, including its Copy/Move effect,
so saving and restoring the clipboard around your own use of it brings back what the user had copied.
