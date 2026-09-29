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
    /// gives a simple, cross-thread-safe, non-blocking shutdown primitive
    /// (<see cref="Dispatcher.BeginInvokeShutdown"/>) to pair with a bounded <see cref="Thread.Join(int)"/>,
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
            /// The whole coalescing-throttle state for this window as ONE word: last-raised
            /// <c>Environment.TickCount64</c> plus the trailing-pending bit, packed by
            /// <see cref="StructureChangedThrottle.Pack"/>. Seeded at registration time (see
            /// <see cref="WatchWindow"/>) via <see cref="StructureChangedThrottle.InitialState"/> -
            /// see <see cref="StructureChangedThrottle.InitialSeed"/> for why <see cref="long.MinValue"/>
            /// is unsafe (it permanently silenced <see cref="WindowStructureChanged"/>; a critical
            /// Task 4 code-review finding). One word, not two fields, so a timer callback can never
            /// act on a stale timestamp while clearing a pending bit set after it (a review finding:
            /// lost trailing). Read/written only via <see cref="Interlocked"/> inside
            /// <see cref="StructureChangedThrottle"/>.
            /// </summary>
            public long ThrottleState;

            /// <summary>Guards <see cref="TrailingTimer"/> and <see cref="TimerDisposed"/>; never held while calling out or while taking <c>_watchLock</c>.</summary>
            public readonly object TimerLock = new object();

            /// <summary>The lazily-created single-shot trailing timer; null until the first suppressed signal.</summary>
            public Timer TrailingTimer;

            /// <summary>Set (under <see cref="TimerLock"/>) once the entry is unwatched/stopped so a late arm cannot resurrect a timer.</summary>
            public bool TimerDisposed;
        }

        // Lock order (outermost first): _lifecycleLock -> _watchLock; WatchEntry.TimerLock is a
        // separate leaf never held together with _watchLock. _watchLock and TimerLock are held only
        // for short dictionary/flag operations: never across a UIA/COM call (Add/RemoveStructureChanged
        // EventHandler, FromHandle), a subscriber invocation or any other lock. Event callbacks
        // (UIA threads, timer threads) take only _watchLock or TimerLock, never _lifecycleLock, so
        // Start/Stop holding _lifecycleLock across COM calls and Thread.Join cannot block a callback.
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
                    // The thread returned without entering Dispatcher.Run() (PumpExpected is false),
                    // so nothing pumps its dispatcher: a shutdown request here could never be served
                    // (a synchronous InvokeShutdown would hang Start forever, under _lifecycleLock).
                    // It is already exiting on its own; just bound the wait.
                    RequestDispatcherShutdown(startup.Dispatcher, startup.PumpExpected);
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

            /// <summary>
            /// Set by the hook thread, before it signals <see cref="Ready"/>, only when setup and
            /// registration succeeded and it is about to enter <c>Dispatcher.Run()</c>. False means
            /// the thread is returning without ever pumping, so its dispatcher must never be asked
            /// to shut down (see <see cref="HookDispatcherShutdownPolicy"/>).
            /// </summary>
            public volatile bool PumpExpected;
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

                    // Have UIA deliver each event's source element with NativeWindowHandle already
                    // cached, so OnWindowOpenedRaw needs no cross-process read on UIA's serialized
                    // event thread (a slow provider would otherwise stall ALL event delivery, and
                    // every top-level window on the desktop reaches this callback). The cache
                    // request in effect at registration time is the one applied to the delivered
                    // elements (platform knowledge of the managed Automation client, not verified).
                    var cacheRequest = new CacheRequest();
                    cacheRequest.Add(AutomationElement.NativeWindowHandleProperty);
                    using (cacheRequest.Activate())
                    {
                        Automation.AddAutomationEventHandler(
                            WindowPattern.WindowOpenedEvent,
                            AutomationElement.RootElement,
                            TreeScope.Children,
                            startup.WindowOpenedHandler);
                    }
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

            startup.PumpExpected = !failed;

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

                // _dispatcher/_thread are only ever set by a successful Start, so the pump is expected.
                // The request is asynchronous and the join bounded: even a wedged pump cannot hang
                // Stop (the background thread is then abandoned, not waited for).
                RequestDispatcherShutdown(_dispatcher, true);
                _thread?.Join(StopTimeoutMs);

                _dispatcher = null;
                _thread = null;
                _onFault = null;
            }
        }

        /// <summary>
        /// Asks a pumping hook thread's dispatcher to shut down, WITHOUT waiting for it:
        /// <see cref="Dispatcher.BeginInvokeShutdown"/> is a fire-and-forget post (safe from any
        /// thread, and safe even if the thread has not reached <c>Dispatcher.Run()</c> yet: the
        /// request is queued and served when the pump starts), unlike <c>InvokeShutdown</c>, which
        /// blocks until the dispatcher's thread serves it. Callers pair it with a bounded
        /// <see cref="Thread.Join(int)"/>. Does nothing (see <see cref="HookDispatcherShutdownPolicy"/>)
        /// for a thread that never pumps. Never throws. (The InvokeShutdown-blocks and
        /// BeginInvokeShutdown-is-async claims are WPF platform knowledge, not verified here.)
        /// </summary>
        private static void RequestDispatcherShutdown(Dispatcher dispatcher, bool pumpExpected)
        {
            if (dispatcher == null)
                return;
            try
            {
                if (HookDispatcherShutdownPolicy.ShouldRequestShutdown(pumpExpected, dispatcher.HasShutdownStarted, dispatcher.HasShutdownFinished))
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
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
                    if (!_started)
                        return; // stopped: no registration after Stop (cheap early-out; re-checked at insert)
                }

                AutomationElement windowElement = ResolveFromHandle(windowRoot.Hwnd);
                if (windowElement == null)
                    return; // the window is gone or unresolvable; nothing to watch

                StructureChangedEventHandler handler = (sender, args) => OnStructureChangedRaw(windowRoot, args);

                var entry = new WatchEntry
                {
                    Element = windowElement,
                    Handler = handler,
                    // Seeded so the first real StructureChanged callback for this window always
                    // raises immediately; see the WatchEntry.ThrottleState doc comment.
                    ThrottleState = StructureChangedThrottle.InitialState(Environment.TickCount64, StructureChangedCoalesceMs)
                };

                // Register OUTSIDE _watchLock: a Subtree registration is a cross-process UIA call
                // that can take hundreds of ms on Chromium (which switches accessibility on), and
                // every StructureChanged callback and trailing timer takes _watchLock. Holding it
                // across this call would stall UIA's serialized event delivery for all watched
                // windows and WindowOpened, and risks a lock-order deadlock if the managed client
                // holds its own lock while dispatching to handlers (platform knowledge, not verified).
                // A callback delivered before the insert below finds no entry and is dropped. That is
                // the same exposure as a change that happened just before registration (the watch
                // only reports changes after it exists); catching up on content already there is the
                // engine's sweep's job, not this registry's.
                Automation.AddStructureChangedEventHandler(windowElement, TreeScope.Subtree, handler);

                bool inserted = false;
                lock (_watchLock)
                {
                    // Re-check under the lock: a racing WatchWindow for the same window may have
                    // inserted while this call was registering. Stop clears _started before it takes
                    // this lock to drain _watched, so an insert that sees _started true is always
                    // drained by Stop, and one that sees false never leaves a handler behind.
                    if (_started && !_watched.ContainsKey(windowRoot))
                    {
                        _watched[windowRoot] = entry;
                        inserted = true;
                    }
                }

                if (!inserted)
                {
                    // Lost the race (or Stop ran): undo this call's own registration, outside the lock.
                    // Only this call's delegate instance is removed, so the winner's is untouched.
                    DisposeTrailingTimer(entry);
                    try { Automation.RemoveStructureChangedEventHandler(windowElement, handler); }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }
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
                // Detached under the lock above; the COM removal and timer disposal happen outside
                // it. A callback already in flight finds no entry (or a disposed timer) and is harmless.
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
        /// The desktop-wide <c>WindowOpened</c> callback. Bridges to Win32-land through the
        /// <c>NativeWindowHandle</c> the registration's <see cref="CacheRequest"/> delivered with the
        /// event's element (<see cref="AutomationElement.Cached"/>, no cross-process call); only if
        /// that is unexpectedly not cached does it fall back to one guarded live
        /// <see cref="AutomationElement.Current"/> read. After that only Win32/.NET calls, no
        /// further UIA work. The engine calls back into the real <see cref="IBrowserPopupProbe"/>
        /// for any enrichment later, on its own worker thread.
        /// </summary>
        private void OnWindowOpenedRaw(object sender, AutomationEventArgs e)
        {
            try
            {
                if (!(sender is AutomationElement element))
                    return;

                if (!TryReadNativeWindowHandle(element, out int nativeHandle))
                    return; // gone, or unreadable: dropped quietly

                if (nativeHandle == 0)
                    return;
                IntPtr hwnd = new IntPtr(nativeHandle);
                if (!NativeMethods.IsWindow(hwnd))
                    return;
                if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
                    return; // not genuinely top-level

                // The calls below are in-process Win32/.NET calls (IsWindow, GetAncestor, GetClassName,
                // GetWindowThreadProcessId, Process.GetProcessById), not UIA cross-process reads, so
                // they are left inline. ProcessNameOf is the costliest; a process-name cache is a
                // later improvement.
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
        /// Reads the event element's native window handle: the cached value first (no cross-process
        /// call), then, only if the property was not cached (<see cref="InvalidOperationException"/>),
        /// one live <see cref="AutomationElement.Current"/> read. False when the element is gone or
        /// both reads fail. Never throws.
        /// </summary>
        private static bool TryReadNativeWindowHandle(AutomationElement element, out int nativeHandle)
        {
            nativeHandle = 0;
            try
            {
                nativeHandle = element.Cached.NativeWindowHandle;
                return true;
            }
            catch (InvalidOperationException)
            {
                // Not cached (the registration's cache request did not apply): fall through once.
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return false;
            }

            try
            {
                nativeHandle = element.Current.NativeWindowHandle;
                return true;
            }
            catch (ElementNotAvailableException)
            {
                return false; // gone before this callback got to read it
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return false;
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
                switch (StructureChangedThrottle.Decide(ref entry.ThrottleState, now, StructureChangedCoalesceMs, out int armDelayMs))
                {
                    case StructureChangedThrottle.Decision.Raise:
                        RaiseWindowStructureChanged(windowRoot);
                        break;
                    case StructureChangedThrottle.Decision.SuppressedArmTrailing:
                        // First suppressed signal since the last raise (the 0-to-1 pending transition,
                        // so exactly one arm per pending episode): one trailing delivery when the
                        // cooldown ends. The delay comes from the same state snapshot that set the
                        // pending bit. If a leading raise clears the bit before this timer fires, that
                        // raise covers the signal and the timer finds nothing pending; a signal
                        // suppressed after that raise sets the bit again and arms its own timer.
                        ArmTrailingTimer(windowRoot, entry, armDelayMs);
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
                    ref entry.ThrottleState, Environment.TickCount64, StructureChangedCoalesceMs, out int rearmDelayMs))
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
