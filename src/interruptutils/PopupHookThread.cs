using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace InterruptAutomation
{
    /// <summary>
    /// Runs a dedicated background thread with its own message pump and a set of
    /// out-of-context WinEvent hooks, and reports the handle of each top-level window that
    /// is created, shown, or comes to the front, and of each window that is destroyed. The
    /// callback does almost nothing (a class check and a hand-off) because a stalled pump
    /// stalls every WinEvent delivered to it; the clicking happens on the worker thread. A
    /// fresh thread is created for each <see cref="Start"/> and ended by <see cref="Stop"/>.
    /// </summary>
    internal sealed class PopupHookThread : IPopupHookSource
    {
        private static readonly uint[] HookedEvents =
        {
            NativeMethods.EVENT_OBJECT_CREATE,
            NativeMethods.EVENT_OBJECT_SHOW,
            NativeMethods.EVENT_OBJECT_DESTROY,
            NativeMethods.EVENT_SYSTEM_DIALOGSTART,
            NativeMethods.EVENT_SYSTEM_FOREGROUND
        };

        private const int StartTimeoutMs = 5000;
        private const int EndTimeoutMs = 2000;

        /// <summary>
        /// Everything one run of the hook thread owns. The thread's closure keeps it, and so the
        /// callback delegate it holds, alive until the thread has unhooked and exited, however
        /// long that takes: USER32 may call the delegate at any moment while a hook is installed,
        /// so it must never be collectable before then, even if <see cref="Stop"/> gave up waiting.
        /// </summary>
        private sealed class HookRun
        {
            private readonly Action<IntPtr> _onWindow;
            private readonly Action<IntPtr> _onDestroyed;
            private int _threadId;

            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            public readonly NativeMethods.WinEventProc Callback;
            public readonly Action<string> OnFault;
            public Thread Thread;

            /// <summary>Set when the run is no longer wanted; the thread ends at its next check (or on WM_QUIT).</summary>
            public volatile bool Abandon;

            /// <summary>Why the hooks could not be installed, if they could not; read once <see cref="Ready"/> is set.</summary>
            public volatile string StartError;

            public HookRun(Action<IntPtr> onWindow, Action<IntPtr> onDestroyed, Action<string> onFault)
            {
                _onWindow = onWindow;
                _onDestroyed = onDestroyed;
                OnFault = onFault;
                Callback = OnWinEvent;
            }

            public uint ThreadId
            {
                get => (uint)Volatile.Read(ref _threadId);
                set => Volatile.Write(ref _threadId, (int)value);
            }

            private void OnWinEvent(IntPtr hHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
            {
                try
                {
                    if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
                        return;

                    if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
                    {
                        // Every destroyed window is reported (the worker ignores handles it does not
                        // track): whether it was top-level cannot be asked of a window being destroyed.
                        _onDestroyed?.Invoke(hwnd);
                        return;
                    }

                    // Only top-level windows are popups; controls inside them raise the same events.
                    if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
                        return;
                    _onWindow?.Invoke(hwnd);
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    // The callback must never throw into the message pump.
                    Debug.WriteLine("InterruptUtils: WinEvent callback failed: " + ex.Message);
                }
            }
        }

        private readonly object _lock = new object();
        private HookRun _run;

        public bool Start(Action<IntPtr> onWindow, Action<IntPtr> onWindowDestroyed, Action<string> onFault, out string message)
        {
            message = null;
            lock (_lock)
            {
                if (_run != null)
                {
                    message = "The hook is already running.";
                    return false;
                }

                var run = new HookRun(onWindow, onWindowDestroyed, onFault);
                run.Thread = new Thread(() => Run(run))
                {
                    IsBackground = true,
                    Name = "InterruptUtils.WinEventHook"
                };
                run.Thread.Start();

                if (!run.Ready.Wait(StartTimeoutMs))
                {
                    message = "The WinEvent hook thread did not start in time.";
                    End(run); // it may still come up: it is told to end, and does so at its next check
                    return false;
                }

                if (run.StartError != null)
                {
                    message = run.StartError;
                    run.Thread.Join(EndTimeoutMs); // it is already on its way out
                    return false;
                }

                _run = run;
                return true;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                HookRun run = _run;
                _run = null;
                if (run != null)
                    End(run);
            }
        }

        /// <summary>
        /// Asks the run's thread to end and waits a bounded time for it. Nothing is torn down here:
        /// if the thread is slow to end (or has not yet got as far as installing its hooks) it ends
        /// itself, and until it has, the run object keeps its callback delegate alive.
        /// </summary>
        private static void End(HookRun run)
        {
            // Flag first, then read the thread id: the thread publishes its id and then reads the
            // flag, so at least one side sees the other and the thread cannot be missed.
            run.Abandon = true;
            uint threadId = run.ThreadId;
            if (threadId != 0)
                NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            run.Thread.Join(EndTimeoutMs);
        }

        private static void Run(HookRun run)
        {
            var hooks = new IntPtr[HookedEvents.Length];
            bool signaled = false;
            try
            {
                // Create this thread's message queue before anyone can post to it: a thread that
                // has not yet called a USER function has no queue, and a WM_QUIT posted to it
                // would be lost.
                NativeMethods.PeekMessage(out _, IntPtr.Zero, NativeMethods.WM_USER, NativeMethods.WM_USER, NativeMethods.PM_NOREMOVE);
                run.ThreadId = NativeMethods.GetCurrentThreadId();
                if (run.Abandon)
                    return; // stopped before it got going

                for (int i = 0; i < HookedEvents.Length; i++)
                {
                    hooks[i] = NativeMethods.SetWinEventHook(HookedEvents[i], HookedEvents[i], IntPtr.Zero, run.Callback, 0, 0,
                        NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
                    if (hooks[i] == IntPtr.Zero)
                    {
                        run.StartError = "SetWinEventHook failed (Win32 error " + Marshal.GetLastWin32Error() + "). "
                            + "Window events are not available in this session.";
                        return;
                    }
                    if (run.Abandon)
                        return;
                }

                run.Ready.Set();
                signaled = true;

                int result;
                while ((result = NativeMethods.GetMessage(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0)) > 0)
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }

                // 0 is WM_QUIT, the normal end (Stop). -1 is an error: the pump is gone, so window
                // events have stopped even though Start succeeded. Say so rather than going quiet.
                if (result < 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    Report(run.OnFault, "The window event pump failed (Win32 error " + error + "); popups are no longer "
                        + "noticed through window events (the periodic scan, if on, still runs).");
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                string text = NeverThrowsGuard.Failure("Window event hook", ex);
                if (signaled)
                    Report(run.OnFault, text);
                else
                    run.StartError = text;
            }
            finally
            {
                foreach (IntPtr hook in hooks)
                {
                    if (hook != IntPtr.Zero)
                        NativeMethods.UnhookWinEvent(hook);
                }
                // Every early exit still has to release Start, which is waiting for this signal;
                // once it has been given it is never given again. (The event is not disposed: it
                // never waits on an OS handle, so it holds nothing that needs releasing.)
                if (!signaled)
                    run.Ready.Set();
            }
        }

        private static void Report(Action<string> onFault, string text)
        {
            try
            {
                onFault?.Invoke(text);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                Debug.WriteLine("InterruptUtils: fault callback failed: " + ex.Message);
            }
        }
    }
}
