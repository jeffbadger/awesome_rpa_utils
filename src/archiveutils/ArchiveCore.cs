using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ArchiveAutomation
{
    /// <summary>
    /// Shared build/extract/publish logic behind <see cref="ArchiveUtils.CreateArchive"/>,
    /// <see cref="ArchiveUtils.CreateDiagnosticBundle"/>, and every extraction method, so
    /// timestamp handling, atomic-publish, and zip-bomb-limit semantics cannot drift
    /// between them. Internal - not part of the Pega-facing surface.
    /// </summary>
    internal static class ArchiveCore
    {
        /// <summary>The earliest timestamp the ZIP/DOS date format can represent.</summary>
        internal static readonly DateTimeOffset MinZipTimestamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>The latest timestamp the ZIP/DOS date format can represent.</summary>
        internal static readonly DateTimeOffset MaxZipTimestamp = new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);

        /// <summary>
        /// Validates a timestamp against the ZIP/DOS date format's representable range
        /// (1980-01-01 through 2107-12-31) before it reaches <c>ZipArchiveEntry.LastWriteTime</c>'s
        /// setter, which throws <see cref="ArgumentOutOfRangeException"/> outside that range -
        /// this turns that into a guarded, clearly-worded failure instead.
        /// </summary>
        internal static bool TryValidateZipTimestamp(DateTimeOffset timestamp, out string error)
        {
            error = null;
            if (timestamp < MinZipTimestamp || timestamp > MaxZipTimestamp)
            {
                error = $"Timestamp '{timestamp:O}' is outside the ZIP format's representable range (1980-01-01 to 2107-12-31).";
                return false;
            }
            return true;
        }

        /// <summary>Builds a sibling temp-file path next to <paramref name="finalPath"/>, guaranteed to be on the same volume so a later move stays atomic.</summary>
        internal static string MakeTempSiblingPath(string finalPath) =>
            finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        /// <summary>Publishes a fully-built temp file to its final path via <see cref="File.Move(string, string, bool)"/>, the same same-volume-atomic idiom as <c>FileWatchUtils.AtomicMoveFile</c>. Best-effort deletes the temp file on failure.</summary>
        internal static bool TryPublishAtomically(string tempPath, string finalPath, bool overwrite, out string error)
        {
            error = null;
            try
            {
                File.Move(tempPath, finalPath, overwrite);
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                error = NeverThrowsGuard.Failure("Publish", ex);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
                return false;
            }
        }

        /// <summary>
        /// Builds a ZIP archive at <paramref name="tempArchivePath"/> from every file under
        /// <paramref name="sourceDirectoryPath"/> (recursively), including explicit entries
        /// for subdirectories that contain no files anywhere in their own subtree (so an
        /// empty directory tree round-trips through extraction instead of silently
        /// vanishing). Every entry's timestamp is either the source file's own local
        /// last-write time ("preserve") or <paramref name="normalizedTimestamp"/>
        /// ("normalize"), validated against the ZIP-representable range before being
        /// assigned. The classic ZIP/DOS date-time format has no time-zone field - only
        /// the wall-clock date/time survives a round-trip through
        /// <c>ZipArchiveEntry.LastWriteTime</c>, re-tagged with whatever machine's local
        /// offset reads it back, so entries are timestamped using each file's own local
        /// time (not UTC) to match this format's actual semantics and avoid a spurious
        /// hour/day shift on a machine in a different time zone.
        /// </summary>
        internal static bool TryBuildArchiveFromDirectory(string sourceDirectoryPath, string tempArchivePath, bool includeBaseDirectory, bool normalizeTimestamps, DateTimeOffset normalizedTimestamp, out string error)
        {
            error = null;
            string sourceRoot = Path.GetFullPath(sourceDirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string entryPrefix = includeBaseDirectory ? Path.GetFileName(sourceRoot) + "/" : string.Empty;

            using (var fs = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (string filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(sourceRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
                    string entryName = entryPrefix + relative;

                    DateTimeOffset timestamp = normalizeTimestamps ? normalizedTimestamp : new DateTimeOffset(File.GetLastWriteTime(filePath));
                    if (!TryValidateZipTimestamp(timestamp, out error))
                    {
                        error = $"'{filePath}': {error}";
                        return false;
                    }

                    // LastWriteTime can only be set before the entry's stream is opened -
                    // CreateEntryFromFile opens/writes/finalizes internally, so the entry is
                    // created and timestamped first, then the file content is streamed in
                    // manually.
                    ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    entry.LastWriteTime = timestamp;
                    using (Stream entryStream = entry.Open())
                    using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        sourceStream.CopyTo(entryStream);
                    }
                }

                foreach (string dirPath in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
                {
                    if (Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories).Any())
                        continue;

                    string relative = Path.GetRelativePath(sourceRoot, dirPath).Replace(Path.DirectorySeparatorChar, '/');
                    string entryName = entryPrefix + relative + "/";
                    ZipArchiveEntry dirEntry = archive.CreateEntry(entryName);

                    DateTimeOffset timestamp = normalizeTimestamps ? normalizedTimestamp : new DateTimeOffset(Directory.GetLastWriteTime(dirPath));
                    if (TryValidateZipTimestamp(timestamp, out _))
                        dirEntry.LastWriteTime = timestamp;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds a ZIP archive at <paramref name="tempArchivePath"/> from a discrete list
        /// of source files (each stored at the archive root by its own file name), plus an
        /// optional <c>manifest.txt</c> entry. Used by <c>CreateDiagnosticBundle</c>, whose
        /// input shape (a file list) differs from <see cref="TryBuildArchiveFromDirectory"/>'s
        /// (a source directory).
        /// </summary>
        internal static bool TryBuildArchiveFromFiles(IEnumerable<string> sourceFilePaths, string tempArchivePath, string manifestText, out string error)
        {
            error = null;
            DateTimeOffset now = DateTimeOffset.Now;

            using (var fs = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string filePath in sourceFilePaths)
                {
                    if (!File.Exists(filePath))
                    {
                        error = $"Source file '{filePath}' does not exist.";
                        return false;
                    }

                    string entryName = Path.GetFileName(filePath);
                    int suffix = 2;
                    string candidate = entryName;
                    while (!usedNames.Add(candidate))
                        candidate = $"{Path.GetFileNameWithoutExtension(entryName)}_{suffix++}{Path.GetExtension(entryName)}";

                    DateTimeOffset timestamp = new DateTimeOffset(File.GetLastWriteTime(filePath));
                    if (!TryValidateZipTimestamp(timestamp, out error))
                    {
                        error = $"'{filePath}': {error}";
                        return false;
                    }

                    ZipArchiveEntry entry = archive.CreateEntry(candidate, CompressionLevel.Optimal);
                    entry.LastWriteTime = timestamp;
                    using (Stream entryStream = entry.Open())
                    using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        sourceStream.CopyTo(entryStream);
                    }
                }

                if (!string.IsNullOrEmpty(manifestText))
                {
                    ZipArchiveEntry manifestEntry = archive.CreateEntry("manifest.txt");
                    manifestEntry.LastWriteTime = now;
                    using var writer = new StreamWriter(manifestEntry.Open());
                    writer.Write(manifestText);
                }
            }

            return true;
        }

        /// <summary>
        /// Checks a set of entries' declared <c>Length</c>/<c>CompressedLength</c> metadata
        /// against a total-expanded-size limit and a per-entry compression-ratio limit -
        /// both checked against declared central-directory metadata, before any bytes are
        /// decompressed. A limit <c>&lt;= 0</c> means "no limit."
        /// </summary>
        internal static bool TryCheckExpansionLimits(IEnumerable<ZipArchiveEntry> entries, long maxTotalExpandedSizeBytes, double maxCompressionRatio, out string error)
        {
            error = null;
            long total = 0;
            foreach (ZipArchiveEntry entry in entries)
            {
                total += entry.Length;

                if (maxCompressionRatio > 0)
                {
                    if (entry.CompressedLength > 0)
                    {
                        double ratio = (double)entry.Length / entry.CompressedLength;
                        if (ratio > maxCompressionRatio)
                        {
                            error = $"Entry '{entry.FullName}' has a compression ratio of {ratio:F1}:1, exceeding the limit of {maxCompressionRatio:F1}:1.";
                            return false;
                        }
                    }
                    else if (entry.Length > 0)
                    {
                        error = $"Entry '{entry.FullName}' declares {entry.Length} bytes from a zero-byte compressed stream, exceeding the limit of {maxCompressionRatio:F1}:1.";
                        return false;
                    }
                }
            }

            if (maxTotalExpandedSizeBytes > 0 && total > maxTotalExpandedSizeBytes)
            {
                error = $"The archive's total declared expanded size ({total} bytes) exceeds the limit of {maxTotalExpandedSizeBytes} bytes.";
                return false;
            }

            return true;
        }
    }
}
