using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace ClipboardAutomation
{
    public partial class ClipboardUtils
    {
        private const int MaxHistoryItems = 1000;
        private const int MaxCaptureAttempts = 3;
        private const int MaxSettleRounds = 40;
        internal const int MinHistoryPollMs = 25;
        internal const int MaxHistoryPollMs = 60000;
        private const int TextPreviewLength = 120;

        private readonly object _historyLock = new object();

        // Serializes reading the clipboard for the history: the watcher and an own operation flushing a pending change never overlap.
        // Lock order, always: _pollLock, then _historyLock, then _lock.
        private readonly object _pollLock = new object();
        private readonly List<ClipboardHistoryItem> _history = new List<ClipboardHistoryItem>();   // oldest first
        private long _historyBytes;
        private Thread _historyThread;
        private CancellationTokenSource _historyCts;
        private int _historyMaxItems;
        private int _historyPollMs;
        private ClipboardHistoryMode _historyMode;

        // The sequence number of the change the history has dealt with (recorded, skipped, or made by this component).
        private long _handledSequence;
        private int _ownOperations;
        private int _captureAttempts;
        private int _historyRecorded;
        private int _historySkippedExcluded;
        private int _historySkippedTooLarge;
        private int _historyFailed;
        private string _historyLastError;

        #region History

        /// <summary>
        /// Starts keeping a history of the clipboard: every time something new is copied, it is added, and only the last
        /// <paramref name="maxItems"/> are kept. It watches from a background thread of its own, so the automation carries
        /// on while it runs. Read the history with <see cref="GetClipboardHistoryJson"/>, <see cref="GetClipboardHistoryText"/>
        /// and put an item back with <see cref="RestoreClipboardHistoryItem"/>.
        /// </summary>
        /// <param name="maxItems">How many items to keep, 1 to 1000. The oldest is dropped when a new one arrives.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <param name="mode"><see cref="ClipboardHistoryMode.TextOnly"/> (the default) keeps the text and file lists and never asks the copying application for anything else; <see cref="ClipboardHistoryMode.AllFormats"/> keeps every format so an item restores completely.</param>
        /// <param name="pollIntervalMs">How often to look for a change, in milliseconds (25 to 60,000; default 250).</param>
        /// <param name="captureCurrent"><c>true</c> to also record what is on the clipboard now; <c>false</c> (the default) to record only what is copied from now on.</param>
        /// <returns><c>true</c> if the history is running; <c>false</c> if it already is, a value is out of range, or the component is disposed. Never throws.</returns>
        /// <remarks>
        /// A history holds whatever was copied, which can include passwords. Content that asks to be kept out of clipboard
        /// history (what password managers set) is skipped without being read, and what this component itself puts on
        /// the clipboard (<see cref="PasteText"/>, <see cref="SetClipboardText"/>, <see cref="RestoreClipboard"/> and so on)
        /// is never recorded. Copying the same thing twice in a row is one item. Items count against
        /// <see cref="MaximumClipboardMegabytes"/> (separately from saved snapshots), and the oldest are dropped to stay under it.
        /// Stopping keeps the items; <see cref="ClearClipboardHistory"/> discards them.
        /// </remarks>
        [Category("Clipboard - History")]
        [Description("Starts keeping a history of the last N things copied to the clipboard, from a background thread. Text-only by default, or every format. Returns True if it started; never throws.")]
        public bool StartClipboardHistory(int maxItems, out string message, ClipboardHistoryMode mode = ClipboardHistoryMode.TextOnly,
            int pollIntervalMs = 250, bool captureCurrent = false)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (maxItems < 1 || maxItems > MaxHistoryItems)
                {
                    message = "maxItems must be between 1 and " + MaxHistoryItems + ".";
                    return false;
                }
                if (!Enum.IsDefined(typeof(ClipboardHistoryMode), mode))
                {
                    message = "mode is not a defined ClipboardHistoryMode value.";
                    return false;
                }
                if (pollIntervalMs < MinHistoryPollMs || pollIntervalMs > MaxHistoryPollMs)
                {
                    message = "pollIntervalMs must be between " + MinHistoryPollMs + " and " + MaxHistoryPollMs + ".";
                    return false;
                }

                lock (_historyLock)
                {
                    if (_historyThread != null)
                    {
                        message = "The clipboard history is already running; call StopClipboardHistory first to change its settings.";
                        return false;
                    }
                    if (IsDisposed(out message))
                        return false;

                    _historyMaxItems = maxItems;
                    _historyMode = mode;
                    _historyPollMs = pollIntervalMs;
                    _handledSequence = captureCurrent ? -1 : _engine.SequenceNumber;
                    _captureAttempts = 0;
                    _historyRecorded = 0;
                    _historySkippedExcluded = 0;
                    _historySkippedTooLarge = 0;
                    _historyFailed = 0;
                    _historyLastError = null;
                    TrimHistory();

                    var cts = new CancellationTokenSource();
                    var thread = new Thread(() => HistoryLoop(cts.Token, pollIntervalMs, mode))
                    {
                        IsBackground = true,
                        Name = "ClipboardUtils.History"
                    };
                    _historyCts = cts;
                    _historyThread = thread;
                    thread.Start();
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("StartClipboardHistory", ex);
                return false;
            }
        }

        /// <summary>Stops watching the clipboard. The items already recorded are kept.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success, including when it was not running; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Stops keeping the clipboard history. The items recorded so far are kept. Returns True on success, including when it was not running; never throws.")]
        public bool StopClipboardHistory(out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                StopHistoryWatcher();
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("StopClipboardHistory", ex);
                return false;
            }
        }

        /// <summary>How many items the history holds now.</summary>
        /// <param name="count">The number of items, 0 to the number asked to keep.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("How many items the clipboard history holds now. Returns True on success; never throws.")]
        public bool GetClipboardHistoryCount(out int count, out string message)
        {
            count = 0;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                lock (_historyLock)
                    count = _history.Count;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                count = 0;
                message = NeverThrowsGuard.Failure("GetClipboardHistoryCount", ex);
                return false;
            }
        }

        /// <summary>Describes the history's state as JSON: whether it is running, its settings, and what it has skipped or failed to read.</summary>
        /// <param name="statusJson">A JSON object with <c>running</c>, <c>mode</c>, <c>maxItems</c>, <c>pollIntervalMs</c>, <c>count</c>, <c>totalBytes</c>, <c>recorded</c> (added since it started), <c>skippedExcluded</c> (marked to stay out of clipboard history), <c>skippedTooLarge</c>, <c>failed</c> (could not be read after retries) and <c>lastError</c>; empty if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Describes the clipboard history's state as JSON: settings, size, and what it skipped or failed to read. Returns True on success; never throws.")]
        public bool GetClipboardHistoryStatusJson(out string statusJson, out string message)
        {
            statusJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                lock (_historyLock)
                {
                    statusJson = JsonSerializer.Serialize(new
                    {
                        running = _historyThread != null,
                        mode = _historyMode.ToString(),
                        maxItems = _historyMaxItems,
                        pollIntervalMs = _historyPollMs,
                        count = _history.Count,
                        totalBytes = _historyBytes,
                        recorded = _historyRecorded,
                        skippedExcluded = _historySkippedExcluded,
                        skippedTooLarge = _historySkippedTooLarge,
                        failed = _historyFailed,
                        lastError = _historyLastError ?? string.Empty
                    }, JsonOptions);
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                statusJson = string.Empty;
                message = NeverThrowsGuard.Failure("GetClipboardHistoryStatusJson", ex);
                return false;
            }
        }

        /// <summary>Lists the history, newest first, as JSON. Item 0 is the newest.</summary>
        /// <param name="maxEntries">How many of the newest items to list, 1 to 1000.</param>
        /// <param name="includeText"><c>true</c> to include each item's text (shortened) and file paths; <c>false</c> (the default) to leave them out, so logging the list cannot leak what was copied.</param>
        /// <param name="historyJson">A JSON array (<c>[]</c> if empty) of <c>index</c>, <c>capturedUtc</c>, <c>formats</c>, <c>hasText</c>, <c>textLength</c>, <c>fileCount</c>, <c>effect</c> (when there are files), <c>bytes</c>, <c>complete</c>, and with <paramref name="includeText"/> also <c>text</c> and <c>files</c>; empty if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="maxEntries"/> is out of range or the component is disposed. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Lists the clipboard history, newest first, as JSON. The text is left out unless includeText is True. Returns True on success; never throws.")]
        public bool GetClipboardHistoryJson(int maxEntries, out string historyJson, out string message, bool includeText = false)
        {
            historyJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (maxEntries < 1 || maxEntries > MaxHistoryItems)
                {
                    message = "maxEntries must be between 1 and " + MaxHistoryItems + ".";
                    return false;
                }

                lock (_historyLock)
                {
                    var items = new List<object>();
                    for (int index = 0; index < Math.Min(maxEntries, _history.Count); index++)
                        items.Add(DescribeHistoryItem(index, _history[_history.Count - 1 - index], includeText));
                    historyJson = JsonSerializer.Serialize(items, JsonOptions);
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                historyJson = string.Empty;
                message = NeverThrowsGuard.Failure("GetClipboardHistoryJson", ex);
                return false;
            }
        }

        /// <summary>Reads the text of one history item.</summary>
        /// <param name="index">Which item: 0 is the newest.</param>
        /// <param name="text">The text; an empty string if the item held none or this method returns <c>false</c>.</param>
        /// <param name="textAvailable"><c>true</c> if the item held text.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if there is no item at that index or the component is disposed. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Reads the text of one clipboard history item (0 is the newest). Returns True on success; never throws.")]
        public bool GetClipboardHistoryText(int index, out string text, out bool textAvailable, out string message)
        {
            text = string.Empty;
            textAvailable = false;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                lock (_historyLock)
                {
                    if (!TryGetHistoryItem(index, out ClipboardHistoryItem item, out message))
                        return false;
                    textAvailable = item.Text != null;
                    text = item.Text ?? string.Empty;
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                text = string.Empty;
                textAvailable = false;
                message = NeverThrowsGuard.Failure("GetClipboardHistoryText", ex);
                return false;
            }
        }

        /// <summary>
        /// Finds the first history item, newest first, whose text or file paths contain the given text. The index can be used
        /// with <see cref="GetClipboardHistoryText"/> and <see cref="RestoreClipboardHistoryItem"/> at once; indexes shift as
        /// new items arrive, so use it straight away.
        /// </summary>
        /// <param name="searchText">The text to look for; must not be empty.</param>
        /// <param name="index">The index of the match, or -1 if nothing matched (which is still a successful search).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <param name="matchCase"><c>true</c> to match upper and lower case exactly; <c>false</c> (the default) to ignore case.</param>
        /// <param name="startIndex">The index to start from (0, the newest, by default); pass the previous match plus one to find the next.</param>
        /// <returns><c>true</c> if the search ran (check <paramref name="index"/> for -1); <c>false</c> if <paramref name="searchText"/> is empty, <paramref name="startIndex"/> is negative, or the component is disposed. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Finds the first clipboard history item (newest first) whose text or file paths contain the given text; index is -1 if none does. Returns True if the search ran; never throws.")]
        public bool FindClipboardHistoryIndex(string searchText, out int index, out string message, bool matchCase = false, int startIndex = 0)
        {
            index = -1;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (string.IsNullOrEmpty(searchText))
                {
                    message = "searchText must not be empty.";
                    return false;
                }
                if (startIndex < 0)
                {
                    message = "startIndex must not be negative.";
                    return false;
                }

                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                lock (_historyLock)
                {
                    for (int i = startIndex; i < _history.Count; i++)
                    {
                        if (HistoryItemContains(_history[_history.Count - 1 - i], searchText, comparison))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                index = -1;
                message = NeverThrowsGuard.Failure("FindClipboardHistoryIndex", ex);
                return false;
            }
        }

        /// <summary>Lists the history items whose text or file paths contain the given text, newest first, as JSON. Each entry carries its <c>index</c> in the whole history.</summary>
        /// <param name="searchText">The text to look for; must not be empty.</param>
        /// <param name="historyJson">A JSON array (<c>[]</c> if nothing matched) in the same shape as <see cref="GetClipboardHistoryJson"/>; empty if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <param name="matchCase"><c>true</c> to match upper and lower case exactly; <c>false</c> (the default) to ignore case.</param>
        /// <param name="maxEntries">How many matches to list at most, 1 to 1000 (50 by default).</param>
        /// <param name="includeText"><c>true</c> to include each match's text (shortened) and file paths; <c>false</c> (the default) to leave them out.</param>
        /// <returns><c>true</c> if the search ran; <c>false</c> if an argument is out of range or the component is disposed. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Lists the clipboard history items whose text or file paths contain the given text, newest first, as JSON, each with its index. Returns True if the search ran; never throws.")]
        public bool SearchClipboardHistoryJson(string searchText, out string historyJson, out string message, bool matchCase = false, int maxEntries = 50, bool includeText = false)
        {
            historyJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (string.IsNullOrEmpty(searchText))
                {
                    message = "searchText must not be empty.";
                    return false;
                }
                if (maxEntries < 1 || maxEntries > MaxHistoryItems)
                {
                    message = "maxEntries must be between 1 and " + MaxHistoryItems + ".";
                    return false;
                }

                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                lock (_historyLock)
                {
                    var items = new List<object>();
                    for (int i = 0; i < _history.Count && items.Count < maxEntries; i++)
                    {
                        ClipboardHistoryItem item = _history[_history.Count - 1 - i];
                        if (HistoryItemContains(item, searchText, comparison))
                            items.Add(DescribeHistoryItem(i, item, includeText));
                    }
                    historyJson = JsonSerializer.Serialize(items, JsonOptions);
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                historyJson = string.Empty;
                message = NeverThrowsGuard.Failure("SearchClipboardHistoryJson", ex);
                return false;
            }
        }

        /// <summary>
        /// Puts a history item back on the clipboard. An item kept in <see cref="ClipboardHistoryMode.AllFormats"/> mode comes
        /// back complete unless it was captured with something left out (its <c>complete</c> field in the list says so); a
        /// text-only item comes back as its files (with their copy/move effect) if it had any, otherwise its text. The history
        /// does not record this itself.
        /// </summary>
        /// <param name="index">Which item: 0 is the newest.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <param name="requireCompleteRestore"><c>true</c> to refuse, leaving the clipboard untouched, an item that was captured with a format left out (one that could not be copied); <c>false</c> (the default) to put back what it kept.</param>
        /// <returns><c>true</c> if the item is on the clipboard; <c>false</c> if there is no item at that index, it kept nothing that can be restored, or the clipboard could not be set. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Puts a clipboard history item (0 is the newest) back on the clipboard. Returns True on success; never throws.")]
        public bool RestoreClipboardHistoryItem(int index, out string message, bool requireCompleteRestore = false)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;

                // Entered before the lock below (never inside it), to keep the lock order.
                using (OwnOperation())
                {
                    // The lock is held throughout so that discarding the item cannot overwrite its bytes mid-restore.
                    lock (_historyLock)
                    {
                        if (!TryGetHistoryItem(index, out ClipboardHistoryItem item, out message))
                            return false;
                        if (requireCompleteRestore && !item.Complete)
                        {
                            message = "The clipboard was not touched, because this item was captured without everything that was on the clipboard (a format could not be copied).";
                            return false;
                        }

                        if (!TryRunBounded("Restoring the history item", () =>

                            {
                                bool ok = _engine.TryRestoreHistoryItem(item, out string error);
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
                message = NeverThrowsGuard.Failure("RestoreClipboardHistoryItem", ex);
                return false;
            }
        }

        /// <summary>Discards one history item, overwriting its bytes in memory where it kept them.</summary>
        /// <param name="index">Which item: 0 is the newest.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the item existed and was discarded; <c>false</c> if there is no item at that index. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Discards one clipboard history item (0 is the newest). Returns True if it existed; never throws.")]
        public bool DiscardClipboardHistoryItem(int index, out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                lock (_historyLock)
                {
                    if (!TryGetHistoryItem(index, out ClipboardHistoryItem item, out message))
                        return false;
                    _history.Remove(item);
                    _historyBytes -= item.Bytes;
                    item.Wipe();
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DiscardClipboardHistoryItem", ex);
                return false;
            }
        }

        /// <summary>Discards every history item, overwriting the bytes it kept. The history keeps running if it was.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success (including when it was empty); <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Clipboard - History")]
        [Description("Discards every clipboard history item. The history keeps running if it was. Returns True on success; never throws.")]
        public bool ClearClipboardHistory(out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                // Waits for a capture that is in flight, so it cannot add itself back after the clear, and treats a copy
                // that was made but not yet noticed as part of what is being cleared.
                lock (_pollLock)
                {
                    lock (_historyLock)
                    {
                        WipeHistory();
                        if (_historyThread != null)
                            _handledSequence = _engine.SequenceNumber;
                    }
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ClearClipboardHistory", ex);
                return false;
            }
        }

        #endregion

        // ------------------------------------------------------------------ the watcher

        /// <summary>
        /// Marks a stretch in which this component is changing the clipboard itself, so the history does not record its own
        /// writes. When it ends, the clipboard's sequence number at that moment is the one already dealt with.
        /// </summary>
        private IDisposable OwnOperation() => new OwnScope(this);

        private sealed class OwnScope : IDisposable
        {
            private readonly ClipboardUtils _owner;

            public OwnScope(ClipboardUtils owner)
            {
                _owner = owner;
                // Something copied a moment ago may not have been noticed yet, and this operation is about to overwrite
                // the clipboard: record it first, or it would be lost.
                owner.FlushPendingHistory();
                lock (owner._historyLock)
                    owner._ownOperations++;
            }

            public void Dispose()
            {
                lock (_owner._historyLock)
                {
                    _owner._handledSequence = _owner._engine.SequenceNumber;
                    _owner._ownOperations--;
                }
            }
        }

        private sealed class HistoryCapture : IWipeable
        {
            public void Wipe() => Item?.Wipe();


            public HistoryCaptureOutcome Outcome;
            public ClipboardHistoryItem Item;
            public string Error;
        }

        /// <summary>
        /// Records a change that has happened but the watcher has not yet noticed, on the calling thread. Does nothing (and
        /// costs nothing) when the history is not running or nothing is pending.
        /// </summary>
        private void FlushPendingHistory()
        {
            CancellationToken token;
            ClipboardHistoryMode mode;
            lock (_historyLock)
            {
                if (_historyThread == null || _ownOperations > 0 || _historyCts == null)
                    return;
                token = _historyCts.Token;
                mode = _historyMode;
            }

            try
            {
                PollHistory(token, mode);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                lock (_historyLock)
                    _historyLastError = NeverThrowsGuard.Failure("Clipboard history", ex);
            }
        }

        private void HistoryLoop(CancellationToken token, int pollIntervalMs, ClipboardHistoryMode mode)
        {
            while (!token.WaitHandle.WaitOne(pollIntervalMs))
            {
                try
                {
                    PollHistory(token, mode);
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    lock (_historyLock)
                        _historyLastError = NeverThrowsGuard.Failure("Clipboard history", ex);
                }
            }
        }

        private void PollHistory(CancellationToken token, ClipboardHistoryMode mode)
        {
            lock (_pollLock)
                PollHistoryCore(token, mode);
        }

        private void PollHistoryCore(CancellationToken token, ClipboardHistoryMode mode)
        {
            long sequence, handledAtStart;
            lock (_historyLock)
            {
                if (_ownOperations > 0)
                    return;
                sequence = _engine.SequenceNumber;
                handledAtStart = _handledSequence;
                if (sequence == handledAtStart)
                    return;
            }

            // One copy changes the sequence number several times (the clipboard is emptied, then each format is written),
            // so wait until it has stopped moving before reading.
            for (int round = 0; round < MaxSettleRounds; round++)
            {
                if (token.WaitHandle.WaitOne(_historySettleMs))
                    return;
                long now = _engine.SequenceNumber;
                if (now == sequence)
                    break;
                sequence = now;
            }

            lock (_historyLock)
            {
                if (_ownOperations > 0 || _handledSequence != handledAtStart)
                    return;
            }

            long limit = MaximumBytes;
            HistoryCapture capture;
            if (TryRunBounded("Reading the clipboard for the history", () =>
                {
                    HistoryCaptureOutcome outcome = _engine.TryCaptureHistoryItem(mode, limit, out ClipboardHistoryItem item, out string error);
                    return new HistoryCapture { Outcome = outcome, Item = item, Error = error };
                }, out capture, out string timeoutMessage) == false)
            {
                capture = new HistoryCapture { Outcome = HistoryCaptureOutcome.Failed, Error = timeoutMessage };
            }

            lock (_historyLock)
            {
                // This component wrote to the clipboard while it was being read: what was read may be its own content.
                if (_ownOperations > 0 || _handledSequence != handledAtStart)
                {
                    capture.Item?.Wipe();
                    return;
                }

                bool retry = false;
                switch (capture.Outcome)
                {
                    case HistoryCaptureOutcome.Captured:
                        StoreHistoryItem(capture.Item);
                        break;
                    case HistoryCaptureOutcome.Excluded:
                        _historySkippedExcluded++;
                        break;
                    case HistoryCaptureOutcome.TooLarge:
                        _historySkippedTooLarge++;
                        _historyLastError = capture.Error;
                        break;
                    case HistoryCaptureOutcome.Failed:
                        // Often another application briefly holding the clipboard: try again on the next poll.
                        _historyLastError = capture.Error;
                        if (++_captureAttempts < MaxCaptureAttempts)
                            retry = true;
                        else
                            _historyFailed++;
                        break;
                }

                if (!retry)
                {
                    _handledSequence = sequence;
                    _captureAttempts = 0;
                }
            }
        }

        private void StoreHistoryItem(ClipboardHistoryItem item)
        {
            if (item.Hash != null && _history.Count > 0 && _history[_history.Count - 1].Hash == item.Hash)
            {
                item.Wipe(); // the same thing copied again
                return;
            }

            _history.Add(item);
            _historyBytes += item.Bytes;
            _historyRecorded++;
            TrimHistory();
        }

        /// <summary>Drops the oldest items until there are no more than were asked for and they fit in the size limit.</summary>
        private void TrimHistory()
        {
            long limit = MaximumBytes;
            while (_history.Count > 0 && (_history.Count > _historyMaxItems || (_historyBytes > limit && _history.Count > 1)))
                RemoveOldestHistoryItem();
        }

        private void RemoveOldestHistoryItem()
        {
            ClipboardHistoryItem oldest = _history[0];
            _history.RemoveAt(0);
            _historyBytes -= oldest.Bytes;
            oldest.Wipe();
        }

        private void WipeHistory()
        {
            foreach (ClipboardHistoryItem item in _history)
                item.Wipe();
            _history.Clear();
            _historyBytes = 0;
        }

        private void StopHistoryWatcher()
        {
            Thread thread;
            CancellationTokenSource cts;
            lock (_historyLock)
            {
                thread = _historyThread;
                cts = _historyCts;
                _historyThread = null;
                _historyCts = null;
            }
            if (thread == null)
                return;

            cts.Cancel();
            // A capture that is waiting on a hung clipboard owner is abandoned by its own timeout, so this is bounded too.
            if (thread.Join(_operationTimeoutMs + 3000))
                cts.Dispose();
        }

        private bool TryGetHistoryItem(int index, out ClipboardHistoryItem item, out string message)
        {
            item = null;
            if (index < 0 || index >= _history.Count)
            {
                message = "There is no history item at index " + index + " (the history holds " + _history.Count + "; 0 is the newest).";
                return false;
            }
            item = _history[_history.Count - 1 - index];
            message = null;
            return true;
        }

        private static bool HistoryItemContains(ClipboardHistoryItem item, string searchText, StringComparison comparison)
        {
            if (item.Text != null && item.Text.IndexOf(searchText, comparison) >= 0)
                return true;
            foreach (string file in item.Files)
                if (file.IndexOf(searchText, comparison) >= 0)
                    return true;
            return false;
        }

        private static object DescribeHistoryItem(int index, ClipboardHistoryItem item, bool includeText)
        {
            string effect = item.Files.Count > 0 ? item.Effect.ToString() : null;
            if (!includeText)
            {
                return new
                {
                    index,
                    capturedUtc = item.CapturedUtc.ToString("o"),
                    formats = item.FormatNames,
                    hasText = item.Text != null,
                    textLength = item.Text == null ? 0 : item.Text.Length,
                    fileCount = item.Files.Count,
                    effect,
                    bytes = item.Bytes,
                    complete = item.Complete
                };
            }

            return new
            {
                index,
                capturedUtc = item.CapturedUtc.ToString("o"),
                formats = item.FormatNames,
                hasText = item.Text != null,
                textLength = item.Text == null ? 0 : item.Text.Length,
                fileCount = item.Files.Count,
                effect,
                bytes = item.Bytes,
                complete = item.Complete,
                text = Preview(item.Text),
                files = item.Files.Take(50).ToList()
            };
        }

        private static string Preview(string text)
        {
            if (text == null)
                return string.Empty;
            string flat = text.Replace("\r", " ").Replace("\n", " ");
            return flat.Length <= TextPreviewLength ? flat : flat.Substring(0, TextPreviewLength) + "...";
        }
    }
}
