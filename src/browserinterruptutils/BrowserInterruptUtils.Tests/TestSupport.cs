using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>
    /// One element in the fake desktop: either a native dialog's top-level window
    /// (<see cref="IsWindow"/>), a page-overlay element, or a plain descendant (button) of
    /// either. <see cref="Children"/> is what <see cref="FakeBrowserPopupProbe.FindOverlayCandidates"/>
    /// walks - reused both for overlay discovery (root = a watched browser window) and for
    /// target lookup (root = an already-matched popup), mirroring how the engine itself reuses
    /// that one probe method for both purposes.
    /// </summary>
    internal sealed class FakeElement
    {
        public BrowserElementRef Ref;
        public string Name = "";
        public string AutomationId = "";
        public string ClassName = "";
        public string ControlType = "Group";
        public string LocalizedControlType = "dialog";
        public int Pid;
        public string ProcessName = "";
        public string Message = "";
        public bool Alive = true;

        /// <summary>The probe reports this element alive even when it is not (an unpinned, evicted ref: unknown is not dead).</summary>
        public bool LivenessUnknown;
        public bool Visible = true;
        public bool IsWindow;
        public readonly List<FakeElement> Children = new List<FakeElement>();

        /// <summary>The window or overlay this element was added under, if any. A successful <see cref="FakeBrowserPopupProbe.TryInvoke"/> of a child also closes its owner, matching a real dialog/overlay closing when its OK/Accept button is pressed.</summary>
        public FakeElement Owner;

        public bool IgnoreInvoke;
        public bool IgnoreClose;

        /// <summary>The window has a minimize/maximize box (a normal application window, e.g. the browser's main window) as opposed to a dialog.</summary>
        public bool IsMainWindowLike;

        /// <summary>The invoke/close reports success but the popup stays open (a UIA call that returned before the browser acted, or that the page ignored).</summary>
        public bool SucceedWithoutClosing;
        public int Invokes;
        public int Closes;
        public string LastInvokedName;

        public BrowserElementInfo ToInfo() => new BrowserElementInfo
        {
            Name = Name,
            AutomationId = AutomationId,
            ClassName = ClassName,
            ControlType = ControlType,
            LocalizedControlType = LocalizedControlType,
            ProcessId = Pid,
            IsMainWindowLike = IsMainWindowLike,
            Ref = Ref
        };
    }

    /// <summary>A desktop made of <see cref="FakeElement"/>s, so the engine's decisions can be checked without a real one.</summary>
    internal sealed class FakeBrowserPopupProbe : IBrowserPopupProbe
    {
        private readonly Dictionary<BrowserElementRef, FakeElement> _elements = new Dictionary<BrowserElementRef, FakeElement>();
        private int _nextHandle = 0x2000;
        private int _nextRuntimeId = 1;

        public int CurrentProcessId { get; set; } = 1;

        /// <summary>
        /// Throw-injection: called with the operation name (<c>IsAlive</c>, <c>DescribeWindow</c>, <c>EnumerateTopLevelWindows</c>,
        /// <c>FindOverlayCandidates</c>, <c>TryInvoke</c>, <c>TryClose</c>, <c>TryGetMessageText</c>) and the element it concerns
        /// (default for enumeration); a non-null result is thrown before the operation does anything.
        /// </summary>
        public Func<string, BrowserElementRef, Exception> Fault;

        private void Inject(string operation, BrowserElementRef element)
        {
            var exception = Fault?.Invoke(operation, element);
            if (exception != null)
                throw exception;
        }

        /// <summary>How many times <see cref="FindOverlayCandidates"/> has been called - the perf-gating contract's assertion point.</summary>
        public int OverlaySearchCalls { get; private set; }

        /// <summary>Every root <see cref="FindOverlayCandidates"/> was called with, in order.</summary>
        public readonly List<BrowserElementRef> OverlaySearchRoots = new List<BrowserElementRef>();

        /// <summary>How many times <see cref="DescribeWindow"/> was called (the expensive per-window UIA read).</summary>
        public int DescribeWindowCalls { get; private set; }

        /// <summary>How many times <see cref="TryGetMessageText"/> was called (a bounded subtree walk in the real probe).</summary>
        public int MessageTextCalls { get; private set; }

        public FakeElement AddWindow(string name, string message = "", int pid = 100, string processName = "chrome", string className = "Chrome_WidgetWin_1")
        {
            var hwnd = new IntPtr(_nextHandle);
            _nextHandle += 0x10;
            var el = new FakeElement
            {
                Ref = new BrowserElementRef(new[] { _nextRuntimeId++ }, hwnd),
                Name = name,
                Message = message,
                Pid = pid,
                ProcessName = processName,
                ClassName = className,
                ControlType = "Window",
                LocalizedControlType = "dialog",
                IsWindow = true
            };
            _elements[el.Ref] = el;
            return el;
        }

        /// <summary>Adds a page-overlay element, either free-standing or as a child of another element (typically a watched browser window).</summary>
        public FakeElement AddOverlay(FakeElement parent, string name, string message = "", int? pid = null, string role = "dialog", string automationId = "")
        {
            var el = new FakeElement
            {
                Ref = new BrowserElementRef(new[] { _nextRuntimeId++ }),
                Name = name,
                AutomationId = automationId ?? "",
                Message = message,
                Pid = pid ?? parent?.Pid ?? 0,
                ControlType = "Group",
                LocalizedControlType = role
            };
            _elements[el.Ref] = el;
            if (parent != null)
            {
                parent.Children.Add(el);
                el.Owner = parent;
            }
            return el;
        }

        /// <summary>Adds a plain descendant (typically a button) of a window or overlay, found by <see cref="FindOverlayCandidates"/>.</summary>
        public FakeElement AddChild(FakeElement parent, string name, string automationId = "", string role = "button")
        {
            var el = new FakeElement
            {
                Ref = new BrowserElementRef(new[] { _nextRuntimeId++ }),
                Name = name,
                AutomationId = automationId ?? "",
                Pid = parent.Pid,
                ControlType = "Button",
                LocalizedControlType = role,
                Owner = parent
            };
            _elements[el.Ref] = el;
            parent.Children.Add(el);
            return el;
        }

        public void Destroy(FakeElement element) => element.Alive = false;

        public int TotalInvokes => _elements.Values.Sum(e => e.Invokes);
        public int TotalCloses => _elements.Values.Sum(e => e.Closes);

        // ---- IBrowserPopupProbe ----

        public IReadOnlyList<BrowserWindowInfo> EnumerateTopLevelWindows()
        {
            Inject("EnumerateTopLevelWindows", default);
            return _elements.Values.Where(e => e.IsWindow && e.Alive && e.Visible)
                .Select(e => new BrowserWindowInfo { Hwnd = e.Ref.Hwnd, ProcessName = e.ProcessName, ProcessId = e.Pid, ClassName = e.ClassName })
                .ToList();
        }

        public BrowserElementInfo DescribeWindow(IntPtr hwnd)
        {
            DescribeWindowCalls++;
            var el = _elements.Values.FirstOrDefault(e => e.IsWindow && e.Ref.Hwnd == hwnd);
            Inject("DescribeWindow", el?.Ref ?? default);
            return el == null || !el.Alive ? null : el.ToInfo();
        }

        public IReadOnlyList<BrowserElementInfo> FindOverlayCandidates(BrowserElementRef browserWindowRoot, int maxNodes, int maxDepth)
        {
            OnFindOverlayCandidates?.Invoke();
            Inject("FindOverlayCandidates", browserWindowRoot);
            OverlaySearchCalls++;
            OverlaySearchRoots.Add(browserWindowRoot);
            if (!_elements.TryGetValue(browserWindowRoot, out var root) || !root.Alive)
                return Array.Empty<BrowserElementInfo>();

            var result = new List<BrowserElementInfo>();
            void Walk(FakeElement element, int depth)
            {
                if (depth > maxDepth)
                    return;
                foreach (var child in element.Children)
                {
                    if (result.Count >= maxNodes)
                        return;
                    if (child.Alive)
                        result.Add(child.ToInfo());
                    Walk(child, depth + 1);
                }
            }
            Walk(root, 0);
            return result;
        }

        public bool IsAlive(BrowserElementRef element)
        {
            Inject("IsAlive", element);
            return _elements.TryGetValue(element, out var el) && (el.Alive || el.LivenessUnknown);
        }

        /// <summary>The refs currently pinned through <see cref="Retain"/>; must be empty when the engine tracks nothing.</summary>
        public readonly HashSet<BrowserElementRef> Retained = new HashSet<BrowserElementRef>();

        /// <summary>Every <see cref="Retain"/> call, including repeats.</summary>
        public int RetainCalls { get; private set; }

        /// <summary>Every <see cref="Release"/> call, including ones for refs that were not retained.</summary>
        public int ReleaseCalls { get; private set; }

        /// <summary>Release calls for a ref that was not retained (an unbalanced release).</summary>
        public int UnbalancedReleases { get; private set; }

        public void Retain(BrowserElementRef element)
        {
            RetainCalls++;
            Retained.Add(element);
        }

        public void Release(BrowserElementRef element)
        {
            ReleaseCalls++;
            if (!Retained.Remove(element))
                UnbalancedReleases++;
        }

        public string TryGetMessageText(BrowserElementRef element)
        {
            MessageTextCalls++;
            Inject("TryGetMessageText", element);
            return _elements.TryGetValue(element, out var el) ? el.Message : null;
        }

        /// <summary>Called when an invoke/close begins, before it lands (on the calling thread).</summary>
        public Action ActionEntered;

        /// <summary>When set, an invoke/close blocks on it (up to 15s) before it lands, so a test can hold one mid-action.</summary>
        public ManualResetEventSlim ActionGate;

        /// <summary>Called at the start of every <see cref="FindOverlayCandidates"/>, so a test can act during the slow discovery.</summary>
        public Action OnFindOverlayCandidates;

        private void EnterAction()
        {
            ActionEntered?.Invoke();
            ActionGate?.Wait(15000);
        }

        public bool TryInvoke(BrowserElementRef target, out string failureReason)
        {
            EnterAction();
            Inject("TryInvoke", target);
            failureReason = null;
            if (!_elements.TryGetValue(target, out var el) || !el.Alive)
            {
                failureReason = "element not found";
                return false;
            }
            el.Invokes++;
            el.LastInvokedName = el.Name;
            if (el.IgnoreInvoke)
            {
                failureReason = "invoke ignored";
                return false;
            }
            if (el.SucceedWithoutClosing)
                return true;
            el.Alive = false;
            if (el.Owner != null)
                el.Owner.Alive = false; // invoking a dialog/overlay's button closes it, like a real one
            return true;
        }

        public bool TryClose(BrowserElementRef target, out string failureReason)
        {
            EnterAction();
            Inject("TryClose", target);
            failureReason = null;
            if (!_elements.TryGetValue(target, out var el) || !el.Alive)
            {
                failureReason = "element not found";
                return false;
            }
            el.Closes++;
            if (el.IgnoreClose)
            {
                failureReason = "close ignored";
                return false;
            }
            if (el.SucceedWithoutClosing)
                return true;
            el.Alive = false;
            return true;
        }
    }

    /// <summary>A stand-in for the UIA hook thread: the test decides when a window appears or a watched window's structure changes.</summary>
    internal sealed class FakeBrowserPopupHookSource : IBrowserPopupHookSource
    {
        public event Action<BrowserWindowInfo> WindowOpened;
        public event Action<BrowserElementRef> WindowStructureChanged;

        private Action<string> _onFault;

        public bool FailToStart;
        public int StartCalls;
        public int StopCalls;

        /// <summary>Throw-injection for <c>WatchWindow</c>/<c>UnwatchWindow</c> (operation name and window); a non-null result is thrown before the call does anything.</summary>
        public Func<string, BrowserElementRef, Exception> Fault;

        private void Inject(string operation, BrowserElementRef window)
        {
            var exception = Fault?.Invoke(operation, window);
            if (exception != null)
                throw exception;
        }

        public readonly List<BrowserElementRef> WatchCalls = new List<BrowserElementRef>();
        public readonly List<BrowserElementRef> UnwatchCalls = new List<BrowserElementRef>();
        public readonly HashSet<BrowserElementRef> CurrentlyWatched = new HashSet<BrowserElementRef>();

        public bool Start(Action<string> onFault, out string message)
        {
            StartCalls++;
            if (FailToStart)
            {
                message = "fake hook failed to start";
                return false;
            }
            _onFault = onFault;
            message = null;
            return true;
        }

        public void Stop()
        {
            StopCalls++;
            _onFault = null;
        }

        public void WatchWindow(BrowserElementRef windowRoot)
        {
            WatchCalls.Add(windowRoot);
            Inject("WatchWindow", windowRoot);
            CurrentlyWatched.Add(windowRoot);
        }

        public void UnwatchWindow(BrowserElementRef windowRoot)
        {
            UnwatchCalls.Add(windowRoot);
            Inject("UnwatchWindow", windowRoot);
            CurrentlyWatched.Remove(windowRoot);
        }

        public void FireWindowOpened(BrowserWindowInfo info) => WindowOpened?.Invoke(info);

        public void FireWindowOpened(FakeElement window) => WindowOpened?.Invoke(
            new BrowserWindowInfo { Hwnd = window.Ref.Hwnd, ProcessName = window.ProcessName, ProcessId = window.Pid, ClassName = window.ClassName });

        public void FireStructureChanged(BrowserElementRef windowRoot) => WindowStructureChanged?.Invoke(windowRoot);

        public void FireFault(string text) => _onFault?.Invoke(text);
    }
}
