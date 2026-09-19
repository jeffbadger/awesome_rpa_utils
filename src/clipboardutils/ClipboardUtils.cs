using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace ClipboardAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component for the Windows clipboard: keep a copy of everything on it (every format,
    /// not just text) and put it back, paste text without losing what was there, wait for the clipboard to change,
    /// and read or set a list of files. It uses the raw Win32 clipboard functions (no OLE), so it works from any
    /// thread.
    /// </summary>
    [Description("Saves and restores everything on the clipboard (all formats), pastes text without destroying what was " +
                 "there, waits for the clipboard to change, and reads or sets a file list. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public partial class ClipboardUtils : Component
    {
        private const int DefaultMaximumMegabytes = 128;
        private const int AbsoluteMaximumMegabytes = 1024;
        private const int MaxNameLength = 64;

        internal const int DefaultOperationTimeoutMs = 20000;
        internal const int DefaultHistorySettleMs = 100;
        internal const int MaxWaitMs = 3600000;
        internal const int MinPollIntervalMs = 5;
        internal const int MaxPollIntervalMs = 60000;
        internal const int MaxPasteDelayMs = 10000;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly ClipboardEngine _engine;
        private readonly IKeySender _keys;
        private readonly Action<int> _sleep;
        private readonly Func<long> _nowMs;
        private readonly int _operationTimeoutMs;
        private readonly int _historySettleMs;

        private readonly object _lock = new object();
        private readonly Dictionary<string, ClipboardSnapshot> _snapshots = new Dictionary<string, ClipboardSnapshot>(StringComparer.OrdinalIgnoreCase);
        private long _storedBytes;
        private int _maximumMegabytes = DefaultMaximumMegabytes;
        private bool _disposed;
        private int _abandonedOperations;   // timed-out operations whose worker thread has not finished yet

        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public ClipboardUtils() : this(new Win32ClipboardApi(), new Win32KeySender(), Thread.Sleep, () => Environment.TickCount64, DefaultOperationTimeoutMs, DefaultHistorySettleMs)
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public ClipboardUtils(IContainer container) : this()
        {
            container?.Add(this);
        }

        internal ClipboardUtils(IClipboardApi api, IKeySender keys, Action<int> sleep, Func<long> nowMs, int operationTimeoutMs, int historySettleMs = DefaultHistorySettleMs)
        {
            _engine = new ClipboardEngine(api);
            _keys = keys;
            _sleep = sleep;
            _nowMs = nowMs;
            _operationTimeoutMs = operationTimeoutMs;
            _historySettleMs = historySettleMs;
        }

        /// <summary>
        /// The most clipboard data, in megabytes, that one operation will read and that all saved snapshots together
        /// will hold (1 to 1024; the default is 128). A clipboard bigger than this is refused rather than partly copied.
        /// </summary>
        [Category("Clipboard - Limits")]
        [Description("The most clipboard data, in megabytes, that one read will take and that all saved snapshots together will hold (1 to 1024).")]
        [DefaultValue(DefaultMaximumMegabytes)]
        public int MaximumClipboardMegabytes
        {
            get
            {
                lock (_lock)
                    return _maximumMegabytes;
            }
            set
            {
                if (value < 1 || value > AbsoluteMaximumMegabytes)
                    throw new ArgumentOutOfRangeException(nameof(value), "MaximumClipboardMegabytes must be between 1 and " + AbsoluteMaximumMegabytes + ".");
                lock (_lock)
                    _maximumMegabytes = value;
            }
        }

        private long MaximumBytes
        {
            get
            {
                lock (_lock)
                    return _maximumMegabytes * 1024L * 1024L;
            }
        }

        // ------------------------------------------------------------------ text

        #region Text

        /// <summary>Reads the text on the clipboard.</summary>
        /// <param name="text">The text; an empty string if the clipboard holds none or this method returns <c>false</c>.</param>
        /// <param name="textAvailable"><c>true</c> if the clipboard held text (which may itself be empty); <c>false</c> if it held none.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success, including when the clipboard holds no text; <c>false</c> if the clipboard could not be read. Never throws.</returns>
        [Category("Clipboard - Text")]
        [Description("Reads the clipboard's text. An empty clipboard is a success with textAvailable False. Returns True on success; never throws.")]
        public bool GetClipboardText(out string text, out bool textAvailable, out string message)
        {
            text = string.Empty;
            textAvailable = false;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                long limit = MaximumBytes;
                if (!TryRunBounded("Reading the clipboard", () =>
                    {
                        bool ok = _engine.TryGetText(limit, out string value, out bool found, out string error);
                        return new TextResult { Ok = ok, Text = value, Available = found, Error = error };
                    }, out TextResult result, out message))
                    return false;
                if (!result.Ok)
                {
                    message = result.Error;
                    return false;
                }
                text = result.Text;
                textAvailable = result.Available;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                text = string.Empty;
                textAvailable = false;
                message = NeverThrowsGuard.Failure("GetClipboardText", ex);
                return false;
            }
        }

        /// <summary>
        /// Replaces the clipboard with the text. Everything else on the clipboard is discarded; use
        /// <see cref="SaveClipboard"/> first if it must come back.
        /// </summary>
        /// <param name="text">The text to put on the clipboard. May be empty; may not be <c>null</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <param name="excludeFromHistory"><c>true</c> to mark the content so Windows' clipboard history and cloud sync, and well-behaved clipboard monitors, skip it. Use it for anything sensitive.</param>
        /// <returns><c>true</c> if the text is on the clipboard; <c>false</c> otherwise. Never throws.</returns>
        [Category("Clipboard - Text")]
        [Description("Replaces the clipboard with the given text, discarding every other format. Optionally keeps it out of clipboard history. Returns True on success; never throws.")]
        public bool SetClipboardText(string text, out string message, bool excludeFromHistory = false)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (text == null)
                {
                    message = "text may not be null (use an empty string for empty text).";
                    return false;
                }
                using (OwnOperation())
                {
                if (!TryRunBounded("Setting the clipboard text", () =>
                    {
                        bool ok = _engine.TrySetText(text, excludeFromHistory, out string error);
                        return new SimpleResult { Ok = ok, Error = error };
                    }, out SimpleResult result, out message))
                    return false;
                message = result.Ok ? null : result.Error;
                return result.Ok;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetClipboardText", ex);
                return false;
            }
        }

        /// <summary>Empties the clipboard.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the clipboard was emptied; <c>false</c> otherwise. Never throws.</returns>
        [Category("Clipboard - Text")]
        [Description("Empties the clipboard. Returns True on success; never throws.")]
        public bool ClearClipboard(out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                using (OwnOperation())
                {
                    if (!TryRunBounded("Clearing the clipboard", () =>
                        {
                            bool ok = _engine.TryClear(out string error);
                            return new SimpleResult { Ok = ok, Error = error };
                        }, out SimpleResult result, out message))
                        return false;
                    message = result.Ok ? null : result.Error;
                    return result.Ok;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ClearClipboard", ex);
                return false;
            }
        }

        /// <summary>
        /// Pastes text into whatever has the keyboard focus by putting it on the clipboard and sending Ctrl+V, then
        /// puts back <b>everything</b> that was on the clipboard - images, files, formatted text, whatever the
        /// clipboard held - not just plain text.
        /// </summary>
        /// <param name="text">The text to paste. May be empty; may not be <c>null</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason. If both the paste and the restore failed, both are reported.</param>
        /// <param name="postPasteDelayMilliseconds">How long to wait after sending Ctrl+V before restoring the clipboard, in milliseconds (0 to 10000; default 100). A target reads the clipboard at its own pace, so a delay that is too short risks it pasting the <i>restored</i> content instead.</param>
        /// <param name="excludeFromHistory"><c>true</c> (the default) to mark the pasted text so Windows' clipboard history and cloud sync, and well-behaved clipboard monitors, do not record it.</param>
        /// <param name="requireCompleteRestore"><c>true</c> to refuse (without touching the clipboard) if part of what is on it cannot be copied and so could not be put back; <c>false</c> (the default) to paste anyway.</param>
        /// <returns><c>true</c> if the text was pasted and the clipboard was put back; <c>false</c> otherwise. Never throws.</returns>
        /// <remarks>
        /// The text does sit on the system clipboard for the length of the paste, where a process watching the
        /// clipboard can see it; for a secret, typing it is safer (KeyboardUtils.TypeText). The focus must already be
        /// in the field: this only sends the keystroke. Formats that hold a GDI handle rather than data cannot be
        /// copied (Windows rebuilds the usual ones, such as a bitmap, from the DIB that is); use
        /// <paramref name="requireCompleteRestore"/> if losing one would matter.
        /// </remarks>
        [Category("Clipboard - Text")]
        [Description("Pastes text with Ctrl+V and then restores everything that was on the clipboard, all formats. Optionally refuses if something cannot be restored. Returns True on success; never throws.")]
        public bool PasteText(string text, out string message, int postPasteDelayMilliseconds = 100,
            bool excludeFromHistory = true, bool requireCompleteRestore = false)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (text == null)
                {
                    message = "text may not be null (use an empty string for empty text).";
                    return false;
                }
                if (postPasteDelayMilliseconds < 0 || postPasteDelayMilliseconds > MaxPasteDelayMs)
                {
                    message = "postPasteDelayMilliseconds must be between 0 and " + MaxPasteDelayMs + ".";
                    return false;
                }

                // The whole paste, including the restore, is this component's own doing: the history must not record it.
                using (OwnOperation())
                {
                long limit = MaximumBytes;
                if (!TryRunBounded("Saving the clipboard", () =>
                    {
                        bool ok = _engine.TryCapture(limit, out ClipboardSnapshot copy, out string error);
                        return new CaptureResult { Ok = ok, Snapshot = copy, Error = error };
                    }, out CaptureResult captured, out message))
                    return false;
                if (!captured.Ok)
                {
                    message = "The clipboard was not touched: " + captured.Error;
                    return false;
                }

                ClipboardSnapshot snapshot = captured.Snapshot;
                bool snapshotHandedOver = false;   // an abandoned worker still needs the snapshot, and wipes it itself when done
                try
                {
                    if (requireCompleteRestore && snapshot.HasLoss)
                    {
                        message = "The clipboard was not touched, because it holds something that could not be put back afterwards: "
                            + DescribeLosses(snapshot) + ".";
                        return false;
                    }

                    bool pasted = false;
                    string failure = null;
                    if (!TryRunBounded("Setting the clipboard text", () =>
                        {
                            bool ok = _engine.TrySetText(text, excludeFromHistory, out string error);
                            return new SimpleResult { Ok = ok, Error = error };
                        }, out SimpleResult set, out string setTimeout, out bool setAbandoned, () =>
                        {
                            // The text landed late, over the clipboard's real contents: put them back now.
                            try { _engine.TryRestore(snapshot, out _); }
                            finally { snapshot.Wipe(); }
                        }))
                    {
                        failure = setTimeout;
                        if (setAbandoned)
                        {
                            snapshotHandedOver = true;
                            failure += " The clipboard's original contents are kept and are put back automatically if the owner ever answers.";
                        }
                    }
                    else if (!set.Ok)
                    {
                        failure = set.Error;
                    }
                    else
                    {
                        // A failure while sending the keystroke or waiting must still reach the restore below.
                        try
                        {
                            pasted = _keys.SendPasteChord(out failure);
                        }
                        catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                        {
                            pasted = false;
                            failure = NeverThrowsGuard.Failure("Sending Ctrl+V", ex);
                        }

                        if (pasted && postPasteDelayMilliseconds > 0)
                        {
                            try
                            {
                                _sleep(postPasteDelayMilliseconds);
                            }
                            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                            {
                                // The keystroke has gone; only the wait was cut short. Carry on to the restore.
                            }
                        }
                    }

                    // Whatever happened above, the clipboard is put back: the text may already be on it.
                    string restoreProblem = null;
                    if (snapshotHandedOver)
                    {
                        // Nothing more to do here: the abandoned setter restores the clipboard when it finishes.
                    }
                    else if (!TryRunBounded("Restoring the clipboard", () =>
                        {
                            bool ok = _engine.TryRestore(snapshot, out string error);
                            return new SimpleResult { Ok = ok, Error = error };
                        }, out SimpleResult restored, out string restoreTimeout, out bool restoreAbandoned, snapshot.Wipe))
                    {
                        restoreProblem = restoreTimeout;
                        snapshotHandedOver = restoreAbandoned;
                    }
                    else if (!restored.Ok)
                        restoreProblem = restored.Error;

                    return Combine(pasted, failure, restoreProblem, out message);
                }
                finally
                {
                    // An abandoned worker is still using the snapshot; it wipes it itself when it finishes.
                    if (!snapshotHandedOver)
                        snapshot.Wipe();
                }
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("PasteText", ex);
                return false;
            }
        }

        #endregion

        // ------------------------------------------------------------------ formats

        #region Formats

        /// <summary>
        /// Reads the clipboard's sequence number, which changes every time its contents change. Note down the number
        /// before an action that will change the clipboard and pass it to <see cref="WaitForClipboardChangeSince"/>, so
        /// a change that happens before the wait starts is not missed.
        /// </summary>
        /// <param name="sequenceNumber">The sequence number; 0 if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Clipboard - Formats")]
        [Description("Reads the clipboard's sequence number, which changes whenever its contents change. Returns True on success; never throws.")]
        public bool GetClipboardSequenceNumber(out long sequenceNumber, out string message)
        {
            sequenceNumber = 0;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                sequenceNumber = _engine.SequenceNumber;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                sequenceNumber = 0;
                message = NeverThrowsGuard.Failure("GetClipboardSequenceNumber", ex);
                return false;
            }
        }

        /// <summary>Lists the formats on the clipboard, in the order the owner offered them, as JSON.</summary>
        /// <param name="formatsJson">A JSON array of <c>{"id","name","registered"}</c> (<c>[]</c> for an empty clipboard); empty if this method returns <c>false</c>. Listing the formats does not ask the clipboard's owner to produce any data.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the clipboard could not be opened. Never throws.</returns>
        [Category("Clipboard - Formats")]
        [Description("Lists the formats on the clipboard as JSON (id, name, registered). Does not render any data. Returns True on success; never throws.")]
        public bool GetFormatsJson(out string formatsJson, out string message)
        {
            formatsJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!TryRunBounded("Listing the clipboard formats", () =>
                    {
                        bool ok = _engine.TryListFormats(out List<FormatInfo> formats, out string error);
                        return new FormatsResult { Ok = ok, Formats = formats, Error = error };
                    }, out FormatsResult result, out message))
                    return false;
                if (!result.Ok)
                {
                    message = result.Error;
                    return false;
                }

                var items = new List<object>();
                foreach (FormatInfo format in result.Formats)
                    items.Add(new { id = format.Id, name = format.Name, registered = format.IsRegistered });
                formatsJson = JsonSerializer.Serialize(items, JsonOptions);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                formatsJson = string.Empty;
                message = NeverThrowsGuard.Failure("GetFormatsJson", ex);
                return false;
            }
        }

        /// <summary>Whether a format is on the clipboard.</summary>
        /// <param name="formatName">The format: a predefined name with or without <c>CF_</c> (<c>CF_HDROP</c>, <c>UnicodeText</c>, <c>DIB</c>), a number (<c>15</c> or <c>0x0F</c>), or a name it was registered under (<c>HTML Format</c>, <c>Rich Text Format</c>). Ignoring case.</param>
        /// <param name="available"><c>true</c> if the format is on the clipboard; <c>false</c> if it is not, or if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the question could be answered; <c>false</c> if the name is empty or not a valid format name. Never throws.</returns>
        [Category("Clipboard - Formats")]
        [Description("Whether a format (CF_ name, number, or registered name such as 'HTML Format') is on the clipboard. Returns True if it could be answered; never throws.")]
        public bool IsFormatAvailable(string formatName, out bool available, out string message)
        {
            available = false;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!_engine.TryIsFormatAvailable(formatName, out available, out message))
                    return false;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                available = false;
                message = NeverThrowsGuard.Failure("IsFormatAvailable", ex);
                return false;
            }
        }

        #endregion

        // ------------------------------------------------------------------ waiting

        #region Wait

        /// <summary>
        /// Waits for the clipboard's contents to change, measured from the moment of the call. If the change may
        /// happen before this is called (for example, when the step that causes it comes just before), use
        /// <see cref="GetClipboardSequenceNumber"/> first and <see cref="WaitForClipboardChangeSince"/>.
        /// </summary>
        /// <param name="timeoutMs">How long to wait, in milliseconds (0 to 3,600,000). 0 checks once.</param>
        /// <param name="pollIntervalMs">How often to check, in milliseconds (5 to 60,000; 50 is typical).</param>
        /// <param name="timedOut"><c>true</c> if the wait ended because the time ran out.</param>
        /// <param name="message"><c>null</c> on success and on a timeout; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the clipboard changed; <c>false</c> on a timeout (with <paramref name="timedOut"/> set and no message) or an error. Never throws.</returns>
        [Category("Clipboard - Wait")]
        [Description("Waits for the clipboard to change, from the time of the call. Returns True if it changed, False on timeout (timedOut True) or error; never throws.")]
        public bool WaitForClipboardChange(int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = false;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!ValidateWait(timeoutMs, pollIntervalMs, out message))
                    return false;
                long baseline = _engine.SequenceNumber;
                return CompleteWait(_engine.WaitForChange(baseline, timeoutMs, pollIntervalMs, _nowMs, _sleep), out timedOut, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                timedOut = false;
                message = NeverThrowsGuard.Failure("WaitForClipboardChange", ex);
                return false;
            }
        }

        /// <summary>
        /// Waits until the clipboard's sequence number is no longer <paramref name="sequenceNumber"/>. Returns at once
        /// if it has already changed since that number was read.
        /// </summary>
        /// <param name="sequenceNumber">A number from <see cref="GetClipboardSequenceNumber"/>, read before the action that changes the clipboard.</param>
        /// <param name="timeoutMs">How long to wait, in milliseconds (0 to 3,600,000). 0 checks once.</param>
        /// <param name="pollIntervalMs">How often to check, in milliseconds (5 to 60,000; 50 is typical).</param>
        /// <param name="timedOut"><c>true</c> if the wait ended because the time ran out.</param>
        /// <param name="message"><c>null</c> on success and on a timeout; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the clipboard changed; <c>false</c> on a timeout (with <paramref name="timedOut"/> set and no message) or an error. Never throws.</returns>
        [Category("Clipboard - Wait")]
        [Description("Waits until the clipboard's sequence number differs from the one given, so a change made before the wait began is not missed. Returns True if it changed, False on timeout or error; never throws.")]
        public bool WaitForClipboardChangeSince(long sequenceNumber, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = false;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (sequenceNumber < 0 || sequenceNumber > uint.MaxValue)
                {
                    message = "sequenceNumber must be a value from GetClipboardSequenceNumber.";
                    return false;
                }
                if (!ValidateWait(timeoutMs, pollIntervalMs, out message))
                    return false;
                return CompleteWait(_engine.WaitForChange(sequenceNumber, timeoutMs, pollIntervalMs, _nowMs, _sleep), out timedOut, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                timedOut = false;
                message = NeverThrowsGuard.Failure("WaitForClipboardChangeSince", ex);
                return false;
            }
        }

        /// <summary>Waits until a format is on the clipboard (checking first, so it returns at once if it already is).</summary>
        /// <param name="formatName">The format: a predefined name with or without <c>CF_</c>, a number, or a registered name. See <see cref="IsFormatAvailable"/>.</param>
        /// <param name="timeoutMs">How long to wait, in milliseconds (0 to 3,600,000). 0 checks once.</param>
        /// <param name="pollIntervalMs">How often to check, in milliseconds (5 to 60,000; 50 is typical).</param>
        /// <param name="timedOut"><c>true</c> if the wait ended because the time ran out.</param>
        /// <param name="message"><c>null</c> on success and on a timeout; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the format is on the clipboard; <c>false</c> on a timeout (with <paramref name="timedOut"/> set and no message) or an error. Never throws.</returns>
        [Category("Clipboard - Wait")]
        [Description("Waits until the given format (for example CF_HDROP, DIB or 'HTML Format') is on the clipboard. Returns True if it is, False on timeout or error; never throws.")]
        public bool WaitForClipboardFormat(string formatName, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = false;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!ValidateWait(timeoutMs, pollIntervalMs, out message))
                    return false;
                if (!_engine.TryResolveFormat(formatName, out uint id, out message))
                    return false;
                return CompleteWait(_engine.WaitForFormat(id, timeoutMs, pollIntervalMs, _nowMs, _sleep), out timedOut, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                timedOut = false;
                message = NeverThrowsGuard.Failure("WaitForClipboardFormat", ex);
                return false;
            }
        }

        private static bool ValidateWait(int timeoutMs, int pollIntervalMs, out string message)
        {
            message = null;
            if (timeoutMs < 0 || timeoutMs > MaxWaitMs)
            {
                message = "timeoutMs must be between 0 and " + MaxWaitMs + ".";
                return false;
            }
            if (pollIntervalMs < MinPollIntervalMs || pollIntervalMs > MaxPollIntervalMs)
            {
                message = "pollIntervalMs must be between " + MinPollIntervalMs + " and " + MaxPollIntervalMs + ".";
                return false;
            }
            return true;
        }

        private static bool CompleteWait(bool succeeded, out bool timedOut, out string message)
        {
            // A timeout is a normal negative outcome: false, timedOut set, and no message.
            timedOut = !succeeded;
            message = null;
            return succeeded;
        }

        #endregion

        // ------------------------------------------------------------------ helpers

        private sealed class SimpleResult
        {
            public bool Ok;
            public string Error;
        }

        private sealed class TextResult
        {
            public bool Ok;
            public string Text;
            public bool Available;
            public string Error;
        }

        private sealed class CaptureResult
        {
            public bool Ok;
            public ClipboardSnapshot Snapshot;
            public string Error;
        }

        private sealed class FormatsResult
        {
            public bool Ok;
            public List<FormatInfo> Formats;
            public string Error;
        }

        /// <summary>
        /// Runs an operation that opens the clipboard on a thread this call can walk away from. Reading a format
        /// that the clipboard's owner has not produced yet asks that application for it, and if it is hung the read
        /// never returns; giving up after a while keeps the automation from hanging with it. An abandoned operation
        /// finishes (and closes the clipboard) on its own if the owner ever answers, and until it has, no other
        /// clipboard operation is started, so it can never race a later one.
        /// </summary>
        private bool TryRunBounded<T>(string operation, Func<T> work, out T result, out string message) where T : class =>
            TryRunBounded(operation, work, out result, out message, out _, null);

        // abandoned: true if the operation was given up on and may still be running.
        // afterAbandonedFinish: runs on the worker thread if the operation finishes after being abandoned; for releasing what it was still using.
        private bool TryRunBounded<T>(string operation, Func<T> work, out T result, out string message, out bool abandoned,
            Action afterAbandonedFinish) where T : class
        {
            abandoned = false;
            if (Volatile.Read(ref _abandonedOperations) > 0)
            {
                result = null;
                message = operation + " was not started: an earlier clipboard operation that timed out is still running, "
                    + "because the application that owns the clipboard has not answered. Try again once it has.";
                return false;
            }

            T value = null;
            Exception failure = null;
            var state = new RunState();
            using (var done = new ManualResetEventSlim(false))
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        value = work();
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        bool wasAbandoned;
                        lock (state)
                        {
                            state.Finished = true;
                            wasAbandoned = state.Abandoned;
                        }
                        if (wasAbandoned)
                        {
                            // Other operations stay refused until this follow-up (a deferred restore) is done too.
                            try { afterAbandonedFinish?.Invoke(); }
                            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
                            finally { Interlocked.Decrement(ref _abandonedOperations); }
                        }
                        else
                        {
                            try { done.Set(); }
                            catch (ObjectDisposedException) { }
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "ClipboardUtils.Operation"
                };
                thread.Start();

                if (!done.Wait(_operationTimeoutMs))
                {
                    lock (state)
                    {
                        if (!state.Finished)
                        {
                            state.Abandoned = true;
                            Interlocked.Increment(ref _abandonedOperations);
                            abandoned = true;
                        }
                    }
                    if (abandoned)
                    {
                        result = null;
                        message = operation + " did not finish within " + _operationTimeoutMs + " ms. The application that owns the clipboard "
                            + "may be hung, or another one is holding it open; the operation was abandoned.";
                        return false;
                    }
                    // It finished in the instant between the wait and the lock: it is a normal result after all.
                }
            }

            if (failure != null)
            {
                if (!NeverThrowsGuard.IsRecoverable(failure))
                    throw failure;
                result = null;
                message = NeverThrowsGuard.Failure(operation, failure);
                return false;
            }

            result = value;
            message = null;
            return true;
        }

        private sealed class RunState
        {
            public bool Finished;
            public bool Abandoned;
        }

        private static string DescribeLosses(ClipboardSnapshot snapshot)
        {
            var parts = new List<string>();
            foreach (SkippedFormat skipped in snapshot.Skipped)
            {
                if (skipped.IsLoss)
                    parts.Add(skipped.Name + " (" + skipped.Reason.TrimEnd('.') + ")");
            }
            return string.Join("; ", parts);
        }

        /// <summary>Combines the outcome of an operation with that of the clean-up after it, so neither failure is hidden.</summary>
        private static bool Combine(bool primaryOk, string primaryMessage, string cleanupMessage, out string message)
        {
            if (primaryOk && string.IsNullOrEmpty(cleanupMessage))
            {
                message = null;
                return true;
            }
            if (!primaryOk && !string.IsNullOrEmpty(cleanupMessage))
            {
                message = (string.IsNullOrEmpty(primaryMessage) ? "The paste failed." : primaryMessage) + " Restoring the clipboard also failed: " + cleanupMessage;
                return false;
            }
            message = primaryOk
                ? "The text was pasted, but restoring the clipboard failed: " + cleanupMessage
                : (string.IsNullOrEmpty(primaryMessage) ? "The paste failed." : primaryMessage);
            return false;
        }

        private bool IsDisposed(out string message)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    message = "The component has been disposed.";
                    return true;
                }
            }
            message = null;
            return false;
        }

        /// <summary>Releases the component, overwriting any saved clipboard content held in memory.</summary>
        /// <param name="disposing"><c>true</c> when called from <c>Dispose</c>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_lock)
                {
                    _disposed = true;
                    foreach (ClipboardSnapshot snapshot in _snapshots.Values)
                        snapshot.Wipe();
                    _snapshots.Clear();
                    _storedBytes = 0;
                }

                // Outside the lock above: stopping the watcher waits for its thread, which itself takes locks.
                StopHistoryWatcher();
                lock (_historyLock)
                    WipeHistory();
            }
            base.Dispose(disposing);
        }
    }
}
