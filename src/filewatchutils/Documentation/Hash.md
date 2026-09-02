# Hash

## Detect whether an input file has changed since last run

```csharp
fw.ComputeFileHashSha256(inputPath, out string currentHash, out string message);

if (currentHash == previouslyStoredHash)
{
    // Unchanged since last run - safe to skip reprocessing.
}
```

## Detect duplicate files before processing

```csharp
bool identical = false;
fw.AreFilesIdenticalByHash(candidatePath, alreadyProcessedPath, out identical, out string message);

if (identical)
{
    // Same content - skip, this is a duplicate delivery.
}
```

## Use a specific algorithm for legacy-system interop

```csharp
// algorithmName is one of "SHA256", "SHA1", "MD5", "SHA384", "SHA512"
// (case-insensitive). Prefer ComputeFileHashSha256 unless something else
// specifically requires a different algorithm.
fw.ComputeFileHash(path, "MD5", out string hashHex, out string message);
```
