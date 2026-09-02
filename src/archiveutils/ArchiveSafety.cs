using System;
using System.IO;

namespace ArchiveAutomation
{
    /// <summary>
    /// The zip-slip / path-traversal guard used by every per-entry extraction method in
    /// this component. <see cref="System.IO.Compression.ZipFile.ExtractToDirectory(string, string)"/>
    /// blocks a traversal attempt on its own, but the per-entry
    /// <c>ZipArchiveEntry</c> extraction path does not - verified empirically during
    /// planning: an entry named <c>"../../evil.txt"</c>, extracted via a naive
    /// <c>Path.Combine</c> + <c>entry.ExtractToFile</c>, wrote outside the destination
    /// directory without throwing. This helper is the fix, and every extraction method in
    /// this component routes through it before writing a single byte. Internal - not part
    /// of the Pega-facing surface.
    /// </summary>
    internal static class ArchiveSafety
    {
        /// <summary>
        /// Resolves the on-disk path a ZIP entry should be extracted to, rejecting any
        /// entry name that would escape <paramref name="destinationDirectory"/> - whether
        /// via a relative <c>"../"</c> traversal or an absolute/rooted path (which
        /// <see cref="Path.Combine(string, string)"/> would otherwise silently honor by
        /// discarding <paramref name="destinationDirectory"/> entirely - confirmed .NET
        /// behavior, not a hypothetical).
        /// </summary>
        internal static bool TryResolveSafeExtractionPath(string destinationDirectory, string entryFullName, out string safePath, out string error)
        {
            safePath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                error = "A destination directory is required.";
                return false;
            }
            if (string.IsNullOrEmpty(entryFullName))
            {
                error = "The archive entry has no name.";
                return false;
            }

            // Rejected explicitly, with its own specific message, even though the
            // resolved-path prefix check below would also catch this - Path.Combine
            // silently discards destinationDirectory when entryFullName is rooted (e.g.
            // "/evil.txt" or "C:\evil.txt"), so a rooted entry deserves a clearer
            // diagnostic than a generic "outside the destination directory" message.
            if (Path.IsPathRooted(entryFullName))
            {
                error = $"Archive entry '{entryFullName}' has an absolute/rooted path and was rejected.";
                return false;
            }

            string destinationFull;
            string combinedFull;
            try
            {
                destinationFull = Path.GetFullPath(destinationDirectory);
                combinedFull = Path.GetFullPath(Path.Combine(destinationDirectory, entryFullName));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                error = $"Archive entry '{entryFullName}' has an invalid path and was rejected: {ex.Message}";
                return false;
            }

            // A trailing separator on the prefix, not a bare StartsWith(destinationFull) -
            // otherwise a sibling directory like "/dest2" would falsely pass a "/dest" check.
            string destinationPrefix = destinationFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!combinedFull.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Archive entry '{entryFullName}' would extract outside the destination directory and was rejected.";
                return false;
            }

            safePath = combinedFull;
            return true;
        }
    }
}
