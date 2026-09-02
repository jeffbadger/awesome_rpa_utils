using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ArchiveAutomation.Tests
{
    /// <summary>
    /// Builds benign and deliberately malicious test archives directly via
    /// <see cref="System.IO.Compression"/>, reproducing the same techniques used
    /// empirically during this component's design (zip-slip entries, a corrupted-CRC
    /// entry, an artificially high compression ratio).
    /// </summary>
    internal static class ArchiveFixtures
    {
        internal static string CreateBenignFixture(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "readme.txt", "hello world");
                WriteEntry(archive, "data/values.csv", "a,b,c\n1,2,3\n");
                var dirEntry = archive.CreateEntry("empty-dir/");
                dirEntry.LastWriteTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            }
            return path;
        }

        internal static string CreateEmptyFixture(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // No entries.
            }
            return path;
        }

        internal static string CreateZipSlipFixture(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "../../evil-relative.txt", "pwned via relative traversal");
                WriteEntry(archive, "/evil-rooted.txt", "pwned via rooted path");
                WriteEntry(archive, "safe.txt", "this one is fine");
            }
            return path;
        }

        /// <summary>One entry with an artificially high compression ratio (a long run of a repeated byte compresses extremely well).</summary>
        internal static string CreateHighRatioBombFixture(string path, int uncompressedSize = 50_000_000)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("bomb.bin", CompressionLevel.Optimal);
                using Stream s = entry.Open();
                byte[] chunk = new byte[65536];
                int written = 0;
                while (written < uncompressedSize)
                {
                    int toWrite = Math.Min(chunk.Length, uncompressedSize - written);
                    s.Write(chunk, 0, toWrite);
                    written += toWrite;
                }
            }
            return path;
        }

        /// <summary>Many small entries whose summed declared size exceeds a total-size limit without any single entry doing so alone.</summary>
        internal static string CreateManyEntriesSizeBombFixture(string path, int entryCount = 20, int perEntryBytes = 1_000_000)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var rng = new Random(12345);
                for (int i = 0; i < entryCount; i++)
                {
                    ZipArchiveEntry entry = archive.CreateEntry($"part-{i}.bin", CompressionLevel.NoCompression);
                    using Stream s = entry.Open();
                    byte[] data = new byte[perEntryBytes];
                    rng.NextBytes(data); // random, so it doesn't itself trip a ratio limit
                    s.Write(data, 0, data.Length);
                }
            }
            return path;
        }

        /// <summary>A stored (uncompressed) entry whose on-disk content is corrupted after the fact, so its declared CRC-32 no longer matches. Reproduces this component's design-time empirical CRC finding.</summary>
        internal static string CreateCorruptedCrcFixture(string path, out string entryName)
        {
            entryName = "data.txt";
            const string content = "hello world, this is some test content for crc checking";
            using (var fs = new FileStream(path, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(archive, entryName, content, CompressionLevel.NoCompression);
            }

            byte[] raw = File.ReadAllBytes(path);
            byte[] needle = Encoding.ASCII.GetBytes(entryName);
            int nameIndex = IndexOf(raw, needle, 0);
            if (nameIndex < 0)
                throw new InvalidOperationException("Fixture construction failed: entry name not found in raw zip bytes.");
            int flipAt = nameIndex + needle.Length + 5;
            raw[flipAt] ^= 0xFF;
            File.WriteAllBytes(path, raw);
            return path;
        }

        /// <summary>
        /// A single-entry archive with the ZIP general-purpose bit flag's encryption bit
        /// (bit 0) set directly in both the local file header and the central directory
        /// header, so <c>ZipArchiveEntry.IsEncrypted</c> reports true. .NET's own
        /// <see cref="ZipArchive"/> writer has no encryption support at all, so this is
        /// constructed via raw byte manipulation rather than real encryption - the entry's
        /// content is plain, unencrypted text; only the flag is set. This is sufficient for
        /// testing detection/skip logic, since ArchiveUtils never calls <c>entry.Open()</c>
        /// on an encrypted entry regardless of what real encrypted content would do.
        /// </summary>
        internal static string CreateEncryptedFlagFixture(string path, out string entryName)
        {
            entryName = "secret.txt";
            using (var fs = new FileStream(path, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(archive, entryName, "not actually encrypted, only flagged as such");
            }

            byte[] raw = File.ReadAllBytes(path);
            SetGeneralPurposeBitFlagBit0(raw, 0x04034b50, flagOffsetFromSignature: 6);  // local file header
            SetGeneralPurposeBitFlagBit0(raw, 0x02014b50, flagOffsetFromSignature: 8);  // central directory header
            File.WriteAllBytes(path, raw);
            return path;
        }

        private static void SetGeneralPurposeBitFlagBit0(byte[] raw, uint signature, int flagOffsetFromSignature)
        {
            byte[] sig = BitConverter.GetBytes(signature);
            int sigIndex = IndexOf(raw, sig, 0);
            if (sigIndex < 0)
                throw new InvalidOperationException($"Fixture construction failed: signature 0x{signature:X8} not found in raw zip bytes.");
            int flagPos = sigIndex + flagOffsetFromSignature;
            raw[flagPos] |= 0x01;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            for (int i = start; i + needle.Length <= haystack.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static void WriteEntry(ZipArchive archive, string entryName, string content, CompressionLevel level = CompressionLevel.Optimal)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, level);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
