using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Linq;
using System.Text.Json;

namespace ArchiveAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component for handling ZIP archives when intake arrives as
    /// ZIP files: creating/extracting archives, listing contents before extraction,
    /// extracting a single matching entry, validating CRC-32 checksums, detecting
    /// encrypted entries, and building diagnostic/failure bundles.
    /// <para>
    /// Like every component in this suite, all methods honor the never-throws contract:
    /// invalid input and runtime failures return <c>false</c> with a descriptive message
    /// instead of throwing.
    /// </para>
    /// <para>
    /// Unlike a plain wrapper around <see cref="ZipFile"/>, every extraction path in this
    /// component is hardened against zip-slip/path-traversal (verified empirically during
    /// design: the per-entry extraction API has no such protection on its own, unlike
    /// <see cref="ZipFile.ExtractToDirectory(string, string)"/>) and zip-bomb-style
    /// excessive expansion, and CRC-32 validation is computed independently rather than
    /// trusted from the archive's own declared metadata - .NET performs no automatic CRC
    /// check on read. This is the actual value of this component over calling
    /// <see cref="System.IO.Compression"/> directly.
    /// </para>
    /// </summary>
    [Description("Creates, extracts, inspects, and validates ZIP archives, with zip-slip and zip-bomb protection built in. " +
                 "All methods return True/False with a failure message instead of throwing. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class ArchiveUtils : Component
    {
        private const long DefaultMaxTotalExpandedSizeBytes = 1_073_741_824L; // 1 GiB
        private const double DefaultMaxCompressionRatio = 100.0;

        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public ArchiveUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public ArchiveUtils(IContainer container)
        {
            container?.Add(this);
        }

        #region Create & Publish

        /// <summary>
        /// Creates a ZIP archive from a directory's contents, built to a temp file and
        /// published to <paramref name="archivePath"/> only once complete (a same-volume
        /// <see cref="File.Move(string, string, bool)"/>, never a partially-written
        /// archive at the final path). Entries for subdirectories with no descendant files
        /// are included explicitly, so an empty directory tree round-trips through
        /// extraction. Never throws.
        /// </summary>
        /// <param name="sourceDirectoryPath">The directory to archive, recursively.</param>
        /// <param name="archivePath">The archive's final path.</param>
        /// <param name="overwrite">Whether an existing file at <paramref name="archivePath"/> may be replaced.</param>
        /// <param name="includeBaseDirectory">Whether entries are prefixed with <paramref name="sourceDirectoryPath"/>'s own directory name.</param>
        /// <param name="normalizeTimestamps"><c>false</c> (default/preserve) keeps each entry's real source-file last-write time; <c>true</c> sets every entry to <paramref name="normalizedTimestampUtcIso8601"/>.</param>
        /// <param name="normalizedTimestampUtcIso8601">Only used when <paramref name="normalizeTimestamps"/> is <c>true</c>. Empty/null defaults to the ZIP format's earliest representable date, 1980-01-01T00:00:00Z.</param>
        /// <param name="message"><c>null</c> on success; a failure reason otherwise, including an out-of-range timestamp.</param>
        [Category("Archive - Create")]
        [Description("Creates a ZIP archive from a directory, published atomically only once fully built. Never throws.")]
        public bool CreateArchive(string sourceDirectoryPath, string archivePath, bool overwrite, bool includeBaseDirectory, bool normalizeTimestamps, string normalizedTimestampUtcIso8601, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDirectoryPath))
                {
                    message = "A source directory path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    message = "An archive path is required.";
                    return false;
                }
                if (!Directory.Exists(sourceDirectoryPath))
                {
                    message = $"Source directory '{sourceDirectoryPath}' does not exist.";
                    return false;
                }
                if (!overwrite && File.Exists(archivePath))
                {
                    message = $"Archive '{archivePath}' already exists.";
                    return false;
                }

                DateTimeOffset normalizedTimestamp = ArchiveCore.MinZipTimestamp;
                if (normalizeTimestamps && !string.IsNullOrWhiteSpace(normalizedTimestampUtcIso8601))
                {
                    if (!DateTimeOffset.TryParse(normalizedTimestampUtcIso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out normalizedTimestamp))
                    {
                        message = $"'{normalizedTimestampUtcIso8601}' is not a valid ISO-8601 date/time.";
                        return false;
                    }
                }

                string tempPath = ArchiveCore.MakeTempSiblingPath(archivePath);
                if (!ArchiveCore.TryBuildArchiveFromDirectory(sourceDirectoryPath, tempPath, includeBaseDirectory, normalizeTimestamps, normalizedTimestamp, out message))
                {
                    TryDeleteBestEffort(tempPath);
                    return false;
                }

                return ArchiveCore.TryPublishAtomically(tempPath, archivePath, overwrite, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CreateArchive", ex);
                return false;
            }
        }

        #endregion

        #region Extract

        /// <summary>
        /// Extracts a ZIP archive to a directory. Before extracting anything, scans every
        /// entry's declared size/compression-ratio metadata and fails closed if either
        /// limit would be exceeded - <see cref="ZipFile.ExtractToDirectory(string, string, bool)"/>
        /// has no such awareness on its own. Relies on that same method's built-in
        /// zip-slip protection for the actual extraction. Never throws.
        /// </summary>
        /// <param name="archivePath">The archive to extract.</param>
        /// <param name="destinationDirectoryPath">The directory to extract into. Created if it doesn't already exist.</param>
        /// <param name="overwrite">Whether existing files at the destination may be replaced.</param>
        /// <param name="maxTotalExpandedSizeBytes">The maximum total declared uncompressed size across all entries. <c>0</c> or negative means no limit.</param>
        /// <param name="maxCompressionRatio">The maximum declared uncompressed:compressed ratio for any single entry. <c>0</c> or negative means no limit.</param>
        /// <param name="preserveTimestamps"><c>true</c> (default) keeps each extracted file's stored entry timestamp; <c>false</c> sets every extracted file to the current time.</param>
        /// <param name="message"><c>null</c> on success; a failure reason otherwise, naming the offending entry and limit when a zip-bomb check fails. Nothing is extracted when this check fails.</param>
        [Category("Archive - Extract")]
        [Description("Extracts a ZIP archive to a directory, rejecting it up front if declared sizes/ratios exceed the given limits. Never throws.")]
        public bool ExtractArchive(string archivePath, string destinationDirectoryPath, bool overwrite, long maxTotalExpandedSizeBytes, double maxCompressionRatio, bool preserveTimestamps, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    message = "An archive path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
                {
                    message = "A destination directory path is required.";
                    return false;
                }
                if (!File.Exists(archivePath))
                {
                    message = $"Archive '{archivePath}' does not exist.";
                    return false;
                }

                using (ZipArchive scan = ZipFile.OpenRead(archivePath))
                {
                    if (!ArchiveCore.TryCheckExpansionLimits(scan.Entries, maxTotalExpandedSizeBytes, maxCompressionRatio, out message))
                        return false;
                }

                Directory.CreateDirectory(destinationDirectoryPath);
                ZipFile.ExtractToDirectory(archivePath, destinationDirectoryPath, overwrite);

                if (!preserveTimestamps)
                {
                    DateTime now = DateTime.UtcNow;
                    foreach (string filePath in Directory.EnumerateFiles(destinationDirectoryPath, "*", SearchOption.AllDirectories))
                        File.SetLastWriteTimeUtc(filePath, now);
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ExtractArchive", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="ExtractArchive"/>, defaulting to a 1 GiB total-expanded-size limit, a 100:1 compression-ratio limit, and preserved timestamps. Never throws.</summary>
        [Category("Archive - Extract")]
        [Description("Same as ExtractArchive, defaulting to a 1 GiB size limit and a 100:1 compression-ratio limit. Never throws.")]
        public bool ExtractArchiveSimple(string archivePath, string destinationDirectoryPath, bool overwrite, out string message)
        {
            return ExtractArchive(archivePath, destinationDirectoryPath, overwrite, DefaultMaxTotalExpandedSizeBytes, DefaultMaxCompressionRatio, true, out message);
        }

        #endregion

        #region Extract Single File

        /// <summary>
        /// Extracts one entry from a ZIP archive by its exact name. Routes through
        /// <see cref="ArchiveSafety.TryResolveSafeExtractionPath"/> before writing a single
        /// byte - the per-entry extraction path has no built-in zip-slip protection,
        /// unlike <see cref="ExtractArchive"/>'s whole-archive path. Never throws.
        /// </summary>
        /// <param name="archivePath">The archive to extract from.</param>
        /// <param name="entryFullName">The entry's exact path within the archive (as reported by <see cref="ListArchiveContentsJson"/>).</param>
        /// <param name="destinationDirectoryPath">The directory to extract into.</param>
        /// <param name="overwrite">Whether an existing file at the destination may be replaced.</param>
        /// <param name="maxExpandedSizeBytes">The maximum declared uncompressed size for this entry. <c>0</c> or negative means no limit.</param>
        /// <param name="maxCompressionRatio">The maximum declared uncompressed:compressed ratio for this entry. <c>0</c> or negative means no limit.</param>
        /// <param name="message"><c>null</c> on success; a failure reason otherwise (including a rejected path-traversal attempt).</param>
        [Category("Archive - Extract Single File")]
        [Description("Extracts one entry from a ZIP archive by its exact name, guarded against path traversal and zip-bomb limits. Never throws.")]
        public bool ExtractSingleFile(string archivePath, string entryFullName, string destinationDirectoryPath, bool overwrite, long maxExpandedSizeBytes, double maxCompressionRatio, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    message = "An archive path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(entryFullName))
                {
                    message = "An entry name is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
                {
                    message = "A destination directory path is required.";
                    return false;
                }
                if (!File.Exists(archivePath))
                {
                    message = $"Archive '{archivePath}' does not exist.";
                    return false;
                }

                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                ZipArchiveEntry entry = archive.GetEntry(entryFullName);
                if (entry == null)
                {
                    message = $"Archive '{archivePath}' contains no entry named '{entryFullName}'.";
                    return false;
                }

                return ExtractEntrySafely(entry, destinationDirectoryPath, overwrite, maxExpandedSizeBytes, maxCompressionRatio, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ExtractSingleFile", ex);
                return false;
            }
        }

        /// <summary>
        /// Extracts the first entry whose name matches a glob pattern (via
        /// <see cref="FileSystemName"/>'s simple-expression matching, e.g. <c>"*.csv"</c>),
        /// rather than requiring an exact name. Same zip-slip and zip-bomb guards as
        /// <see cref="ExtractSingleFile"/>. Never throws.
        /// </summary>
        /// <param name="archivePath">The archive to extract from.</param>
        /// <param name="entryNamePattern">A glob pattern matched against each entry's full name.</param>
        /// <param name="destinationDirectoryPath">The directory to extract into.</param>
        /// <param name="overwrite">Whether an existing file at the destination may be replaced.</param>
        /// <param name="maxExpandedSizeBytes">The maximum declared uncompressed size for the matched entry. <c>0</c> or negative means no limit.</param>
        /// <param name="maxCompressionRatio">The maximum declared uncompressed:compressed ratio for the matched entry. <c>0</c> or negative means no limit.</param>
        /// <param name="matchedEntryName">The full name of the entry that was extracted, on success.</param>
        /// <param name="message"><c>null</c> on success; a failure reason otherwise (including a rejected path-traversal attempt or no match found).</param>
        [Category("Archive - Extract Single File")]
        [Description("Extracts the first entry whose name matches a glob pattern, guarded against path traversal and zip-bomb limits. Never throws.")]
        public bool ExtractFirstMatchingFile(string archivePath, string entryNamePattern, string destinationDirectoryPath, bool overwrite, long maxExpandedSizeBytes, double maxCompressionRatio, out string matchedEntryName, out string message)
        {
            matchedEntryName = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    message = "An archive path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(entryNamePattern))
                {
                    message = "An entry name pattern is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
                {
                    message = "A destination directory path is required.";
                    return false;
                }
                if (!File.Exists(archivePath))
                {
                    message = $"Archive '{archivePath}' does not exist.";
                    return false;
                }

                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                ZipArchiveEntry match = null;
                foreach (ZipArchiveEntry candidate in archive.Entries)
                {
                    if (FileSystemName.MatchesSimpleExpression(entryNamePattern, candidate.FullName))
                    {
                        match = candidate;
                        break;
                    }
                }
                if (match == null)
                {
                    message = $"Archive '{archivePath}' contains no entry matching pattern '{entryNamePattern}'.";
                    return false;
                }

                if (!ExtractEntrySafely(match, destinationDirectoryPath, overwrite, maxExpandedSizeBytes, maxCompressionRatio, out message))
                    return false;

                matchedEntryName = match.FullName;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ExtractFirstMatchingFile", ex);
                return false;
            }
        }

        private static bool ExtractEntrySafely(ZipArchiveEntry entry, string destinationDirectoryPath, bool overwrite, long maxExpandedSizeBytes, double maxCompressionRatio, out string message)
        {
            message = default;
            if (!ArchiveCore.TryCheckExpansionLimits(new[] { entry }, maxExpandedSizeBytes, maxCompressionRatio, out message))
                return false;

            if (!ArchiveSafety.TryResolveSafeExtractionPath(destinationDirectoryPath, entry.FullName, out string safePath, out message))
                return false;

            if (IsDirectoryEntry(entry))
            {
                Directory.CreateDirectory(safePath);
                message = null;
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(safePath));
            entry.ExtractToFile(safePath, overwrite);
            message = null;
            return true;
        }

        private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
            entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal);

        #endregion

        #region Inspect

        /// <summary>Lists every entry in a ZIP archive as a JSON array, without extracting anything - the pre-flight method for inspecting <c>IsEncrypted</c>/<c>Crc32</c>/sizes before committing to extraction. Never throws.</summary>
        [Category("Archive - Inspect")]
        [Description("Lists every entry in a ZIP archive as a JSON array, without extracting anything. Never throws.")]
        public bool ListArchiveContentsJson(string archivePath, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    message = "An archive path is required.";
                    return false;
                }
                if (!File.Exists(archivePath))
                {
                    message = $"Archive '{archivePath}' does not exist.";
                    return false;
                }

                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                var results = new List<ArchiveEntryInfo>();
                foreach (ZipArchiveEntry entry in archive.Entries)
                    results.Add(ToArchiveEntryInfo(entry));

                json = JsonSerializer.Serialize(results, ArchiveJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ListArchiveContentsJson", ex);
                return false;
            }
        }

        /// <summary>Gets summary metadata for a ZIP archive: entry count, total declared sizes, and whether any entry is encrypted. Never throws.</summary>
        [Category("Archive - Inspect")]
        [Description("Gets summary metadata for a ZIP archive: entry count, total sizes, and whether any entry is encrypted. Never throws.")]
        public bool TryGetArchiveMetadata(string archivePath, out int entryCount, out long totalUncompressedBytes, out long totalCompressedBytes, out bool hasEncryptedEntries, out string message)
        {
            entryCount = default;
            totalUncompressedBytes = default;
            totalCompressedBytes = default;
            hasEncryptedEntries = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    message = "An archive path is required.";
                    return false;
                }
                if (!File.Exists(archivePath))
                {
                    message = $"Archive '{archivePath}' does not exist.";
                    return false;
                }

                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    entryCount++;
                    totalUncompressedBytes += entry.Length;
                    totalCompressedBytes += entry.CompressedLength;
                    if (entry.IsEncrypted)
                        hasEncryptedEntries = true;
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("TryGetArchiveMetadata", ex);
                return false;
            }
        }

        private static ArchiveEntryInfo ToArchiveEntryInfo(ZipArchiveEntry entry) => new ArchiveEntryInfo
        {
            FullName = entry.FullName,
            Length = entry.Length,
            CompressedLength = entry.CompressedLength,
            CompressionRatio = entry.CompressedLength > 0 ? (double)entry.Length / entry.CompressedLength : 0.0,
            LastWriteUtcIso8601 = entry.LastWriteTime.UtcDateTime.ToString("o"),
            Crc32 = entry.Crc32,
            IsEncrypted = entry.IsEncrypted,
            IsDirectory = IsDirectoryEntry(entry)
        };

        #endregion

        #region Validate

        /// <summary>A single entry's CRC-32 validation outcome, serialized by <see cref="ValidateArchiveCrcJson"/>.</summary>
        private class CrcValidationEntry
        {
            public string FullName { get; set; }
            public uint DeclaredCrc32 { get; set; }
            public uint ComputedCrc32 { get; set; }
            public bool IsValid { get; set; }

            /// <summary>One of "Valid", "Mismatch", or "SkippedEncrypted".</summary>
            public string Status { get; set; }
        }

        /// <summary>
        /// Verifies every non-encrypted entry's actual decompressed content against its
        /// declared CRC-32 checksum - .NET performs no such check automatically on read,
        /// so a corrupted entry otherwise reads back silently with no error. Encrypted
        /// entries are never opened/decompressed and are always reported separately, never
        /// treated as a mismatch. Never throws.
        /// </summary>
        [Category("Archive - Validate")]
        [Description("Verifies every non-encrypted entry's actual content against its declared CRC-32 checksum. Never throws.")]
        public bool ValidateArchiveCrc(string archivePath, out bool allEntriesValid, out string message)
        {
            allEntriesValid = default;
            message = default;
            try
            {
                if (!TryValidateCrcCore(archivePath, out List<CrcValidationEntry> results, out message))
                    return false;

                allEntriesValid = results.All(r => r.Status != "Mismatch");
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ValidateArchiveCrc", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="ValidateArchiveCrc"/>, but reports a JSON array with each entry's declared/computed CRC-32 and status ("Valid", "Mismatch", or "SkippedEncrypted"). Never throws.</summary>
        [Category("Archive - Validate")]
        [Description("Same as ValidateArchiveCrc, reporting a JSON array with each entry's declared/computed CRC-32 and status. Never throws.")]
        public bool ValidateArchiveCrcJson(string archivePath, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (!TryValidateCrcCore(archivePath, out List<CrcValidationEntry> results, out message))
                    return false;

                json = JsonSerializer.Serialize(results, ArchiveJson.Options);
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ValidateArchiveCrcJson", ex);
                return false;
            }
        }

        private static bool TryValidateCrcCore(string archivePath, out List<CrcValidationEntry> results, out string message)
        {
            results = default;
            message = default;
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                message = "An archive path is required.";
                return false;
            }
            if (!File.Exists(archivePath))
            {
                message = $"Archive '{archivePath}' does not exist.";
                return false;
            }

            var list = new List<CrcValidationEntry>();
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (IsDirectoryEntry(entry))
                        continue;

                    // Never opened/decompressed - System.IO.Compression cannot decrypt an
                    // encrypted entry under any circumstance, and its exact behavior on
                    // Open() for one is not relied upon either way.
                    if (entry.IsEncrypted)
                    {
                        list.Add(new CrcValidationEntry
                        {
                            FullName = entry.FullName,
                            DeclaredCrc32 = entry.Crc32,
                            ComputedCrc32 = 0,
                            IsValid = false,
                            Status = "SkippedEncrypted"
                        });
                        continue;
                    }

                    uint computed;
                    using (Stream stream = entry.Open())
                        computed = Crc32Core.Compute(stream);

                    bool valid = computed == entry.Crc32;
                    list.Add(new CrcValidationEntry
                    {
                        FullName = entry.FullName,
                        DeclaredCrc32 = entry.Crc32,
                        ComputedCrc32 = computed,
                        IsValid = valid,
                        Status = valid ? "Valid" : "Mismatch"
                    });
                }
            }

            results = list;
            message = null;
            return true;
        }

        /// <summary>
        /// Scans a ZIP archive for any encrypted entry, stopping at the first one found.
        /// Detection only - <see cref="System.IO.Compression"/> cannot decrypt or extract
        /// an encrypted entry under any circumstance, and no password parameter exists
        /// anywhere in this component. Never throws.
        /// </summary>
        [Category("Archive - Validate")]
        [Description("Scans a ZIP archive for any encrypted entry. Detection only - cannot decrypt or extract encrypted entries. Never throws.")]
        public bool HasEncryptedEntries(string archivePath, out bool hasEncryptedEntries, out string message)
        {
            hasEncryptedEntries = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    message = "An archive path is required.";
                    return false;
                }
                if (!File.Exists(archivePath))
                {
                    message = $"Archive '{archivePath}' does not exist.";
                    return false;
                }

                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.IsEncrypted)
                    {
                        hasEncryptedEntries = true;
                        break;
                    }
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("HasEncryptedEntries", ex);
                return false;
            }
        }

        #endregion

        #region Diagnostic Bundles

        /// <summary>
        /// Creates a diagnostic/failure bundle by zipping a list of files, with an
        /// optional manifest entry, built atomically like <see cref="CreateArchive"/>.
        /// Never throws.
        /// </summary>
        /// <param name="sourceFilePathsCsv">A comma-separated list of file paths to include. Duplicate file names are disambiguated with a numeric suffix.</param>
        /// <param name="outputArchivePath">The bundle's final path.</param>
        /// <param name="overwrite">Whether an existing file at <paramref name="outputArchivePath"/> may be replaced.</param>
        /// <param name="manifestText">Optional text written as a <c>manifest.txt</c> entry. Null/empty means no manifest entry is written.</param>
        /// <param name="message"><c>null</c> on success; a failure reason otherwise.</param>
        [Category("Archive - Diagnostic Bundles")]
        [Description("Creates a diagnostic/failure bundle by zipping a list of files, with an optional manifest, built atomically. Never throws.")]
        public bool CreateDiagnosticBundle(string sourceFilePathsCsv, string outputArchivePath, bool overwrite, string manifestText, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceFilePathsCsv))
                {
                    message = "At least one source file path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(outputArchivePath))
                {
                    message = "An output archive path is required.";
                    return false;
                }
                if (!overwrite && File.Exists(outputArchivePath))
                {
                    message = $"Archive '{outputArchivePath}' already exists.";
                    return false;
                }

                List<string> paths = sourceFilePathsCsv.Split(',')
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();
                if (paths.Count == 0)
                {
                    message = "At least one source file path is required.";
                    return false;
                }

                string tempPath = ArchiveCore.MakeTempSiblingPath(outputArchivePath);
                if (!ArchiveCore.TryBuildArchiveFromFiles(paths, tempPath, manifestText, out message))
                {
                    TryDeleteBestEffort(tempPath);
                    return false;
                }

                return ArchiveCore.TryPublishAtomically(tempPath, outputArchivePath, overwrite, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CreateDiagnosticBundle", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="CreateDiagnosticBundle"/>, auto-naming the archive with a timestamp (<c>diagnostic-bundle-yyyyMMdd-HHmmss.zip</c>) inside <paramref name="outputDirectoryPath"/> and returning its resolved path. Never throws.</summary>
        [Category("Archive - Diagnostic Bundles")]
        [Description("Same as CreateDiagnosticBundle, auto-naming the archive with a timestamp and returning its path. Never throws.")]
        public bool CreateDiagnosticBundleSimple(string sourceFilePathsCsv, string outputDirectoryPath, out string createdArchivePath, out string message)
        {
            createdArchivePath = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(outputDirectoryPath))
                {
                    message = "An output directory path is required.";
                    return false;
                }
                if (!Directory.Exists(outputDirectoryPath))
                {
                    message = $"Output directory '{outputDirectoryPath}' does not exist.";
                    return false;
                }

                string fileName = "diagnostic-bundle-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".zip";
                string archivePath = Path.Combine(outputDirectoryPath, fileName);
                if (!CreateDiagnosticBundle(sourceFilePathsCsv, archivePath, false, null, out message))
                    return false;

                createdArchivePath = archivePath;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CreateDiagnosticBundleSimple", ex);
                return false;
            }
        }

        #endregion

        #region Internal Helpers

        private static void TryDeleteBestEffort(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }

        #endregion
    }
}
