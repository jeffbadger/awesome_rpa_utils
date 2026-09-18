# File Dialogs (Open / Save As)

Upload and download flows end at an Open or Save As dialog, and until now
`DialogUtils` could only click its buttons - it could not type the file name.
`SubmitFileDialog` does the whole job in one call; `SetFileDialogPath` and
`SelectFileDialogFileType` are the pieces when you need control over the steps.

All three return `bool` (success) with an `out string message` explaining why on
failure - none of them throw. They work on a dialog in another application.

## Save a file: type the path and confirm

```csharp
// "Save As" appeared after the automation clicked Save in the target app
dialog.WaitForDialog("Save As", 10000, 100, out IntPtr hDialog, exactMatch: true);

if (!dialog.SubmitFileDialog(hDialog, @"C:\Reports\claims-2026-09.csv", out string message))
{
    Logger.Warn($"Save As did not complete: {message}");
}
```

`SubmitFileDialog` types the path into the File name box, clicks the dialog's
Open/Save button, and waits for the dialog to close (5 seconds by default; pass
`closeTimeoutMs` for a slower network path, up to 5 minutes). A full path also
changes the dialog's folder, so there is no need to navigate first.

A `true` return means the dialog closed after being confirmed. It does not prove
the file exists or was written, and a dialog closed with Cancel would look the
same - `SubmitFileDialog` only ever clicks Open/Save.

## When the dialog stays open

Windows sometimes keeps the dialog open after Open/Save is clicked, with a second
message box in front of it:

- **Save As over an existing file** asks *"... already exists. Do you want to replace
  it?"*
- **Open of a file that does not exist** reports that it cannot be found.

Either way `SubmitFileDialog` returns `false` and the message says the dialog is
still open. Answer the message box by its **button text**:

```csharp
if (!dialog.SubmitFileDialog(hDialog, path, out string message, closeTimeoutMs: 2000))
{
    // Overwrite prompt? It is a separate window in the same process.
    if (dialog.WaitForDialog("Confirm Save As", 3000, 50, out IntPtr hConfirm, exactMatch: true))
    {
        dialog.ClickDialogButtonByText(hConfirm, "Yes", out _, out _);   // replace the file
        dialog.WaitForDialogToClose(hDialog, 5000, 50);
    }
}
```

Use `ClickDialogButtonByText`, not `ClickDialogButtonById(...DialogButton.Yes)`: these
boxes are laid out by DirectUI and every button in them has control ID 0, so an ID
lookup finds nothing. Their titles and button text are also in the language of the
operating system, so a non-English machine needs the localized words.

For an error box, click its `OK` the same way, then either correct the path or click
the file dialog's Cancel (`ClickDialogButtonById(hDialog, (int)DialogButton.Cancel, ...)`
does work on the file dialog itself).

## Choose the file type first

A Save As dialog gives a name typed *without* an extension the extension of the
selected type, so pick the type before submitting:

```csharp
dialog.SelectFileDialogFileType(hDialog, "*.csv", out _, exactMatch: false);   // first type containing "*.csv"
dialog.SubmitFileDialog(hDialog, @"C:\Reports\claims", out _);                 // saved as claims.csv
```

`exactMatch: true` (the default) needs the entry's whole text, such as
`CSV (*.csv)`; `exactMatch: false` chooses the first entry that contains the text,
ignoring case. If nothing matches, the message lists the entries that are there:

```text
The combo box has no item matching 'Nonexistent'. Items: 'Text (*.txt)', 'CSV (*.csv)', 'All files (*.*)'.
```

## Type the path without confirming

Use `SetFileDialogPath` when something must happen between typing and confirming,
or when the confirming click is a different button:

```csharp
dialog.SetFileDialogPath(hDialog, @"C:\Inbox\statement.pdf", out _);
// ...
dialog.ClickDialogButtonById(hDialog, (int)DialogButton.Ok, out _);   // Open/Save is always control ID 1
```

A dialog that allows several selections takes each name in quotes, separated by
spaces:

```csharp
dialog.SetFileDialogPath(hDialog, "\"a.pdf\" \"b.pdf\" \"c.pdf\"", out _);
```

## How the File name box is found

The File name box has one control ID in some dialogs and no ID at all (it sits inside
a DirectUI host) in others, and every Open/Save dialog also contains an Explorer
address bar and a search box, which are edit boxes too. So it is found by structure,
and the address bar and search box are skipped.

These methods refuse a dialog that does not look like an Open/Save dialog - one with
no folder view - rather than type into whatever edit box comes first. A Font dialog,
for instance, has combo boxes with edit boxes in them, and a path typed into its
font-name box would be a silent misfire. If your dialog is not recognized, inspect it
and drive it by handle:

```csharp
foreach (var c in dialog.ListDialogControls(hDialog))
    Console.WriteLine($"0x{c.Handle.ToInt64():X}  id={c.Id}  {c.ClassName}  '{c.Text}'");

dialog.SetControlText(editHandle, @"C:\file.txt", out _);   // see Set Values
```

Not tested: folder pickers (Browse For Folder). Not reachable: the Windows 11 Notepad-style XAML
dialogs, which have no controls of their own for `DialogUtils` to reach - see the
README's note on `canDismiss`.
