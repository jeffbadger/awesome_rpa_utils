# Actions

## Move a completed file into place

```csharp
fw.AtomicMoveFile(
    sourcePath: @"C:\Processing\report.tmp",
    destinationPath: @"C:\Output\report.pdf",
    overwrite: false,
    out string message);
```

Remember: this is only truly atomic when `sourcePath` and `destinationPath`
are on the same volume - a cross-volume move falls back to a non-atomic
copy-then-delete.

## Replace an existing file, keeping a backup

```csharp
// Requires destinationPath to already exist, unlike AtomicMoveFile.
// Windows-only behavior - throws PlatformNotSupportedException (reported
// as a failure, never an unhandled throw) on non-Windows.
fw.ReplaceFile(
    sourcePath: @"C:\Staging\prices-new.csv",
    destinationPath: @"C:\Live\prices.csv",
    backupPath: @"C:\Live\prices.csv.bak",
    out string message);
```

## Claim a work file for exclusive processing

The pattern for multiple robot instances pulling from the same inbox
without double-processing a file:

```csharp
bool claimed = fw.ClaimFile(
    sourcePath: @"C:\Inbox\order-4821.xml",
    inProgressDirectoryPath: @"C:\InProgress",
    out string claimedPath,
    out string message);

if (!claimed)
{
    // Either a real failure, or - just as likely - another robot instance
    // already claimed this exact file a moment earlier. Move on to the
    // next candidate rather than retrying this one.
    return;
}

// claimedPath now points at the file's new location under InProgress -
// process it there, then AtomicMoveFile it to a Done/Failed directory
// when finished.
```

`ClaimFile` never overwrites or auto-renames on a destination collision -
that collision is the whole point, since it's what makes two robot
instances racing for the same file safe.
