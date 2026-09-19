using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ClipboardAutomation
{
    /// <summary>What one clipboard change looked like when the history noticed it.</summary>
    internal sealed class ClipboardHistoryItem
    {
        public DateTime CapturedUtc { get; set; }

        /// <summary>The names of the formats on the clipboard, in the order they were offered.</summary>
        public List<string> FormatNames { get; } = new List<string>();

        /// <summary>The text, or <c>null</c> if the clipboard held none.</summary>
        public string Text { get; set; }

        /// <summary>The files, if the clipboard held a file list.</summary>
        public List<string> Files { get; } = new List<string>();

        public FileDropEffect Effect { get; set; } = FileDropEffect.Copy;

        /// <summary>Every format, when the history keeps all of them; <c>null</c> in text-only mode.</summary>
        public ClipboardSnapshot Snapshot { get; set; }

        /// <summary>Roughly how much memory the item holds, for the size limit.</summary>
        public long Bytes { get; set; }

        /// <summary>A fingerprint of the content, so copying the same thing twice in a row is not two items.</summary>
        public string Hash { get; set; }

        /// <summary>Whether everything on the clipboard was kept (false if a format could not be copied).</summary>
        public bool Complete { get; set; } = true;

        /// <summary>Releases what the item holds; the bytes of a snapshot are overwritten.</summary>
        public void Wipe()
        {
            Snapshot?.Wipe();
            Snapshot = null;
            Text = null;
            Files.Clear();
        }
    }

    internal enum HistoryCaptureOutcome
    {
        /// <summary>An item was captured.</summary>
        Captured,
        /// <summary>The clipboard was empty, so there is nothing to record.</summary>
        Empty,
        /// <summary>The content is marked to be kept out of clipboard history, and was not read.</summary>
        Excluded,
        /// <summary>The content is bigger than the limit.</summary>
        TooLarge,
        /// <summary>The clipboard could not be opened or read.</summary>
        Failed
    }

    internal sealed partial class ClipboardEngine
    {
        /// <summary>
        /// Reads what is on the clipboard now as one history item, in a single open of the clipboard. Content that is
        /// marked to stay out of clipboard history (what a password manager sets) is not read at all.
        /// </summary>
        internal HistoryCaptureOutcome TryCaptureHistoryItem(ClipboardHistoryMode mode, long maxBytes, out ClipboardHistoryItem item, out string error)
        {
            item = null;
            if (!_api.Open(out error))
                return HistoryCaptureOutcome.Failed;
            try
            {
                IReadOnlyList<uint> ids = _api.EnumerateFormats();
                if (ids.Count == 0)
                {
                    error = null;
                    return HistoryCaptureOutcome.Empty;
                }
                if (IsMarkedExcluded(ids))
                {
                    error = null;
                    return HistoryCaptureOutcome.Excluded;
                }

                var result = new ClipboardHistoryItem { CapturedUtc = DateTime.UtcNow };
                foreach (uint id in ids)
                    result.FormatNames.Add(NameOf(id));

                if (mode == ClipboardHistoryMode.AllFormats)
                {
                    if (!CaptureOpen(ids, maxBytes, out ClipboardSnapshot snapshot, out error))
                        return LooksTooLarge(error) ? HistoryCaptureOutcome.TooLarge : HistoryCaptureOutcome.Failed;
                    result.Snapshot = snapshot;
                    result.Bytes = snapshot.TotalBytes;
                    result.Complete = !snapshot.HasLoss;
                    result.Hash = HashSnapshot(snapshot);
                    ReadTextAndFilesFrom(snapshot, result); // so the text and file list can be read without restoring
                    error = null;
                    item = result;
                    return HistoryCaptureOutcome.Captured;
                }

                long remaining = maxBytes;
                if (ReadTextOpen(remaining, out string text, out bool hasText, out error))
                {
                    if (hasText)
                    {
                        result.Text = text;
                        result.Bytes += text.Length * 2L;
                    }
                }
                else
                {
                    return LooksTooLarge(error) ? HistoryCaptureOutcome.TooLarge : HistoryCaptureOutcome.Failed;
                }

                if (ReadFileListOpen(maxBytes, out List<string> files, out FileDropEffect effect, out error))
                {
                    result.Files.AddRange(files);
                    result.Effect = effect;
                    foreach (string path in files)
                        result.Bytes += path.Length * 2L;
                }
                else
                {
                    return LooksTooLarge(error) ? HistoryCaptureOutcome.TooLarge : HistoryCaptureOutcome.Failed;
                }

                if (result.Bytes > maxBytes)
                {
                    error = "The clipboard's content is over the " + FormatSize(maxBytes) + " limit.";
                    return HistoryCaptureOutcome.TooLarge;
                }

                result.Hash = HashText(result);
                error = null;
                item = result;
                return HistoryCaptureOutcome.Captured;
            }
            finally
            {
                _api.Close();
            }
        }

        /// <summary>Fills an item's text and files from formats a snapshot has already copied, without touching the clipboard again.</summary>
        private static void ReadTextAndFilesFrom(ClipboardSnapshot snapshot, ClipboardHistoryItem item)
        {
            ClipboardEntry unicode = null, ansi = null, drop = null, effect = null;
            foreach (ClipboardEntry entry in snapshot.Entries)
            {
                if (entry.Id == ClipboardFormats.CF_UNICODETEXT) unicode = entry;
                else if (entry.Id == ClipboardFormats.CF_TEXT) ansi = entry;
                else if (entry.Id == ClipboardFormats.CF_HDROP) drop = entry;
                else if (ClipboardFormats.IsRegistered(entry.Id)
                    && string.Equals(entry.Name, ClipboardFormats.PreferredDropEffectName, StringComparison.OrdinalIgnoreCase))
                    effect = entry;
            }

            if (unicode != null)
                item.Text = DecodeUnicode(unicode.Data);
            else if (ansi != null)
                item.Text = DropFileList.DecodeAnsi(ansi.Data, 0, IndexOfZero(ansi.Data));

            if (drop != null && DropFileList.TryParse(drop.Data, out List<string> files, out _))
            {
                item.Files.AddRange(files);
                if (effect != null && effect.Data.Length >= 4)
                    item.Effect = ToEffect(BitConverter.ToUInt32(effect.Data, 0));
            }
        }

        /// <summary>Puts a history item back: everything, if it kept every format; otherwise its files, or else its text.</summary>
        internal bool TryRestoreHistoryItem(ClipboardHistoryItem item, out string error)
        {
            if (item.Snapshot != null)
                return TryRestore(item.Snapshot, out error);
            if (item.Files.Count > 0)
                return TrySetFileDropList(item.Files, item.Effect, out error);
            if (item.Text != null)
                return TrySetText(item.Text, false, out error);

            error = "This item kept neither text nor files (its formats were " + string.Join(", ", item.FormatNames)
                + "); start the history in AllFormats mode to be able to restore such content.";
            return false;
        }

        /// <summary>
        /// Whether the content asks not to be recorded: <c>ExcludeClipboardContentFromMonitorProcessing</c> is present
        /// (whatever its data), or <c>CanIncludeInClipboardHistory</c> is present and 0. Password managers set these.
        /// </summary>
        private bool IsMarkedExcluded(IReadOnlyList<uint> ids)
        {
            uint exclude = _api.RegisterFormat(ClipboardFormats.ExcludeFromMonitorName);
            if (exclude != 0 && Contains(ids, exclude))
                return true;

            uint canInclude = _api.RegisterFormat(ClipboardFormats.CanIncludeInHistoryName);
            if (canInclude != 0 && Contains(ids, canInclude))
            {
                ReadResult read = _api.ReadFormat(canInclude, 16);
                if (read.Outcome == ReadOutcome.Ok && read.Data.Length >= 4 && BitConverter.ToUInt32(read.Data, 0) == 0)
                    return true;
            }
            return false;
        }

        private static bool Contains(IReadOnlyList<uint> ids, uint id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                    return true;
            }
            return false;
        }

        private static bool LooksTooLarge(string error) =>
            error != null && (error.IndexOf("does not fit in the", StringComparison.Ordinal) >= 0
                              || error.IndexOf(" is over the ", StringComparison.Ordinal) >= 0
                              || error.IndexOf("content is over the", StringComparison.Ordinal) >= 0);

        private static string HashText(ClipboardHistoryItem item)
        {
            using (var hash = SHA256.Create())
            {
                var builder = new StringBuilder();
                builder.Append(item.Text ?? "\0none").Append('').Append(item.Effect);
                foreach (string file in item.Files)
                    builder.Append('').Append(file);
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
            }
        }

        private static string HashSnapshot(ClipboardSnapshot snapshot)
        {
            using (var hash = SHA256.Create())
            {
                foreach (ClipboardEntry entry in snapshot.Entries)
                {
                    byte[] id = BitConverter.GetBytes(entry.Id);
                    hash.TransformBlock(id, 0, id.Length, null, 0);
                    hash.TransformBlock(entry.Data, 0, entry.Data.Length, null, 0);
                }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return Convert.ToBase64String(hash.Hash);
            }
        }
    }
}
