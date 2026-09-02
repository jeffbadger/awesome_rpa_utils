using System.Text.Json;

namespace ArchiveAutomation
{
    /// <summary>Shared serializer options for <see cref="ArchiveEntryInfo"/>.</summary>
    internal static class ArchiveJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions();
    }

    /// <summary>
    /// A single ZIP archive entry's metadata, flattened into Pega-mappable properties.
    /// Produced internally by <see cref="ArchiveUtils.ListArchiveContentsJson"/> and
    /// <see cref="ArchiveUtils.ValidateArchiveCrcJson"/> - not itself a public method
    /// return type, since Pega Robot Studio prefers scalars/JSON over complex objects.
    /// </summary>
    public class ArchiveEntryInfo
    {
        /// <summary>The entry's path within the archive, using forward slashes.</summary>
        public string FullName { get; internal set; }

        /// <summary>The declared uncompressed size in bytes, from the central directory.</summary>
        public long Length { get; internal set; }

        /// <summary>The declared compressed size in bytes, from the central directory.</summary>
        public long CompressedLength { get; internal set; }

        /// <summary><see cref="Length"/> divided by <see cref="CompressedLength"/>, or 0 when <see cref="CompressedLength"/> is 0 (an empty entry).</summary>
        public double CompressionRatio { get; internal set; }

        /// <summary>The entry's stored last-write time, as a round-trippable ISO-8601 UTC string.</summary>
        public string LastWriteUtcIso8601 { get; internal set; }

        /// <summary>The entry's declared CRC-32 checksum, as stored in the archive.</summary>
        public uint Crc32 { get; internal set; }

        /// <summary>
        /// <c>true</c> if the entry is password-protected. Detection only -
        /// <see cref="System.IO.Compression"/> cannot decrypt or extract an encrypted entry
        /// under any circumstance.
        /// </summary>
        public bool IsEncrypted { get; internal set; }

        /// <summary>
        /// <c>true</c> if this entry represents a directory. Some ZIP writers omit explicit
        /// directory entries entirely, so this reflects only entries actually present in the
        /// central directory, not directories implied by nested file paths.
        /// </summary>
        public bool IsDirectory { get; internal set; }
    }
}
