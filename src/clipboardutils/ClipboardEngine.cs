using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClipboardAutomation
{
    /// <summary>One entry of the clipboard's format list.</summary>
    internal sealed class FormatInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public bool IsRegistered { get; set; }
    }

    /// <summary>
    /// The clipboard logic, free of the real clipboard: taking and putting back a copy of everything on it, reading
    /// and writing text and file lists, and waiting for it to change. Every operation opens the clipboard, does its
    /// work, and closes it again, so the clipboard is never left open. Nothing here throws for an expected failure;
    /// each reports it through its <c>error</c> value.
    /// </summary>
    internal sealed partial class ClipboardEngine
    {
        private readonly IClipboardApi _api;

        internal ClipboardEngine(IClipboardApi api)
        {
            _api = api;
        }

        internal long SequenceNumber => _api.SequenceNumber;

        // ------------------------------------------------------------------ snapshots

        /// <summary>
        /// Copies every format on the clipboard. A format that cannot be copied is recorded in the snapshot with the
        /// reason rather than stopping the copy; one that is too big to fit in <paramref name="maxBytes"/> stops it,
        /// because leaving out a large image silently would be worse than refusing.
        /// </summary>
        internal bool TryCapture(long maxBytes, out ClipboardSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (!_api.Open(out error))
                return false;
            try
            {
                return CaptureOpen(_api.EnumerateFormats(), maxBytes, out snapshot, out error);
            }
            finally
            {
                _api.Close();
            }
        }

        /// <summary>The copying itself, for a clipboard that is already open.</summary>
        private bool CaptureOpen(IReadOnlyList<uint> ids, long maxBytes, out ClipboardSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            {
                var present = new HashSet<uint>(ids);
                var result = new ClipboardSnapshot { CapturedUtc = DateTime.UtcNow };
                long total = 0;

                foreach (uint id in ids)
                {
                    string name = NameOf(id);

                    if (ClipboardFormats.IsRebuiltFrom(id, present))
                    {
                        result.Skipped.Add(new SkippedFormat
                        {
                            Id = id, Name = name, IsLoss = false,
                            Reason = "Windows rebuilds it from another format that was copied."
                        });
                        continue;
                    }
                    if (ClipboardFormats.IsHandleBased(id))
                    {
                        result.Skipped.Add(new SkippedFormat
                        {
                            Id = id, Name = name, IsLoss = true,
                            Reason = "It holds a GDI handle rather than data that can be copied."
                        });
                        continue;
                    }
                    if (ClipboardFormats.IsRegistered(id) && ClipboardFormats.IsOleBookkeeping(name))
                    {
                        result.Skipped.Add(new SkippedFormat
                        {
                            Id = id, Name = name, IsLoss = false,
                            Reason = "It describes the setting application's live OLE data object, which is stale once the clipboard changes; the data itself is copied."
                        });
                        continue;
                    }

                    ReadResult read = _api.ReadFormat(id, maxBytes - total);
                    switch (read.Outcome)
                    {
                        case ReadOutcome.Ok:
                            total += read.Data.LongLength;
                            result.Entries.Add(new ClipboardEntry { Id = id, Name = name, Data = read.Data });
                            break;

                        case ReadOutcome.TooLarge:
                            result.Wipe();
                            error = "The clipboard's " + name + " data is " + FormatSize(read.Size) + ", which does not fit in the "
                                + FormatSize(maxBytes) + " limit (MaximumClipboardMegabytes) with what was already copied.";
                            return false;

                        default:
                            result.Skipped.Add(new SkippedFormat
                            {
                                Id = id, Name = name, IsLoss = true,
                                Reason = read.Reason ?? "It could not be read."
                            });
                            break;
                    }
                }

                snapshot = result;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Replaces the clipboard with the snapshot, format by format in the order they were offered. Formats that
        /// were registered by name are put back by name, since their numbers are not fixed. Carries on past a
        /// format that cannot be put back and names it in the error.
        /// </summary>
        internal bool TryRestore(ClipboardSnapshot snapshot, out string error)
        {
            if (!_api.Open(out error))
                return false;
            try
            {
                if (!_api.Empty(out error))
                    return false;

                var failures = new List<string>();
                var written = new List<KeyValuePair<uint, string>>();
                foreach (ClipboardEntry entry in snapshot.Entries)
                {
                    uint id = entry.Id;
                    if (ClipboardFormats.IsRegistered(id))
                    {
                        id = _api.RegisterFormat(entry.Name);
                        if (id == 0)
                        {
                            failures.Add(entry.Name + " (its name could not be registered)");
                            continue;
                        }
                    }

                    if (_api.WriteFormat(id, entry.Data, out string why))
                        written.Add(new KeyValuePair<uint, string>(id, entry.Name));
                    else
                        failures.Add(entry.Name + " (" + why + ")");
                }

                foreach (var format in written)
                {
                    if (!_api.IsFormatAvailable(format.Key))
                        failures.Add(format.Value + " (it is not on the clipboard afterwards)");
                }

                if (failures.Count > 0)
                {
                    error = "The clipboard was not fully restored: " + string.Join(", ", failures) + ".";
                    return false;
                }
                error = null;
                return true;
            }
            finally
            {
                _api.Close();
            }
        }

        // ------------------------------------------------------------------ text

        /// <summary>
        /// Reads the clipboard's text (Unicode if there is any, otherwise ANSI). <paramref name="available"/> says
        /// whether there was any text at all: an empty clipboard is a success with no text, not an error.
        /// </summary>
        internal bool TryGetText(long maxBytes, out string text, out bool available, out string error)
        {
            text = string.Empty;
            available = false;
            if (!_api.Open(out error))
                return false;
            try
            {
                return ReadTextOpen(maxBytes, out text, out available, out error);
            }
            finally
            {
                _api.Close();
            }
        }

        /// <summary>Reads the text, for a clipboard that is already open.</summary>
        private bool ReadTextOpen(long maxBytes, out string text, out bool available, out string error)
        {
            text = string.Empty;
            available = false;
            {
                uint id = _api.IsFormatAvailable(ClipboardFormats.CF_UNICODETEXT) ? ClipboardFormats.CF_UNICODETEXT
                    : _api.IsFormatAvailable(ClipboardFormats.CF_TEXT) ? ClipboardFormats.CF_TEXT
                    : 0;
                if (id == 0)
                {
                    error = null;
                    return true;
                }

                ReadResult read = _api.ReadFormat(id, maxBytes);
                if (read.Outcome == ReadOutcome.TooLarge)
                {
                    error = "The clipboard's text is " + FormatSize(read.Size) + ", which is over the " + FormatSize(maxBytes) + " limit.";
                    return false;
                }
                if (read.Outcome != ReadOutcome.Ok)
                {
                    error = "The clipboard would not give its text" + (string.IsNullOrEmpty(read.Reason) ? "." : ": " + read.Reason);
                    return false;
                }

                text = id == ClipboardFormats.CF_UNICODETEXT ? DecodeUnicode(read.Data) : DropFileList.DecodeAnsi(read.Data, 0, IndexOfZero(read.Data));
                available = true;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Replaces the clipboard with the text alone. With <paramref name="excludeFromHistory"/> it also marks the
        /// content so that Windows' clipboard history and cloud sync, and well-behaved clipboard monitors, skip it.
        /// </summary>
        internal bool TrySetText(string text, bool excludeFromHistory, out string error)
        {
            if (!_api.Open(out error))
                return false;
            try
            {
                if (!_api.Empty(out error))
                    return false;

                byte[] unicode = Encoding.Unicode.GetBytes(text);
                var withTerminator = new byte[unicode.Length + 2];
                Buffer.BlockCopy(unicode, 0, withTerminator, 0, unicode.Length);
                if (!_api.WriteFormat(ClipboardFormats.CF_UNICODETEXT, withTerminator, out error))
                    return false;

                if (excludeFromHistory)
                {
                    foreach (string name in new[] { ClipboardFormats.ExcludeFromMonitorName, ClipboardFormats.CanIncludeInHistoryName, ClipboardFormats.CanUploadToCloudName })
                    {
                        uint id = _api.RegisterFormat(name);
                        string why = null;
                        if (id == 0 || !_api.WriteFormat(id, new byte[4], out why))
                        {
                            error = "The text was set, but it could not be marked to stay out of the clipboard history (" + name + (id == 0 ? " could not be registered)." : ": " + why + ").");
                            return false;
                        }
                    }
                }

                error = null;
                return true;
            }
            finally
            {
                _api.Close();
            }
        }

        internal bool TryClear(out string error)
        {
            if (!_api.Open(out error))
                return false;
            try
            {
                return _api.Empty(out error);
            }
            finally
            {
                _api.Close();
            }
        }

        // ------------------------------------------------------------------ formats

        internal bool TryListFormats(out List<FormatInfo> formats, out string error)
        {
            formats = new List<FormatInfo>();
            if (!_api.Open(out error))
                return false;
            try
            {
                foreach (uint id in _api.EnumerateFormats())
                    formats.Add(new FormatInfo { Id = id, Name = NameOf(id), IsRegistered = ClipboardFormats.IsRegistered(id) });
                error = null;
                return true;
            }
            finally
            {
                _api.Close();
            }
        }

        /// <summary>Whether a format, named by its <c>CF_</c> name, a number, or the name it was registered under, is on the clipboard.</summary>
        internal bool TryIsFormatAvailable(string format, out bool available, out string error)
        {
            available = false;
            if (!TryResolveFormat(format, out uint id, out error))
                return false;
            available = _api.IsFormatAvailable(id);
            return true;
        }

        internal bool TryResolveFormat(string format, out uint id, out string error)
        {
            id = 0;
            error = null;
            if (string.IsNullOrWhiteSpace(format))
            {
                error = "formatName may not be empty.";
                return false;
            }
            if (ClipboardFormats.TryParseStandard(format, out id))
                return true;

            format = format.Trim();
            if (format.Length > 255)
            {
                error = "formatName may be at most 255 characters.";
                return false;
            }
            id = _api.RegisterFormat(format);
            if (id == 0)
            {
                error = "'" + format + "' is not a valid clipboard format name.";
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ files

        /// <summary>
        /// Reads the clipboard's file list and what a paste is asked to do with it. No file list is a success with an
        /// empty list; a clipboard that says nothing about the effect is read as Copy, which is what a paste does.
        /// </summary>
        internal bool TryGetFileDropList(long maxBytes, out List<string> paths, out FileDropEffect effect, out string error)
        {
            paths = new List<string>();
            effect = FileDropEffect.Copy;
            if (!_api.Open(out error))
                return false;
            try
            {
                return ReadFileListOpen(maxBytes, out paths, out effect, out error);
            }
            finally
            {
                _api.Close();
            }
        }

        /// <summary>Reads the file list, for a clipboard that is already open.</summary>
        private bool ReadFileListOpen(long maxBytes, out List<string> paths, out FileDropEffect effect, out string error)
        {
            paths = new List<string>();
            effect = FileDropEffect.Copy;
            {
                if (!_api.IsFormatAvailable(ClipboardFormats.CF_HDROP))
                {
                    error = null;
                    return true;
                }

                ReadResult read = _api.ReadFormat(ClipboardFormats.CF_HDROP, maxBytes);
                if (read.Outcome != ReadOutcome.Ok)
                {
                    error = read.Outcome == ReadOutcome.TooLarge
                        ? "The clipboard's file list is over the " + FormatSize(maxBytes) + " limit."
                        : "The clipboard would not give its file list" + (string.IsNullOrEmpty(read.Reason) ? "." : ": " + read.Reason);
                    return false;
                }
                if (!DropFileList.TryParse(read.Data, out paths, out error))
                {
                    paths = new List<string>();
                    return false;
                }

                uint effectId = _api.RegisterFormat(ClipboardFormats.PreferredDropEffectName);
                if (effectId != 0 && _api.IsFormatAvailable(effectId))
                {
                    ReadResult effectRead = _api.ReadFormat(effectId, 64);
                    if (effectRead.Outcome == ReadOutcome.Ok && effectRead.Data.Length >= 4)
                        effect = ToEffect(BitConverter.ToUInt32(effectRead.Data, 0));
                }
                error = null;
                return true;
            }
        }

        /// <summary>Replaces the clipboard with a file list and the effect a paste is to have.</summary>
        internal bool TrySetFileDropList(IReadOnlyList<string> paths, FileDropEffect effect, out string error)
        {
            if (!_api.Open(out error))
                return false;
            try
            {
                if (!_api.Empty(out error))
                    return false;
                if (!_api.WriteFormat(ClipboardFormats.CF_HDROP, DropFileList.Build(paths), out error))
                    return false;

                uint effectId = _api.RegisterFormat(ClipboardFormats.PreferredDropEffectName);
                string why = null;
                if (effectId == 0 || !_api.WriteFormat(effectId, BitConverter.GetBytes((uint)effect), out why))
                {
                    error = "The file list was set, but not whether to copy or move it" + (effectId == 0 ? "." : ": " + why);
                    return false;
                }
                error = null;
                return true;
            }
            finally
            {
                _api.Close();
            }
        }

        // ------------------------------------------------------------------ waiting

        /// <summary>Waits until the clipboard's sequence number is no longer <paramref name="baseline"/>. Returns <c>false</c> on timeout.</summary>
        internal bool WaitForChange(long baseline, int timeoutMs, int pollIntervalMs, Func<long> nowMs, Action<int> sleep)
        {
            return WaitUntil(() => _api.SequenceNumber != baseline, timeoutMs, pollIntervalMs, nowMs, sleep);
        }

        /// <summary>Waits until the format is on the clipboard. Returns <c>false</c> on timeout.</summary>
        internal bool WaitForFormat(uint id, int timeoutMs, int pollIntervalMs, Func<long> nowMs, Action<int> sleep)
        {
            return WaitUntil(() => _api.IsFormatAvailable(id), timeoutMs, pollIntervalMs, nowMs, sleep);
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs, int pollIntervalMs, Func<long> nowMs, Action<int> sleep)
        {
            if (condition())
                return true;

            long deadline = nowMs() + timeoutMs;
            while (true)
            {
                long remaining = deadline - nowMs();
                if (remaining <= 0)
                    return false;
                sleep((int)Math.Min(pollIntervalMs, remaining));
                if (condition())
                    return true;
            }
        }

        // ------------------------------------------------------------------ helpers

        private string NameOf(uint id)
        {
            return ClipboardFormats.StandardName(id)
                ?? _api.GetFormatName(id)
                ?? "0x" + id.ToString("X4", CultureInfo.InvariantCulture);
        }

        private static FileDropEffect ToEffect(uint value)
        {
            if ((value & (uint)FileDropEffect.Move) != 0)
                return FileDropEffect.Move;
            if ((value & (uint)FileDropEffect.Link) != 0 && (value & (uint)FileDropEffect.Copy) == 0)
                return FileDropEffect.Link;
            return FileDropEffect.Copy;
        }

        private static int IndexOfZero(byte[] data)
        {
            int index = Array.IndexOf(data, (byte)0);
            return index < 0 ? data.Length : index;
        }

        private static string DecodeUnicode(byte[] data)
        {
            int end = 0;
            while (end + 1 < data.Length && !(data[end] == 0 && data[end + 1] == 0))
                end += 2;
            return Encoding.Unicode.GetString(data, 0, end);
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L)
                return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024L)
                return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            return bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
        }
    }
}
