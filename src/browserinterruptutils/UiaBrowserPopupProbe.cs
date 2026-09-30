using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// The real probe: backs <see cref="IBrowserPopupProbe"/> with actual Win32 top-level window
    /// enumeration and actual <c>System.Windows.Automation</c> element interaction. Mirrors
    /// <c>InterruptAutomation.Win32PopupProbe</c> for the native-window pieces (enumeration,
    /// class-name/process lookup) and <c>UIAutomationUtils</c>'s safe-UIA-access idioms for
    /// everything that touches an <see cref="AutomationElement"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <b>Never-throws.</b> Every public member on this class follows this repo's never-throws
    /// discipline: <see cref="ElementNotAvailableException"/> (the defining failure mode of a UIA
    /// element whose underlying UI has died), <see cref="InvalidOperationException"/> (and its
    /// subclasses, e.g. <see cref="ElementNotEnabledException"/>), and any other recoverable
    /// exception (including a COM exception surfaced from the UIA provider) are all caught and
    /// translated into a <c>false</c>/<c>null</c>/empty result, per <see cref="NeverThrowsGuard"/> -
    /// exactly as <c>UIAutomationUtils.cs</c>'s <c>TryUia</c>/<c>TryUiaAction</c> do. No UIA/COM
    /// exception is allowed to escape into <c>BrowserPopupEngine</c>.
    /// </item>
    /// <item>
    /// <b>Resolution/caching layer.</b> <see cref="BrowserElementRef"/> is deliberately UIA-free
    /// (a runtime-id array plus an optional HWND, no live <see cref="AutomationElement"/>), so
    /// every call that receives one must resolve it back into a real element. <see cref="_cache"/>
    /// (a <see cref="GenerationalCache{TKey,TValue}"/> of strong references) is
    /// populated every time this probe discovers or describes an element (in
    /// <see cref="DescribeElement"/>, the single place every element turns into a
    /// <see cref="BrowserElementInfo"/>) so that a later <see cref="TryInvoke"/>/
    /// <see cref="TryClose"/>/<see cref="IsAlive"/>/<see cref="TryGetMessageText"/> call against
    /// that same <see cref="BrowserElementRef"/> can skip re-searching the tree. A cache hit is
    /// always re-validated with <see cref="IsElementAvailable"/> before being trusted (a UIA
    /// wrapper can outlive the underlying UI). On a cache miss, <see cref="TryResolve"/> falls
    /// back to <see cref="AutomationElement.FromHandle"/> against the ref's <c>Hwnd</c> (only
    /// ever set for a native dialog's own top-level window) plus a bounded runtime-id-matching
    /// subtree search if the ref names a descendant rather than the window itself; a ref with no
    /// <c>Hwnd</c> (a page-overlay element, which has none) that misses the cache is simply
    /// unreachable - there is no other way to relocate it - and <see cref="IsAlive"/> reports it as
    /// UNKNOWN (<c>true</c>), never as closed. To keep the elements the engine still cares about
    /// out of that state, the engine <see cref="Retain">pins</see> every candidate it tracks: a
    /// pinned element sits in a separate dictionary of strong references that cache rotation never
    /// evicts (until <see cref="Release"/>), so <see cref="IsAlive"/> is definitive for it even after
    /// a pass that walked more distinct elements than the cache holds. The cache
    /// holds STRONG references: the wrappers a subtree walk discovers are otherwise unreferenced,
    /// so weak ones could be collected by a GC between discovery and the engine's later
    /// re-check (0.15-2 s later), which would make a live overlay look dead. Growth is bounded
    /// by generation instead: the cache rotates (drops entries nobody looked up or re-discovered
    /// for two generations) when the current generation reaches <see cref="CacheMaxEntriesPerGeneration"/>
    /// entries or is <see cref="CacheRotateAfterMs"/> old, so it holds at most twice the cap, and a
    /// hit is promoted so an element still in use survives. A hit that turns out dead is removed
    /// at once. Rotation is not tied to <c>FindOverlayCandidates</c> calls, because one sweep
    /// makes several (one per watched window, plus one per popup for target lookup) and rotating
    /// per call would evict a popup found early in the sweep before the engine re-checks it.
    /// A native window that falls out is re-resolved from its <c>Hwnd</c>; an overlay that does
    /// is re-found by the next walk (walks re-cache every element they visit).
    /// </item>
    /// <item>
    /// <b>Tree walker choice.</b> <see cref="_childWalker"/> is built from
    /// <see cref="Condition.TrueCondition"/> - the same condition <c>UIAutomationUtils.cs</c>'s own
    /// <c>ChildrenWalker</c> uses, and deliberately not the pre-built
    /// <see cref="TreeWalker.RawViewWalker"/> (whose condition can surface implementation/
    /// non-control noise) nor <see cref="TreeWalker.ControlViewWalker"/> (which risks silently
    /// omitting a browser-hosted ARIA element that Chrome's UIA bridge does not map to a
    /// recognized <see cref="ControlType"/> - exactly the dialog-shaped overlays this probe must
    /// never miss). Matching the sibling utility's own precedent here is a deliberate choice, not
    /// an oversight.
    /// </item>
    /// <item>
    /// <b>Message-text convention.</b> <see cref="TryGetMessageText"/> matches
    /// <see cref="IBrowserPopupProbe.TryGetMessageText"/>'s own doc comment exactly: a bounded,
    /// breadth-first search for the *first* non-empty <see cref="ControlType.Text"/> descendant's
    /// <c>Name</c>, or <c>null</c> if none is found within the (small) bound.
    /// </item>
    /// <item>
    /// <b>Cached walks.</b> Walks (<see cref="FindOverlayCandidates"/>, <see cref="TryGetMessageText"/>,
    /// the runtime-id search) fetch each child through a <see cref="CacheRequest"/> (properties of the
    /// element itself, <see cref="AutomationElementMode.Full"/>) so it arrives with its properties in
    /// one round trip; <see cref="DescribeElement"/> reads <c>Cached.*</c> and falls back once to the
    /// live read if a property was not cached. Liveness (<see cref="IsAlive"/>, <see cref="Resolve"/>)
    /// and patterns always use live reads: a cached snapshot is stale by definition.
    /// </item>
    /// <item>
    /// <b><c>EnumerateTopLevelWindows</c> skips invisible windows.</b> A native JS
    /// <c>alert</c>/<c>confirm</c>/<c>prompt</c> dialog is visible for as long as it exists, while a
    /// browser process owns many hidden top-level windows, so <c>NativeMethods.IsWindowVisible</c>
    /// is checked inside the enumeration itself (no interface member is involved). Windows that
    /// arrive through the hook are not filtered this way.
    /// </item>
    /// </list>
    /// </remarks>
    internal sealed class UiaBrowserPopupProbe : IBrowserPopupProbe
    {
        /// <summary>
        /// A <see cref="TreeWalker"/> built from <see cref="Condition.TrueCondition"/> - see the
        /// type remarks for why this, rather than <see cref="TreeWalker.RawViewWalker"/> or
        /// <see cref="TreeWalker.ControlViewWalker"/>, was chosen.
        /// </summary>
        private static readonly TreeWalker _childWalker = new TreeWalker(Condition.TrueCondition);

        // UIA property caching (CacheRequest). A walk asks UIA to deliver each child WITH the
        // properties DescribeElement needs in the same cross-process round trip that returns the
        // child, instead of one round trip per property read. The request is built per walk by
        // CreateCacheRequest (never shared, never Activate()d for walks: the walker overloads take
        // it as an argument), so there is no cross-thread sharing of a mutable CacheRequest to
        // reason about. TreeScope.Element (only the element itself) and AutomationElementMode.Full
        // (the default, set explicitly): elements keep LIVE references so TryInvoke, pinning and
        // liveness checks still work; AutomationElementMode.None would break all three.
        private static readonly AutomationProperty[] DescribeProperties =
        {
            AutomationElement.RuntimeIdProperty,
            AutomationElement.NameProperty,
            AutomationElement.AutomationIdProperty,
            AutomationElement.ClassNameProperty,
            AutomationElement.ControlTypeProperty,
            AutomationElement.LocalizedControlTypeProperty,
            AutomationElement.ProcessIdProperty
        };

        private static readonly AutomationProperty[] MessageTextProperties =
        {
            AutomationElement.ControlTypeProperty,
            AutomationElement.NameProperty
        };

        private static readonly AutomationProperty[] RuntimeIdOnlyProperties =
        {
            AutomationElement.RuntimeIdProperty
        };

        private int _cachedReadFallbacks;

        /// <summary>
        /// Test hook: how many times a <c>Cached</c> read threw <see cref="InvalidOperationException"/>
        /// (property not cached) and fell back to the live <c>Current</c> read. Zero after a walk
        /// proves the cached path was really used.
        /// </summary>
        internal int CachedReadFallbackCount => Volatile.Read(ref _cachedReadFallbacks);

        /// <summary>Most elements one cache generation holds before it rotates (a default 5000-node walk fits in one).</summary>
        internal const int CacheMaxEntriesPerGeneration = 8192;

        /// <summary>How long a cache generation lives before it rotates, in milliseconds.</summary>
        internal const int CacheRotateAfterMs = 60000;

        /// <summary>Holds every element this probe has discovered/described. See the type remarks.</summary>
        private readonly GenerationalCache<BrowserElementRef, AutomationElement> _cache =
            new GenerationalCache<BrowserElementRef, AutomationElement>(CacheMaxEntriesPerGeneration, CacheRotateAfterMs);

        /// <summary>
        /// Elements the engine has <see cref="Retain">pinned</see> (strong references, keyed like the
        /// cache) that <see cref="GenerationalCache{TKey,TValue}"/> rotation never evicts. Bounded by
        /// the engine's tracked-candidate cap; entries live until <see cref="Release"/>.
        /// </summary>
        private readonly Dictionary<BrowserElementRef, AutomationElement> _pinned = new Dictionary<BrowserElementRef, AutomationElement>();
        private readonly object _pinLock = new object();

        /// <summary>Bounds the fallback runtime-id search used to relocate a cache-missed descendant of a still-resolvable window.</summary>
        private const int ResolveFallbackMaxNodes = 5000;
        private const int ResolveFallbackMaxDepth = 50;

        /// <summary>Bounds <see cref="TryGetMessageText"/>'s descendant search - small, since a message is expected near the popup's surface.</summary>
        private const int MessageTextMaxNodes = 200;
        private const int MessageTextMaxDepth = 10;

        public int CurrentProcessId { get; } = Environment.ProcessId;

        // ------------------------------------------------------------------ native windows

        public IReadOnlyList<BrowserWindowInfo> EnumerateTopLevelWindows()
        {
            var windows = new List<BrowserWindowInfo>();
            try
            {
                NativeMethods.EnumWindows((hwnd, _) =>
                {
                    try
                    {
                        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                            return true;
                        // A JS dialog is visible for its whole lifetime, so hidden windows (of which
                        // a browser process has many) are skipped before anything else is read.
                        if (!NativeMethods.IsWindowVisible(hwnd))
                            return true;
                        // Only top-level windows are candidates; controls inside them are not
                        // (same check PopupHookThread/Win32PopupProbe's sibling component uses).
                        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
                            return true;

                        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                        windows.Add(new BrowserWindowInfo
                        {
                            Hwnd = hwnd,
                            ClassName = ClassNameOf(hwnd),
                            ProcessId = (int)pid,
                            ProcessName = ProcessNames.NameOf((int)pid)
                        });
                    }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                    {
                        // One bad window must not abort the whole enumeration.
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Never throws; a partial/empty list is the safe degraded result.
            }
            return windows;
        }

        public BrowserElementInfo DescribeWindow(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                    return null;

                AutomationElement element = ResolveFromHandle(hwnd, out _, DescribeProperties);
                return element == null ? null : DescribeElement(element, hwnd);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ subtree walk

        public IReadOnlyList<BrowserElementInfo> FindOverlayCandidates(BrowserElementRef browserWindowRoot, int maxNodes, int maxDepth)
        {
            var results = new List<BrowserElementInfo>();
            try
            {
                if (maxNodes <= 0 || maxDepth <= 0)
                    return results;
                if (!TryResolve(browserWindowRoot, out AutomationElement root) || root == null)
                    return results;

                CacheRequest request = CreateCacheRequest(DescribeProperties);
                var queue = new Queue<(AutomationElement Element, int Depth)>();
                EnqueueChildrenUpToBudget(root, 1, 0, maxNodes, queue, request);

                // `visited` is a true visited-node bound: it increments once per dequeue,
                // unconditionally, whether or not DescribeElement below succeeds. Gating the
                // bound on results.Count (successfully-described elements) instead would let a
                // run of dead/unavailable elements (ElementNotAvailableException mid-walk - the
                // exact churn this bound exists to guard against) buy extra real tree-walk work
                // beyond maxNodes, since a dequeue that yields no result wouldn't count against
                // it. See TryGetMessageText/FindByRuntimeId in this file for the same pattern.
                int visited = 0;
                while (queue.Count > 0 && visited < maxNodes)
                {
                    (AutomationElement element, int depth) = queue.Dequeue();
                    visited++;

                    BrowserElementInfo info = DescribeElement(element, IntPtr.Zero);
                    if (info == null)
                        continue; // died mid-walk: best-effort, skip it and keep going (still counts toward `visited`)

                    results.Add(info);
                    if (depth < maxDepth)
                        EnqueueChildrenUpToBudget(element, depth + 1, visited, maxNodes, queue, request);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Never throws; whatever was gathered before the failure is returned.
            }
            return results;
        }

        /// <summary>
        /// Enqueues <paramref name="parent"/>'s children lazily (<see cref="_childWalker"/>'s
        /// first-child/next-sibling), stopping the moment <paramref name="alreadyVisited"/> plus
        /// the queue's length reaches <paramref name="maxNodes"/> - the walk can never dequeue
        /// more than that, so an enormous sibling list (a huge page) is never enumerated past the
        /// budget, and never materialized as a list. Each child is fetched through the
        /// <paramref name="request"/> overloads, so it arrives with its cached properties in the
        /// same round trip. Best-effort: an enumeration failure keeps whatever was enqueued so far
        /// and never throws.
        /// </summary>
        private static void EnqueueChildrenUpToBudget(AutomationElement parent, int depth, int alreadyVisited, int maxNodes,
            Queue<(AutomationElement Element, int Depth)> queue, CacheRequest request)
        {
            try
            {
                if (alreadyVisited + queue.Count >= maxNodes)
                    return;
                AutomationElement child = _childWalker.GetFirstChild(parent, request);
                while (child != null)
                {
                    queue.Enqueue((child, depth));
                    if (alreadyVisited + queue.Count >= maxNodes)
                        return;
                    child = _childWalker.GetNextSibling(child, request);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Best-effort: whatever was gathered before the failure is used.
            }
        }

        // ------------------------------------------------------------------ liveness / message text

        public bool IsAlive(BrowserElementRef element)
        {
            try
            {
                // Tri-state: only a definitive "gone" reports false. A window-less reference that is
                // neither pinned nor cached (evicted by cache rotation) is unknown, not dead: the
                // engine bounds it by MaxAttempts, its owner window's death and the periodic reap
                // (which only trusts refs it pinned), instead of dropping a possibly live popup.
                Liveness liveness = Resolve(element, out AutomationElement resolved);
                if (liveness == Liveness.Alive)
                    liveness = LivenessClassifier.ClassifyRead(ReadElement(resolved));
                // Only a definitive Dead reports false; Unknown (transient read/provider failure,
                // or nothing to resolve from) reports alive. The engine treats "alive at the
                // verification deadline" as a failed attempt bounded by MaxAttempts, so an
                // unknown answer is retried rather than recorded as a dismissal.
                return LivenessClassifier.ReportsAlive(liveness);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return true; // a failure to even ask is not evidence the popup closed
            }
        }

        public string TryGetMessageText(BrowserElementRef element)
        {
            try
            {
                if (!TryResolve(element, out AutomationElement root) || root == null)
                    return null;

                CacheRequest request = CreateCacheRequest(MessageTextProperties);
                var queue = new Queue<(AutomationElement Element, int Depth)>();
                EnqueueChildrenUpToBudget(root, 1, 0, MessageTextMaxNodes, queue, request);

                int visited = 0;
                while (queue.Count > 0 && visited < MessageTextMaxNodes)
                {
                    (AutomationElement candidate, int depth) = queue.Dequeue();
                    visited++;

                    ControlType controlType = ReadCachedOrLive(true, () => candidate.Cached.ControlType, () => candidate.Current.ControlType, null);
                    if (Equals(controlType, ControlType.Text))
                    {
                        string name = ReadCachedOrLive(true, () => candidate.Cached.Name, () => candidate.Current.Name, string.Empty);
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }

                    if (depth < MessageTextMaxDepth)
                    {
                        EnqueueChildrenUpToBudget(candidate, depth + 1, visited, MessageTextMaxNodes, queue, request);
                    }
                }
                return null;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ actions

        public bool TryInvoke(BrowserElementRef target, out string failureReason)
        {
            failureReason = null;
            try
            {
                if (!ResolveForAction(target, out AutomationElement element, out failureReason))
                    return false;

                if (TryGetPattern(element, InvokePattern.Pattern, out object invokeObj) && invokeObj is InvokePattern invoke)
                    return TryUiaAction("Invoke", () => invoke.Invoke(), out failureReason);

                // Some overlay dismiss controls only expose Toggle, not Invoke.
                if (TryGetPattern(element, TogglePattern.Pattern, out object toggleObj) && toggleObj is TogglePattern toggle)
                    return TryUiaAction("Toggle", () => toggle.Toggle(), out failureReason);

                failureReason = "This element supports neither InvokePattern nor TogglePattern.";
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                failureReason = NeverThrowsGuard.Failure("TryInvoke", ex);
                return false;
            }
        }

        public bool TryClose(BrowserElementRef target, out string failureReason)
        {
            failureReason = null;
            try
            {
                if (!ResolveForAction(target, out AutomationElement element, out failureReason))
                    return false;

                if (!TryGetPattern(element, WindowPattern.Pattern, out object windowObj) || !(windowObj is WindowPattern windowPattern))
                {
                    failureReason = "This element does not support WindowPattern (it is not a native dialog window).";
                    return false;
                }

                return TryUiaAction("Close", () => windowPattern.Close(), out failureReason);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                failureReason = NeverThrowsGuard.Failure("TryClose", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------ resolution / caching

        /// <summary>Records <paramref name="element"/> against <paramref name="elementRef"/> for later resolution. See the type remarks.</summary>
        private void CacheElement(BrowserElementRef elementRef, AutomationElement element)
        {
            if (element == null)
                return;
            _cache.Set(elementRef, element);
        }

        /// <summary>
        /// Resolves a <see cref="BrowserElementRef"/> back into a live <see cref="AutomationElement"/>:
        /// the cache first, then (only when <see cref="BrowserElementRef.Hwnd"/> is set) a fresh
        /// <see cref="AutomationElement.FromHandle"/> plus a bounded runtime-id-matching subtree
        /// search if the ref names a descendant rather than the window itself. A ref with no
        /// <c>Hwnd</c> that misses the cache cannot be relocated (see the type remarks) and this
        /// returns <c>false</c>.
        /// </summary>
        private bool TryResolve(BrowserElementRef target, out AutomationElement element) =>
            Resolve(target, out element) == Liveness.Alive;

        /// <summary>
        /// <see cref="TryResolve"/> for actions: on failure says why. An <see cref="Liveness.Unknown"/>
        /// resolution (a transient provider failure) is reported as "could not be verified", never as
        /// "already closed", so the caller does not conclude the popup is gone.
        /// </summary>
        private bool ResolveForAction(BrowserElementRef target, out AutomationElement element, out string failureReason)
        {
            Liveness liveness = Resolve(target, out element);
            if (liveness == Liveness.Alive && element != null)
            {
                failureReason = null;
                return true;
            }
            element = null;
            failureReason = liveness == Liveness.Unknown
                ? "The element's state could not be determined right now (a transient UI Automation failure or an unresolvable reference); it may still be open."
                : "The element could not be found (it may have already closed).";
            return false;
        }

        /// <summary>
        /// The tri-state core of <see cref="TryResolve"/>/<see cref="IsAlive"/>. A pinned element (see
        /// <see cref="Retain"/>) is consulted before the cache and is never evicted by rotation, so for
        /// a pinned reference a destroyed window or a confirmed-unavailable element is definitive.
        /// A window-less reference that is neither pinned nor cached, and any non-definitive read or
        /// provider failure (see <see cref="LivenessClassifier"/>), is <see cref="Liveness.Unknown"/>
        /// and never evicts a cache entry.
        /// </summary>
        private Liveness Resolve(BrowserElementRef target, out AutomationElement element)
        {
            element = null;

            // A ref with a window handle is only as alive as that window. IsWindow is a cheap,
            // deterministic answer; the UIA "Current" read in IsElementAvailable is not (a
            // just-destroyed window's element can keep answering from a stale provider while
            // teardown is still in progress), so it must never be the only liveness test. A dead
            // handle also evicts the cache entry so a strong element reference is not kept for it.
            if (LivenessClassifier.ClassifyWindow(target.Hwnd != IntPtr.Zero, target.Hwnd == IntPtr.Zero || NativeMethods.IsWindow(target.Hwnd)) == Liveness.Dead)
            {
                _cache.Remove(target);
                return Liveness.Dead;
            }

            bool pinnedButDead = false;
            AutomationElement pinned;
            lock (_pinLock)
                _pinned.TryGetValue(target, out pinned);
            if (pinned != null)
            {
                ReadOutcome pinnedRead = ReadElement(pinned);
                if (pinnedRead == ReadOutcome.Ok)
                {
                    element = pinned;
                    return Liveness.Alive;
                }
                if (pinnedRead == ReadOutcome.OtherFailure)
                    return Liveness.Unknown; // non-definitive: keep the pin, do not fall through to a re-resolve
                // The pin stays until Release (the engine owns its lifetime) so this stays a
                // definitive "dead" on every later call instead of decaying into "unknown".
                pinnedButDead = true;
            }

            if (_cache.TryGet(target, out AutomationElement cached))
            {
                ReadOutcome cachedRead = ReadElement(cached);
                if (cachedRead == ReadOutcome.Ok)
                {
                    element = cached;
                    return Liveness.Alive;
                }
                if (!LivenessClassifier.ShouldEvictCacheEntry(cachedRead))
                    return Liveness.Unknown; // non-definitive failure: keep the entry, do not claim it is gone
                _cache.Remove(target); // confirmed unavailable: drop it now rather than wait for rotation
                if (target.Hwnd == IntPtr.Zero)
                    return Liveness.Dead;
            }

            if (target.Hwnd == IntPtr.Zero)
                return pinnedButDead ? Liveness.Dead : Liveness.Unknown; // no cache entry, no window to re-resolve from

            AutomationElement windowElement = ResolveFromHandle(target.Hwnd, out ReadOutcome handleRead);
            if (windowElement == null)
                return LivenessClassifier.ClassifyRead(handleRead) == Liveness.Dead ? Liveness.Dead : Liveness.Unknown;

            int[] runtimeId = target.RuntimeId;
            int[] windowRuntimeId = runtimeId == null || runtimeId.Length == 0 ? null : SafeGet(() => windowElement.GetRuntimeId(), null);
            if (runtimeId != null && runtimeId.Length != 0 && windowRuntimeId == null)
                return Liveness.Unknown; // could not read the window's identity: not evidence the element is gone
            if (runtimeId == null || runtimeId.Length == 0
                || RuntimeIdEquals(windowRuntimeId, runtimeId))
            {
                CacheElement(target, windowElement);
                element = windowElement;
                return Liveness.Alive;
            }

            AutomationElement found = FindByRuntimeId(windowElement, runtimeId);
            if (found == null)
                return Liveness.Dead;

            CacheElement(target, found);
            element = found;
            return Liveness.Alive;
        }

        // ------------------------------------------------------------------ pinning

        /// <inheritdoc/>
        public void Retain(BrowserElementRef element)
        {
            try
            {
                lock (_pinLock)
                {
                    if (_pinned.ContainsKey(element))
                        return;
                }
                if (Resolve(element, out AutomationElement resolved) != Liveness.Alive || resolved == null)
                    return; // cannot be resolved now: nothing to pin, IsAlive keeps reporting unknown
                lock (_pinLock)
                    _pinned[element] = resolved;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Never throws; an unpinned element is merely bounded by the engine's other limits.
            }
        }

        /// <inheritdoc/>
        public void Release(BrowserElementRef element)
        {
            try
            {
                lock (_pinLock)
                    _pinned.Remove(element);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
            }
        }

        /// <inheritdoc/>
        public void ClearCache()
        {
            try
            {
                _cache.Clear();
                lock (_pinLock)
                    _pinned.Clear();
                ProcessNames.Clear();
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Never throws; whatever is left is released with the probe or at the next reset.
            }
        }

        private AutomationElement FindByRuntimeId(AutomationElement root, int[] runtimeId)
        {
            CacheRequest request = CreateCacheRequest(RuntimeIdOnlyProperties);
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            EnqueueChildrenUpToBudget(root, 1, 0, ResolveFallbackMaxNodes, queue, request);

            int visited = 0;
            while (queue.Count > 0 && visited < ResolveFallbackMaxNodes)
            {
                (AutomationElement element, int depth) = queue.Dequeue();
                visited++;

                if (RuntimeIdEquals(ReadCachedOrLive(true, () => ReadCachedRuntimeId(element), () => element.GetRuntimeId(), null), runtimeId))
                    return element;

                if (depth < ResolveFallbackMaxDepth)
                {
                    EnqueueChildrenUpToBudget(element, depth + 1, visited, ResolveFallbackMaxNodes, queue, request);
                }
            }
            return null;
        }

        private static bool RuntimeIdEquals(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Turns a live element into a <see cref="BrowserElementInfo"/>, caching it as it goes.
        /// Every property is read independently via <see cref="SafeGet{T}"/> so one stale/
        /// unsupported property does not prevent describing the rest (matches
        /// <c>UIAutomationUtils.BuildSubtreeSummaryBreadthFirst</c>'s best-effort convention).
        /// </summary>
        /// <param name="element">The element to describe.</param>
        /// <param name="hwnd">The owning top-level window, or <see cref="IntPtr.Zero"/> for a descendant that is not itself a window.</param>
        private BrowserElementInfo DescribeElement(AutomationElement element, IntPtr hwnd)
        {
            if (element == null)
                return null;

            // Identity first, then the rest: an element with no runtime ID and no window handle
            // cannot be told apart from any other such element (they would all share one key,
            // aliasing one candidate and one cache slot), so it is not describable at all. An
            // element that is a window (non-zero hwnd) may lack a runtime ID and is keyed by its
            // handle, which is unique; see BrowserElementRef.TryCreate.
            //
            // The RuntimeId read doubles as the "was this element fetched through a CacheRequest?"
            // probe. If it was, the fetch that produced the element already proved it reachable, so
            // no separate live availability read is made (that is the per-node `Current.IsEnabled`
            // round trip this replaces). If it was not (element from FromHandle without an active
            // request, or from the cache/pins), fall back ONCE to the old behaviour: a live
            // availability probe plus live reads. Definitive liveness (IsAlive/Resolve) never goes
            // through here: it stays on live `Current` reads, because a cached snapshot is stale.
            bool cached = TryReadCachedRuntimeId(element, out int[] runtimeId);
            if (!cached)
            {
                if (!IsElementAvailable(element))
                    return null;
                runtimeId = SafeGet(() => element.GetRuntimeId(), null);
            }
            else if (runtimeId == null)
            {
                runtimeId = SafeGet(() => element.GetRuntimeId(), null); // cached value absent/unsupported
            }
            if (!BrowserElementRef.TryCreate(runtimeId, hwnd, out BrowserElementRef elementRef))
                return null;

            string name = ReadCachedOrLive(cached, () => element.Cached.Name, () => element.Current.Name, string.Empty) ?? string.Empty;
            string automationId = ReadCachedOrLive(cached, () => element.Cached.AutomationId, () => element.Current.AutomationId, string.Empty) ?? string.Empty;
            string className = ReadCachedOrLive(cached, () => element.Cached.ClassName, () => element.Current.ClassName, string.Empty) ?? string.Empty;
            string controlType = FriendlyControlTypeName(ReadCachedOrLive(cached, () => element.Cached.ControlType, () => element.Current.ControlType, null));
            string localizedControlType = ReadCachedOrLive(cached, () => element.Cached.LocalizedControlType, () => element.Current.LocalizedControlType, string.Empty) ?? string.Empty;
            int processId = ReadCachedOrLive(cached, () => element.Cached.ProcessId, () => element.Current.ProcessId, 0);

            CacheElement(elementRef, element);

            // Only a native window (non-zero hwnd) has a style to read. Read on every describe, so the
            // engine's per-evaluation DescribeWindow always acts on the current style.
            bool isMainWindowLike = hwnd != IntPtr.Zero && NativeMethods.IsMainWindowStyle(NativeMethods.GetWindowStyle(hwnd));

            return new BrowserElementInfo
            {
                Name = name,
                AutomationId = automationId,
                ClassName = className,
                ControlType = controlType,
                LocalizedControlType = localizedControlType,
                ProcessId = processId,
                IsMainWindowLike = isMainWindowLike,
                Ref = elementRef
            };
        }

        // ------------------------------------------------------------------ cache request helpers

        /// <summary>
        /// Builds a fresh, fully configured, never-activated <see cref="CacheRequest"/> caching
        /// <paramref name="properties"/> for the element itself (<see cref="TreeScope.Element"/>) with
        /// <see cref="AutomationElementMode.Full"/> (live references kept). Built per walk and passed
        /// to the walker overloads, so nothing mutable is shared between threads; configuration is
        /// complete before first use (a CacheRequest cannot be modified once active). The
        /// <see cref="CacheRequest.TreeFilter"/> defaults to the control view; it is set to
        /// <see cref="Condition.TrueCondition"/> so it can never filter a raw-view child (it is not
        /// consulted for TreeScope.Element or walker navigation; this is belt and braces).
        /// </summary>
        private static CacheRequest CreateCacheRequest(AutomationProperty[] properties)
        {
            var request = new CacheRequest
            {
                AutomationElementMode = AutomationElementMode.Full,
                TreeScope = TreeScope.Element,
                TreeFilter = Condition.TrueCondition
            };
            foreach (AutomationProperty property in properties)
                request.Add(property);
            return request;
        }

        private static int[] ReadCachedRuntimeId(AutomationElement element) =>
            element.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty) as int[];

        /// <summary>
        /// Reads the cached RuntimeId. Returns false when the element was not fetched through a
        /// request that cached it (<see cref="InvalidOperationException"/>, counted as a fallback) or
        /// when the element is unavailable; the caller then uses the live path.
        /// </summary>
        private bool TryReadCachedRuntimeId(AutomationElement element, out int[] runtimeId)
        {
            runtimeId = null;
            try
            {
                runtimeId = ReadCachedRuntimeId(element);
                return true;
            }
            catch (ElementNotAvailableException)
            {
                return false; // the live availability probe decides what this means
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref _cachedReadFallbacks);
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return false;
            }
        }

        /// <summary>
        /// Reads a property from the element's cache when <paramref name="tryCached"/>; a
        /// <see cref="InvalidOperationException"/> (property not cached) falls back ONCE to the live
        /// read and is counted. <see cref="ElementNotAvailableException"/> and other failures give
        /// <paramref name="fallback"/> (same best-effort as <see cref="SafeGet{T}"/>).
        /// </summary>
        private T ReadCachedOrLive<T>(bool tryCached, Func<T> cachedRead, Func<T> liveRead, T fallback)
        {
            if (tryCached)
            {
                try
                {
                    return cachedRead();
                }
                catch (ElementNotAvailableException)
                {
                    return fallback;
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref _cachedReadFallbacks);
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    return fallback;
                }
            }
            return SafeGet(liveRead, fallback);
        }

        // ------------------------------------------------------------------ safe UIA helpers

        private static AutomationElement ResolveFromHandle(IntPtr hwnd, out ReadOutcome outcome, AutomationProperty[] cacheProperties = null)
        {
            try
            {
                AutomationElement element;
                if (cacheProperties == null)
                {
                    element = AutomationElement.FromHandle(hwnd);
                }
                else
                {
                    // FromHandle has no CacheRequest overload: it uses the calling thread's active
                    // request (Activate is per-thread, so this never affects another thread). The
                    // request is thread-local to this call and disposed (popped) before returning.
                    using (CreateCacheRequest(cacheProperties).Activate())
                        element = AutomationElement.FromHandle(hwnd);
                }
                outcome = ReadOutcome.Ok;
                return element;
            }
            catch (ElementNotAvailableException)
            {
                outcome = ReadOutcome.Unavailable;
                return null;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                outcome = ReadOutcome.OtherFailure;
                return null;
            }
        }

        /// <summary>Mirrors <c>UIAutomationUtils.IsElementAvailable</c>: a cheap <c>Current</c> read is enough to detect a dead element.</summary>
        private static bool IsElementAvailable(AutomationElement element) =>
            ReadElement(element) == ReadOutcome.Ok;

        /// <summary>
        /// One cheap <c>Current</c> read, classified: only <see cref="ElementNotAvailableException"/> is
        /// <see cref="ReadOutcome.Unavailable"/> (definitive); any other failure is
        /// <see cref="ReadOutcome.OtherFailure"/> (non-definitive). A null element is unavailable.
        /// </summary>
        private static ReadOutcome ReadElement(AutomationElement element)
        {
            if (element == null)
                return ReadOutcome.Unavailable;
            try
            {
                _ = element.Current.IsEnabled;
                return ReadOutcome.Ok;
            }
            catch (ElementNotAvailableException)
            {
                return ReadOutcome.Unavailable;
            }
            catch (Exception)
            {
                return ReadOutcome.OtherFailure;
            }
        }

        private static bool TryGetPattern(AutomationElement element, AutomationPattern pattern, out object patternObj)
        {
            try
            {
                return element.TryGetCurrentPattern(pattern, out patternObj);
            }
            catch (ElementNotAvailableException)
            {
                patternObj = null;
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                patternObj = null;
                return false;
            }
        }

        /// <summary>Runs a pattern action (<c>Invoke</c>/<c>Toggle</c>/<c>Close</c>), converting UIA's runtime failure modes into a failure reason instead of throwing.</summary>
        private static bool TryUiaAction(string operation, Action action, out string message)
        {
            try
            {
                action();
                message = null;
                return true;
            }
            catch (ElementNotAvailableException ex)
            {
                message = $"The element is no longer available (its underlying UI has gone away): {ex.Message}";
                return false;
            }
            catch (InvalidOperationException ex)
            {
                message = $"The UI Automation provider rejected the operation: {ex.Message}";
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure(operation, ex);
                return false;
            }
        }

        /// <summary>Runs a UIA property read, returning <paramref name="fallback"/> instead of throwing. See <c>UIAutomationUtils.TryUia</c>.</summary>
        private static T SafeGet<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (ElementNotAvailableException)
            {
                return fallback;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return fallback;
            }
        }

        /// <summary>Strips the <c>"ControlType."</c> prefix from the control type's <c>ProgrammaticName</c>, matching <c>UIAutomationUtils.GetControlTypeName</c>'s convention.</summary>
        private static string FriendlyControlTypeName(ControlType controlType)
        {
            string programmatic = controlType?.ProgrammaticName;
            if (string.IsNullOrEmpty(programmatic))
                return string.Empty;
            const string prefix = "ControlType.";
            return programmatic.StartsWith(prefix, StringComparison.Ordinal) ? programmatic.Substring(prefix.Length) : programmatic;
        }

        private static string ClassNameOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
