using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipboardAutomation
{
    /// <summary>One clipboard format and the bytes it held.</summary>
    internal sealed class ClipboardEntry
    {
        public uint Id { get; set; }

        /// <summary>The registered name for a registered format (needed to restore it by name), or the <c>CF_</c> name for a predefined one.</summary>
        public string Name { get; set; }

        public byte[] Data { get; set; }
    }

    /// <summary>A format that a snapshot did not copy, and why.</summary>
    internal sealed class SkippedFormat
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public string Reason { get; set; }

        /// <summary>
        /// Whether skipping it loses something. A format that Windows rebuilds on its own from another one
        /// that was copied (<c>CF_BITMAP</c> from <c>CF_DIB</c>) is skipped without loss.
        /// </summary>
        public bool IsLoss { get; set; }
    }

    /// <summary>Everything that was on the clipboard, in the order the owner offered it.</summary>
    internal sealed class ClipboardSnapshot
    {
        public List<ClipboardEntry> Entries { get; } = new List<ClipboardEntry>();
        public List<SkippedFormat> Skipped { get; } = new List<SkippedFormat>();
        public DateTime CapturedUtc { get; set; }

        public long TotalBytes
        {
            get
            {
                long total = 0;
                foreach (var entry in Entries)
                    total += entry.Data.LongLength;
                return total;
            }
        }

        /// <summary>Whether something that was on the clipboard could not be copied and so will not come back.</summary>
        public bool HasLoss
        {
            get
            {
                foreach (var skipped in Skipped)
                {
                    if (skipped.IsLoss)
                        return true;
                }
                return false;
            }
        }

        /// <summary>Overwrites the copied bytes, so a snapshot that held something sensitive does not linger in memory once it is discarded.</summary>
        public void Wipe()
        {
            foreach (var entry in Entries)
            {
                if (entry.Data != null)
                    Array.Clear(entry.Data, 0, entry.Data.Length);
            }
            Entries.Clear();
        }
    }

    /// <summary>The predefined clipboard formats and what can be done with each of them.</summary>
    internal static class ClipboardFormats
    {
        internal const uint CF_TEXT = 1;
        internal const uint CF_BITMAP = 2;
        internal const uint CF_METAFILEPICT = 3;
        internal const uint CF_OEMTEXT = 7;
        internal const uint CF_DIB = 8;
        internal const uint CF_PALETTE = 9;
        internal const uint CF_UNICODETEXT = 13;
        internal const uint CF_ENHMETAFILE = 14;
        internal const uint CF_HDROP = 15;
        internal const uint CF_LOCALE = 16;
        internal const uint CF_DIBV5 = 17;
        internal const uint CF_OWNERDISPLAY = 0x80;
        internal const uint CF_DSPTEXT = 0x81;
        internal const uint CF_DSPBITMAP = 0x82;
        internal const uint CF_DSPMETAFILEPICT = 0x83;
        internal const uint CF_DSPENHMETAFILE = 0x8E;

        /// <summary>Formats registered by name (<c>RegisterClipboardFormat</c>) are numbered from here.</summary>
        internal const uint FirstRegistered = 0xC000;

        internal const string PreferredDropEffectName = "Preferred DropEffect";
        internal const string ExcludeFromMonitorName = "ExcludeClipboardContentFromMonitorProcessing";
        internal const string CanIncludeInHistoryName = "CanIncludeInClipboardHistory";
        internal const string CanUploadToCloudName = "CanUploadToCloudClipboard";

        private static readonly Dictionary<uint, string> Names = new Dictionary<uint, string>
        {
            { 1, "CF_TEXT" }, { 2, "CF_BITMAP" }, { 3, "CF_METAFILEPICT" }, { 4, "CF_SYLK" }, { 5, "CF_DIF" },
            { 6, "CF_TIFF" }, { 7, "CF_OEMTEXT" }, { 8, "CF_DIB" }, { 9, "CF_PALETTE" }, { 10, "CF_PENDATA" },
            { 11, "CF_RIFF" }, { 12, "CF_WAVE" }, { 13, "CF_UNICODETEXT" }, { 14, "CF_ENHMETAFILE" },
            { 15, "CF_HDROP" }, { 16, "CF_LOCALE" }, { 17, "CF_DIBV5" },
            { 0x80, "CF_OWNERDISPLAY" }, { 0x81, "CF_DSPTEXT" }, { 0x82, "CF_DSPBITMAP" },
            { 0x83, "CF_DSPMETAFILEPICT" }, { 0x8E, "CF_DSPENHMETAFILE" }
        };

        /// <summary>The <c>CF_</c> name of a predefined format, or <c>null</c> if the number is not one.</summary>
        internal static string StandardName(uint id)
        {
            if (Names.TryGetValue(id, out string name))
                return name;
            if (id >= 0x200 && id <= 0x2FF)
                return "CF_PRIVATE_0x" + id.ToString("X", CultureInfo.InvariantCulture);
            if (id >= 0x300 && id <= 0x3FF)
                return "CF_GDIOBJ_0x" + id.ToString("X", CultureInfo.InvariantCulture);
            return null;
        }

        /// <summary>
        /// Reads a predefined format from its <c>CF_</c> name (with or without the prefix, any case) or from a
        /// number (<c>13</c> or <c>0x0D</c>).
        /// </summary>
        internal static bool TryParseStandard(string text, out uint id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();

            foreach (var pair in Names)
            {
                if (string.Equals(pair.Value, text, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Value.Substring(3), text, StringComparison.OrdinalIgnoreCase))
                {
                    id = pair.Key;
                    return true;
                }
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id) && id != 0 && id < FirstRegistered;
            return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0 && id < FirstRegistered;
        }

        internal static bool IsRegistered(uint id) => id >= FirstRegistered && id <= 0xFFFF;

        /// <summary>
        /// Formats OLE adds to the clipboard to describe the OLE data object of the process that set it
        /// (<c>OleSetClipboard</c>). They point at that process's live object, so once the clipboard has changed they are
        /// stale, and putting them back would send readers looking for an object that is gone. The data itself is
        /// separate and is copied. They are left out of a snapshot on purpose, and that is not a loss.
        /// </summary>
        internal static bool IsOleBookkeeping(string registeredName)
        {
            return string.Equals(registeredName, "DataObject", StringComparison.OrdinalIgnoreCase)
                || string.Equals(registeredName, "Ole Private Data", StringComparison.OrdinalIgnoreCase)
                || string.Equals(registeredName, "OleClipboardPersistOnFlush", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Formats whose data is a GDI object handle (or holds one) rather than a block of memory, so their
        /// bytes cannot simply be copied. <c>CF_ENHMETAFILE</c> is a handle too, but it is copied through
        /// <c>GetEnhMetaFileBits</c>, so it is not listed here.
        /// </summary>
        internal static bool IsHandleBased(uint id)
        {
            return id == CF_BITMAP || id == CF_METAFILEPICT || id == CF_PALETTE
                || id == CF_OWNERDISPLAY || id == CF_DSPBITMAP || id == CF_DSPMETAFILEPICT || id == CF_DSPENHMETAFILE
                || (id >= 0x300 && id <= 0x3FF);
        }

        /// <summary>
        /// Whether Windows rebuilds this handle-based format by itself from another format that is on the
        /// clipboard and will be copied, so leaving it out loses nothing: <c>CF_BITMAP</c> and
        /// <c>CF_PALETTE</c> from a DIB, <c>CF_METAFILEPICT</c> from an enhanced metafile.
        /// </summary>
        internal static bool IsRebuiltFrom(uint id, ICollection<uint> present)
        {
            if (id == CF_BITMAP || id == CF_PALETTE)
                return present.Contains(CF_DIB) || present.Contains(CF_DIBV5);
            if (id == CF_METAFILEPICT)
                return present.Contains(CF_ENHMETAFILE);
            return false;
        }
    }

    /// <summary>Reads and writes the <c>DROPFILES</c> block that a <c>CF_HDROP</c> file list is made of.</summary>
    internal static class DropFileList
    {
        private const int HeaderSize = 20;         // DWORD pFiles; POINT pt; BOOL fNC; BOOL fWide
        internal const int MaxFiles = 100000;

        /// <summary>
        /// Reads the file paths out of a <c>DROPFILES</c> block. Returns <c>false</c> (and no paths) if the block is
        /// too short to be one or says its list starts outside itself. A list that is not terminated properly is
        /// read as far as it goes.
        /// </summary>
        internal static bool TryParse(byte[] data, out List<string> paths, out string error)
        {
            paths = new List<string>();
            error = null;
            if (data == null || data.Length < HeaderSize)
            {
                error = "The file list is too short to be a DROPFILES block.";
                return false;
            }

            uint offset = BitConverter.ToUInt32(data, 0);
            bool wide = BitConverter.ToInt32(data, 16) != 0;
            if (offset < HeaderSize || offset > data.Length)
            {
                error = "The file list says its paths start outside the data.";
                return false;
            }

            int position = (int)offset;
            if (wide)
            {
                while (position + 1 < data.Length && paths.Count < MaxFiles)
                {
                    int end = position;
                    while (end + 1 < data.Length && !(data[end] == 0 && data[end + 1] == 0))
                        end += 2;
                    if (end == position)
                        break; // an empty string ends the list
                    paths.Add(Encoding.Unicode.GetString(data, position, end - position));
                    position = end + 2;
                }
            }
            else
            {
                while (position < data.Length && paths.Count < MaxFiles)
                {
                    int end = position;
                    while (end < data.Length && data[end] != 0)
                        end++;
                    if (end == position)
                        break;
                    paths.Add(DecodeAnsi(data, position, end - position));
                    position = end + 1;
                }
            }
            return true;
        }

        /// <summary>Builds a <c>DROPFILES</c> block (Unicode) for the paths.</summary>
        internal static byte[] Build(IReadOnlyList<string> paths)
        {
            int characters = 1; // the list's own terminator
            foreach (string path in paths)
                characters += path.Length + 1;

            var data = new byte[HeaderSize + characters * 2];
            BitConverter.GetBytes((uint)HeaderSize).CopyTo(data, 0);
            BitConverter.GetBytes(1).CopyTo(data, 16); // fWide

            int position = HeaderSize;
            foreach (string path in paths)
            {
                position += Encoding.Unicode.GetBytes(path, 0, path.Length, data, position);
                position += 2; // the string's terminator, already zero
            }
            return data;
        }

        /// <summary>Decodes text in the system's ANSI code page.</summary>
        internal static string DecodeAnsi(byte[] data, int index, int count)
        {
            if (count <= 0)
                return string.Empty;
            var copy = new byte[count + 1]; // room for a terminator
            Buffer.BlockCopy(data, index, copy, 0, count);
            GCHandle handle = GCHandle.Alloc(copy, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStringAnsi(handle.AddrOfPinnedObject()) ?? string.Empty;
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
