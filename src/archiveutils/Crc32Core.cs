using System.IO;

namespace ArchiveAutomation
{
    /// <summary>
    /// A hand-rolled CRC-32 (IEEE 802.3 / ZIP polynomial <c>0xEDB88320</c>, reflected)
    /// implementation, so <see cref="ArchiveUtils.ValidateArchiveCrc"/> can independently
    /// verify a decompressed entry's content against its declared checksum without adding
    /// a NuGet dependency - .NET's <c>System.IO.Compression</c> never validates this
    /// automatically (a corrupted entry reads back silently with no exception). Internal -
    /// not part of the Pega-facing surface.
    /// </summary>
    internal static class Crc32Core
    {
        private const uint Polynomial = 0xEDB88320;
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int bit = 0; bit < 8; bit++)
                    c = (c & 1) != 0 ? (Polynomial ^ (c >> 1)) : (c >> 1);
                table[i] = c;
            }
            return table;
        }

        /// <summary>Computes the CRC-32 of a byte array. Exposed mainly for test vectors; streaming callers should use <see cref="Compute(Stream)"/>.</summary>
        internal static uint Compute(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>Computes the CRC-32 of a stream's remaining content, reading in fixed-size chunks - never buffers the whole stream into memory.</summary>
        internal static uint Compute(Stream stream)
        {
            uint crc = 0xFFFFFFFF;
            byte[] buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                    crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFF;
        }
    }
}
