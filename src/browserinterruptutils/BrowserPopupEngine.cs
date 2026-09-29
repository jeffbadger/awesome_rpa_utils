using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// The browser interrupt handler's decision logic: thread-free and free of
    /// <c>System.Windows.Automation</c>/Win32, so it can be driven deterministically through
    /// <see cref="IBrowserPopupProbe"/>/<see cref="IBrowserPopupHookSource"/> fakes. Mirrors
    /// <c>InterruptAutomation.PopupEngine</c> architecturally; see the type-level remarks below
    /// for the places this component's two-scope (native dialog vs. page overlay) model forced a
    /// genuinely different design, each called out explicitly for the Task 5 implementer.
    /// </summary>
    /// <remarks>
    /// Judgment calls made while adapting the reference engine to this component's actual
    /// contracts (Task 1's <see cref="IBrowserPopupProbe"/>/<see cref="IBrowserPopupHookSource"/>
    /// and <see cref="BrowserPopupRule"/>), each worth Task 5 knowing about:
    /// <list type="bullet">
    /// <item>
    /// <b>Verify-before-count, with the same in-flight gate.</b> The reference's
    /// <c>Dismiss</c> clicks a button and then polls <c>IsWindow</c> for ~300ms to confirm the
    /// window closed. <see cref="IBrowserPopupProbe.TryInvoke"/>/<see cref="IBrowserPopupProbe.TryClose"/>
    /// report success synchronously, but UI Automation returns before the browser acts, so
    /// success proves nothing. Instead of sleeping on the worker, a successful action leaves the
    /// candidate in place, still unresolved, with a deadline <see cref="VerifyDelayMs"/> later.
    /// At the deadline <see cref="IBrowserPopupProbe.IsAlive"/> decides: gone means the dismissal
    /// is recorded and counted (per-rule count, total, runaway window); still open means the
    /// action did nothing, so it consumes an attempt (retry through the normal path, or
    /// <c>DismissFailed</c> once <see cref="MaxAttempts"/> is spent) and no <c>Dismissed</c> is
    /// ever recorded for it. That bounds a do-nothing invoke to <see cref="MaxAttempts"/> per
    /// popup instance; the runaway breaker only sees confirmed dismissals, which is what it is
    /// for (a popup that keeps coming back after real dismissals). The verification is
    /// read-only, so it takes no action lock and needs no Paused/rule re-check; a retry it
    /// schedules goes through <see cref="Act"/> and its re-checks. The action
    /// still has a window to race against, though: a pass reads <see cref="Paused"/>, then does
    /// slow UIA discovery, then acts. So, like the reference, the worker holds an action lock
    /// around the final <see cref="Paused"/>/rule-enabled/rule-registered re-check, the probe
    /// call and the recording of its result (never during discovery), and
    /// <see cref="WaitForIdle"/> takes that lock. <see cref="SetRuleEnabled"/> (disabling),
    /// <see cref="RemoveRule"/> and <see cref="ClearRules"/> call it, and the public Pause calls it
    /// after setting <see cref="Paused"/>, so once they return no new probe invoke/close starts for
    /// what was switched off beforehand and any under way has finished. That is all it guarantees:
    /// the lock covers only the probe call, which returns before the browser acts, so an action
    /// issued just before may still take effect, and its verification (which runs while paused, as
    /// it is read-only) may still record <c>Dismissed</c> or <c>DismissFailed</c> afterwards.
    /// </item>
    /// <item>
    /// <b>Per-rule runaway/trip/retry state lives in the engine, not on the rule.</b> The
    /// reference's <c>PopupRule</c> carries its own <c>Tripped</c>/<c>RecentDismissals</c>/
    /// <c>RetryToken</c> fields; Task 1's <see cref="BrowserPopupRule"/> is a plain data model
    /// with none of that, so this engine keeps a private <c>RuleRuntime</c> record per rule name
    /// (in <c>_ruleRuntime</c>, guarded by <c>_lock</c>). <see cref="IsRuleTripped"/> is the
    /// query Task 5 needs in place of reading <c>rule.Tripped</c> directly (compare
    /// <c>InterruptUtils.cs</c>' <c>stopped = rule.Tripped</c> when it serializes rules to JSON).
    /// </item>
    /// <item>
    /// <b><see cref="IBrowserPopupProbe.FindOverlayCandidates"/> is reused as the engine's only
    /// "find a descendant" primitive.</b> <see cref="BrowserPopupRule.Action"/>'s
    /// <c>InvokeByName</c>/<c>InvokeByAutomationId</c> need to find a specific descendant element
    /// inside an already-identified popup (its OK/Accept button, say), but Task 1's probe
    /// interface exposes no "find one descendant by name" method - only a bounded subtree walk
    /// documented as returning ARIA-dialog-shaped elements. Since that is the only subtree-walk
    /// primitive available, this engine calls it a second way: with the popup itself as the root,
    /// to enumerate its descendants and pick the one matching
    /// <see cref="BrowserPopupRule.TargetElementName"/>/<see cref="BrowserPopupRule.TargetAutomationId"/>.
    /// This means Task 3's real UIA probe must make sure that, given an already-identified
    /// popup/dialog element as the root, <c>FindOverlayCandidates</c> also surfaces its plain
    /// interactive descendants (buttons etc.), not only further nested dialog-shaped containers -
    /// a load-bearing detail the interface's doc comment does not spell out. This reuse only ever
    /// walks a single already-matched popup's own (small) subtree, so it never costs anything for
    /// a process no <see cref="BrowserPopupScope.PageOverlay"/> rule targets - the perf-gating
    /// contract concerns the periodic/dirty-signal overlay *discovery* sweep, covered next.
    /// </item>
    /// <item>
    /// <b>Overlay discovery is opt-in per process, driven by which rules are registered.</b> The
    /// engine tracks, from each rules-version bump, which process names at least one registered
    /// <see cref="BrowserPopupScope.PageOverlay"/> rule names (case-insensitive, trailing
    /// <c>.exe</c> ignored). Every rule must name its process (<see cref="BrowserPopupRule.ValidateCommon"/>
    /// rejects one that does not): nothing in the engine can tell a browser from any other
    /// application, so a rule that opted in to "every process" would describe, watch and sweep the
    /// whole desktop. Only for a window whose process is in that interest set does the engine ever
    /// call <see cref="IBrowserPopupHookSource.WatchWindow"/> (enabling
    /// <see cref="IBrowserPopupHookSource.WindowStructureChanged"/> for it) or call
    /// <see cref="IBrowserPopupProbe.FindOverlayCandidates"/> against it during a sweep; a window
    /// unwatched (process no longer of interest, or the window is gone) is immediately told to
    /// <see cref="IBrowserPopupHookSource.UnwatchWindow"/>.
    /// </item>
    /// <item>
    /// <b>Configuration is by settable property, not constructor parameter or a separate
    /// <c>Configure</c> method</b> - mirrors the reference's <c>SweepIntervalMs</c>/
    /// <c>MaxAttempts</c>/<c>MaxDismissalsPerMinute</c>, which <c>InterruptUtils.Start</c> sets on
    /// the engine before starting the worker. Task 5's <c>Start(...)</c> should do the same here.
    /// </item>
    /// <item>
    /// <b><see cref="Pump"/>'s return value and the wake signal both exist</b>, exactly mirroring
    /// the reference: <see cref="Pump"/> returns the next due tick (or <see cref="long.MaxValue"/>)
    /// so a worker loop can compute a bounded sleep, and <see cref="Wake"/>/<see cref="WaitForWork"/>
    /// let that sleep be cut short the moment a hook event, rule change, or fault arrives, instead
    /// of waiting out a stale timeout.
    /// </item>
    /// <item>
    /// <b>A candidate's structural snapshot is refreshed differently per scope.</b> For a
    /// <see cref="BrowserPopupScope.NativeDialog"/> candidate, <see cref="IBrowserPopupProbe.DescribeWindow"/>
    /// is called fresh on every <see cref="Evaluate"/>, exactly like the reference's
    /// <c>Describe(hwnd)</c>. For a <see cref="BrowserPopupScope.PageOverlay"/> candidate there is
    /// no per-element "describe" method - only the bulk <c>FindOverlayCandidates</c> sweep that
    /// found it - so its cached <see cref="BrowserElementInfo"/> is refreshed only when it
    /// reappears in a later sweep; liveness (<see cref="IBrowserPopupProbe.IsAlive"/>) and message
    /// text (<see cref="IBrowserPopupProbe.TryGetMessageText"/>) are still always fetched fresh,
    /// for both scopes.
    /// </item>
    /// <item>
    /// <b>Target-name matching for <see cref="BrowserPopupAction.InvokeByAutomationId"/> is an
    /// exact, case-insensitive match</b> (unlike <see cref="BrowserPopupAction.InvokeByName"/>,
    /// which respects <see cref="BrowserPopupRule.ExactTargetElementName"/>), since Task 1 gave
    /// automation-ID targeting no equivalent "exact" flag and automation IDs are ordinarily
    /// stable, exact identifiers rather than free text.
    /// </item>
    /// <item>
    /// <b><c>_processNameCache</c> is long-lived, unlike the reference's per-pass cache.</b> The
    /// reference's <c>PopupEngine.Pump</c> clears its <c>_processNames</c> lookup at the top of
    /// every pass, with a comment explaining that a reused process id can then never resolve to a
    /// stale name from an earlier pass - every window it ever looks at has a real Win32 handle it
    /// can re-describe on the spot, so nothing is lost by throwing the lookup away each time.
    /// Task 1's <see cref="BrowserElementInfo"/> gives a <see cref="BrowserPopupScope.PageOverlay"/>
    /// candidate only a <see cref="BrowserElementInfo.ProcessId"/>, never a process name - and that
    /// PID can be a renderer's rather than the browser window's - so an overlay INHERITS its
    /// identity (PID, and through this cache the name) from the watched top-level window the sweep
    /// walked; see <c>SweepOneOverlayWindow</c>. And there
    /// is no per-element "describe" call this engine can make to learn one directly (see the
    /// candidate-refresh bullet above) - the only place a process name is ever learned is
    /// <see cref="IBrowserPopupProbe.DescribeWindow"/>/<see cref="IBrowserPopupProbe.EnumerateTopLevelWindows"/>,
    /// both <see cref="BrowserPopupScope.NativeDialog"/>-only. So <c>_processNameCache</c> has to
    /// outlive a single <see cref="Pump"/> pass, or a <see cref="BrowserPopupRule.ProcessName"/>
    /// criterion could never be satisfied for an overlay rule at all: clearing it every pass the
    /// way the reference does would mean nothing ever populates it again for a window that was
    /// already known before that pass began. The risk this trades away: if a process exits and its
    /// PID is reused by an unrelated process before every candidate that still carries the old PID
    /// has been removed, <see cref="ProcessNameOf"/> can return the exited process's stale name for
    /// the new one - wrongly matching, or failing to match, a <see cref="BrowserPopupRule.ProcessName"/>
    /// criterion (including via <see cref="IsOverlayInterestingProcess"/>, which decides overlay
    /// watch interest). <see cref="RemoveCandidate"/> bounds this window as tightly as the engine
    /// can manage with no independent "this process exited" signal: it evicts a PID's cache entry
    /// the moment no tracked candidate of either scope still carries it, rather than waiting for
    /// <see cref="ResetRuntime"/>.
    /// </item>
    /// </list>
    /// </remarks>
    internal sealed class BrowserPopupEngine : IDisposable
    {
        /// <summary>
        /// When to re-check a newly seen candidate, in milliseconds after it was first seen. A
        /// freshly rendered popup or overlay announces itself before its buttons/Invoke targets
        /// actually exist. Identical to the reference's <c>PopupEngine.ScheduleMs</c>.
        /// </summary>
        internal static readonly int[] ScheduleMs = { 0, 150, 400, 1000, 2000 };

        internal const int LogCapacity = 500;
        internal const int MaxTrackedCandidates = 2000;
        internal const int MaxQueuedItems = 4096;
        internal const int RetryDelayMs = 250;
        internal const int PausedRecheckMs = 250;
        internal const int RunawayWindowMs = 60000;

        /// <summary>
        /// How long after a successful invoke/close the engine waits before checking whether the
        /// popup really went away. UI Automation's <c>Invoke</c>/<c>Close</c> return before the
        /// browser has acted, so success of the call proves nothing; a dismissal is only recorded
        /// and counted once <see cref="IBrowserPopupProbe.IsAlive"/> says the element is gone.
        /// </summary>
        internal const int VerifyDelayMs = 400;

        /// <summary>
        /// How often (milliseconds, on the pump's clock) the engine checks whether parked
        /// candidates, overlay or native - watch-only matches, failed dismissals, unmatched elements whose retry schedule
        /// ran out - are still alive, dropping the dead ones. A sweep only ever processes the
        /// elements it finds, so without this a banner that comes and goes on its own (or a browser window
        /// tracked only as a watch owner) would sit in the candidate table until
        /// <see cref="MaxTrackedCandidates"/> blocked every new popup. Independent of
        /// <see cref="OverlaySweepIntervalMs"/> and <see cref="SweepIntervalMs"/> (it also runs when they are 0).
        /// </summary>
        internal const int ReapIntervalMs = 2000;

        /// <summary>The most <see cref="IBrowserPopupProbe.IsAlive"/> checks one reap pass makes in total, both kinds (a UIA property read each); a cursor carries the rest to the next pass.</summary>
        internal const int ReapMaxChecksPerPass = 256;

        /// <summary>The floor enforced on <see cref="OverlaySweepIntervalMs"/> when it is set above zero.</summary>
        internal const int MinOverlaySweepIntervalMs = 500;

        private sealed class RuleRuntime
        {
            public bool Tripped;
            public readonly Queue<long> RecentDismissals = new Queue<long>();
            public int RetryToken;
        }

        /// <summary>What an invoke/close that reported success left behind until the popup is seen to close.</summary>
        private sealed class PendingDismissal
        {
            public BrowserPopupRule Rule;
            public BrowserElementInfo Info;
            public string Text;
            public string ProcessName;
            public string Label;
            public long ActedAt;
            public string Scope;
        }

        private sealed class CandidateState
        {
            public BrowserElementRef Ref;
            public BrowserPopupScope Scope;

            /// <summary><see cref="BrowserPopupScope.NativeDialog"/> only; the window handle, re-describe key.</summary>
            public IntPtr Hwnd;

            /// <summary><see cref="BrowserPopupScope.PageOverlay"/> only; the browser window this overlay was found under.</summary>
            public BrowserElementRef OwnerWindowRef;

            public int ProcessId;
            public BrowserElementInfo Info;
            public long FirstSeen;
            public long NextDue;
            public int Step;
            public int Attempts;
            public bool Failed;
            public bool Reported;
            public BrowserPopupRule Rule;
            public int RetryToken;

            /// <summary>Set between a successful action and its verification (<see cref="VerifyDelayMs"/> later); the candidate stays counted as unresolved meanwhile.</summary>
            public PendingDismissal Pending;

            public bool Unresolved => Rule != null && Rule.Action != BrowserPopupAction.WatchOnly;

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
                Pending = null;
            }
        }

        private readonly IBrowserPopupProbe _probe;
        private readonly IBrowserPopupHookSource _hookSource;
        private readonly Action<BrowserPopupRecord> _sink;

        private readonly object _lock = new object();
        private readonly List<BrowserPopupRule> _rules = new List<BrowserPopupRule>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RuleRuntime> _ruleRuntime = new Dictionary<string, RuleRuntime>(StringComparer.OrdinalIgnoreCase);
        private readonly List<BrowserPopupRecord> _log = new List<BrowserPopupRecord>();
        private BrowserPopupRule[] _ruleSnapshot = Array.Empty<BrowserPopupRule>();
        private int _total;

        // Process names of interest to at least one registered PageOverlay rule (trimmed, no
        // ".exe"), recomputed whenever the rule list changes. See the type remarks.
        private HashSet<string> _overlayProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Same, for enabled NativeDialog rules: a top-level window is only tracked/described when
        // its process is named by one of them. See TrackNativeWindow.
        private HashSet<string> _nativeProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentQueue<BrowserWindowInfo> _nativeQueue = new ConcurrentQueue<BrowserWindowInfo>();
        private int _nativeQueued;
        private readonly ConcurrentQueue<BrowserElementRef> _overlayDirtyQueue = new ConcurrentQueue<BrowserElementRef>();
        private int _overlayDirtyQueued;
        private readonly ConcurrentQueue<string> _faults = new ConcurrentQueue<string>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        private int _rulesVersion;
        private int _seenRulesVersion;

        // Worker-thread-only state (touched only from Pump's call graph; needs no lock).
        private readonly Dictionary<BrowserElementRef, CandidateState> _candidates = new Dictionary<BrowserElementRef, CandidateState>();
        private readonly Dictionary<IntPtr, BrowserElementRef> _hwndIndex = new Dictionary<IntPtr, BrowserElementRef>();
        private readonly Dictionary<IntPtr, BrowserElementRef> _watchedWindows = new Dictionary<IntPtr, BrowserElementRef>();
        private readonly Dictionary<int, string> _processNameCache = new Dictionary<int, string>();
        private readonly List<CandidateState> _due = new List<CandidateState>();
        private long _lastNativeSweep;
        private bool _nativeSwept;
        private long _lastOverlaySweep;
        private bool _overlaySwept;
        private int _unresolved;
        private int _overlaySweepIntervalMs;
        private long _lastReap;
        private bool _reapStarted;
        private int _reapCursor;

        // Records produced by the worker thread while it is inside Pump are held back and handed to
        // the sink only after the pass has finished updating ALL engine state (counts, removed
        // candidates, the unresolved count, the process-name cache). Otherwise a subscriber woken by
        // the event (or reading state from inside the handler) could observe a half-updated
        // engine, e.g. Dismissed delivered while HasUnresolvedPopup is still true.
        private readonly List<BrowserPopupRecord> _deferred = new List<BrowserPopupRecord>();
        private int _pumpThreadId; // managed thread id currently inside Pump, 0 when none

        // Overlay windows already swept during the current Pump pass, so a dirty-signal sweep and
        // the periodic sweep (or N signals for one window) never walk the same window twice.
        private readonly HashSet<BrowserElementRef> _sweptThisPass = new HashSet<BrowserElementRef>();

        // Set once the candidate cap has been reported, cleared when the count drops below it, so
        // a page that keeps the engine at the cap logs one error, not one per sweep.
        private bool _capReported;

        // Windows the hook reported that no rule wanted at the time (see TrackNativeWindow), so a
        // rule added or enabled later can still act on one that is already open even with the
        // periodic scan off. Bounded and oldest-first; a window with no need is never described.
        internal const int MaxSkippedWindows = 256;
        private readonly Dictionary<IntPtr, BrowserWindowInfo> _skippedWindows = new Dictionary<IntPtr, BrowserWindowInfo>();
        private readonly Queue<IntPtr> _skippedOrder = new Queue<IntPtr>();

        // The six settable configuration properties below are plain, unsynchronized properties -
        // like the reference's identical SweepIntervalMs/MaxAttempts/MaxDismissalsPerMinute, this
        // is not a regression. Per the "configuration is by settable property" remark above, each
        // one must be set before a worker loop starts calling Pump, not hot-swapped while one is
        // running: unlike Paused (volatile) and the rule-CRUD methods (lock-guarded), reading one
        // of these mid-Pump is not guaranteed to observe a concurrent write.

        /// <summary>
        /// How often to scan for native-dialog windows the hook did not report; 0 turns the scan
        /// off. Set before starting the worker loop; not safe to change while <see cref="Pump"/>
        /// is running on another thread.
        /// </summary>
        internal int SweepIntervalMs { get; set; } = 1000;

        /// <summary>
        /// How often to sweep every currently watched browser window's subtree for page
        /// overlays; 0 turns the periodic sweep off (overlay discovery still happens on
        /// <see cref="IBrowserPopupHookSource.WindowStructureChanged"/>). Clamped to at least
        /// <see cref="MinOverlaySweepIntervalMs"/> whenever set above zero, since each sweep is a
        /// bounded but real cross-process subtree walk per watched window. Set before starting the
        /// worker loop; not safe to change while <see cref="Pump"/> is running on another thread.
        /// </summary>
        internal int OverlaySweepIntervalMs
        {
            get => _overlaySweepIntervalMs;
            set => _overlaySweepIntervalMs = value <= 0 ? 0 : Math.Max(value, MinOverlaySweepIntervalMs);
        }

        /// <summary>
        /// How many times to try to dismiss one popup before giving up on it. Set before starting
        /// the worker loop; not safe to change while <see cref="Pump"/> is running on another thread.
        /// </summary>
        internal int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// How many popups one rule may dismiss in a minute before it stops itself. Set before
        /// starting the worker loop; not safe to change while <see cref="Pump"/> is running on
        /// another thread.
        /// </summary>
        internal int MaxDismissalsPerMinute { get; set; } = 20;

        /// <summary>
        /// The most elements a single <see cref="IBrowserPopupProbe.FindOverlayCandidates"/> walk
        /// may visit. Set before starting the worker loop; not safe to change while
        /// <see cref="Pump"/> is running on another thread.
        /// </summary>
        /// <remarks>The engine-level default is conservative; <c>BrowserInterruptUtils.Start</c> always overrides it (public default 5000).</remarks>
        internal int MaxOverlayNodes { get; set; } = 500;

        /// <summary>
        /// The deepest a single <see cref="IBrowserPopupProbe.FindOverlayCandidates"/> walk may
        /// descend. Set before starting the worker loop; not safe to change while <see cref="Pump"/>
        /// is running on another thread.
        /// </summary>
        /// <remarks>The engine-level default is conservative; <c>BrowserInterruptUtils.Start</c> always overrides it (public default 50).</remarks>
        internal int MaxOverlayDepth { get; set; } = 25;

        // Held around the final action-state re-check and the probe's TryInvoke/TryClose (never
        // during the slow discovery walks), so lifecycle and rule changes can wait for an action
        // already under way instead of returning while it is still about to land.
        private readonly object _actionLock = new object();
        private bool _actionsStopped;
        private int _resumed;

        /// <summary>
        /// While set, popups are noticed (a matching one is selected, and counts as unresolved) but
        /// not touched; a rule's action on them is taken after it is cleared with <see cref="Resume"/>.
        /// Setting it does not by itself wait for a call already under way; call
        /// <see cref="WaitForIdle"/> afterwards for that.
        /// </summary>
        internal volatile bool Paused;

        internal BrowserPopupEngine(IBrowserPopupProbe probe, IBrowserPopupHookSource hookSource, Action<BrowserPopupRecord> sink)
        {
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _hookSource = hookSource ?? throw new ArgumentNullException(nameof(hookSource));
            _sink = sink;

            // Handlers here must never do real work: they only enqueue. All evaluation happens
            // inside Pump, on the single worker thread Task 5 owns. See the type remarks.
            _hookSource.WindowOpened += OnWindowOpened;
            _hookSource.WindowStructureChanged += OnWindowStructureChanged;
        }

        public void Dispose()
        {
            try { _hookSource.WindowOpened -= OnWindowOpened; }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
            try { _hookSource.WindowStructureChanged -= OnWindowStructureChanged; }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
            _wake.Dispose();
        }

        // ------------------------------------------------------------------ rules

        internal bool AddRule(BrowserPopupRule rule, out string message)
        {
            if (rule == null)
            {
                message = "rule is required.";
                return false;
            }
            string validation = BrowserPopupRule.ValidateCommon(rule.RuleName, rule.NameContains, rule.MessageContains, rule.AutomationIdContains,
                rule.ProcessName, rule.RoleContains, rule.Scope, rule.Action);
            if (validation != null)
            {
                message = validation;
                return false;
            }
            lock (_lock)
            {
                if (_rules.Count >= BrowserPopupRule.MaxRules)
                {
                    message = "At most " + BrowserPopupRule.MaxRules + " rules are allowed.";
                    return false;
                }
                foreach (var existing in _rules)
                {
                    if (string.Equals(existing.RuleName, rule.RuleName, StringComparison.OrdinalIgnoreCase))
                    {
                        message = "A rule named '" + existing.RuleName + "' already exists.";
                        return false;
                    }
                }
                _rules.Add(rule);
                _counts[rule.RuleName] = 0;
                _ruleRuntime[rule.RuleName] = new RuleRuntime();
                _ruleSnapshot = _rules.ToArray();
                Interlocked.Increment(ref _rulesVersion);
            }
            Wake();
            message = null;
            return true;
        }

        internal bool RemoveRule(string name)
        {
            lock (_lock)
            {
                int index = _rules.FindIndex(r => string.Equals(r.RuleName, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    return false;
                _counts.Remove(_rules[index].RuleName);
                _ruleRuntime.Remove(_rules[index].RuleName);
                _rules.RemoveAt(index);
                _ruleSnapshot = _rules.ToArray();
                Interlocked.Increment(ref _rulesVersion);
            }
            WaitForIdle();
            Wake();
            return true;
        }

        internal void ClearRules()
        {
            lock (_lock)
            {
                _rules.Clear();
                _counts.Clear();
                _ruleRuntime.Clear();
                _ruleSnapshot = Array.Empty<BrowserPopupRule>();
                Interlocked.Increment(ref _rulesVersion);
            }
            WaitForIdle();
            Wake();
        }

        /// <summary>Enables or disables a rule. Enabling also clears a runaway trip and the record of recent dismissals.</summary>
        internal bool SetRuleEnabled(string name, bool enabled)
        {
            lock (_lock)
            {
                var rule = _rules.Find(r => string.Equals(r.RuleName, name, StringComparison.OrdinalIgnoreCase));
                if (rule == null)
                    return false;
                rule.Enabled = enabled;
                if (enabled && _ruleRuntime.TryGetValue(rule.RuleName, out var runtime))
                {
                    runtime.Tripped = false;
                    runtime.RecentDismissals.Clear();
                    runtime.RetryToken++;
                }
                Interlocked.Increment(ref _rulesVersion);
            }
            if (!enabled)
                WaitForIdle();
            Wake();
            return true;
        }

        /// <summary>
        /// Returns once any invoke or close the worker is in the middle of has finished. The worker
        /// re-checks <see cref="Paused"/> and the rule's state under the same lock before it starts
        /// one, so after this returns no new invoke or close starts for what was switched off
        /// beforehand. It does not stop the browser acting on a call that already returned, nor the
        /// pending verification of such a call from recording its outcome.
        /// Safe to call from the worker thread (for example from an event subscriber): the lock is
        /// re-entrant and the worker never holds it while it raises an event.
        /// </summary>
        internal void WaitForIdle()
        {
            lock (_actionLock)
            {
            }
        }

        /// <summary>
        /// Clears <see cref="Paused"/> and wakes the worker. Candidates a rule matched while paused
        /// are held with a recheck due up to <see cref="PausedRecheckMs"/> later; the next pass
        /// makes them due at once, so they are handled promptly rather than after that hold-back.
        /// </summary>
        internal void Resume()
        {
            Paused = false;
            Volatile.Write(ref _resumed, 1);
            Wake();
        }

        /// <summary>Makes the candidates held for <see cref="Resume"/> due now (once per resume).</summary>
        private void ResumeHeldCandidates(long now)
        {
            if (Interlocked.Exchange(ref _resumed, 0) == 0)
                return;
            foreach (var state in _candidates.Values)
            {
                if (state.Rule != null && state.Pending == null && !state.Failed && state.NextDue != long.MaxValue && state.NextDue > now)
                    state.NextDue = now;
            }
        }

        /// <summary>Stops new invoke/close calls and waits for any one already under way to finish.</summary>
        internal void StopActions()
        {
            lock (_actionLock)
                _actionsStopped = true;
        }

        private bool IsRegistered(BrowserPopupRule rule) => Array.IndexOf(SnapshotRules(), rule) >= 0;

        internal BrowserPopupRule[] SnapshotRules()
        {
            lock (_lock)
                return _ruleSnapshot;
        }

        /// <summary>Whether the named rule has stopped itself for dismissing too many popups too fast.</summary>
        internal bool IsRuleTripped(string ruleName)
        {
            lock (_lock)
                return _ruleRuntime.TryGetValue(ruleName ?? string.Empty, out var runtime) && runtime.Tripped;
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

        /// <summary>The number of candidates a click/invoke/close rule matched that are still unresolved, as of the last <see cref="Pump"/>.</summary>
        internal int UnresolvedCount => Volatile.Read(ref _unresolved);

        internal bool HasUnresolvedPopup => UnresolvedCount > 0;

        /// <summary>The number of tracked candidates of one scope. For tests; not thread-safe against a running worker.</summary>
        internal int TrackedCandidateCountForTests(BrowserPopupScope scope)
        {
            int count = 0;
            foreach (var state in _candidates.Values)
            {
                if (state.Scope == scope)
                    count++;
            }
            return count;
        }

        internal BrowserPopupRecord[] GetLog(int maxEntries)
        {
            lock (_lock)
            {
                int take = Math.Min(Math.Max(maxEntries, 0), _log.Count);
                var result = new BrowserPopupRecord[take];
                _log.CopyTo(_log.Count - take, result, 0, take);
                return result;
            }
        }

        internal BrowserPopupRecord LastRecord()
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

        private void OnWindowOpened(BrowserWindowInfo info)
        {
            if (info == null)
                return;
            if (Volatile.Read(ref _nativeQueued) >= MaxQueuedItems)
                return; // the periodic native sweep will find anything dropped here
            Interlocked.Increment(ref _nativeQueued);
            _nativeQueue.Enqueue(info);
            Wake();
        }

        private void OnWindowStructureChanged(BrowserElementRef windowRoot)
        {
            if (Volatile.Read(ref _overlayDirtyQueued) >= MaxQueuedItems)
                return; // the periodic overlay sweep is the fallback
            Interlocked.Increment(ref _overlayDirtyQueued);
            _overlayDirtyQueue.Enqueue(windowRoot);
            Wake();
        }

        /// <summary>
        /// Reports a failure of the hook's event pump. Only queued: the worker records it on its
        /// next pass, so <c>InterruptError</c> (a later phase's event) is raised on the worker
        /// thread like every other event, never inside the hook's own callback. Pass this as the
        /// <c>onFault</c> argument when calling <see cref="IBrowserPopupHookSource.Start"/>.
        /// </summary>
        internal void EnqueueFault(string text)
        {
            if (string.IsNullOrEmpty(text) || _faults.Count >= 64)
                return;
            _faults.Enqueue(text);
            Wake();
        }

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

        /// <summary>Forgets every tracked candidate, queued report and watch registration. Only call while no worker is pumping.</summary>
        internal void ResetRuntime()
        {
            lock (_actionLock)
                _actionsStopped = false;

            while (_faults.TryDequeue(out _)) { }
            while (_nativeQueue.TryDequeue(out _)) Interlocked.Decrement(ref _nativeQueued);
            while (_overlayDirtyQueue.TryDequeue(out _)) Interlocked.Decrement(ref _overlayDirtyQueued);

            foreach (var windowRef in _watchedWindows.Values)
            {
                try { _hookSource.UnwatchWindow(windowRef); }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
            }
            _watchedWindows.Clear();
            _skippedWindows.Clear();
            _skippedOrder.Clear();
            foreach (var key in new List<BrowserElementRef>(_candidates.Keys))
                DropCandidate(key);
            _hwndIndex.Clear();
            _processNameCache.Clear();
            _nativeSwept = false;
            _overlaySwept = false;
            _reapStarted = false;
            _reapCursor = 0;
            Volatile.Write(ref _unresolved, 0);
        }

        // ------------------------------------------------------------------ the pump

        /// <summary>
        /// Drains queues, applies pending rule changes, sweeps (native-dialog and/or overlay,
        /// each gated by its own interval) if due, evaluates every candidate whose next-check
        /// time has arrived, and returns the time (on the caller's clock) the next candidate is
        /// due, or <see cref="long.MaxValue"/> if none is - the single method an external worker
        /// loop calls repeatedly.
        /// </summary>
        internal long Pump(long now)
        {
            _pumpThreadId = Environment.CurrentManagedThreadId;
            try
            {
                return PumpCore(now);
            }
            finally
            {
                _pumpThreadId = 0;
                FlushDeferred();
            }
        }

        private long PumpCore(long now)
        {
            while (_faults.TryDequeue(out string fault))
                RecordError(string.Empty, fault);

            // Ahead of draining: the overlay process-interest set it (re)computes here is what
            // DrainNativeQueue's watch-registration decision below reads, so a rule added in the
            // same tick a window is first reported must be visible before that decision is made.
            _sweptThisPass.Clear();
            ApplyRuleChanges(now);
            ResumeHeldCandidates(now);
            DrainNativeQueue(now);
            DrainOverlayDirtyQueue(now);

            if (SweepIntervalMs > 0 && (!_nativeSwept || now - _lastNativeSweep >= SweepIntervalMs))
            {
                _nativeSwept = true;
                _lastNativeSweep = now;
                NativeSweep(now);
            }
            if (OverlaySweepIntervalMs > 0 && (!_overlaySwept || now - _lastOverlaySweep >= OverlaySweepIntervalMs))
            {
                _overlaySwept = true;
                _lastOverlaySweep = now;
                OverlaySweepAll(now);
            }

            ReapDeadCandidates(now);

            _due.Clear();
            foreach (var state in _candidates.Values)
            {
                if (state.NextDue <= now)
                    _due.Add(state);
            }
            foreach (var state in _due)
                Evaluate(state, now);

            long next = long.MaxValue;
            int unresolved = 0;
            bool anyParked = false;
            foreach (var state in _candidates.Values)
            {
                if (state.NextDue < next)
                    next = state.NextDue;
                if (state.Unresolved)
                    unresolved++;
                if (IsParked(state))
                    anyParked = true;
            }
            Volatile.Write(ref _unresolved, unresolved);
            if (_capReported && _candidates.Count < MaxTrackedCandidates)
                _capReported = false;

            if (SweepIntervalMs > 0)
                next = Math.Min(next, _lastNativeSweep + SweepIntervalMs);
            if (OverlaySweepIntervalMs > 0)
                next = Math.Min(next, _lastOverlaySweep + OverlaySweepIntervalMs);
            if (anyParked)
                next = Math.Min(next, _lastReap + ReapIntervalMs); // wake up to reap even if nothing else is due
            return next;
        }

        /// <summary>A candidate (either scope) waiting on nothing but a rule change or a sweep re-finding it (NextDue is never) and with no dismissal awaiting verification.</summary>
        private static bool IsParked(CandidateState state) =>
            state.NextDue == long.MaxValue && state.Pending == null;

        /// <summary>
        /// Drops parked candidates - overlay elements and native windows, including a browser window
        /// tracked only so its page can be watched for overlays - whose element is definitively gone.
        /// Runs whatever <see cref="SweepIntervalMs"/> and <see cref="OverlaySweepIntervalMs"/> are
        /// (0 turns discovery off, never this cleanup). Rate-limited to one
        /// pass per <see cref="ReapIntervalMs"/> and at most <see cref="ReapMaxChecksPerPass"/>
        /// liveness checks per pass (a cursor rotates through the rest). Absence from a bounded
        /// subtree walk is deliberately NOT taken as closure - a walk can stop at its node budget
        /// and miss a live element - so this only asks <see cref="IBrowserPopupProbe.IsAlive"/>, which
        /// is definitive for the pinned refs the engine admits (an unknown ref reports alive and is
        /// simply left alone). A watch-only popup that vanishes just stops being tracked: no
        /// <c>Dismissed</c> record (only a verified action produces one). A reaped native window goes
        /// through <see cref="RemoveCandidate"/> like any other removal: pin released, watch dropped,
        /// its overlays orphaned, its slot and process-name cache entry freed.
        /// </summary>
        private void ReapDeadCandidates(long now)
        {
            if (_reapStarted && now - _lastReap < ReapIntervalMs)
                return;
            _reapStarted = true;
            _lastReap = now;

            List<BrowserElementRef> parked = null;
            foreach (var pair in _candidates)
            {
                if (IsParked(pair.Value))
                    (parked ??= new List<BrowserElementRef>()).Add(pair.Key);
            }
            if (parked == null)
                return;

            List<BrowserElementRef> dead = null;
            int start = _reapCursor % parked.Count;
            int checks = Math.Min(parked.Count, ReapMaxChecksPerPass);
            for (int i = 0; i < checks; i++)
            {
                BrowserElementRef key = parked[(start + i) % parked.Count];
                if (!_probe.IsAlive(key))
                    (dead ??= new List<BrowserElementRef>()).Add(key);
            }
            _reapCursor = (start + checks) % parked.Count;

            if (dead == null)
                return;
            foreach (var key in dead)
            {
                // An earlier removal in this loop may have orphaned this one (an owner window's overlays).
                if (_candidates.TryGetValue(key, out var state) && IsParked(state))
                    RemoveCandidate(state);
            }
        }

        /// <summary>
        /// When the rules have changed: recomputes which processes overlay discovery cares about
        /// (watching/unwatching windows accordingly), looks again at candidates that had been
        /// parked, so a rule added (or switched on) while a popup is already open takes effect
        /// even with the scan off, and forgets what a removed rule had decided about a candidate.
        /// </summary>
        private void ApplyRuleChanges(long now)
        {
            int version = Volatile.Read(ref _rulesVersion);
            if (version == _seenRulesVersion)
                return;
            _seenRulesVersion = version;

            RecomputeOverlayInterest();
            RetrySkippedWindows(now);

            BrowserPopupRule[] rules = SnapshotRules();
            foreach (var state in _candidates.Values)
            {
                if (state.Rule != null && Array.IndexOf(rules, state.Rule) < 0)
                {
                    state.Rule = null;
                    state.Pending = null; // its rule is gone: nothing left to count or report
                    state.Failed = false;
                    state.Reported = false;
                    state.Attempts = 0;
                    state.NextDue = now;
                }
                else if (state.Rule != null && !state.Rule.Enabled && state.Pending == null)
                {
                    // The rule that selected this candidate (and may have parked it as Failed/Reported)
                    // was disabled: release it so the enabled rules are evaluated again, e.g. a second
                    // rule that also matches. A PENDING verification is left alone: the action was
                    // already issued and must still be confirmed and recorded (VerifyStillOpen).
                    ReleaseFromRule(state, now);
                }
                else if (state.Failed)
                {
                    int token = 0;
                    if (state.Rule != null)
                    {
                        lock (_lock)
                        {
                            token = _ruleRuntime.TryGetValue(state.Rule.RuleName, out var runtime) ? runtime.RetryToken : 0;
                        }
                    }
                    if (state.Rule != null && token != state.RetryToken)
                    {
                        state.Failed = false;
                        state.Attempts = 0;
                        state.NextDue = now;
                    }
                }
                else if (state.NextDue == long.MaxValue)
                {
                    state.NextDue = now;
                }
            }

            ReconcileOverlayWatches();
        }

        /// <summary>Forgets what a (now disabled) rule decided about a candidate and makes it due for re-evaluation.</summary>
        private static void ReleaseFromRule(CandidateState state, long now)
        {
            state.Rule = null;
            state.Failed = false;
            state.Reported = false;
            state.Attempts = 0;
            state.NextDue = now;
        }

        private void RecomputeOverlayInterest()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nativeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in SnapshotRules())
            {
                // A disabled rule never acts, so it must not keep a process in the watch set.
                // Turning it back on bumps the rules version, which recomputes this and re-watches.
                if (!rule.Enabled)
                    continue;
                // Every rule names its process (ValidateCommon), so there is always a name to add.
                if (rule.Scope == BrowserPopupScope.PageOverlay)
                    names.Add(BrowserPopupRule.TrimExe(rule.ProcessName));
                else
                    nativeNames.Add(BrowserPopupRule.TrimExe(rule.ProcessName));
            }
            _overlayProcessNames = names;
            _nativeProcessNames = nativeNames;
        }

        /// <summary>Whether some enabled NativeDialog rule could match a window of this process (the cheap pre-filter before describing it).</summary>
        private bool IsNativeInterestingProcess(string processName) =>
            !string.IsNullOrEmpty(processName) && _nativeProcessNames.Contains(BrowserPopupRule.TrimExe(processName));

        /// <summary>Whether a top-level window of this process is worth tracking at all: a native rule or an overlay rule wants it.</summary>
        private bool IsTrackableProcess(string processName) =>
            IsNativeInterestingProcess(processName) || IsOverlayInterestingProcess(processName);

        /// <summary>Notes, once until the count drops below the cap again, that discovery was cut short by <see cref="MaxTrackedCandidates"/>.</summary>
        private void NoteCandidateCapHit()
        {
            if (_capReported)
                return;
            _capReported = true;
            RecordError(string.Empty, "The engine is tracking " + MaxTrackedCandidates + " candidate popups, its safety limit, "
                + "so newly found windows or page elements are being ignored until some go away. "
                + "This usually means a rule is too broad; add roleContains/nameContains to narrow it.");
        }

        private bool IsOverlayInterestingProcess(string processName) =>
            !string.IsNullOrEmpty(processName) && _overlayProcessNames.Contains(BrowserPopupRule.TrimExe(processName));

        /// <summary>
        /// Un-watches a currently watched window whose process no longer interests any
        /// <see cref="BrowserPopupScope.PageOverlay"/> rule, and watches any already-known window
        /// that newly qualifies (a rule naming its process was just added).
        /// </summary>
        private void ReconcileOverlayWatches()
        {
            List<IntPtr> toUnwatch = null;
            foreach (var pair in _watchedWindows)
            {
                string processName = _hwndIndex.TryGetValue(pair.Key, out var candidateRef) && _candidates.TryGetValue(candidateRef, out var candidate)
                    ? ProcessNameOf(candidate.ProcessId)
                    : null;
                if (!IsOverlayInterestingProcess(processName))
                    (toUnwatch ??= new List<IntPtr>()).Add(pair.Key);
            }
            if (toUnwatch != null)
            {
                foreach (var hwnd in toUnwatch)
                {
                    _hookSource.UnwatchWindow(_watchedWindows[hwnd]);
                    _watchedWindows.Remove(hwnd);
                }
            }

            foreach (var pair in _hwndIndex)
            {
                if (_watchedWindows.ContainsKey(pair.Key))
                    continue;
                if (!_candidates.TryGetValue(pair.Value, out var candidate))
                    continue;
                if (IsOverlayInterestingProcess(ProcessNameOf(candidate.ProcessId)))
                {
                    _hookSource.WatchWindow(candidate.Ref);
                    _watchedWindows[pair.Key] = candidate.Ref;
                }
            }
        }

        private string ProcessNameOf(int processId) =>
            _processNameCache.TryGetValue(processId, out string name) ? name : string.Empty;

        private void CacheProcessName(int processId, string name)
        {
            if (!string.IsNullOrEmpty(name))
                _processNameCache[processId] = name;
        }

        // ------------------------------------------------------------------ native-dialog discovery

        private void DrainNativeQueue(long now)
        {
            while (_nativeQueue.TryDequeue(out BrowserWindowInfo win))
            {
                Interlocked.Decrement(ref _nativeQueued);
                if (_hwndIndex.TryGetValue(win.Hwnd, out var existingRef) && _candidates.TryGetValue(existingRef, out var existing))
                {
                    CacheProcessName(win.ProcessId, win.ProcessName);
                    if (existing.NextDue == long.MaxValue && IsNativeInterestingProcess(win.ProcessName))
                        existing.NextDue = now;
                    MaybeWatchForOverlay(win.Hwnd, win.ProcessName, existing.Ref);
                    continue;
                }
                TrackNativeWindow(win, now, rememberIfSkipped: true);
            }
        }

        private void NativeSweep(long now)
        {
            List<BrowserElementRef> gone = null;
            foreach (var pair in _hwndIndex)
            {
                if (!_candidates.TryGetValue(pair.Value, out var state))
                    continue;
                if (!_probe.IsAlive(pair.Value))
                    (gone ??= new List<BrowserElementRef>()).Add(pair.Value);
                else if (state.NextDue == long.MaxValue && !state.Failed && IsNativeInterestingProcess(ProcessNameOf(state.ProcessId)))
                    state.NextDue = now; // idle: look again, its name or message may have changed
            }
            if (gone != null)
            {
                foreach (var key in gone)
                    RemoveCandidate(_candidates[key]);
            }

            foreach (var win in _probe.EnumerateTopLevelWindows())
            {
                if (win == null)
                    continue;
                if (_hwndIndex.ContainsKey(win.Hwnd))
                {
                    CacheProcessName(win.ProcessId, win.ProcessName);
                    MaybeWatchForOverlay(win.Hwnd, win.ProcessName, _hwndIndex[win.Hwnd]);
                }
                else
                {
                    TrackNativeWindow(win, now);
                }
            }
        }

        private void TrackNativeWindow(BrowserWindowInfo win, long now, bool rememberIfSkipped = false)
        {
            // Cheap pre-filter before the expensive DescribeWindow (a UIA FromHandle plus several
            // property reads): a window is only worth describing if a native rule could match its
            // process, or an overlay rule wants to watch it. Anything else (most of the desktop) is
            // never touched. It is not remembered, so a rule added later picks it up on the next sweep.
            bool nativeInteresting = IsNativeInterestingProcess(win.ProcessName);
            if (!nativeInteresting && !IsOverlayInterestingProcess(win.ProcessName))
            {
                if (rememberIfSkipped)
                    RememberSkipped(win);
                return;
            }
            if (_candidates.Count >= MaxTrackedCandidates)
            {
                NoteCandidateCapHit();
                return;
            }
            CacheProcessName(win.ProcessId, win.ProcessName);
            BrowserElementInfo info = _probe.DescribeWindow(win.Hwnd);
            if (info == null)
                return; // gone already

            var state = new CandidateState
            {
                Ref = info.Ref,
                Scope = BrowserPopupScope.NativeDialog,
                Hwnd = win.Hwnd,
                ProcessId = info.ProcessId,
                Info = info,
                FirstSeen = now,
                // A window tracked only so its page can be watched for overlays has nothing to evaluate.
                NextDue = nativeInteresting ? now : long.MaxValue
            };
            AdmitCandidate(state);
            _hwndIndex[win.Hwnd] = state.Ref;
            MaybeWatchForOverlay(win.Hwnd, win.ProcessName, info.Ref);
        }

        private void RememberSkipped(BrowserWindowInfo win)
        {
            if (_skippedWindows.ContainsKey(win.Hwnd))
                return;
            while (_skippedOrder.Count >= MaxSkippedWindows)
                _skippedWindows.Remove(_skippedOrder.Dequeue());
            _skippedWindows[win.Hwnd] = win;
            _skippedOrder.Enqueue(win.Hwnd);
        }

        /// <summary>After a rule change: tracks any remembered window a rule now wants. The rest stay remembered, undescribed.</summary>
        private void RetrySkippedWindows(long now)
        {
            if (_skippedWindows.Count == 0)
                return;
            List<BrowserWindowInfo> wanted = null;
            foreach (var win in _skippedWindows.Values)
            {
                if (IsTrackableProcess(win.ProcessName))
                    (wanted ??= new List<BrowserWindowInfo>()).Add(win);
            }
            if (wanted == null)
                return;
            foreach (var win in wanted)
            {
                _skippedWindows.Remove(win.Hwnd); // stale entries in _skippedOrder are harmless: Remove of a missing key is a no-op
                if (!_hwndIndex.ContainsKey(win.Hwnd))
                    TrackNativeWindow(win, now);
            }
        }

        private void MaybeWatchForOverlay(IntPtr hwnd, string processName, BrowserElementRef windowRef)
        {
            if (_watchedWindows.ContainsKey(hwnd))
                return;
            if (!IsOverlayInterestingProcess(processName))
                return;
            _hookSource.WatchWindow(windowRef);
            _watchedWindows[hwnd] = windowRef;
        }

        // ------------------------------------------------------------------ page-overlay discovery

        private void DrainOverlayDirtyQueue(long now)
        {
            // Drain first, then sweep each distinct window once: N signals for one window
            // (a page mutating rapidly) cost one walk, not N.
            HashSet<BrowserElementRef> dirty = null;
            while (_overlayDirtyQueue.TryDequeue(out BrowserElementRef windowRef))
            {
                Interlocked.Decrement(ref _overlayDirtyQueued);
                if (!_watchedWindows.ContainsValue(windowRef))
                    continue; // stale: unwatched (or never watched) since the signal was queued
                (dirty ??= new HashSet<BrowserElementRef>()).Add(windowRef);
            }
            if (dirty == null)
                return;
            foreach (var windowRef in dirty)
                SweepOneOverlayWindow(windowRef, now);
        }

        private void OverlaySweepAll(long now)
        {
            if (_watchedWindows.Count == 0)
                return;
            var windows = new BrowserElementRef[_watchedWindows.Count];
            _watchedWindows.Values.CopyTo(windows, 0);
            foreach (var windowRef in windows)
                SweepOneOverlayWindow(windowRef, now);
        }

        private void SweepOneOverlayWindow(BrowserElementRef windowRef, long now)
        {
            if (!_sweptThisPass.Add(windowRef))
                return; // already walked during this pass
            if (!_probe.IsAlive(windowRef))
                return; // the native-dialog sweep/liveness check will notice and unwatch it
            if (!_candidates.TryGetValue(windowRef, out CandidateState owner))
                return; // not a tracked window (any more): its identity is unknown, so nothing found under it can be judged

            // Process identity comes from the top-level window that was walked, never from the
            // element: an in-page element's own UIA ProcessId can be a different (renderer) process
            // than the window's, which has no cached name and would fail every process-scoped rule
            // and the own-process check. Every overlay found under this window is stamped with the
            // window's PID, so rule matching, the own-process safety check and the records all judge
            // it by the same identity.
            int ownerProcessId = owner.ProcessId;
            string ownerProcessName = ProcessNameOf(ownerProcessId);

            IReadOnlyList<BrowserElementInfo> found = _probe.FindOverlayCandidates(windowRef, MaxOverlayNodes, MaxOverlayDepth);
            if (found == null)
                return;

            // Snapshot the enabled overlay rules once; an element is only worth tracking if it
            // passes the cheap phase of at least one, so the (mostly irrelevant) rest of a big
            // page never fills the candidate table.
            List<BrowserPopupRule> overlayRules = null;
            foreach (var rule in SnapshotRules())
            {
                if (rule.Enabled && rule.Scope == BrowserPopupScope.PageOverlay)
                    (overlayRules ??= new List<BrowserPopupRule>()).Add(rule);
            }
            if (overlayRules == null)
                return;

            foreach (var element in found)
            {
                if (element == null)
                    continue;
                element.ProcessId = ownerProcessId;
                if (_candidates.TryGetValue(element.Ref, out var existing))
                {
                    existing.Info = element;
                    if (existing.NextDue == long.MaxValue && !existing.Failed)
                        existing.NextDue = now;
                    continue;
                }
                bool relevant = false;
                foreach (var rule in overlayRules)
                {
                    if (rule.MatchesCheap(element, ownerProcessName))
                    {
                        relevant = true;
                        break;
                    }
                }
                if (!relevant)
                    continue;
                if (_candidates.Count >= MaxTrackedCandidates)
                {
                    NoteCandidateCapHit();
                    continue;
                }
                var state = new CandidateState
                {
                    Ref = element.Ref,
                    Scope = BrowserPopupScope.PageOverlay,
                    OwnerWindowRef = windowRef,
                    ProcessId = ownerProcessId,
                    Info = element,
                    FirstSeen = now,
                    NextDue = now
                };
                AdmitCandidate(state);
            }
        }

        /// <summary>
        /// The one place a candidate starts being tracked: adds it and pins its element in the probe
        /// (<see cref="IBrowserPopupProbe.Retain"/>) so its liveness stays definitive however much
        /// the probe's cache churns. Paired with <see cref="DropCandidate"/>, which releases the pin.
        /// </summary>
        private void AdmitCandidate(CandidateState state)
        {
            bool replacing = _candidates.ContainsKey(state.Ref); // already pinned: Retain is idempotent, and Release must stay 1:1
            _candidates[state.Ref] = state;
            if (!replacing)
                _probe.Retain(state.Ref);
        }

        /// <summary>The one place a candidate stops being tracked (every removal path): removes it and releases its pin.</summary>
        private void DropCandidate(BrowserElementRef key)
        {
            if (_candidates.Remove(key))
                _probe.Release(key);
        }

        private void RemoveCandidate(CandidateState state)
        {
            // A candidate that disappears while a dismissal awaits verification is that
            // dismissal's confirmation (every caller removes only what is gone or orphaned).
            ConfirmDismissal(state);
            DropCandidate(state.Ref);
            if (state.Scope != BrowserPopupScope.NativeDialog)
                return;

            _hwndIndex.Remove(state.Hwnd);
            if (_watchedWindows.TryGetValue(state.Hwnd, out var watchedRef))
            {
                _hookSource.UnwatchWindow(watchedRef);
                _watchedWindows.Remove(state.Hwnd);
            }

            List<BrowserElementRef> orphaned = null;
            foreach (var pair in _candidates)
            {
                if (pair.Value.Scope == BrowserPopupScope.PageOverlay && pair.Value.OwnerWindowRef.Equals(state.Ref))
                    (orphaned ??= new List<BrowserElementRef>()).Add(pair.Key);
            }
            if (orphaned != null)
            {
                foreach (var key in orphaned)
                {
                    ConfirmDismissal(_candidates[key]);
                    DropCandidate(key);
                }
            }

            // Bounds _processNameCache's staleness window (see the type remarks' 8th bullet):
            // once the last candidate anywhere that still carries this PID is gone, forget the
            // name we cached for it, so a later process that reuses the PID is never resolved to
            // the exited process's name. Only checked here (a NativeDialog removal), since only
            // the NativeDialog discovery paths ever populate the cache in the first place.
            EvictProcessNameIfUnreferenced(state.ProcessId);
        }

        /// <summary>
        /// Removes <paramref name="processId"/>'s cached name once no tracked candidate - of
        /// either scope - still carries it, so an overlay candidate belonging to the same
        /// still-open browser process keeps resolving its name correctly.
        /// </summary>
        private void EvictProcessNameIfUnreferenced(int processId)
        {
            if (processId == 0)
                return;
            foreach (var candidate in _candidates.Values)
            {
                if (candidate.ProcessId == processId)
                    return;
            }
            _processNameCache.Remove(processId);
        }

        // ------------------------------------------------------------------ deciding

        private void Evaluate(CandidateState state, long now)
        {
            if (!_probe.IsAlive(state.Ref))
            {
                RemoveCandidate(state);
                return;
            }

            if (state.Pending != null)
            {
                VerifyStillOpen(state, now);
                return;
            }

            BrowserElementInfo info = state.Info;
            if (state.Scope == BrowserPopupScope.NativeDialog)
            {
                if (!IsNativeInterestingProcess(ProcessNameOf(state.ProcessId)))
                {
                    // Tracked only so its page can be watched, or its rule was disabled/removed:
                    // nothing to describe. A rule change re-arms it (ApplyRuleChanges).
                    state.Rule = null;
                    state.NextDue = long.MaxValue;
                    return;
                }
                info = _probe.DescribeWindow(state.Hwnd);
                if (info == null)
                {
                    RemoveCandidate(state);
                    return;
                }
                if (state.ProcessId != 0 && state.ProcessId != info.ProcessId)
                    state.Reset(now);
                state.ProcessId = info.ProcessId;
                state.Info = info;
            }

            if (info.ProcessId == _probe.CurrentProcessId)
            {
                state.NextDue = long.MaxValue; // never touch the automation's own popups
                return;
            }
            if (state.Failed)
            {
                state.NextDue = long.MaxValue;
                return;
            }
            if (Paused && state.Rule != null && state.Rule.Action != BrowserPopupAction.WatchOnly)
            {
                // Already matched (so already counted as unresolved) and held for Resume: nothing
                // to re-decide, and no reason to re-read its message text every recheck. A rule
                // change that could alter the match resets state.Rule (ApplyRuleChanges).
                state.NextDue = now + PausedRecheckMs;
                return;
            }

            string processName = ProcessNameOf(info.ProcessId);
            BrowserPopupRule rule = null;
            string text = null;
            foreach (var candidate in SnapshotRules())
            {
                if (!candidate.Enabled || candidate.Scope != state.Scope)
                    continue;
                // Cheap criteria first; message text (a subtree walk) only once they pass, and
                // fetched at most once however many rules want it.
                if (!candidate.MatchesCheap(info, processName))
                    continue;
                if (candidate.NeedsMessage)
                {
                    if (text == null)
                        text = _probe.TryGetMessageText(state.Ref) ?? string.Empty;
                    if (!candidate.MatchesMessage(text))
                        continue;
                }
                rule = candidate;
                break;
            }

            if (rule == null)
            {
                state.Rule = null;
                if (!Retry(state, now))
                    state.NextDue = long.MaxValue;
                return;
            }

            state.Rule = rule;
            if (IsTripped(rule))
            {
                state.NextDue = long.MaxValue;
                return;
            }

            // Rule selection deliberately runs while paused: a popup that opens during a pause is
            // matched, counted as unresolved (HasUnresolvedPopup), and only its ACTION is held
            // back until Resume (see ResumeHeldCandidates). A watch-only match is not an action, so
            // its Detected report is still raised while paused.
            if (Paused && rule.Action != BrowserPopupAction.WatchOnly)
            {
                state.NextDue = now + PausedRecheckMs;
                return;
            }

            text = text ?? _probe.TryGetMessageText(state.Ref) ?? string.Empty;

            if (rule.Action == BrowserPopupAction.WatchOnly)
            {
                if (!state.Reported)
                {
                    state.Reported = true;
                    RecordCore(BrowserPopupRecordKind.Detected, rule.RuleName, state.Scope.ToString(), info.Name,
                        info.LocalizedControlType, text, processName, info.ProcessId, string.Empty, 0, string.Empty);
                }
                state.NextDue = long.MaxValue;
                return;
            }

            Act(state, rule, info, text, processName, now);
        }

        private void Act(CandidateState state, BrowserPopupRule rule, BrowserElementInfo info, string text, string processName, long now)
        {
            // Defense in depth for every acting NativeDialog rule: a name/message substring can match
            // the browser's MAIN window (a tab title containing it). Closing that closes the whole
            // browser, and an invoke rule would walk the window's whole subtree - the PAGE - and press
            // the first page control with the target's name. A window with a minimize/maximize box is a
            // normal application window, not a JS or system dialog, so refuse any action on it before
            // any probe call (WatchOnly never reaches here). Refused once, without spending retries:
            // nothing about it will change on a retry.
            if (state.Scope == BrowserPopupScope.NativeDialog && info.IsMainWindowLike)
            {
                Fail(state, rule, info, text, processName, MainWindowRefusal);
                return;
            }

            if (IsRunaway(rule, now))
            {
                TripRule(rule);
                state.NextDue = long.MaxValue;
                RecordError(rule.RuleName, "Rule '" + rule.RuleName + "' dismissed " + MaxDismissalsPerMinute
                    + " popups in the last minute, so it stopped dismissing. Its popup keeps coming back; "
                    + "call SetRuleEnabled to turn it back on once that is sorted out.");
                return;
            }
            if (state.Attempts >= MaxAttempts)
            {
                Fail(state, rule, info, text, processName, "The popup was still open after " + state.Attempts + " attempts.");
                return;
            }

            var targets = new List<KeyValuePair<BrowserElementRef, string>>();
            string label = "(close)";
            if (rule.Action != BrowserPopupAction.CloseWindowPattern)
            {
                FindTargets(state, rule, targets);
                if (targets.Count == 0)
                {
                    if (!Retry(state, now))
                        Fail(state, rule, info, text, processName,
                            "No element named " + Describe(rule) + " was found inside the popup.");
                    return;
                }
            }

            bool ok;
            string failureReason;
            lock (_actionLock)
            {
                // Pause, SetRuleEnabled(false), RemoveRule and ClearRules wait on this lock, so what
                // was switched off before they returned cannot start here, and what is under way
                // has finished by the time they return. Checked again here because it can have
                // changed since Evaluate chose the rule (the discovery above is slow).
                // rule.Enabled is not volatile: this read sees a writer's change because the writer
                // (SetRuleEnabled) takes and releases _actionLock in WaitForIdle before this acquire.
                if (_actionsStopped || Paused || !rule.Enabled || !IsRegistered(rule))
                {
                    state.NextDue = now + PausedRecheckMs;
                    return;
                }

                state.Attempts++;
                // CloseWindowPattern is only ever paired with NativeDialog (enforced by
                // BrowserPopupRule.ValidateCommon); the defense-in-depth check below still refuses to
                // call TryClose against a PageOverlay candidate if it somehow ends up here regardless.
                if (rule.Action == BrowserPopupAction.CloseWindowPattern)
                {
                    ok = state.Scope == BrowserPopupScope.NativeDialog
                        ? _probe.TryClose(state.Ref, out failureReason)
                        : SetUnreachableFailure(out failureReason);
                }
                else
                {
                    // More than one element can match by name (a text node "Please accept the
                    // terms" ahead of the "Accept" button); try them in order within this one
                    // attempt until an invoke succeeds.
                    ok = false;
                    failureReason = null;
                    foreach (var candidateTarget in targets)
                    {
                        ok = _probe.TryInvoke(candidateTarget.Key, out failureReason);
                        if (ok)
                        {
                            label = candidateTarget.Value;
                            break;
                        }
                    }
                }
            }

            if (ok)
            {
                // The call returned, but the browser may not have acted (or may have ignored it):
                // keep the candidate, still unresolved, and look again after VerifyDelayMs. Only
                // then is it recorded as Dismissed and counted (ConfirmDismissal) or, if still
                // open, treated as a failed attempt (VerifyStillOpen).
                state.Pending = new PendingDismissal
                {
                    Rule = rule,
                    Info = info,
                    Text = text,
                    ProcessName = processName,
                    Label = label,
                    ActedAt = now,
                    Scope = state.Scope.ToString()
                };
                state.NextDue = now + VerifyDelayMs;
                return;
            }

            if (state.Attempts >= MaxAttempts)
                Fail(state, rule, info, text, processName, "The popup was still open after " + state.Attempts
                    + " attempts." + (string.IsNullOrEmpty(failureReason) ? string.Empty : " (" + failureReason + ")"));
            else
                state.NextDue = now + RetryDelayMs;
        }

        /// <summary>
        /// The popup an action was taken on is gone: counts it (per-rule count, total, the runaway
        /// breaker's window) and records <c>Dismissed</c>. No-op unless a dismissal awaits
        /// verification. Counted even if the rule was removed meanwhile (the popup did close), but
        /// only into a rule that is still registered.
        /// </summary>
        private void ConfirmDismissal(CandidateState state)
        {
            PendingDismissal pending = state.Pending;
            if (pending == null)
                return;
            state.Pending = null;

            BrowserPopupRule rule = pending.Rule;
            lock (_lock)
            {
                if (_ruleRuntime.TryGetValue(rule.RuleName, out var runtime))
                    runtime.RecentDismissals.Enqueue(pending.ActedAt);
                if (_rules.Exists(r => string.Equals(r.RuleName, rule.RuleName, StringComparison.OrdinalIgnoreCase)))
                    _counts[rule.RuleName] = (_counts.TryGetValue(rule.RuleName, out int c) ? c : 0) + 1;
                _total++;
            }
            RecordCore(BrowserPopupRecordKind.Dismissed, rule.RuleName, pending.Scope, pending.Info.Name,
                pending.Info.LocalizedControlType, pending.Text, pending.ProcessName, pending.Info.ProcessId,
                pending.Label, state.Attempts, string.Empty);
        }

        /// <summary>
        /// The verification deadline of a successful action arrived and the popup is still open
        /// (the caller already checked liveness): the action did nothing. That is a failed
        /// attempt - never a <c>Dismissed</c> - so it either retries through the normal path
        /// (which re-checks <see cref="Paused"/> and the rule) or, with attempts exhausted, reports
        /// <c>DismissFailed</c>. Read-only: it never acts itself, so it needs no action lock.
        /// </summary>
        private void VerifyStillOpen(CandidateState state, long now)
        {
            PendingDismissal pending = state.Pending;
            if (now < state.NextDue)
                return; // not yet: NextDue is the verification deadline
            state.Pending = null;
            if (!pending.Rule.Enabled)
            {
                // The rule was disabled while this verification was pending (ApplyRuleChanges leaves a
                // pending one alone): the popup is still open, but the rule no longer applies, so give
                // the enabled rules a fresh look instead of parking it as Failed under a disabled rule.
                ReleaseFromRule(state, now);
                return;
            }
            if (state.Attempts >= MaxAttempts)
                Fail(state, pending.Rule, pending.Info, pending.Text, pending.ProcessName,
                    "The action succeeded but the popup is still open after " + state.Attempts + " attempts.");
            else
                state.NextDue = now + RetryDelayMs;
        }

        /// <summary>The <c>DismissFailed</c> detail when an acting (invoke or close) native rule matched a main-window-like window.</summary>
        internal const string MainWindowRefusal = "refused to act on a window that looks like a main application window; "
            + "use a page-overlay rule (scope PageOverlay) or a more specific rule";

        private static bool SetUnreachableFailure(out string failureReason)
        {
            failureReason = "CloseWindowPattern is not valid for a PageOverlay candidate.";
            return false;
        }

        private static string Describe(BrowserPopupRule rule) =>
            rule.Action == BrowserPopupAction.InvokeByAutomationId
                ? "automation ID '" + rule.TargetAutomationId + "'"
                : "'" + rule.TargetElementName + "'";

        /// <summary>The most name matches tried per attempt; a popup rarely has more than a couple.</summary>
        private const int MaxTargetsPerAttempt = 5;

        /// <summary>
        /// Collects the descendants a click/invoke rule may target, in the order they should be
        /// tried, by walking the popup's own subtree via <see cref="IBrowserPopupProbe.FindOverlayCandidates"/>
        /// - the only descendant-search primitive Task 1's probe interface exposes. See the type
        /// remarks. Button-like matches come first (a name substring can also match a text node),
        /// each group in the probe's walk order, capped at <see cref="MaxTargetsPerAttempt"/>.
        /// </summary>
        private void FindTargets(CandidateState state, BrowserPopupRule rule, List<KeyValuePair<BrowserElementRef, string>> targets)
        {
            IReadOnlyList<BrowserElementInfo> descendants = _probe.FindOverlayCandidates(state.Ref, MaxOverlayNodes, MaxOverlayDepth);
            if (descendants == null)
                return;

            var others = new List<KeyValuePair<BrowserElementRef, string>>();
            foreach (var element in descendants)
            {
                if (element == null)
                    continue;
                bool match = rule.Action == BrowserPopupAction.InvokeByAutomationId
                    ? string.Equals(element.AutomationId, rule.TargetAutomationId, StringComparison.OrdinalIgnoreCase)
                    : rule.ExactTargetElementName
                        ? string.Equals(element.Name, rule.TargetElementName, StringComparison.OrdinalIgnoreCase)
                        : BrowserPopupRule.Contains(element.Name, rule.TargetElementName);
                if (!match)
                    continue;
                var entry = new KeyValuePair<BrowserElementRef, string>(element.Ref,
                    string.IsNullOrEmpty(element.Name) ? element.AutomationId : element.Name);
                if (BrowserPopupRule.Contains(element.ControlType, "Button"))
                    targets.Add(entry);
                else
                    others.Add(entry);
                if (targets.Count >= MaxTargetsPerAttempt)
                    break;
            }
            foreach (var entry in others)
            {
                if (targets.Count >= MaxTargetsPerAttempt)
                    break;
                targets.Add(entry);
            }
        }

        private bool IsRunaway(BrowserPopupRule rule, long now)
        {
            lock (_lock)
            {
                if (!_ruleRuntime.TryGetValue(rule.RuleName, out var runtime))
                    return false;
                while (runtime.RecentDismissals.Count > 0 && now - runtime.RecentDismissals.Peek() > RunawayWindowMs)
                    runtime.RecentDismissals.Dequeue();
                return runtime.RecentDismissals.Count >= MaxDismissalsPerMinute;
            }
        }

        private void TripRule(BrowserPopupRule rule)
        {
            lock (_lock)
            {
                if (_ruleRuntime.TryGetValue(rule.RuleName, out var runtime))
                    runtime.Tripped = true;
            }
        }

        private bool IsTripped(BrowserPopupRule rule)
        {
            lock (_lock)
                return _ruleRuntime.TryGetValue(rule.RuleName, out var runtime) && runtime.Tripped;
        }

        private void Fail(CandidateState state, BrowserPopupRule rule, BrowserElementInfo info, string text, string processName, string detail)
        {
            state.Failed = true;
            lock (_lock)
                state.RetryToken = _ruleRuntime.TryGetValue(rule.RuleName, out var runtime) ? runtime.RetryToken : 0;
            state.NextDue = long.MaxValue;
            RecordCore(BrowserPopupRecordKind.DismissFailed, rule.RuleName, state.Scope.ToString(), info.Name,
                info.LocalizedControlType, text, processName, info.ProcessId, string.Empty, state.Attempts, detail);
        }

        /// <summary>Moves to the next time slot for the candidate; false if there is none left.</summary>
        private static bool Retry(CandidateState state, long now)
        {
            if (state.Step + 1 >= ScheduleMs.Length)
                return false;
            state.Step++;
            state.NextDue = Math.Max(state.FirstSeen + ScheduleMs[state.Step], now + 1);
            return true;
        }

        // ------------------------------------------------------------------ recording

        private void RecordCore(BrowserPopupRecordKind kind, string ruleName, string scope, string name, string role,
            string message, string processName, int processId, string targetInvoked, int attempts, string detail)
        {
            var record = new BrowserPopupRecord
            {
                Kind = kind,
                RuleName = ruleName ?? string.Empty,
                Scope = scope ?? string.Empty,
                Name = name ?? string.Empty,
                Role = role ?? string.Empty,
                MessageText = message ?? string.Empty,
                ProcessName = processName ?? string.Empty,
                ProcessId = processId,
                TargetInvoked = targetInvoked ?? string.Empty,
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
            if (_pumpThreadId == Environment.CurrentManagedThreadId)
                _deferred.Add(record); // delivered by Pump's finally, once the state is consistent
            else
                Deliver(record);
        }

        private void FlushDeferred()
        {
            if (_deferred.Count == 0)
                return;
            var batch = _deferred.ToArray();
            _deferred.Clear();
            foreach (var record in batch)
                Deliver(record);
        }

        internal void RecordError(string ruleName, string message) =>
            RecordCore(BrowserPopupRecordKind.Error, ruleName, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, 0, string.Empty, 0, message);

        private void Deliver(BrowserPopupRecord record)
        {
            try
            {
                _sink?.Invoke(record);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // The consumer of the log must never be able to stop the worker.
                Debug.WriteLine("BrowserInterruptUtils: record sink failed: " + ex.Message);
            }
        }
    }
}
