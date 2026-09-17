# ArchiveAutomation

A Pega Robot Studio-ready component (`ArchiveUtils`) for handling ZIP
archives when intake arrives as ZIP files. Like every component in this
suite, it honors the never-throws contract: invalid input and runtime
failures return `False` with a descriptive message instead of throwing.

- Target framework: `net8.0-windows` / `net10.0-windows`
- Namespace: `ArchiveAutomation`
- Assembly: `ArchiveAutomation`

See the [Documentation](Documentation/README.md) folder for real-world usage
examples of every method.

**The security validation is the actual value of this component over
calling `System.IO.Compression` directly.** Verified empirically during
design: `ZipFile.ExtractToDirectory` already blocks zip-slip/path-traversal
attempts, but the per-entry extraction API used by `ExtractSingleFile`/
`ExtractFirstMatchingFile` does **not** — every extraction method in this
component routes through a shared safety guard before writing a single
byte. .NET also performs **zero automatic CRC validation on read** — a
corrupted entry otherwise reads back silently with no error — so
`ValidateArchiveCrc`/`Json` compute CRC-32 independently (hand-rolled, no
new dependency) rather than trusting the archive's own declared checksum.

Like `FileWatchUtils`, every method except `CreateEncryptedArchive`/
`ExtractArchiveWithPassword` is plain cross-platform BCL
(`System.IO.Compression`, `System.IO`) with zero P/Invoke — the `-windows`
target framework is kept only for suite consistency. Those two methods
depend on `ICSharpCode.SharpZipLib` (also cross-platform), the only
dependency in this component, added because `System.IO.Compression` cannot
write or read encrypted ZIP entries under any circumstance. Its `.Tests`
project gets real, full functional coverage on Linux (real archives, real
corrupted bytes, real path-traversal attempts, real password-protected
archives), not just guard-clause coverage.

## Types

### `ArchiveEntryInfo`
The JSON shape produced by `ListArchiveContentsJson`/`ValidateArchiveCrcJson`:
`FullName`, `Length`, `CompressedLength`, `CompressionRatio`,
`LastWriteUtcIso8601`, `Crc32`, `IsEncrypted`, `IsDirectory`.

## Constructors

| Constructor | Description |
|---|---|
| `ArchiveUtils()` | Empty constructor required so Pega Robot Studio can create the component. |
| `ArchiveUtils(IContainer container)` | Standard designer constructor; attaches the component to a container. |

## Methods

### Create & Publish

| Method | Signature | Description |
|---|---|---|
| `CreateArchive` | `bool CreateArchive(string sourceDirectoryPath, string archivePath, bool overwrite, bool includeBaseDirectory, bool normalizeTimestamps, string normalizedTimestampUtcIso8601, out string message)` | Creates a ZIP archive from a directory, built to a temp file and published atomically only once complete - never a partially-written archive at the final path. |

### Create Encrypted

| Method | Signature | Description |
|---|---|---|
| `CreateEncryptedArchive` | `bool CreateEncryptedArchive(string sourceDirectoryPath, string archivePath, string password, bool overwrite, bool includeBaseDirectory, bool useLegacyZipCrypto, out string message)` | Creates a password-protected ZIP archive from a directory, via `ICSharpCode.SharpZipLib`. AES-256 by default; `useLegacyZipCrypto: true` opts into the older, weaker ZipCrypto scheme for compatibility with tools that can't read AES-encrypted zips. |

### Extract

| Method | Signature | Description |
|---|---|---|
| `ExtractArchive` | `bool ExtractArchive(string archivePath, string destinationDirectoryPath, bool overwrite, long maxTotalExpandedSizeBytes, double maxCompressionRatio, bool preserveTimestamps, out string message)` | Extracts a ZIP archive to a directory. Scans declared entry sizes/ratios first and fails closed - extracting nothing - if either limit would be exceeded. `0`/negative means no limit. |
| `ExtractArchiveSimple` | `bool ExtractArchiveSimple(string archivePath, string destinationDirectoryPath, bool overwrite, out string message)` | Same, defaulting to a 1 GiB total-size limit and a 100:1 compression-ratio limit. |

### Extract — Single File

| Method | Signature | Description |
|---|---|---|
| `ExtractSingleFile` | `bool ExtractSingleFile(string archivePath, string entryFullName, string destinationDirectoryPath, bool overwrite, long maxExpandedSizeBytes, double maxCompressionRatio, out string message)` | Extracts one entry by its exact name. Guarded against path traversal - the per-entry extraction API has no built-in protection, unlike `ExtractArchive`. |
| `ExtractFirstMatchingFile` | `bool ExtractFirstMatchingFile(string archivePath, string entryNamePattern, string destinationDirectoryPath, bool overwrite, long maxExpandedSizeBytes, double maxCompressionRatio, out string matchedEntryName, out string message)` | Extracts the first entry matching a glob pattern (e.g. `"*.csv"`) instead of an exact name. Same guards as `ExtractSingleFile`. |

### Extract Encrypted

| Method | Signature | Description |
|---|---|---|
| `ExtractArchiveWithPassword` | `bool ExtractArchiveWithPassword(string archivePath, string destinationDirectoryPath, string password, bool overwrite, long maxTotalExpandedSizeBytes, double maxCompressionRatio, out string message)` | Extracts a password-protected ZIP archive, via `ICSharpCode.SharpZipLib`. The only extraction method able to open an encrypted entry; same zip-slip guard and declared-size/ratio pre-check as `ExtractArchive`. |

### Inspect

| Method | Signature | Description |
|---|---|---|
| `ListArchiveContentsJson` | `bool ListArchiveContentsJson(string archivePath, out string json, out string message)` | Lists every entry as a JSON array, without extracting anything - the pre-flight method for inspecting `IsEncrypted`/`Crc32`/sizes before committing to extraction. |
| `TryGetArchiveMetadata` | `bool TryGetArchiveMetadata(string archivePath, out int entryCount, out long totalUncompressedBytes, out long totalCompressedBytes, out bool hasEncryptedEntries, out string message)` | Gets summary metadata: entry count, total declared sizes, and whether any entry is encrypted. |

### Validate

| Method | Signature | Description |
|---|---|---|
| `ValidateArchiveCrc` | `bool ValidateArchiveCrc(string archivePath, out bool allEntriesValid, out string message)` | Verifies every non-encrypted entry's actual content against its declared CRC-32. |
| `ValidateArchiveCrcJson` | `bool ValidateArchiveCrcJson(string archivePath, out string json, out string message)` | Same, reporting a JSON array with each entry's declared/computed CRC-32 and status (`"Valid"`, `"Mismatch"`, or `"SkippedEncrypted"`). |
| `HasEncryptedEntries` | `bool HasEncryptedEntries(string archivePath, out bool hasEncryptedEntries, out string message)` | Fast scan for any encrypted entry, stopping at the first one found. Detection only. |

### Diagnostic Bundles

| Method | Signature | Description |
|---|---|---|
| `CreateDiagnosticBundle` | `bool CreateDiagnosticBundle(string sourceFilePathsCsv, string outputArchivePath, bool overwrite, string manifestText, out string message)` | Zips a list of files into a diagnostic/failure bundle, with an optional `manifest.txt` entry, published atomically like `CreateArchive`. |
| `CreateDiagnosticBundleSimple` | `bool CreateDiagnosticBundleSimple(string sourceFilePathsCsv, string outputDirectoryPath, out string createdArchivePath, out string message)` | Same, auto-naming the archive with a timestamp (`diagnostic-bundle-yyyyMMdd-HHmmss.zip`) and returning its resolved path. |

### Update Existing Archive

| Method | Signature | Description |
|---|---|---|
| `AddOrReplaceFilesInArchive` | `bool AddOrReplaceFilesInArchive(string archivePath, string sourceFilePathsCsv, string entryNamesCsv, out string message)` | Adds files to an existing archive, replacing any existing entry of the same name. `entryNamesCsv` is an optional parallel CSV giving each file's exact archive path/name; blank falls back to the file's own name at the archive root. Built on the same copy-then-atomically-publish discipline as `CreateArchive`. |
| `RemoveArchiveEntry` | `bool RemoveArchiveEntry(string archivePath, string entryFullName, out string message)` | Removes one named entry from an existing archive. |
| `RenameArchiveEntry` | `bool RenameArchiveEntry(string archivePath, string entryFullName, string newEntryName, out string message)` | Renames one entry in an existing archive, preserving its content and timestamp. Fails, without modifying the archive, if the target name is already taken. |

### Merge

| Method | Signature | Description |
|---|---|---|
| `MergeArchives` | `bool MergeArchives(string firstArchivePath, string secondArchivePath, string outputArchivePath, bool overwrite, out string message)` | Builds a new archive from every entry in two existing archives, without modifying either input. A name collision is disambiguated with a numeric suffix, the same convention `CreateDiagnosticBundle` uses. |

## Notes & Caveats

- **Never throws.** Invalid input and runtime failures return `False` with
  a descriptive message.
- **Zip-slip protection is built into every extraction method, not a
  separate opt-in call.** `ZipFile.ExtractToDirectory` (used by
  `ExtractArchive`) already rejects a traversal attempt on its own; the
  per-entry extraction API (used by `ExtractSingleFile`/
  `ExtractFirstMatchingFile`) does not, so those two methods route through
  a shared internal guard before writing a single byte - this was verified
  empirically during design, not assumed.
- **Zip-bomb defense trusts declared metadata.** Size/ratio limits are
  checked against the archive's own declared `Length`/`CompressedLength`
  before extracting anything - the standard practical mitigation, not a
  cryptographic guarantee against a maliciously hand-crafted header lying
  about its own declared size.
- **CRC-32 is computed independently, never trusted from the archive.**
  .NET performs no automatic CRC check on read - a corrupted entry
  otherwise reads back silently with no error. `ValidateArchiveCrc`/`Json`
  decompress and hash each entry themselves (streamed, never a full entry
  buffered into memory) and compare against the declared checksum.
- **Encrypted entries are detection-only via `System.IO.Compression`,
  but not via this component as a whole.** `IsEncrypted`/
  `HasEncryptedEntries` report whether an entry is password-protected using
  `System.IO.Compression`, which cannot decrypt or extract one under any
  circumstance. `CreateEncryptedArchive`/`ExtractArchiveWithPassword` are
  the exception - backed by `ICSharpCode.SharpZipLib` instead, they can
  create and open a password-protected archive. `ValidateArchiveCrcJson`
  still never opens an encrypted entry; it's reported as
  `"SkippedEncrypted"`, never attempted or treated as a mismatch.
- **`CreateEncryptedArchive` defaults to AES-256**, the modern, strong
  choice; `useLegacyZipCrypto: true` opts into the older ZipCrypto scheme
  only for compatibility with tools that can't read AES-encrypted zips.
- **`RenameArchiveEntry` recompresses the entry** (always
  `CompressionLevel.Optimal`) since `ZipArchiveEntry` exposes no way to
  read back its original compression level for a byte-for-byte raw copy -
  content is identical, only physical size may shift slightly.
- **`AddOrReplaceFilesInArchive` always replaces a same-named entry** -
  there's no separate "add" vs. "replace" mode. A fallback entry name
  (when `entryNamesCsv` is blank) is disambiguated with a numeric suffix
  only against other newly-added files in the same call, never against a
  pre-existing entry, since replacing a same-named existing entry is the
  intended behavior.
- **`MergeArchives` never modifies either input** - it always builds a
  third, new archive (which may reuse one input's own path), built
  atomically like `CreateArchive`.
- **The ZIP/DOS timestamp format has no time-zone field.** Only the
  wall-clock date/time round-trips through an entry's `LastWriteTime` -
  reading it back reattaches whatever machine's *local* offset, not the
  offset originally supplied. `CreateArchive`'s "preserve" mode therefore
  timestamps entries using each source file's own local last-write time
  (not UTC) to match this format's actual semantics; supported range is
  1980-01-01 through 2107-12-31, with 2-second resolution - an out-of-range
  timestamp is a guarded, specific failure rather than a leaked exception.
- **`ExtractArchive`'s `preserveTimestamps`** controls only the
  *extracted-file* last-write time (`false` sets it to "now"); it's
  independent from `CreateArchive`'s timestamp handling.
- **Directory entries may be implicit.** Some ZIP writers omit explicit
  directory entries entirely - `IsDirectory` in `ArchiveEntryInfo` reflects
  only entries actually present in the archive, not directories implied by
  nested file paths.
- **`CreateDiagnosticBundle` disambiguates duplicate file names** with a
  numeric suffix (`log.txt`, `log_2.txt`, ...) rather than overwriting one
  source file's entry with another's.
- **No `Simple`-suffix signature collisions exist in this component** -
  every method pair that shares a similar shape differs in arity
  (`ExtractArchive`/`ExtractArchiveSimple`,
  `CreateDiagnosticBundle`/`Simple`), and every same-shaped-input pair uses
  distinct names instead of an ambiguous overload
  (`ValidateArchiveCrc`/`ValidateArchiveCrcJson`,
  `TryGetArchiveMetadata`/`ListArchiveContentsJson`) - a genuine, notable
  difference from every other component in this suite. See
  `project-docs/pega-usability-reviews/ArchiveUtils-pega-usability-review.md`.
- **Guard tests.** `ArchiveUtils.Tests` (in this folder) covers not just
  guard clauses but real functional behavior - actual archives, an actual
  reproduced zip-slip attempt, an actual corrupted-CRC entry, and actual
  zip-bomb-style size/ratio fixtures. It runs on Linux too - see
  `TESTING.md` at the repo root.
