using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip;

namespace ArchiveAutomation
{
    /// <summary>
    /// SharpZipLib-backed build/extract logic for password-protected archives -
    /// <see cref="System.IO.Compression"/> cannot write or read encrypted ZIP entries under
    /// any circumstance, so this is the one place in the suite that depends on
    /// <c>ICSharpCode.SharpZipLib</c> rather than the BCL. Kept in its own file so that
    /// dependency doesn't spread into <see cref="ArchiveCore"/>. Internal - not part of the
    /// Pega-facing surface.
    /// </summary>
    internal static class ArchiveEncryptionCore
    {
        /// <summary>
        /// Builds a password-protected ZIP archive at <paramref name="tempArchivePath"/> from
        /// every file under <paramref name="sourceDirectoryPath"/> (recursively), mirroring
        /// <see cref="ArchiveCore.TryBuildArchiveFromDirectory"/>'s directory-walking and
        /// local-timestamp conventions but writing via SharpZipLib's <see cref="ZipOutputStream"/>
        /// instead of <see cref="System.IO.Compression.ZipArchive"/>, since only SharpZipLib
        /// can write encrypted entries.
        /// </summary>
        internal static bool TryBuildEncryptedArchiveFromDirectory(string sourceDirectoryPath, string tempArchivePath, bool includeBaseDirectory, string password, bool useLegacyZipCrypto, out string error)
        {
            error = null;
            string sourceRoot = Path.GetFullPath(sourceDirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string entryPrefix = includeBaseDirectory ? Path.GetFileName(sourceRoot) + "/" : string.Empty;

            using (var fsOut = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write))
            using (var zipStream = new ZipOutputStream(fsOut))
            {
                zipStream.SetLevel(9);
                zipStream.Password = password;

                foreach (string filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(sourceRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
                    string entryName = entryPrefix + relative;

                    DateTimeOffset timestamp = new DateTimeOffset(File.GetLastWriteTime(filePath));
                    if (!ArchiveCore.TryValidateZipTimestamp(timestamp, out error))
                    {
                        error = $"'{filePath}': {error}";
                        return false;
                    }

                    var entry = new ZipEntry(entryName)
                    {
                        DateTime = timestamp.LocalDateTime
                    };
                    if (!useLegacyZipCrypto)
                        entry.AESKeySize = 256;

                    zipStream.PutNextEntry(entry);
                    using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        sourceStream.CopyTo(zipStream);
                    }
                    zipStream.CloseEntry();
                }

                foreach (string dirPath in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
                {
                    if (Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories).Any())
                        continue;

                    string relative = Path.GetRelativePath(sourceRoot, dirPath).Replace(Path.DirectorySeparatorChar, '/');
                    string entryName = entryPrefix + relative + "/";
                    var dirEntry = new ZipEntry(entryName);

                    DateTimeOffset timestamp = new DateTimeOffset(Directory.GetLastWriteTime(dirPath));
                    if (ArchiveCore.TryValidateZipTimestamp(timestamp, out _))
                        dirEntry.DateTime = timestamp.LocalDateTime;

                    zipStream.PutNextEntry(dirEntry);
                    zipStream.CloseEntry();
                }

                zipStream.Finish();
            }

            return true;
        }

        /// <summary>
        /// Extracts a password-protected ZIP archive to a directory via SharpZipLib's
        /// <see cref="ZipFile"/> (a different type than <see cref="System.IO.Compression.ZipFile"/>,
        /// fully qualified here to avoid ambiguity), reusing the exact same zip-slip guard
        /// (<see cref="ArchiveSafety.TryResolveSafeExtractionPath"/>) and declared-size/ratio
        /// pre-check (<see cref="ArchiveCore.TryCheckExpansionLimits"/>) that every other
        /// extraction method in this component uses.
        /// </summary>
        internal static bool TryExtractEncryptedArchive(string archivePath, string destinationDirectoryPath, string password, bool overwrite, long maxTotalExpandedSizeBytes, double maxCompressionRatio, bool preserveTimestamps, out string error)
        {
            error = null;

            using var fs = File.OpenRead(archivePath);
            using var zipFile = new ZipFile(fs) { Password = password };

            var sizeInfos = new List<ArchiveEntrySizeInfo>();
            foreach (ZipEntry entry in zipFile)
            {
                if (entry.IsDirectory)
                    continue;
                sizeInfos.Add(new ArchiveEntrySizeInfo(entry.Name, entry.Size < 0 ? 0 : entry.Size, entry.CompressedSize < 0 ? 0 : entry.CompressedSize));
            }
            if (!ArchiveCore.TryCheckExpansionLimits(sizeInfos, maxTotalExpandedSizeBytes, maxCompressionRatio, out error))
                return false;

            Directory.CreateDirectory(destinationDirectoryPath);

            foreach (ZipEntry entry in zipFile)
            {
                if (!ArchiveSafety.TryResolveSafeExtractionPath(destinationDirectoryPath, entry.Name, out string safePath, out error))
                    return false;

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(safePath);
                    continue;
                }

                if (!overwrite && File.Exists(safePath))
                {
                    error = $"Destination file '{safePath}' already exists.";
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(safePath));
                using (Stream entryStream = zipFile.GetInputStream(entry))
                using (var destinationStream = new FileStream(safePath, FileMode.Create, FileAccess.Write))
                {
                    entryStream.CopyTo(destinationStream);
                }

                if (preserveTimestamps)
                    File.SetLastWriteTime(safePath, entry.DateTime);
            }

            return true;
        }
    }
}
