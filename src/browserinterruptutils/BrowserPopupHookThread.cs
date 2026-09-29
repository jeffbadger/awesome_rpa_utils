using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Threading;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// The real <see cref="IBrowserPopupHookSource"/>: backs <see cref="BrowserPopupEngine"/> with
    /// actual <c>System.Windows.Automation</c> event registrations - a desktop-wide
    /// <see cref="WindowPattern.WindowOpenedEvent"/> subscription, persistent for the life of the
    /// hook, plus a dynamic per-window <c>StructureChanged</c> watch registry. Mirrors
    /// <c>InterruptAutomation.PopupHookThread</c>'s architectural shape (a dedicated background
    /// thread, idempotent <see cref="Start"/>/<see cref="Stop"/>, bounded teardown, fault reporting
    /// through a callback rather than an exception) even though the underlying hook mechanism -
    /// UIA client event registration versus <c>SetWinEventHook</c> - is completely different.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <b>Dedicated thread mechanism.</b> A single background thread is started in
    /// <see cref="Start"/>, set to <see cref="ApartmentState.STA"/>, and kept alive for the whole
    /// life of the hook by running a <see cref="Dispatcher"/> message pump
    /// (<see cref="Dispatcher.Run"/>) - the same mechanism Task 3's own test harness
    /// (<c>UiaTests.WpfButtonHost</c>) already uses to host a perpetually-responsive UI thread.
    /// This is a defensive choice, not a strict requirement: unlike <c>SetWinEventHook</c>'s
    /// out-of-context delivery (which genuinely needs the registering thread's message queue to
    /// receive events), a real UIA client event is typically delivered to
    /// <see cref="Automation.AddAutomationEventHandler"/>/
    /// <see cref="Automation.AddStructureChangedEventHandler"/> callbacks on UIA's own COM-sink
    /// thread regardless of what the registering thread is doing. Keeping a live, stable
    /// STA apartment/thread around for as long as the registrations exist is nonetheless the
    /// safer, established pattern for a managed UIA client, and the <see cref="Dispatcher"/> also
    /// gives a simple, cross-thread-safe shutdown primitive
    /// (<see cref="Dispatcher.InvokeShutdown"/>) to pair with a bounded <see cref="Thread.Join(int)"/>,
    /// mirroring <c>PopupHookThread</c>'s own WM_QUIT-based bounded shutdown.
    /// </item>
    /// <item>
    /// <b>Coalescing happens here, not in the engine.</b> <c>BrowserPopupEngine.Pump</c> drains its
    /// <c>_overlayDirtyQueue</c> by running one full <c>SweepOneOverlayWindow</c> subtree walk per
    /// dequeued entry, with no de-duplication across entries for the same window (verified by
    /// reading <c>DrainOverlayDirtyQueue</c>/<c>SweepOneOverlayWindow</c> directly) - it relies
    /// entirely on whoever raises <see cref="WindowStructureChanged"/> to already have coalesced a
    /// burst of activity into one notification per window, exactly as
    /// <see cref="IBrowserPopupHookSource.WindowStructureChanged"/>'s own doc comment says
    /// ("Raised once per window per coalesced burst of activity, not once per underlying UIA
    /// StructureChanged event"). This hook therefore throttles: a real UIA
    /// <c>StructureChanged</c> callback for a given window only results in a raised notification
    /// if at least <see cref="StructureChangedCoalesceMs"/> milliseconds have passed since the
    /// last one raised for that same window; callbacks arriving inside that window are dropped.
    /// This is a leading edge plus ONE trailing notification per burst: the first signal raises
    /// immediately; signals suppressed inside the cooldown are remembered as a single pending
    /// trailing notification (however many arrive), which a lazily-created, per-watched-window,
    /// single-shot <see cref="Timer"/> delivers when the cooldown expires (re-arming for the
    /// remaining time if a newer raise restarted the cooldown, and never raising for a window
    /// that was unwatched or after <see cref="Stop"/>). With
    /// <c>BrowserPopupEngine.OverlaySweepIntervalMs = 0</c> (periodic sweep off) this guarantees a
    /// final walk after a burst of structure changes, so an overlay that appears just after the
    /// leading walk is still discovered. The decision logic is the pure, unit-tested
    /// <see cref="StructureChangedThrottle"/>; the timer only supplies the delay. The timer is a
    /// pool-thread timer, so it never keeps the process alive.
    /// </item>
    /// <item>
    /// <b>Watch registry.</b> A single <see cref="Dictionary{TKey,TValue}"/> keyed by the
    /// requesting <see cref="BrowserElementRef"/> (the window-level ref <see cref="WatchWindow"/>
    /// was called with), storing the resolved <see cref="AutomationElement"/>, the exact
    /// <see cref="StructureChangedEventHandler"/> delegate instance that was registered (needed
    /// verbatim by <see cref="Automation.RemoveStructureChangedEventHandler"/> to remove precisely
    /// that registration), and a per-window last-raised timestamp used for the coalescing throttle
    /// above. Guarded by a dedicated lock (<see cref="_watchLock"/>), separate from the
    /// start/stop lifecycle lock, since a <c>StructureChanged</c> callback may run concurrently
    /// with <see cref="WatchWindow"/>/<see cref="UnwatchWindow"/>/<see cref="Stop"/> calls made
    /// from the engine's own worker thread. Asking to watch an already-watched window is a no-op
    /// (the existing registration is left alone, not replaced); asking to unwatch a window that
    /// is not currently watched is also a no-op. A window-level <see cref="BrowserElementRef"/>
    /// always has a valid <see cref="BrowserElementRef.Hwnd"/> per Task 1's design, so
    /// <see cref="WatchWindow"/> resolves directly via <see cref="AutomationElement.FromHandle"/>
    /// and does not need the more general runtime-id subtree search
    /// <see cref="UiaBrowserPopupProbe"/> uses for arbitrary elements.
    /// </item>
    /// <item>
    /// <b>Never-throws.</b> Every public member, and every UIA/COM event callback, follows this
    /// repo's never-throws discipline exactly as <see cref="UiaBrowserPopupProbe"/> does: nothing
    /// may escape a callback running inside a COM event sink, since an unhandled exception there
    /// can crash the process or corrupt UI Automation's internal client state. A genuine failure
    /// to register the desktop-wide <see cref="WindowOpened"/> handler, or an unexpected exception
    /// during the hook thread's own setup, is reported through <see cref="Start"/>'s
    /// <c>out message</c>; a failure discovered later (after <see cref="Start"/> already returned
    /// <c>true</c>) is reported through the <c>onFault</c> callback instead, never thrown across
    /// the thread boundary.
    /// </item>
    /// </list>
    /// </remarks>
    internal sealed class BrowserPopupHookThread : IBrowserPopupHookSource
    {
        private const int StartTimeoutMs = 5000;
        private const int StopTimeoutMs = 2000;

        /// <summary>
        /// The minimum time between two <see cref="WindowStructureChanged"/> raises for the same
        /// window. See the type remarks ("Coalescing happens here, not in the engine.").
        /// </summary>
        private const int StructureChangedCoalesceMs = 250;

        public event Action<BrowserWindowInfo> WindowOpened;
        public event Action<BrowserElementRef> WindowStructureChanged;

        /// <summary>One watched window's live registration; see the type remarks ("Watch registry.").</summary>
        private sealed class WatchEntry
        {
            public AutomationElement Element;
            public StructureChangedEventHandler Handler;

            /// <summary>
            /// <c>Environment.TickCount64</c> of the last raised notification for this window.
            /// Seeded at registration time (see <see cref="WatchWindow"/>) via
            /// <see cref="StructureChangedThrottle.InitialSeed"/> - see that method's doc comment
            /// for why <see cref="long.MinValue"/> is unsafe here (it made the throttle's
            /// subtraction overflow and permanently silenced <see cref="WindowStructureChanged"/>
            /// for every window; a critical Task 4 code-review finding). Read/written only via
            /// <see cref="Interlocked"/> (see <see cref="StructureChangedThrottle.ShouldRaise"/>).
            /// </summary>
            public long LastRaisedTicks;

            /// <summary>1 while a trailing notification is pending (see <see cref="StructureChangedThrottle.Decide"/>); Interlocked only.</summary>
            public int TrailingPending;

            /// <summary>Guards <see cref="TrailingTimer"/> and <see cref="TimerDisposed"/>; never held while calling out or while taking <c>_watchLock</c>.</summary>
            public readonly object TimerLock = new object();

            /// <summary>The lazily-created single-shot trailing timer; null until the first suppressed signal.</summary>
            public Timer TrailingTimer;

            /// <summary>Set (under <see cref="TimerLock"/>) once the entry is unwatched/stopped so a late arm cannot resurrect a timer.</summary>
            public bool TimerDisposed;
        }

        private readonly object _lifecycleLock = new object();
        private readonly object _watchLock = new object();
        private readonly Dictionary<BrowserElementRef, WatchEntry> _watched = new Dictionary<BrowserElementRef, WatchEntry>();

        private Thread _thread;
        private Dispatcher _dispatcher;
        private AutomationEventHandler _windowOpenedHandler;
        private Action<string> _onFault;
        private volatile bool _started;

        // ------------------------------------------------------------------ lifecycle

        public bool Start(Action<string> onFault, out string message)
        {
            message = null;
            lock (_lifecycleLock)
            {
                if (_started)
                {
                    message = "The hook is already running.";
                    return false;
                }

                _onFault = onFault;

                var startup = new StartupState();

                var thread = new Thread(() => RunHookThread(startup))
                {
                    IsBackground = true,
                    Name = "BrowserInterruptUtils.UiaHook"
                };
                thread.SetApartmentState(ApartmentState.STA);

                try
                {
                    thread.Start();
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    message = NeverThrowsGuard.Failure("Starting the UIA hook thread", ex);
                    return false;
                }

                if (!startup.Ready.Wait(StartTimeoutMs))
                {
                    // Decide the race atomically: either this call abandons the start (the hook
                    // thread then unregisters whatever it registered and exits without pumping), or
                    // the thread already completed in the instant since the wait timed out.
                    if (Interlocked.CompareExchange(ref startup.Phase, StartupState.Abandoned, StartupState.Pending) == StartupState.Pending)
                    {
                        message = "The UIA hook thread did not start in time.";
                        // Nothing else to clean up here: the thread owns the signal and the
                        // registration, and undoes both itself when (if) it gets that far.
                        return false;
                    }
                    startup.Ready.Wait(StopTimeoutMs); // Completed: the signal is being set right now
                }

                if (startup.SetupFailure != null)
                {
                    message = NeverThrowsGuard.Failure("The UIA hook thread's setup", startup.SetupFailure);
                    thread.Join(StopTimeoutMs); // it is already on its way out (no Dispatcher.Run was entered)
                    return false;
                }

                if (startup.RegistrationFailure != null)
                {
                    message = startup.RegistrationFailure;
                    TryShutdownDispatcher(startup.Dispatcher);
                    thread.Join(StopTimeoutMs);
                    return false;
                }

                _thread = thread;
                _dispatcher = startup.Dispatcher;
                _windowOpenedHandler = startup.WindowOpenedHandler;
                _started = true;
                return true;
            }
        }

        /// <summary>
        /// State shared between <see cref="Start"/> and the hook thread for one start attempt. The
        /// hook thread owns the <see cref="Ready"/> signal for its whole life (it is never disposed:
        /// a <see cref="ManualResetEventSlim"/> that nobody takes a wait handle from holds no
        /// kernel resource), so a thread that finishes after <see cref="Start"/> gave up can still
        /// set it safely. <see cref="Phase"/> settles, atomically, who won the race between the
        /// thread finishing its setup and <see cref="Start"/>'s timeout.
        /// </summary>
        private sealed class StartupState
        {
            public const int Pending = 0;
            public const int Completed = 1;
            public const int Abandoned = 2;

            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            public int Phase = Pending;
            public Dispatcher Dispatcher;
            public Exception SetupFailure;
            public string RegistrationFailure;
            public AutomationEventHandler WindowOpenedHandler;
        }

        private void RunHookThread(StartupState startup)
        {
            bool failed = false;
            try
            {
                startup.Dispatcher = Dispatcher.CurrentDispatcher;
                try
                {
                    startup.WindowOpenedHandler = OnWindowOpenedRaw;
                    Automation.AddAutomationEventHandler(
                        WindowPattern.WindowOpenedEvent,
                        AutomationElement.RootElement,
                        TreeScope.Children,
                        startup.WindowOpenedHandler);
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    startup.RegistrationFailure = NeverThrowsGuard.Failure("Registering the desktop-wide WindowOpened handler", ex);
                    failed = true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                startup.SetupFailure = ex;
                failed = true;
            }

            // Publish the outcome. Losing the race means Start already gave up (timed out): it
            // reported failure and will never call Stop for this thread, so a handler this thread
            // just registered would stay alive desktop-wide. Undo it and exit without pumping.
            bool abandoned = Interlocked.CompareExchange(ref startup.Phase, StartupState.Completed, StartupState.Pending)
                == StartupState.Abandoned;
            if (abandoned)
            {
                if (!failed && startup.WindowOpenedHandler != null)
                {
                    try
                    {
                        Automation.RemoveAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, startup.WindowOpenedHandler);
                    }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
                }
                return;
            }

            try { startup.Ready.Set(); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }

            if (failed)
                return;

            try
            {
                Dispatcher.Run();
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                ReportFault(NeverThrowsGuard.Failure("The UIA hook thread's message pump", ex));
            }
        }

        public void Stop()
        {
            lock (_lifecycleLock)
            {
                if (!_started)
                    return;
                _started = false;

                try
                {
                    if (_windowOpenedHandler != null)
                        Automation.RemoveAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, _windowOpenedHandler);
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
                _windowOpenedHandler = null;

                List<WatchEntry> watchedEntries;
                lock (_watchLock)
                {
                    watchedEntries = new List<WatchEntry>(_watched.Values);
                    _watched.Clear();
                }
                foreach (var entry in watchedEntries)
                {
                    DisposeTrailingTimer(entry);
                    try { Automation.RemoveStructureChangedEventHandler(entry.Element, entry.Handler); }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
                }

                TryShutdownDispatcher(_dispatcher);
                // Bounded: never hang forever on a stuck thread (mirrors Task 3's own fix for its
                // test harness's dispatcher shutdown).
                _thread?.Join(StopTimeoutMs);

                _dispatcher = null;
                _thread = null;
                _onFault = null;
            }
        }

        private static void TryShutdownDispatcher(Dispatcher dispatcher)
        {
            if (dispatcher == null)
                return;
            try
            {
                // Safe to call from any thread; unblocks that thread's Dispatcher.Run().
                if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    dispatcher.InvokeShutdown();
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
        }

        // ------------------------------------------------------------------ per-window watch registry

        public void WatchWindow(BrowserElementRef windowRoot)
        {
            try
            {
                if (windowRoot.Hwnd == IntPtr.Zero)
                    return; // a window-level ref always has a valid Hwnd per Task 1's design; defensive no-op otherwise

                lock (_watchLock)
                {
                    if (_watched.ContainsKey(windowRoot))
                        return; // already watched: no-op, see the type remarks
                }

                AutomationElement windowElement = ResolveFromHandle(windowRoot.Hwnd);
                if (windowElement == null)
                    return; // the window is gone or unresolvable; nothing to watch

                StructureChangedEventHandler handler = (sender, args) => OnStructureChangedRaw(windowRoot, args);

                lock (_watchLock)
                {
                    // Re-check under the lock: a racing WatchWindow call for the same window may
                    // have registered while this call was resolving the element above.
                    if (_watched.ContainsKey(windowRoot))
                        return;
                    // Stop clears _watched under this lock after clearing _started; refusing here
                    // means a WatchWindow racing (or following) Stop cannot leave a handler behind.
                    if (!_started)
                        return;

                    Automation.AddStructureChangedEventHandler(windowElement, TreeScope.Subtree, handler);
                    _watched[windowRoot] = new WatchEntry
                    {
                        Element = windowElement,
                        Handler = handler,
                        // Seeded so the first real StructureChanged callback for this window always
                        // raises immediately; see the WatchEntry.LastRaisedTicks doc comment.
                        LastRaisedTicks = StructureChangedThrottle.InitialSeed(Environment.TickCount64, StructureChangedCoalesceMs)
                    };
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Never throws; a failed watch registration simply means no StructureChanged
                // notifications for this window (the engine's periodic overlay sweep is the fallback).
            }
        }

        public void UnwatchWindow(BrowserElementRef windowRoot)
        {
            try
            {
                WatchEntry entry;
                lock (_watchLock)
                {
                    if (!_watched.TryGetValue(windowRoot, out entry))
                        return; // not watched: safe no-op
                    _watched.Remove(windowRoot);
                }
                DisposeTrailingTimer(entry);
                Automation.RemoveStructureChangedEventHandler(entry.Element, entry.Handler);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Never throws; the dictionary entry is already gone either way.
            }
        }

        // ------------------------------------------------------------------ callbacks (minimal work only)

        /// <summary>
        /// The desktop-wide <c>WindowOpened</c> callback. Does the one necessary UIA property read
        /// (<see cref="AutomationElement.Current"/>'s <c>NativeWindowHandle</c>) to bridge to
        /// Win32-land, then only cheap Win32 P/Invoke calls - no further UIA work. The engine calls
        /// back into the real <see cref="IBrowserPopupProbe"/> for any enrichment later, on its own
        /// worker thread.
        /// </summary>
        private void OnWindowOpenedRaw(object sender, AutomationEventArgs e)
        {
            try
            {
                if (!(sender is AutomationElement element))
                    return;

                int nativeHandle;
                try
                {
                    nativeHandle = element.Current.NativeWindowHandle;
                }
                catch (ElementNotAvailableException)
                {
                    return; // gone before this callback got to read it
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    return;
                }

                if (nativeHandle == 0)
                    return;
                IntPtr hwnd = new IntPtr(nativeHandle);
                if (!NativeMethods.IsWindow(hwnd))
                    return;
                if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
                    return; // not genuinely top-level

                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                var info = new BrowserWindowInfo
                {
                    Hwnd = hwnd,
                    ClassName = ClassNameOf(hwnd),
                    ProcessId = (int)pid,
                    ProcessName = ProcessNameOf((int)pid)
                };

                RaiseWindowOpened(info);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Never throws into the COM event sink.
                Debug.WriteLine("BrowserInterruptUtils: WindowOpened callback failed: " + ex.Message);
            }
        }

        /// <summary>
        /// One watched window's <c>StructureChanged</c> callback. Applies the coalescing throttle
        /// (see the type remarks): raises on the leading edge, or arms the one trailing
        /// notification - no further UIA work happens here.
        /// </summary>
        private void OnStructureChangedRaw(BrowserElementRef windowRoot, StructureChangedEventArgs args)
        {
            try
            {
                if (args == null)
                    return;

                WatchEntry entry;
                lock (_watchLock)
                {
                    if (!_watched.TryGetValue(windowRoot, out entry))
                        return; // unwatched since the underlying UIA event was queued/delivered
                }

                long now = Environment.TickCount64;
                switch (StructureChangedThrottle.Decide(ref entry.LastRaisedTicks, ref entry.TrailingPending, now, StructureChangedCoalesceMs))
                {
                    case StructureChangedThrottle.Decision.Raise:
                        RaiseWindowStructureChanged(windowRoot);
                        break;
                    case StructureChangedThrottle.Decision.SuppressedArmTrailing:
                        // First suppressed signal of this cooldown: one trailing delivery when it ends.
                        ArmTrailingTimer(windowRoot, entry,
                            StructureChangedThrottle.DelayUntilCooldownEndsMs(Interlocked.Read(ref entry.LastRaisedTicks), now, StructureChangedCoalesceMs));
                        break;
                    default:
                        break; // a trailing is already pending: this signal coalesces into it
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Never throws into the COM event sink.
                Debug.WriteLine("BrowserInterruptUtils: WindowStructureChanged callback failed: " + ex.Message);
            }
        }

        /// <summary>Arms (creating lazily, else rescheduling) the entry's single-shot trailing timer. Never throws.</summary>
        private void ArmTrailingTimer(BrowserElementRef windowRoot, WatchEntry entry, int delayMs)
        {
            try
            {
                lock (entry.TimerLock)
                {
                    if (entry.TimerDisposed)
                        return; // unwatched/stopped: a late arm must not resurrect the timer
                    if (delayMs < 0)
                        delayMs = 0;
                    if (entry.TrailingTimer == null)
                        entry.TrailingTimer = new Timer(_ => OnTrailingTimer(windowRoot, entry), null, delayMs, Timeout.Infinite);
                    else
                        entry.TrailingTimer.Change(delayMs, Timeout.Infinite);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                Debug.WriteLine("BrowserInterruptUtils: arming the trailing timer failed: " + ex.Message);
            }
        }

        /// <summary>Cancels and disposes the entry's trailing timer and forbids re-creating it. Never throws.</summary>
        private static void DisposeTrailingTimer(WatchEntry entry)
        {
            try
            {
                lock (entry.TimerLock)
                {
                    entry.TimerDisposed = true;
                    entry.TrailingTimer?.Dispose();
                    entry.TrailingTimer = null;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                Debug.WriteLine("BrowserInterruptUtils: disposing the trailing timer failed: " + ex.Message);
            }
        }

        private bool IsStillWatched(BrowserElementRef windowRoot, WatchEntry entry)
        {
            lock (_watchLock)
                return _started && _watched.TryGetValue(windowRoot, out var current) && ReferenceEquals(current, entry);
        }

        /// <summary>
        /// The trailing timer's callback (a thread-pool thread). Delivers the one pending trailing
        /// notification through the same guarded raise path as a leading raise, or re-arms once if a
        /// newer raise restarted the cooldown. Harmless when racing <see cref="UnwatchWindow"/>/
        /// <see cref="Stop"/>: the entry must still be registered and the hook started, checked under
        /// <c>_watchLock</c> (which is not held while subscribers run). Never throws.
        /// </summary>
        private void OnTrailingTimer(BrowserElementRef windowRoot, WatchEntry entry)
        {
            try
            {
                if (!IsStillWatched(windowRoot, entry))
                    return;

                switch (StructureChangedThrottle.TryConsumeTrailing(
                    ref entry.LastRaisedTicks, ref entry.TrailingPending, Environment.TickCount64, StructureChangedCoalesceMs, out int rearmDelayMs))
                {
                    case StructureChangedThrottle.TrailingResult.Raise:
                        if (IsStillWatched(windowRoot, entry))
                            RaiseWindowStructureChanged(windowRoot);
                        break;
                    case StructureChangedThrottle.TrailingResult.Rearm:
                        ArmTrailingTimer(windowRoot, entry, rearmDelayMs);
                        break;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                Debug.WriteLine("BrowserInterruptUtils: trailing StructureChanged timer failed: " + ex.Message);
            }
        }

        private void RaiseWindowOpened(BrowserWindowInfo info)
        {
            try
            {
                WindowOpened?.Invoke(info);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                Debug.WriteLine("BrowserInterruptUtils: WindowOpened subscriber failed: " + ex.Message);
            }
        }

        private void RaiseWindowStructureChanged(BrowserElementRef windowRoot)
        {
            try
            {
                WindowStructureChanged?.Invoke(windowRoot);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                Debug.WriteLine("BrowserInterruptUtils: WindowStructureChanged subscriber failed: " + ex.Message);
            }
        }

        private void ReportFault(string text)
        {
            try
            {
                _onFault?.Invoke(text);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                Debug.WriteLine("BrowserInterruptUtils: fault callback failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ Win32/UIA helpers

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

        private static string ClassNameOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>The name of the process with this ID, or <c>string.Empty</c> for an invalid ID or one that no longer exists (the process can exit between the callback firing and this lookup).</summary>
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
