using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ArchiveAutomation
{
    /// <summary>
    /// A minimal (name, declared uncompressed size, declared compressed size) view over an
    /// archive entry, used so <see cref="ArchiveCore.TryCheckExpansionLimits"/> can validate
    /// entries from both <see cref="ZipArchiveEntry"/> (System.IO.Compression) and
    /// SharpZipLib's <c>ZipEntry</c> without either type depending on the other's assembly.
    /// </summary>
    internal readonly struct ArchiveEntrySizeInfo
    {
        internal ArchiveEntrySizeInfo(string fullName, long length, long compressedLength)
        {
            FullName = fullName;
            Length = length;
            CompressedLength = compressedLength;
        }

        internal string FullName { get; }
        internal long Length { get; }
        internal long CompressedLength { get; }
    }

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

        // Net48 compatibility shim for File.Move(source, dest, overwrite). The
        // delete-then-move fallback is not fully atomic on net48; net8/net10 keep
        // the atomic overload. The caller builds a per-call-unique temp file, so
        // the non-atomic window can never collide with a concurrent publisher.
#if NETFRAMEWORK
        internal static void MoveFileOverwrite(string sourcePath, string destinationPath, bool overwrite)
        {
            if (overwrite && File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(sourcePath, destinationPath);
        }
#else
        internal static void MoveFileOverwrite(string sourcePath, string destinationPath, bool overwrite) => File.Move(sourcePath, destinationPath, overwrite);
#endif

        // Net48 compatibility shim for Path.GetRelativePath: computes the same
        // common-path-prefix relative path, with the ordinal-ignore-case comparison
        // the BCL uses on Windows. Paths under different roots fall back to the
        // full path, matching the BCL.
        internal static string GetRelativePath(string relativeTo, string path)
        {
#if !NETFRAMEWORK
            return Path.GetRelativePath(relativeTo, path);
#else
            relativeTo = Path.GetFullPath(relativeTo).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            path = Path.GetFullPath(path);

            string[] baseParts = relativeTo.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            string[] pathParts = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            int common = 0;
            while (common < baseParts.Length && common < pathParts.Length &&
                   string.Equals(baseParts[common], pathParts[common], StringComparison.OrdinalIgnoreCase))
                common++;
            if (common == 0)
                return path;

            var result = new StringBuilder();
            for (int i = common; i < baseParts.Length; i++)
                result.Append("..").Append(Path.DirectorySeparatorChar);
            for (int i = common; i < pathParts.Length; i++)
            {
                result.Append(pathParts[i]);
                if (i < pathParts.Length - 1)
                    result.Append(Path.DirectorySeparatorChar);
            }

            string text = result.ToString();
            return text.Length == 0 ? "." : text;
#endif
        }

        /// <summary>
        /// Extracts a ZIP archive to <paramref name="destinationDirectoryPath"/> with the modern
        /// BCL's <c>ZipFile.ExtractToDirectory(archivePath, destination, overwrite)</c> semantics:
        /// refuse to clobber existing files unless <paramref name="overwrite"/> is set, never let
        /// an entry escape the destination directory, and carry entry timestamps across. Net48 has
        /// no overwrite overload, so the NETFRAMEWORK branch extracts entry-by-entry.
        /// </summary>
        internal static void ExtractToDirectory(string archivePath, string destinationDirectoryPath, bool overwrite)
        {
#if !NETFRAMEWORK
            ZipFile.ExtractToDirectory(archivePath, destinationDirectoryPath, overwrite);
#else
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                string destinationRoot = Path.GetFullPath(destinationDirectoryPath);
                if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    destinationRoot += Path.DirectorySeparatorChar;
                Directory.CreateDirectory(destinationRoot);

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    // Guard the resolved target before touching anything, so a crafted
                    // entry name ('..\', an absolute path) cannot escape the destination.
                    string targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"Extracting entry '{entry.FullName}' would have escaped the destination directory.");

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    if (Directory.Exists(targetPath))
                        throw new IOException($"The directory '{targetPath}' already exists when a file was expected.");
                    if (File.Exists(targetPath))
                    {
                        if (!overwrite)
                            throw new IOException($"The file '{targetPath}' already exists.");
                        File.Delete(targetPath);
                    }

                    string parentDirectory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parentDirectory))
                        Directory.CreateDirectory(parentDirectory);

                    using (Stream source = entry.Open())
                    using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        source.CopyTo(target);

                    try
                    {
                        if (entry.LastWriteTime.UtcDateTime >= MinZipTimestamp.DateTime)
                            File.SetLastWriteTimeUtc(targetPath, entry.LastWriteTime.UtcDateTime);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Out-of-range entry timestamp: leave the file's current time,
                        // like the modern BCL's extraction does.
                    }
                }
            }
#endif
        }

        /// <summary>Publishes a fully-built temp file to its final path via <see cref="File.Move(string, string, bool)"/>, the same same-volume-atomic idiom as <c>FileWatchUtils.AtomicMoveFile</c>. Best-effort deletes the temp file on failure.</summary>
        internal static bool TryPublishAtomically(string tempPath, string finalPath, bool overwrite, out string error)
        {
            error = null;
            try
            {
                MoveFileOverwrite(tempPath, finalPath, overwrite);
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
                    string relative = GetRelativePath(sourceRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
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

                    string relative = GetRelativePath(sourceRoot, dirPath).Replace(Path.DirectorySeparatorChar, '/');
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
        /// Adds each source file in <paramref name="sourceFilePaths"/> to
        /// <paramref name="tempArchivePath"/> (already a working copy of the target archive,
        /// opened here in <see cref="ZipArchiveMode.Update"/>), replacing any existing entry
        /// of the same name. <paramref name="entryNames"/> is parallel to
        /// <paramref name="sourceFilePaths"/>; a null/empty element falls back to the source
        /// file's own name, disambiguated against other newly-added-in-this-call fallback
        /// names the same way <see cref="TryBuildArchiveFromFiles"/> disambiguates diagnostic
        /// bundle entries - but never against a pre-existing archive entry, since replacing a
        /// same-named existing entry is the whole point of this method. If two entries in the
        /// same call resolve to the same final name (e.g. two explicit, identical
        /// <paramref name="entryNames"/> values), the later one wins.
        /// </summary>
        internal static bool TryAddOrReplaceFilesInArchive(string tempArchivePath, IReadOnlyList<string> sourceFilePaths, IReadOnlyList<string> entryNames, out string error)
        {
            error = null;
            var usedFallbackNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var archive = ZipFile.Open(tempArchivePath, ZipArchiveMode.Update))
            {
                for (int i = 0; i < sourceFilePaths.Count; i++)
                {
                    string filePath = sourceFilePaths[i];
                    string requestedName = entryNames[i];
                    string entryName;

                    if (!string.IsNullOrWhiteSpace(requestedName))
                    {
                        entryName = requestedName.Replace('\\', '/');
                    }
                    else
                    {
                        string fallback = Path.GetFileName(filePath);
                        int suffix = 2;
                        string candidate = fallback;
                        while (!usedFallbackNames.Add(candidate))
                            candidate = $"{Path.GetFileNameWithoutExtension(fallback)}_{suffix++}{Path.GetExtension(fallback)}";
                        entryName = candidate;
                    }

                    DateTimeOffset timestamp = new DateTimeOffset(File.GetLastWriteTime(filePath));
                    if (!TryValidateZipTimestamp(timestamp, out error))
                    {
                        error = $"'{filePath}': {error}";
                        return false;
                    }

                    ZipArchiveEntry existing = archive.GetEntry(entryName);
                    existing?.Delete();

                    ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    entry.LastWriteTime = timestamp;
                    using (Stream entryStream = entry.Open())
                    using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        sourceStream.CopyTo(entryStream);
                    }
                }
            }

            return true;
        }

        /// <summary>Removes the one entry named <paramref name="entryFullName"/> from <paramref name="tempArchivePath"/> (a working copy, opened here in <see cref="ZipArchiveMode.Update"/>).</summary>
        internal static bool TryRemoveArchiveEntry(string tempArchivePath, string entryFullName, out string error)
        {
            error = null;
            using (var archive = ZipFile.Open(tempArchivePath, ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry = archive.GetEntry(entryFullName);
                if (entry == null)
                {
                    error = $"Archive contains no entry named '{entryFullName}'.";
                    return false;
                }
                entry.Delete();
            }
            return true;
        }

        /// <summary>
        /// Renames one entry in <paramref name="tempArchivePath"/> (a working copy, opened
        /// here in <see cref="ZipArchiveMode.Update"/>) by fully buffering its content in
        /// memory, creating a new entry under <paramref name="newEntryName"/> with that
        /// content and the original's timestamp, then deleting the original - rather than
        /// interleaving read/write streams from two entries of the same open archive, which
        /// <see cref="ZipArchiveMode.Update"/> does not reliably support. Fails, without
        /// modifying anything, if the source entry is missing or the target name is already
        /// taken. Note this recompresses the entry (always <see cref="CompressionLevel.Optimal"/>)
        /// since <see cref="ZipArchiveEntry"/> exposes no way to read back its original
        /// compression level for a byte-for-byte raw copy - content is identical, only
        /// physical size may shift slightly.
        /// </summary>
        internal static bool TryRenameArchiveEntry(string tempArchivePath, string entryFullName, string newEntryName, out string error)
        {
            error = null;
            using (var archive = ZipFile.Open(tempArchivePath, ZipArchiveMode.Update))
            {
                ZipArchiveEntry existingSource = archive.GetEntry(entryFullName);
                if (existingSource == null)
                {
                    error = $"Archive contains no entry named '{entryFullName}'.";
                    return false;
                }
                if (archive.GetEntry(newEntryName) != null)
                {
                    error = $"Archive already contains an entry named '{newEntryName}'.";
                    return false;
                }

                byte[] content;
                using (Stream sourceStream = existingSource.Open())
                using (var buffer = new MemoryStream())
                {
                    sourceStream.CopyTo(buffer);
                    content = buffer.ToArray();
                }
                DateTimeOffset originalTimestamp = existingSource.LastWriteTime;
                existingSource.Delete();

                ZipArchiveEntry newEntry = archive.CreateEntry(newEntryName, CompressionLevel.Optimal);
                newEntry.LastWriteTime = originalTimestamp;
                using (Stream destinationStream = newEntry.Open())
                using (var contentStream = new MemoryStream(content))
                {
                    contentStream.CopyTo(destinationStream);
                }
            }
            return true;
        }

        /// <summary>
        /// Builds a new ZIP archive at <paramref name="tempArchivePath"/> containing every
        /// entry from <paramref name="firstArchivePath"/> followed by every entry from
        /// <paramref name="secondArchivePath"/>, disambiguating any entry-name collision
        /// between the two (or within either one) with the same numeric-suffix convention as
        /// <see cref="TryBuildArchiveFromFiles"/>. Directory entries are copied as bare
        /// entries (no content stream); file entries are stream-copied byte for byte.
        /// </summary>
        internal static bool TryMergeArchives(string firstArchivePath, string secondArchivePath, string tempArchivePath, out string error)
        {
            error = null;
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            using (var fs = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write))
            using (var output = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (string sourcePath in new[] { firstArchivePath, secondArchivePath })
                {
                    using ZipArchive source = ZipFile.OpenRead(sourcePath);
                    foreach (ZipArchiveEntry sourceEntry in source.Entries)
                    {
                        string entryName = sourceEntry.FullName;
                        if (!usedNames.Add(entryName))
                        {
                            bool isDirectory = entryName.EndsWith("/", StringComparison.Ordinal);
                            string baseName = isDirectory ? entryName.TrimEnd('/') : entryName;
                            string directory = Path.GetDirectoryName(baseName)?.Replace('\\', '/') ?? string.Empty;
                            string fileName = Path.GetFileName(baseName);
                            int suffix = 2;
                            string candidate;
                            do
                            {
                                string candidateFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{suffix++}{Path.GetExtension(fileName)}";
                                candidate = (directory.Length > 0 ? directory + "/" : string.Empty) + candidateFileName + (isDirectory ? "/" : string.Empty);
                            }
                            while (!usedNames.Add(candidate));
                            entryName = candidate;
                        }

                        if (entryName.EndsWith("/", StringComparison.Ordinal))
                        {
                            ZipArchiveEntry dirEntry = output.CreateEntry(entryName);
                            dirEntry.LastWriteTime = sourceEntry.LastWriteTime;
                            continue;
                        }

                        ZipArchiveEntry newEntry = output.CreateEntry(entryName, CompressionLevel.Optimal);
                        newEntry.LastWriteTime = sourceEntry.LastWriteTime;
                        using (Stream sourceStream = sourceEntry.Open())
                        using (Stream destinationStream = newEntry.Open())
                        {
                            sourceStream.CopyTo(destinationStream);
                        }
                    }
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
        internal static bool TryCheckExpansionLimits(IEnumerable<ArchiveEntrySizeInfo> entries, long maxTotalExpandedSizeBytes, double maxCompressionRatio, out string error)
        {
            error = null;
            long total = 0;
            foreach (ArchiveEntrySizeInfo entry in entries)
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
