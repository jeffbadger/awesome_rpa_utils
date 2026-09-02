# Inspect

## Look before you extract

```csharp
az.ListArchiveContentsJson(intakeZipPath, out string json, out string message);

// Parse json (an array of ArchiveEntryInfo) with Pega's built-in JSON methods
// to inspect FullName/Length/CompressedLength/Crc32/IsEncrypted/IsDirectory
// for every entry before committing to an extraction.
```

## Quick summary without walking every entry yourself

```csharp
az.TryGetArchiveMetadata(intakeZipPath, out int entryCount, out long totalUncompressedBytes,
    out long totalCompressedBytes, out bool hasEncryptedEntries, out string message);

if (hasEncryptedEntries)
{
    // This intake can't be extracted at all - System.IO.Compression has no
    // password support. Route to manual handling instead.
}
```
