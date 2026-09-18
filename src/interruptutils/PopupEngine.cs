using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace InterruptAutomation
{
    internal enum PopupRecordKind
    {
        Detected,
        Dismissed,
        DismissFailed,
        Error
    }

    /// <summary>One entry in the handler's log: a popup detected, dismissed, or not dismissed, or a problem.</summary>
    internal sealed class PopupRecord
    {
        public PopupRecordKind Kind { get; set; }
        public string RuleName { get; set; }
        public string Title { get; set; }
        public string MessageText { get; set; }
        public string ProcessName { get; set; }
        public uint ProcessId { get; set; }
        public string ButtonClicked { get; set; }
        public int Attempts { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>
    /// The interrupt handler's decision logic, free of threads and Win32 so it can be driven
    /// deterministically. <see cref="Enqueue"/> may be called from any thread; everything
    /// else that touches window state runs from <see cref="Pump"/>, which one worker thread
    /// calls repeatedly. Rules, counters and the log are guarded by a lock so the public API
    /// can read them while the worker runs.
    /// </summary>
    internal sealed class PopupEngine : IDisposable
    {
        /// <summary>
        /// When to look at a newly seen window, in milliseconds after it was first seen. A
        /// window announces itself before its controls exist, so a popup that does not
        /// match (or has no buttons yet) on the first look is looked at again shortly.
        /// </summary>
        internal static readonly int[] ScheduleMs = { 0, 150, 400, 1000, 2000 };

        internal const int LogCapacity = 500;
        internal const int MaxRules = 100;
        internal const int MaxTrackedWindows = 2000;
        internal const int MaxQueuedWindows = 4096;
        internal const int EnabledWaitMs = 500;
        internal const int VerifyPolls = 6;
        internal const int VerifyPollMs = 50;
        internal const int RetryDelayMs = 250;
        internal const int PausedRecheckMs = 250;
        internal const int RunawayWindowMs = 60000;

        private sealed class WinState
        {
            public IntPtr Handle;
            public uint ProcessId;
            public long FirstSeen;
            public long NextDue;
            public int Step;
            public int Attempts;
            public bool Failed;
            public bool Reported;
            public PopupRule Rule;   // set once a rule has matched

            public bool Unresolved => Rule != null && Rule.Action != PopupAction.WatchOnly;

            public void Reset(long now)
            {
                ProcessId = 0;
                FirstSeen = now;
                NextDue = now;
                Step = 0;
                Attempts = 0;
                Failed = false;
                Reported = false;
                Rule = null;
            }
        }

        private readonly IPopupProbe _probe;
        private readonly Action<int> _sleep;
        private readonly Action<PopupRecord> _sink;

        private readonly object _lock = new object();
        private readonly List<PopupRule> _rules = new List<PopupRule>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PopupRecord> _log = new List<PopupRecord>();
        private PopupRule[] _ruleSnapshot = new PopupRule[0];
        private int _total;

        private readonly ConcurrentQueue<IntPtr> _queue = new ConcurrentQueue<IntPtr>();
        private int _queued;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        // Worker-thread state.
        private readonly Dictionary<IntPtr, WinState> _windows = new Dictionary<IntPtr, WinState>();
        private readonly List<WinState> _due = new List<WinState>();
        private long _lastSweep;
        private bool _swept;
        private int _unresolved;

        /// <summary>How often to scan every window for popups the hook did not report; 0 turns the scan off.</summary>
        internal int SweepIntervalMs { get; set; } = 1000;

        /// <summary>How many times to try to dismiss one popup before giving up on it.</summary>
        internal int MaxAttempts { get; set; } = 3;

        /// <summary>How many popups one rule may dismiss in a minute before it stops itself.</summary>
        internal int MaxDismissalsPerMinute { get; set; } = 20;

        /// <summary>While set, popups are noticed but not touched; they are dealt with after it is cleared.</summary>
        internal volatile bool Paused;

        internal PopupEngine(IPopupProbe probe, Action<int> sleep, Action<PopupRecord> sink)
        {
            _probe = probe;
            _sleep = sleep;
            _sink = sink;
        }

        public void Dispose() => _wake.Dispose();

        // ------------------------------------------------------------------ rules

        internal bool AddRule(PopupRule rule, out string message)
        {
            lock (_lock)
            {
                if (_rules.Count >= MaxRules)
                {
                    message = "At most " + MaxRules + " rules are allowed.";
                    return false;
                }
                foreach (var existing in _rules)
                {
                    if (string.Equals(existing.Name, rule.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        message = "A rule named '" + existing.Name + "' already exists.";
                        return false;
                    }
                }
                _rules.Add(rule);
                _counts[rule.Name] = 0;
                _ruleSnapshot = _rules.ToArray();
            }
            message = null;
            return true;
        }

        internal bool RemoveRule(string name)
        {
            lock (_lock)
            {
                int index = _rules.FindIndex(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    return false;
                _counts.Remove(_rules[index].Name);
                _rules.RemoveAt(index);
                _ruleSnapshot = _rules.ToArray();
                return true;
            }
        }

        internal void ClearRules()
        {
            lock (_lock)
            {
                _rules.Clear();
                _counts.Clear();
                _ruleSnapshot = new PopupRule[0];
            }
        }

        /// <summary>Enables or disables a rule. Enabling also clears a runaway trip and the record of recent dismissals.</summary>
        internal bool SetRuleEnabled(string name, bool enabled)
        {
            lock (_lock)
            {
                var rule = _rules.Find(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                if (rule == null)
                    return false;
                rule.Enabled = enabled;
                if (enabled)
                {
                    rule.Tripped = false;
                    rule.RecentDismissals.Clear();
                }
                return true;
            }
        }

        internal PopupRule[] SnapshotRules()
        {
            lock (_lock)
                return _ruleSnapshot;
        }

        // ------------------------------------------------------------------ counters and log

        internal bool TryGetCount(string name, out int count)
        {
            lock (_lock)
                return _counts.TryGetValue(name ?? string.Empty, out count);
        }

        internal int TotalDismissals
        {
            get { lock (_lock) return _total; }
        }

        /// <summary>The number of windows a click or close rule matched that are still open (pending or given up on), as of the last <see cref="Pump"/>.</summary>
        internal int UnresolvedCount => Volatile.Read(ref _unresolved);

        internal PopupRecord[] GetLog(int maxEntries)
        {
            lock (_lock)
            {
                int take = Math.Min(Math.Max(maxEntries, 0), _log.Count);
                var result = new PopupRecord[take];
                _log.CopyTo(_log.Count - take, result, 0, take);
                return result;
            }
        }

        internal PopupRecord LastRecord()
        {
            lock (_lock)
                return _log.Count == 0 ? null : _log[_log.Count - 1];
        }

        internal void ClearLog()
        {
            lock (_lock)
                _log.Clear();
        }

        // ------------------------------------------------------------------ input

        /// <summary>Reports a window that appeared. Safe to call from any thread; must return quickly.</summary>
        internal void Enqueue(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;
            if (Volatile.Read(ref _queued) >= MaxQueuedWindows)
                return; // the periodic scan will find anything dropped here
            Interlocked.Increment(ref _queued);
            _queue.Enqueue(hwnd);
            _wake.Set();
        }

        /// <summary>Wakes a worker that is waiting in <see cref="WaitForWork"/>.</summary>
        internal void Wake()
        {
            try { _wake.Set(); }
            catch (ObjectDisposedException) { }
        }

        internal bool WaitForWork(int timeoutMs)
        {
            try { return _wake.WaitOne(Math.Max(timeoutMs, 0)); }
            catch (ObjectDisposedException) { return false; }
        }

        // ------------------------------------------------------------------ the pump

        /// <summary>
        /// Takes in newly reported windows, scans if it is time, and looks at every window
        /// that is due. Returns the time (on the caller's clock) the next window is due, or
        /// <see cref="long.MaxValue"/> if none is.
        /// </summary>
        internal long Pump(long now)
        {
            DrainQueue(now);

            if (SweepIntervalMs > 0 && (!_swept || now - _lastSweep >= SweepIntervalMs))
            {
                _swept = true;
                _lastSweep = now;
                Sweep(now);
            }

            _due.Clear();
            foreach (var state in _windows.Values)
            {
                if (state.NextDue <= now)
                    _due.Add(state);
            }
            foreach (var state in _due)
                Evaluate(state, now);

            long next = long.MaxValue;
            int unresolved = 0;
            foreach (var state in _windows.Values)
            {
                if (state.NextDue < next)
                    next = state.NextDue;
                if (state.Unresolved)
                    unresolved++;
            }
            Volatile.Write(ref _unresolved, unresolved);

            if (SweepIntervalMs > 0)
                next = Math.Min(next, _lastSweep + SweepIntervalMs);
            return next;
        }

        private void DrainQueue(long now)
        {
            while (_queue.TryDequeue(out IntPtr hwnd))
            {
                Interlocked.Decrement(ref _queued);
                if (_windows.TryGetValue(hwnd, out WinState existing))
                {
                    // Reported again (shown, brought to the front): its caption may have changed, or
                    // the handle now belongs to a different window. A window we gave up on is only
                    // looked at again to notice that; Evaluate leaves it alone if it is unchanged.
                    if (existing.NextDue == long.MaxValue)
                        existing.NextDue = now;
                    continue;
                }
                Track(hwnd, now);
            }
        }

        private void Sweep(long now)
        {
            var gone = new List<IntPtr>();
            foreach (var pair in _windows)
            {
                if (!_probe.IsWindow(pair.Key))
                    gone.Add(pair.Key);
                else if (pair.Value.NextDue == long.MaxValue && !pair.Value.Failed)
                    pair.Value.NextDue = now; // idle: look again, its title or message may have changed
            }
            foreach (var hwnd in gone)
                _windows.Remove(hwnd);

            foreach (var hwnd in _probe.EnumerateTopLevelWindows())
            {
                if (!_windows.ContainsKey(hwnd))
                    Track(hwnd, now);
            }
        }

        private void Track(IntPtr hwnd, long now)
        {
            if (_windows.Count >= MaxTrackedWindows)
                return;
            _windows[hwnd] = new WinState { Handle = hwnd, FirstSeen = now, NextDue = now };
        }

        // ------------------------------------------------------------------ deciding

        private void Evaluate(WinState state, long now)
        {
            if (!_probe.IsWindow(state.Handle))
            {
                _windows.Remove(state.Handle);
                return;
            }
            PopupWindowInfo info = _probe.Describe(state.Handle);
            if (info == null)
            {
                _windows.Remove(state.Handle);
                return;
            }

            // A handle can be reused by a different window once the old one is gone.
            if (state.ProcessId != 0 && state.ProcessId != info.ProcessId)
                state.Reset(now);
            state.ProcessId = info.ProcessId;

            if (info.ProcessId == _probe.CurrentProcessId)
            {
                state.NextDue = long.MaxValue; // never touch the automation's own windows
                return;
            }
            if (state.Failed)
            {
                state.NextDue = long.MaxValue;
                return;
            }
            if (Paused)
            {
                state.NextDue = now + PausedRecheckMs;
                return;
            }

            PopupRule rule = null;
            string text = null;
            string processName = null;
            foreach (var candidate in SnapshotRules())
            {
                if (!candidate.Enabled || candidate.Tripped)
                    continue;
                if (!candidate.MatchesWindow(info, () => processName ?? (processName = _probe.GetProcessName(info.ProcessId) ?? string.Empty)))
                    continue;
                if (candidate.NeedsMessage)
                {
                    text = text ?? _probe.GetMessageText(state.Handle) ?? string.Empty;
                    if (!candidate.MatchesMessage(text))
                        continue;
                }
                rule = candidate;
                break;
            }

            if (rule == null)
            {
                if (!Retry(state, now))
                    state.NextDue = long.MaxValue;
                return;
            }

            state.Rule = rule;
            // Read what the popup says now: after a click it is gone.
            text = text ?? _probe.GetMessageText(state.Handle) ?? string.Empty;
            processName = processName ?? _probe.GetProcessName(info.ProcessId) ?? string.Empty;

            if (rule.Action == PopupAction.WatchOnly)
            {
                if (!state.Reported)
                {
                    state.Reported = true;
                    Record(PopupRecordKind.Detected, rule.Name, info.Title, text, processName, info.ProcessId, string.Empty, 0, string.Empty);
                }
                state.NextDue = long.MaxValue;
                return;
            }

            Dismiss(state, rule, info, text, processName, now);
        }

        private void Dismiss(WinState state, PopupRule rule, PopupWindowInfo info, string text, string processName, long now)
        {
            if (IsRunaway(rule, now))
            {
                rule.Tripped = true;
                state.NextDue = long.MaxValue;
                RecordError(rule.Name, "Rule '" + rule.Name + "' dismissed " + MaxDismissalsPerMinute
                    + " popups in the last minute, so it stopped dismissing. Its popup keeps coming back; "
                    + "call SetRuleEnabled to turn it back on once that is sorted out.");
                return;
            }
            if (state.Attempts >= MaxAttempts)
            {
                Fail(state, rule, info, text, processName, "The popup was still open after " + state.Attempts + " attempts.");
                return;
            }

            PopupButton button = null;
            string label = "(close)";
            if (rule.Action != PopupAction.CloseWindow)
            {
                IReadOnlyList<PopupButton> buttons = _probe.GetButtons(state.Handle);
                if (buttons == null || buttons.Count == 0)
                {
                    if (!Retry(state, now))
                        Fail(state, rule, info, text, processName,
                            "The popup has no native buttons to click (it may be a WinUI/UWP or custom-drawn popup); use a close rule, or KeyboardUtils.");
                    return;
                }
                button = Pick(rule, buttons);
                if (button == null)
                {
                    if (!Retry(state, now))
                        Fail(state, rule, info, text, processName, "The popup has no button matching the rule. Buttons: " + Describe(buttons) + ".");
                    return;
                }
                label = button.Text;
            }

            state.Attempts++;
            if (button != null)
                _probe.ClickButton(button, EnabledWaitMs);
            else
                _probe.CloseWindow(state.Handle);

            if (WaitClosed(state.Handle))
            {
                lock (_lock)
                {
                    rule.RecentDismissals.Enqueue(now);
                    _counts[rule.Name] = (_counts.TryGetValue(rule.Name, out int c) ? c : 0) + 1;
                    _total++;
                }
                _windows.Remove(state.Handle);
                Record(PopupRecordKind.Dismissed, rule.Name, info.Title, text, processName, info.ProcessId, label, state.Attempts, string.Empty);
                return;
            }

            if (state.Attempts >= MaxAttempts)
                Fail(state, rule, info, text, processName, "The popup was still open after " + state.Attempts + " attempts.");
            else
                state.NextDue = now + RetryDelayMs;
        }

        private bool IsRunaway(PopupRule rule, long now)
        {
            lock (_lock)
            {
                while (rule.RecentDismissals.Count > 0 && now - rule.RecentDismissals.Peek() > RunawayWindowMs)
                    rule.RecentDismissals.Dequeue();
                return rule.RecentDismissals.Count >= MaxDismissalsPerMinute;
            }
        }

        private void Fail(WinState state, PopupRule rule, PopupWindowInfo info, string text, string processName, string detail)
        {
            state.Failed = true;
            state.NextDue = long.MaxValue;
            Record(PopupRecordKind.DismissFailed, rule.Name, info.Title, text, processName, info.ProcessId, string.Empty, state.Attempts, detail);
        }

        /// <summary>Moves to the next time slot for the window; false if there is none left.</summary>
        private static bool Retry(WinState state, long now)
        {
            if (state.Step + 1 >= ScheduleMs.Length)
                return false;
            state.Step++;
            state.NextDue = Math.Max(state.FirstSeen + ScheduleMs[state.Step], now + 1);
            return true;
        }

        private bool WaitClosed(IntPtr hwnd)
        {
            for (int i = 0; i < VerifyPolls; i++)
            {
                _sleep(VerifyPollMs);
                if (!_probe.IsWindow(hwnd))
                    return true;
            }
            return !_probe.IsWindow(hwnd);
        }

        internal static PopupButton Pick(PopupRule rule, IReadOnlyList<PopupButton> buttons)
        {
            foreach (var button in buttons)
            {
                if (rule.Action == PopupAction.ClickButtonId)
                {
                    if (button.Id == rule.ButtonId)
                        return button;
                }
                else if (rule.ExactButtonText
                    ? string.Equals(button.Text, rule.ButtonText, StringComparison.OrdinalIgnoreCase)
                    : PopupRule.Contains(button.Text, rule.ButtonText))
                {
                    return button;
                }
            }
            return null;
        }

        private static string Describe(IReadOnlyList<PopupButton> buttons)
        {
            var parts = new List<string>();
            foreach (var button in buttons)
                parts.Add("'" + button.Text + "'");
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------------ recording

        private void Record(PopupRecordKind kind, string ruleName, string title, string message, string processName,
            uint processId, string button, int attempts, string detail)
        {
            var record = new PopupRecord
            {
                Kind = kind,
                RuleName = ruleName ?? string.Empty,
                Title = title ?? string.Empty,
                MessageText = message ?? string.Empty,
                ProcessName = processName ?? string.Empty,
                ProcessId = processId,
                ButtonClicked = button ?? string.Empty,
                Attempts = attempts,
                TimestampUtc = DateTime.UtcNow,
                Detail = detail ?? string.Empty
            };
            lock (_lock)
            {
                _log.Add(record);
                if (_log.Count > LogCapacity)
                    _log.RemoveRange(0, _log.Count - LogCapacity);
            }
            Deliver(record);
        }

        /// <summary>Forgets every tracked window and queued report. Only call while no worker is pumping.</summary>
        internal void ResetRuntime()
        {
            while (_queue.TryDequeue(out _))
                Interlocked.Decrement(ref _queued);
            _windows.Clear();
            _swept = false;
            Volatile.Write(ref _unresolved, 0);
        }

        internal void RecordError(string ruleName, string message) =>
            Record(PopupRecordKind.Error, ruleName, string.Empty, string.Empty, string.Empty, 0, string.Empty, 0, message);

        private void Deliver(PopupRecord record)
        {
            try
            {
                _sink?.Invoke(record);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // The consumer of the log must never be able to stop the worker.
                Debug.WriteLine("InterruptUtils: record sink failed: " + ex.Message);
            }
        }
    }
}
