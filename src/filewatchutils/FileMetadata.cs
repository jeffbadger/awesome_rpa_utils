using System.Text.Json;

namespace FileWatchAutomation
{
    /// <summary>Shared serializer options for <see cref="FileMetadata"/>.</summary>
    internal static class FileWatchJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions();
    }

    /// <summary>
    /// A single file or directory's metadata, flattened into Pega-mappable properties.
    /// Produced internally by <see cref="FileWatchUtils.GetFileMetadataJson"/> and
    /// <see cref="FileWatchUtils.GetDirectoryListingJson"/> - not itself a public method
    /// return type, since Pega Robot Studio prefers scalars/JSON over complex objects.
    /// </summary>
    public class FileMetadata
    {
        /// <summary>The fully-qualified path.</summary>
        public string FullPath { get; internal set; }

        /// <summary>The file or directory name, without its parent path.</summary>
        public string Name { get; internal set; }

        /// <summary>The file extension including the leading dot, or an empty string for a directory or extensionless file.</summary>
        public string Extension { get; internal set; }

        /// <summary>The file size in bytes, or 0 for a directory.</summary>
        public long SizeBytes { get; internal set; }

        /// <summary><c>true</c> if this entry is a directory rather than a file.</summary>
        public bool IsDirectory { get; internal set; }

        /// <summary>When the entry was created, as a round-trippable ISO-8601 UTC string.</summary>
        public string CreatedUtcIso8601 { get; internal set; }

        /// <summary>When the entry was last written to, as a round-trippable ISO-8601 UTC string.</summary>
        public string LastWriteUtcIso8601 { get; internal set; }
    }
}
