# Metadata

## Read a file's size and timestamps as scalars

```csharp
fw.TryGetFileMetadata(path, out long sizeBytes, out string lastWriteUtcIso8601, out string createdUtcIso8601, out string message);
```

## Read a file or directory's full metadata as JSON

```csharp
// Includes IsDirectory/Extension, which TryGetFileMetadata omits.
fw.GetFileMetadataJson(path, out string json, out string message);
```

## List every matching file in a directory, with metadata

Pairs naturally with `WaitForFileMatchingPattern` - once you know a
directory has a match, list everything to decide which one(s) to act on:

```csharp
fw.GetDirectoryListingJson(
    directoryPath: @"C:\Inbox",
    searchPattern: "*.csv",
    includeSubdirectories: false,
    out string json,
    out string message);

// json is a JSON array of objects shaped like:
// { "FullPath": "...", "Name": "...", "Extension": ".csv",
//   "SizeBytes": 1024, "IsDirectory": false,
//   "CreatedUtcIso8601": "...", "LastWriteUtcIso8601": "..." }
```
