using System;
using System.Collections.Generic;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// What the engine needs to know about a native top-level window to decide whether it is
    /// worth describing further (mirrors <c>InterruptUtils</c>' own window-enumeration step,
    /// but pre-filtered to windows owned by a process of interest, since resolving every
    /// window's process name up front is cheap and lets the engine skip the rest).
    /// </summary>
    internal sealed class BrowserWindowInfo
    {
        public IntPtr Hwnd { get; set; }
        public string ProcessName { get; set; }
        public int ProcessId { get; set; }
        public string ClassName { get; set; }
    }

    /// <summary>
    /// A snapshot of one candidate popup element - either a native dialog's window or a
    /// page-overlay element - that might match a rule. Fetching the message text is kept
    /// separate (<see cref="IBrowserPopupProbe.TryGetMessageText"/>) because it is the
    /// expensive, probe-dependent part and is only needed lazily, once a rule's cheaper
    /// criteria already match.
    /// </summary>
    internal sealed class BrowserElementInfo
    {
        public string Name { get; set; }
        public string AutomationId { get; set; }
        public string ClassName { get; set; }
        public string ControlType { get; set; }
        public string LocalizedControlType { get; set; }
        public int ProcessId { get; set; }

        /// <summary>
        /// Native windows only: the window has a minimize or maximize box (<c>WS_MINIMIZEBOX</c>/
        /// <c>WS_MAXIMIZEBOX</c>), i.e. it looks like a normal resizable application window such as
        /// the browser's main window rather than a JS/system dialog. Also <c>true</c> when the
        /// window style could not be read (fail safe). The engine refuses to close such a window
        /// with a <see cref="BrowserPopupAction.CloseWindowPattern"/> rule.
        /// </summary>
        public bool IsMainWindowLike { get; set; }

        /// <summary>The opaque handle back to the real element, used to act on it later.</summary>
        public BrowserElementRef Ref { get; set; }
    }

    /// <summary>
    /// An opaque, equatable reference to a real UI Automation element, kept free of any UIA
    /// type (such as <c>AutomationElement</c>) so this interface can be implemented by a fake
    /// in tests, with no desktop or real browser required. Its identity is UIA's own runtime
    /// ID - <c>AutomationElement.GetRuntimeId()</c> - the automation-element analog of a
    /// window handle.
    /// </summary>
    internal readonly struct BrowserElementRef : IEquatable<BrowserElementRef>
    {
        public BrowserElementRef(int[] runtimeId, IntPtr hwnd = default)
        {
            RuntimeId = runtimeId ?? Array.Empty<int>();
            Hwnd = hwnd;
        }

        /// <summary>
        /// Builds a reference only when it can identify one element: a non-empty runtime ID, or
        /// failing that a non-zero <paramref name="hwnd"/> (a top-level window is keyed by its
        /// handle, which is unique). An empty runtime ID with no handle would make every such
        /// element equal to every other (one candidate, one cache slot), so it is refused.
        /// </summary>
        public static bool TryCreate(int[] runtimeId, IntPtr hwnd, out BrowserElementRef elementRef)
        {
            if ((runtimeId == null || runtimeId.Length == 0) && hwnd == IntPtr.Zero)
            {
                elementRef = default;
                return false;
            }
            elementRef = new BrowserElementRef(runtimeId, hwnd);
            return true;
        }

        /// <summary>The element's UIA runtime ID; empty (never <c>null</c>) if none is available.</summary>
        public int[] RuntimeId { get; }

        /// <summary>
        /// The owning top-level window, for a native dialog. <see cref="IntPtr.Zero"/> for a
        /// page-overlay element, which generally has no window of its own.
        /// </summary>
        public IntPtr Hwnd { get; }

        public bool Equals(BrowserElementRef other)
        {
            if (Hwnd != other.Hwnd)
                return false;
            if (ReferenceEquals(RuntimeId, other.RuntimeId))
                return true;
            if (RuntimeId == null || other.RuntimeId == null || RuntimeId.Length != other.RuntimeId.Length)
                return false;
            for (int i = 0; i < RuntimeId.Length; i++)
            {
                if (RuntimeId[i] != other.RuntimeId[i])
                    return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is BrowserElementRef other && Equals(other);

        public override int GetHashCode()
        {
            int hash = Hwnd.GetHashCode();
            if (RuntimeId != null)
            {
                foreach (int part in RuntimeId)
                    hash = unchecked(hash * 31 + part);
            }
            return hash;
        }

        public static bool operator ==(BrowserElementRef left, BrowserElementRef right) => left.Equals(right);
        public static bool operator !=(BrowserElementRef left, BrowserElementRef right) => !left.Equals(right);
    }

    /// <summary>
    /// What the popup engine asks of the UI Automation tree and the desktop's native windows.
    /// The real implementation is <c>UiaBrowserPopupProbe</c> (a later phase); the tests
    /// substitute a fake so the engine's decisions can be checked without a desktop or a real
    /// browser, exactly as <c>InterruptUtils</c>' <c>IPopupProbe</c> lets <c>PopupEngine</c> be
    /// tested via <c>FakeProbe</c>.
    /// </summary>
    internal interface IBrowserPopupProbe
    {
        /// <summary>The ID of the process the handler runs in; popups it owns are never touched.</summary>
        int CurrentProcessId { get; }

        /// <summary>Every visible top-level window owned by a process of interest (native-dialog candidates).</summary>
        IReadOnlyList<BrowserWindowInfo> EnumerateTopLevelWindows();

        /// <summary>Describes a previously discovered top-level window as a candidate element, or <c>null</c> if it no longer exists.</summary>
        BrowserElementInfo DescribeWindow(IntPtr hwnd);

        /// <summary>
        /// Walks a watched browser window's UIA subtree looking for page-overlay elements that
        /// look dialog-shaped (an ARIA <c>role="dialog"</c>/<c>alertdialog</c> and similar).
        /// Bounded so a pathological page can never make one pass run unbounded: the walk stops
        /// after <paramref name="maxNodes"/> elements have been visited or after
        /// <paramref name="maxDepth"/> levels have been descended, whichever comes first.
        /// </summary>
        IReadOnlyList<BrowserElementInfo> FindOverlayCandidates(BrowserElementRef browserWindowRoot, int maxNodes, int maxDepth);

        /// <summary>
        /// Whether a previously seen element still exists (mirrors a window handle's liveness check,
        /// <c>Win32PopupProbe.IsWindow</c>). Tri-state in spirit: <c>false</c> is only ever a
        /// DEFINITIVE "gone". An element the probe can no longer say anything about (a window-less
        /// page-overlay reference that was never <see cref="Retain">retained</see> and has fallen
        /// out of the probe's cache) reports <c>true</c>: unknown is not dead, so the engine bounds
        /// such an element by its attempt limit, its owner window's death and the periodic reap,
        /// rather than silently dropping a live popup or falsely confirming a dismissal.
        /// </summary>
        bool IsAlive(BrowserElementRef element);

        /// <summary>
        /// Pins the element so the probe keeps a strong reference to it, and therefore keeps
        /// answering <see cref="IsAlive"/> definitively for it, until <see cref="Release"/>. The
        /// engine calls it when it starts tracking a candidate. Idempotent; a no-op when the probe
        /// cannot resolve the element at that moment.
        /// </summary>
        void Retain(BrowserElementRef element);

        /// <summary>Drops the pin taken by <see cref="Retain"/>. Safe to call for an element that was never retained.</summary>
        void Release(BrowserElementRef element);

        /// <summary>
        /// Drops everything the probe holds on to between calls: its element cache, every pin and its
        /// process-name cache. Each is a strong reference into a browser process (or state derived from
        /// one), and cache rotation only happens on use, so without this a stopped component would keep
        /// them until the next <c>Start</c>. The engine calls it from <c>ResetRuntime</c>, after it has
        /// dropped its candidates. Never throws; the probe simply re-discovers what it needs afterwards.
        /// </summary>
        void ClearCache();

        /// <summary>The element's first non-empty text descendant, or <c>null</c> if none is found.</summary>
        string TryGetMessageText(BrowserElementRef element);

        /// <summary>
        /// Invokes the element - its UIA Invoke pattern, or an equivalent click - such as
        /// pressing a dialog's or overlay's button. Works for either scope.
        /// </summary>
        bool TryInvoke(BrowserElementRef target, out string failureReason);

        /// <summary>
        /// Closes a native dialog's window (its UIA Window pattern's Close, or the equivalent).
        /// Not meaningful for a page-overlay element, which has no window of its own; refusing
        /// that is the concrete implementation's job; this interface does not encode the
        /// restriction, since the rule/engine layer is what decides which scope a rule applies to.
        /// </summary>
        bool TryClose(BrowserElementRef target, out string failureReason);
    }

    /// <summary>
    /// Delivers the notifications the engine needs to know when to look again, as a background
    /// thread sees them: a new native-dialog-shaped top-level window appearing, and a watched
    /// browser window's page structure changing. Mirrors <c>InterruptUtils</c>' own
    /// <c>IPopupHookSource</c>, plus the dynamic per-window watch registration that UIA's
    /// <c>StructureChanged</c> event needs (unlike <c>SetWinEventHook</c>, which that component
    /// registers once for the whole session).
    /// </summary>
    internal interface IBrowserPopupHookSource
    {
        /// <summary>A new top-level window that looks like a native dialog appeared.</summary>
        event Action<BrowserWindowInfo> WindowOpened;

        /// <summary>
        /// A watched browser window's page structure changed since it was last examined.
        /// Raised once per window per coalesced burst of activity, not once per underlying UIA
        /// <c>StructureChanged</c> event, so the engine only has to re-walk that window's
        /// subtree, not re-run the walk on every DOM mutation.
        /// </summary>
        event Action<BrowserElementRef> WindowStructureChanged;

        /// <summary>
        /// Starts delivering <see cref="WindowOpened"/> notifications (runs on a hook thread and
        /// must return quickly). <see cref="WindowStructureChanged"/> notifications begin only
        /// for windows separately registered with <see cref="WatchWindow"/>.
        /// </summary>
        /// <param name="onFault">The event pump failed after it had started, with a description.</param>
        /// <param name="message"><c>null</c> on success; otherwise why it could not start.</param>
        bool Start(Action<string> onFault, out string message);

        /// <summary>Stops delivering; safe to call when not started.</summary>
        void Stop();

        /// <summary>
        /// Starts raising <see cref="WindowStructureChanged"/> for this browser window's subtree.
        /// Safe to call more than once for the same window. Unlike <c>WindowOpened</c>, this
        /// subscription is per-window because a UIA <c>StructureChanged</c> listener is scoped to
        /// one element's subtree, not the whole desktop.
        /// </summary>
        void WatchWindow(BrowserElementRef windowRoot);

        /// <summary>Stops raising <see cref="WindowStructureChanged"/> for this browser window. Safe to call when not watched.</summary>
        void UnwatchWindow(BrowserElementRef windowRoot);
    }
}
