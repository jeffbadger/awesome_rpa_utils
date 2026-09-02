# CreateExtract

## Zip a completed output directory

```csharp
az.CreateArchive(outputDirectory, archivePath, overwrite: false, includeBaseDirectory: false,
    normalizeTimestamps: false, normalizedTimestampUtcIso8601: null, out string message);
```

The archive is built to a temp file and only renamed into place once fully
written - a reader can never see a partially-written archive at
`archivePath`.

## Build a reproducible archive (same bytes every run)

```csharp
az.CreateArchive(sourceDirectory, archivePath, overwrite: true, includeBaseDirectory: false,
    normalizeTimestamps: true, normalizedTimestampUtcIso8601: "1980-01-01T00:00:00Z", out string message);
```

Every entry gets the same fixed timestamp instead of each file's own
last-write time, so re-running the same build produces byte-identical
output.

## Extract intake, protected against an oversized/malicious archive

```csharp
az.ExtractArchive(intakeZipPath, workingDirectory, overwrite: false,
    maxTotalExpandedSizeBytes: 500_000_000, maxCompressionRatio: 100.0,
    preserveTimestamps: true, out string message);

if (message != null)
{
    // Rejected - message names the offending entry and which limit it broke.
    // Nothing was extracted.
}
```

For the common case, `ExtractArchiveSimple` applies a 1 GiB / 100:1 default
without spelling out the limits explicitly:

```csharp
az.ExtractArchiveSimple(intakeZipPath, workingDirectory, overwrite: false, out string message);
```

## Pull one known file out of an archive without extracting everything

```csharp
az.ExtractSingleFile(intakeZipPath, "invoices/2024-Q4.csv", workingDirectory,
    overwrite: false, maxExpandedSizeBytes: 50_000_000, maxCompressionRatio: 100.0, out string message);
```

`entryFullName` is the entry's exact path within the archive, as reported
by `ListArchiveContentsJson`. This is the security-critical extraction path
- unlike `ExtractArchive`, it must independently guard against a malicious
entry name trying to write outside `workingDirectory`, and it does, on
every call.

## Pull the first file matching a pattern (name unknown in advance)

```csharp
az.ExtractFirstMatchingFile(intakeZipPath, "*.csv", workingDirectory, overwrite: false,
    maxExpandedSizeBytes: 50_000_000, maxCompressionRatio: 100.0,
    out string matchedEntryName, out string message);

// matchedEntryName tells you which entry was actually extracted.
```

