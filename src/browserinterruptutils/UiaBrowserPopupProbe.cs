using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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
    /// unreachable and reported as such - there is no other way to relocate it. The cache
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

        /// <summary>Most elements one cache generation holds before it rotates (a default 5000-node walk fits in one).</summary>
        internal const int CacheMaxEntriesPerGeneration = 8192;

        /// <summary>How long a cache generation lives before it rotates, in milliseconds.</summary>
        internal const int CacheRotateAfterMs = 60000;

        /// <summary>Holds every element this probe has discovered/described. See the type remarks.</summary>
        private readonly GenerationalCache<BrowserElementRef, AutomationElement> _cache =
            new GenerationalCache<BrowserElementRef, AutomationElement>(CacheMaxEntriesPerGeneration, CacheRotateAfterMs);

        /// <summary>Bounds the fallback runtime-id search used to relocate a cache-missed descendant of a still-resolvable window.</summary>
        private const int ResolveFallbackMaxNodes = 5000;
        private const int ResolveFallbackMaxDepth = 50;

        /// <summary>Bounds <see cref="TryGetMessageText"/>'s descendant search - small, since a message is expected near the popup's surface.</summary>
        private const int MessageTextMaxNodes = 200;
        private const int MessageTextMaxDepth = 10;

        public int CurrentProcessId { get; } = Process.GetCurrentProcess().Id;

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
                            ProcessName = ProcessNameOf((int)pid)
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

                AutomationElement element = ResolveFromHandle(hwnd);
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

                var queue = new Queue<(AutomationElement Element, int Depth)>();
                EnqueueChildrenUpToBudget(root, 1, 0, maxNodes, queue);

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
                        EnqueueChildrenUpToBudget(element, depth + 1, visited, maxNodes, queue);
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
        /// budget, and never materialized as a list. Best-effort: an enumeration failure keeps
        /// whatever was enqueued so far and never throws.
        /// </summary>
        private static void EnqueueChildrenUpToBudget(AutomationElement parent, int depth, int alreadyVisited, int maxNodes,
            Queue<(AutomationElement Element, int Depth)> queue)
        {
            try
            {
                if (alreadyVisited + queue.Count >= maxNodes)
                    return;
                AutomationElement child = _childWalker.GetFirstChild(parent);
                while (child != null)
                {
                    queue.Enqueue((child, depth));
                    if (alreadyVisited + queue.Count >= maxNodes)
                        return;
                    child = _childWalker.GetNextSibling(child);
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
                return TryResolve(element, out AutomationElement resolved) && IsElementAvailable(resolved);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return false;
            }
        }

        public string TryGetMessageText(BrowserElementRef element)
        {
            try
            {
                if (!TryResolve(element, out AutomationElement root) || root == null)
                    return null;

                var queue = new Queue<(AutomationElement Element, int Depth)>();
                EnqueueChildrenUpToBudget(root, 1, 0, MessageTextMaxNodes, queue);

                int visited = 0;
                while (queue.Count > 0 && visited < MessageTextMaxNodes)
                {
                    (AutomationElement candidate, int depth) = queue.Dequeue();
                    visited++;

                    ControlType controlType = SafeGet(() => candidate.Current.ControlType, null);
                    if (Equals(controlType, ControlType.Text))
                    {
                        string name = SafeGet(() => candidate.Current.Name, string.Empty);
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }

                    if (depth < MessageTextMaxDepth)
                    {
                        EnqueueChildrenUpToBudget(candidate, depth + 1, visited, MessageTextMaxNodes, queue);
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
                if (!TryResolve(target, out AutomationElement element) || element == null)
                {
                    failureReason = "The element could not be found (it may have already closed).";
                    return false;
                }

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
                if (!TryResolve(target, out AutomationElement element) || element == null)
                {
                    failureReason = "The element could not be found (it may have already closed).";
                    return false;
                }

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
        private bool TryResolve(BrowserElementRef target, out AutomationElement element)
        {
            element = null;

            // A ref with a window handle is only as alive as that window. IsWindow is a cheap,
            // deterministic answer; the UIA "Current" read in IsElementAvailable is not (a
            // just-destroyed window's element can keep answering from a stale provider while
            // teardown is still in progress), so it must never be the only liveness test. A dead
            // handle also evicts the cache entry so a strong element reference is not kept for it.
            if (target.Hwnd != IntPtr.Zero && !NativeMethods.IsWindow(target.Hwnd))
            {
                _cache.Remove(target);
                return false;
            }

            if (_cache.TryGet(target, out AutomationElement cached))
            {
                if (IsElementAvailable(cached))
                {
                    element = cached;
                    return true;
                }
                _cache.Remove(target); // dead: drop it now rather than wait for rotation
            }

            if (target.Hwnd == IntPtr.Zero)
                return false; // a page-overlay element with no live cache entry is unreachable

            AutomationElement windowElement = ResolveFromHandle(target.Hwnd);
            if (windowElement == null)
                return false;

            int[] runtimeId = target.RuntimeId;
            if (runtimeId == null || runtimeId.Length == 0
                || RuntimeIdEquals(SafeGet(() => windowElement.GetRuntimeId(), null), runtimeId))
            {
                CacheElement(target, windowElement);
                element = windowElement;
                return true;
            }

            AutomationElement found = FindByRuntimeId(windowElement, runtimeId);
            if (found == null)
                return false;

            CacheElement(target, found);
            element = found;
            return true;
        }

        private static AutomationElement FindByRuntimeId(AutomationElement root, int[] runtimeId)
        {
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            EnqueueChildrenUpToBudget(root, 1, 0, ResolveFallbackMaxNodes, queue);

            int visited = 0;
            while (queue.Count > 0 && visited < ResolveFallbackMaxNodes)
            {
                (AutomationElement element, int depth) = queue.Dequeue();
                visited++;

                if (RuntimeIdEquals(SafeGet(() => element.GetRuntimeId(), null), runtimeId))
                    return element;

                if (depth < ResolveFallbackMaxDepth)
                {
                    EnqueueChildrenUpToBudget(element, depth + 1, visited, ResolveFallbackMaxNodes, queue);
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
            if (element == null || !IsElementAvailable(element))
                return null;

            // Identity first, then the rest: an element with no runtime ID and no window handle
            // cannot be told apart from any other such element (they would all share one key,
            // aliasing one candidate and one cache slot), so it is not describable at all. An
            // element that is a window (non-zero hwnd) may lack a runtime ID and is keyed by its
            // handle, which is unique; see BrowserElementRef.TryCreate.
            int[] runtimeId = SafeGet(() => element.GetRuntimeId(), null);
            if (!BrowserElementRef.TryCreate(runtimeId, hwnd, out BrowserElementRef elementRef))
                return null;

            string name = SafeGet(() => element.Current.Name, string.Empty) ?? string.Empty;
            string automationId = SafeGet(() => element.Current.AutomationId, string.Empty) ?? string.Empty;
            string className = SafeGet(() => element.Current.ClassName, string.Empty) ?? string.Empty;
            string controlType = SafeGet(() => FriendlyControlTypeName(element.Current.ControlType), string.Empty) ?? string.Empty;
            string localizedControlType = SafeGet(() => element.Current.LocalizedControlType, string.Empty) ?? string.Empty;
            int processId = SafeGet(() => element.Current.ProcessId, 0);

            CacheElement(elementRef, element);

            return new BrowserElementInfo
            {
                Name = name,
                AutomationId = automationId,
                ClassName = className,
                ControlType = controlType,
                LocalizedControlType = localizedControlType,
                ProcessId = processId,
                Ref = elementRef
            };
        }

        // ------------------------------------------------------------------ safe UIA helpers

        private static AutomationElement ResolveFromHandle(IntPtr hwnd)
        {
            try
            {
                return AutomationElement.FromHandle(hwnd);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return null;
            }
        }

        /// <summary>Mirrors <c>UIAutomationUtils.IsElementAvailable</c>: a cheap <c>Current</c> read is enough to detect a dead element.</summary>
        private static bool IsElementAvailable(AutomationElement element)
        {
            if (element == null)
                return false;
            try
            {
                _ = element.Current.IsEnabled;
                return true;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
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

        /// <summary>
        /// The name of the process with this ID, or <c>string.Empty</c> for an invalid ID or one
        /// that no longer exists - mirrors <c>Win32PopupProbe.GetProcessName</c>'s race handling
        /// (the process can exit between enumeration and this lookup).
        /// </summary>
        private static string ProcessNameOf(int processId)
        {
            if (processId <= 0)
                return string.Empty;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    return process.ProcessName;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return string.Empty;
            }
        }
    }
}
