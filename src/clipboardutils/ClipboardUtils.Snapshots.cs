using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;

namespace ClipboardAutomation
{
    public partial class ClipboardUtils
    {
        #region Snapshots

        /// <summary>
        /// Keeps a copy of <b>everything</b> on the clipboard - text, images, formatted text, files, and every other
        /// format - under a name, so it can be put back later with <see cref="RestoreClipboard"/>. The copy is held in
        /// this component's memory only.
        /// </summary>
        /// <param name="snapshotName">A name for the copy: not empty, at most 64 characters, unique ignoring case. Saving under a name that is already used replaces that copy.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <param name="requireCompleteCopy"><c>true</c> to fail, and keep nothing, if part of the clipboard cannot be copied and so would not come back; <c>false</c> (the default) to keep what can be copied. <see cref="GetSnapshotInfoJson"/> lists anything left out.</param>
        /// <returns><c>true</c> if the copy was kept; <c>false</c> if the clipboard could not be read, is bigger than <see cref="MaximumClipboardMegabytes"/> (or the copies together would be), or the copy was incomplete and <paramref name="requireCompleteCopy"/> was set. An empty clipboard is a valid copy (restoring it empties the clipboard). Never throws.</returns>
        /// <remarks>
        /// Saving does not change the clipboard. A copy can hold whatever was on the clipboard, including anything
        /// sensitive; discard it with <see cref="DiscardSnapshot"/> when it is no longer needed (disposing the
        /// component overwrites them all). Formats that hold a GDI handle rather than data cannot be copied; Windows
        /// rebuilds the usual ones (a bitmap from the DIB that is also there), so those are not a loss.
        /// </remarks>
        [Category("Clipboard - Snapshots")]
        [Description("Keeps a copy of everything on the clipboard (all formats) under a name, to restore later. Returns True on success; never throws.")]
        public bool SaveClipboard(string snapshotName, out string message, bool requireCompleteCopy = false)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!TryNormalizeName(snapshotName, out string name, out message))
                    return false;

                long limit = MaximumBytes;
                if (!TryRunBounded("Saving the clipboard", () =>
                    {
                        bool ok = _engine.TryCapture(limit, out ClipboardSnapshot copy, out string error);
                        return new CaptureResult { Ok = ok, Snapshot = copy, Error = error };
                    }, out CaptureResult captured, out message))
                    return false;
                if (!captured.Ok)
                {
                    message = captured.Error;
                    return false;
                }

                ClipboardSnapshot snapshot = captured.Snapshot;
                if (requireCompleteCopy && snapshot.HasLoss)
                {
                    message = "Part of the clipboard cannot be copied, so nothing was kept: " + DescribeLosses(snapshot) + ".";
                    snapshot.Wipe();
                    return false;
                }

                lock (_lock)
                {
                    if (_disposed)
                    {
                        snapshot.Wipe();
                        message = "The component has been disposed.";
                        return false;
                    }

                    _snapshots.TryGetValue(name, out ClipboardSnapshot previous);
                    long total = _storedBytes - (previous == null ? 0 : previous.TotalBytes) + snapshot.TotalBytes;
                    if (total > _maximumMegabytes * 1024L * 1024L)
                    {
                        message = "Keeping this copy would bring the saved clipboard data to " + ClipboardEngine.FormatSize(total)
                            + ", over the " + _maximumMegabytes + " MB limit (MaximumClipboardMegabytes). Discard a snapshot or raise the limit.";
                        snapshot.Wipe();
                        return false;
                    }

                    _snapshots[name] = snapshot;
                    _storedBytes = total;
                    previous?.Wipe();
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SaveClipboard", ex);
                return false;
            }
        }

        /// <summary>
        /// Puts a saved copy back on the clipboard, replacing whatever is there, format by format in the order they
        /// were originally offered. The copy is kept, so it can be restored again.
        /// </summary>
        /// <param name="snapshotName">The name the copy was saved under.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason, naming any format that could not be put back.</param>
        /// <returns><c>true</c> if everything that was copied is back on the clipboard; <c>false</c> if there is no such copy, or the clipboard could not be restored in full. Never throws.</returns>
        [Category("Clipboard - Snapshots")]
        [Description("Puts a saved clipboard copy back, replacing the clipboard's contents in every format. The copy is kept. Returns True on success; never throws.")]
        public bool RestoreClipboard(string snapshotName, out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!TryNormalizeName(snapshotName, out string name, out message))
                    return false;

                // Entered before the lock below (never inside it): the history's lock is always taken first.
                using (OwnOperation())
                {
                    // The lock is held throughout so that discarding the copy cannot overwrite its bytes mid-restore.
                    lock (_lock)
                    {
                        if (!_snapshots.TryGetValue(name, out ClipboardSnapshot snapshot))
                        {
                            message = "There is no snapshot named '" + name + "'.";
                            return false;
                        }

                        if (!TryRunBounded("Restoring the clipboard", () =>
                            {
                                bool ok = _engine.TryRestore(snapshot, out string error);
                                return new SimpleResult { Ok = ok, Error = error };
                            }, out SimpleResult result, out message))
                            return false;

                        message = result.Ok ? null : result.Error;
                        return result.Ok;
                    }
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("RestoreClipboard", ex);
                return false;
            }
        }

        /// <summary>Whether a copy with this name is being kept.</summary>
        /// <param name="snapshotName">The name to look for.</param>
        /// <param name="exists"><c>true</c> if there is such a copy.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the question could be answered; <c>false</c> if the name is not a valid one. Never throws.</returns>
        [Category("Clipboard - Snapshots")]
        [Description("Whether a saved clipboard copy with this name exists. Returns True if it could be answered; never throws.")]
        public bool HasSnapshot(string snapshotName, out bool exists, out string message)
        {
            exists = false;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!TryNormalizeName(snapshotName, out string name, out message))
                    return false;
                lock (_lock)
                    exists = _snapshots.ContainsKey(name);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                exists = false;
                message = NeverThrowsGuard.Failure("HasSnapshot", ex);
                return false;
            }
        }

        /// <summary>Discards a saved copy, overwriting its bytes in memory.</summary>
        /// <param name="snapshotName">The name the copy was saved under.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the copy existed and was discarded; <c>false</c> if there is no such copy. Never throws.</returns>
        [Category("Clipboard - Snapshots")]
        [Description("Discards a saved clipboard copy. Returns True if it existed; never throws.")]
        public bool DiscardSnapshot(string snapshotName, out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!TryNormalizeName(snapshotName, out string name, out message))
                    return false;
                lock (_lock)
                {
                    if (!_snapshots.TryGetValue(name, out ClipboardSnapshot snapshot))
                    {
                        message = "There is no snapshot named '" + name + "'.";
                        return false;
                    }
                    _snapshots.Remove(name);
                    _storedBytes -= snapshot.TotalBytes;
                    snapshot.Wipe();
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DiscardSnapshot", ex);
                return false;
            }
        }

        /// <summary>Discards every saved copy, overwriting their bytes in memory.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success (including when there were none); <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Clipboard - Snapshots")]
        [Description("Discards every saved clipboard copy. Returns True on success; never throws.")]
        public bool ClearSnapshots(out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                lock (_lock)
                {
                    foreach (ClipboardSnapshot snapshot in _snapshots.Values)
                        snapshot.Wipe();
                    _snapshots.Clear();
                    _storedBytes = 0;
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ClearSnapshots", ex);
                return false;
            }
        }

        /// <summary>Describes one saved copy as JSON: what it holds, what was left out and why.</summary>
        /// <param name="snapshotName">The name the copy was saved under.</param>
        /// <param name="infoJson">A JSON object with <c>name</c>, <c>capturedUtc</c>, <c>formatCount</c>, <c>totalBytes</c>, <c>complete</c> (false if something was left out that will not come back), <c>formats</c> (<c>id</c>, <c>name</c>, <c>bytes</c>) and <c>skipped</c> (<c>id</c>, <c>name</c>, <c>reason</c>, <c>loss</c>); empty if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the copy exists; <c>false</c> if there is no such copy. Never throws.</returns>
        [Category("Clipboard - Snapshots")]
        [Description("Describes a saved clipboard copy as JSON: its formats and sizes, and any left out. Returns True if it exists; never throws.")]
        public bool GetSnapshotInfoJson(string snapshotName, out string infoJson, out string message)
        {
            infoJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!TryNormalizeName(snapshotName, out string name, out message))
                    return false;
                lock (_lock)
                {
                    if (!_snapshots.TryGetValue(name, out ClipboardSnapshot snapshot))
                    {
                        message = "There is no snapshot named '" + name + "'.";
                        return false;
                    }
                    infoJson = JsonSerializer.Serialize(Describe(name, snapshot, includeFormats: true), JsonOptions);
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                infoJson = string.Empty;
                message = NeverThrowsGuard.Failure("GetSnapshotInfoJson", ex);
                return false;
            }
        }

        /// <summary>Lists the saved copies, oldest first, as JSON.</summary>
        /// <param name="snapshotsJson">A JSON array (<c>[]</c> if there are none) of <c>name</c>, <c>capturedUtc</c>, <c>formatCount</c>, <c>totalBytes</c> and <c>complete</c>; empty if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Clipboard - Snapshots")]
        [Description("Lists the saved clipboard copies as JSON. Returns True on success; never throws.")]
        public bool ListSnapshotsJson(out string snapshotsJson, out string message)
        {
            snapshotsJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                lock (_lock)
                {
                    var items = _snapshots
                        .OrderBy(pair => pair.Value.CapturedUtc)
                        .Select(pair => Describe(pair.Key, pair.Value, includeFormats: false))
                        .ToList();
                    snapshotsJson = JsonSerializer.Serialize(items, JsonOptions);
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                snapshotsJson = string.Empty;
                message = NeverThrowsGuard.Failure("ListSnapshotsJson", ex);
                return false;
            }
        }

        private static object Describe(string name, ClipboardSnapshot snapshot, bool includeFormats)
        {
            if (!includeFormats)
            {
                return new
                {
                    name,
                    capturedUtc = snapshot.CapturedUtc.ToString("o"),
                    formatCount = snapshot.Entries.Count,
                    totalBytes = snapshot.TotalBytes,
                    complete = !snapshot.HasLoss
                };
            }

            return new
            {
                name,
                capturedUtc = snapshot.CapturedUtc.ToString("o"),
                formatCount = snapshot.Entries.Count,
                totalBytes = snapshot.TotalBytes,
                complete = !snapshot.HasLoss,
                formats = snapshot.Entries.Select(e => new { id = e.Id, name = e.Name, bytes = e.Data.Length }).ToList(),
                skipped = snapshot.Skipped.Select(s => new { id = s.Id, name = s.Name, reason = s.Reason, loss = s.IsLoss }).ToList()
            };
        }

        private static bool TryNormalizeName(string snapshotName, out string name, out string message)
        {
            name = (snapshotName ?? string.Empty).Trim();
            message = null;
            if (name.Length == 0)
            {
                message = "snapshotName may not be empty.";
                return false;
            }
            if (name.Length > MaxNameLength)
            {
                message = "snapshotName may be at most " + MaxNameLength + " characters.";
                return false;
            }
            return true;
        }

        #endregion
    }
}
