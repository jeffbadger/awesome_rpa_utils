using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ArchiveAutomation
{
    // Net48 compatibility shims for the .NET 8-only ZipArchiveEntry.Crc32 and
    // ZipArchiveEntry.IsEncrypted properties. Both values live in the ZIP central
    // directory, which net48 does not expose, so the NETFRAMEWORK branch parses the
    // central directory directly and aligns records with archive.Entries by position
    // (System.IO.Compression always enumerates entries in central-directory order).
    // Parse results are cached per (path, length, last-write) so per-entry lookups
    // stay cheap. Zip64 archives (more than 65535 entries or a >4GB central
    // directory offset) and malformed archives are outside this shim's scope and
    // read as CRC 0 / not-encrypted - callers already treat unrecoverable archives
    // through their NeverThrows guards.
    internal static class ZipCompat
    {
        internal sealed class ZipCentralRecord
        {
            internal string Name;
            internal uint Crc32;
            internal bool IsEncrypted;
        }

        internal static uint Crc32(string archivePath, ZipArchiveEntry entry, int entryIndex)
        {
#if !NETFRAMEWORK
            return entry.Crc32;
#else
            List<ZipCentralRecord> records = TryReadCentralDirectory(archivePath);
            if (records != null && entryIndex >= 0 && entryIndex < records.Count)
                return records[entryIndex].Crc32;
            return 0u;
#endif
        }

        internal static bool IsEncrypted(string archivePath, ZipArchiveEntry entry, int entryIndex)
        {
#if !NETFRAMEWORK
            return entry.IsEncrypted;
#else
            List<ZipCentralRecord> records = TryReadCentralDirectory(archivePath);
            if (records != null && entryIndex >= 0 && entryIndex < records.Count)
                return records[entryIndex].IsEncrypted;
            return false;
#endif
        }

#if NETFRAMEWORK
        private class CacheEntry
        {
            internal readonly long Length;
            internal readonly DateTime LastWriteUtc;
            internal readonly List<ZipCentralRecord> Records;

            internal CacheEntry(long length, DateTime lastWriteUtc, List<ZipCentralRecord> records)
            {
                Length = length;
                LastWriteUtc = lastWriteUtc;
                Records = records;
            }
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        // Returns null when the central directory cannot be parsed for any reason.
        private static List<ZipCentralRecord> TryReadCentralDirectory(string archivePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(archivePath);
                lock (CacheLock)
                {
                    CacheEntry entry;
                    if (Cache.TryGetValue(fullPath, out entry))
                    {
                        long currentLength = new FileInfo(fullPath).Length;
                        DateTime currentWrite = File.GetLastWriteTimeUtc(fullPath);
                        if (entry.Length == currentLength && entry.LastWriteUtc == currentWrite)
                            return entry.Records;
                    }

                    List<ZipCentralRecord> records = ParseCentralDirectory(fullPath);
                    Cache[fullPath] = new CacheEntry(new FileInfo(fullPath).Length, File.GetLastWriteTimeUtc(fullPath), records);
                    return records;
                }
            }
            catch
            {
                // Malformed/locked/missing archive: the callers report a best-effort
                // reading (CRC 0, not encrypted) rather than failing the whole method.
                return null;
            }
        }

        private static List<ZipCentralRecord> ParseCentralDirectory(string archivePath)
        {
            using (FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long length = stream.Length;
                if (length < 22)
                    return null;

                // The end-of-central-directory record is at most 22 bytes plus a
                // 64KB archive comment; scan the tail of the file for its signature.
                int windowSize = (int)Math.Min(length, 22L + ushort.MaxValue);
                byte[] window = new byte[windowSize];
                stream.Seek(length - windowSize, SeekOrigin.Begin);
                if (!TryReadExactly(stream, window, windowSize))
                    return null;

                int eocd = -1;
                for (int i = windowSize - 22; i >= 0; i--)
                {
                    if (window[i] == 0x50 && window[i + 1] == 0x4b && window[i + 2] == 0x05 && window[i + 3] == 0x06)
                    {
                        eocd = i;
                        break;
                    }
                }
                if (eocd < 0)
                    return null;

                ushort entryCount = BitConverter.ToUInt16(window, eocd + 10);
                long centralDirectoryOffset = BitConverter.ToUInt32(window, eocd + 16);
                if (entryCount == 0 || entryCount == ushort.MaxValue || centralDirectoryOffset <= 0 || centralDirectoryOffset >= length)
                    return null; // Empty, Zip64, or malformed.

                stream.Seek(centralDirectoryOffset, SeekOrigin.Begin);
                var records = new List<ZipCentralRecord>(entryCount);
                byte[] header = new byte[46];
                for (int i = 0; i < entryCount; i++)
                {
                    if (!TryReadExactly(stream, header, 46))
                        return null;
                    if (header[0] != 0x50 || header[1] != 0x4b || header[2] != 0x01 || header[3] != 0x02)
                        return null;

                    ushort flags = BitConverter.ToUInt16(header, 8);
                    ushort nameLength = BitConverter.ToUInt16(header, 28);
                    ushort extraLength = BitConverter.ToUInt16(header, 30);
                    ushort commentLength = BitConverter.ToUInt16(header, 32);

                    byte[] nameBytes = new byte[nameLength];
                    if (!TryReadExactly(stream, nameBytes, nameLength))
                        return null;

                    records.Add(new ZipCentralRecord
                    {
                        Name = ((flags & 0x0800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(437)).GetString(nameBytes),
                        Crc32 = BitConverter.ToUInt32(header, 16),
                        IsEncrypted = (flags & 0x0001) != 0
                    });

                    if (extraLength + commentLength > 0 &&
                        stream.Seek(extraLength + commentLength, SeekOrigin.Current) >= length)
                        return null;
                }
                return records;
            }
        }

        private static bool TryReadExactly(FileStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    return false;
                offset += read;
            }
            return true;
        }
#endif
    }
}