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
    /// fresh thread is created for each <see cref="Start"/> and ended by <see cref="Stop"/>,
    /// so nothing is shared between runs.
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

        private readonly object _lock = new object();
        private Thread _thread;
        private uint _threadId;
        private Action<IntPtr> _onWindow;
        private Action<IntPtr> _onDestroyed;
        private Action<string> _onFault;
        private NativeMethods.WinEventProc _callback; // kept alive for as long as the hooks are installed

        public bool Start(Action<IntPtr> onWindow, Action<IntPtr> onWindowDestroyed, Action<string> onFault, out string message)
        {
            message = null;
            lock (_lock)
            {
                if (_thread != null)
                {
                    message = "The hook is already running.";
                    return false;
                }

                string startError = null;
                uint threadId = 0;
                // Not disposed: the hook thread may still touch it after Start has returned (the
                // start timeout below), and a ManualResetEventSlim that never waits on its handle
                // holds no unmanaged resource that needs disposing.
                var ready = new ManualResetEventSlim(false);

                _onWindow = onWindow;
                _onDestroyed = onWindowDestroyed;
                _onFault = onFault;
                _callback = OnWinEvent;
                var thread = new Thread(() => Run(ready, id => threadId = id, error => startError = error))
                {
                    IsBackground = true,
                    Name = "InterruptUtils.WinEventHook"
                };
                thread.Start();

                if (!ready.Wait(5000))
                {
                    message = "The WinEvent hook thread did not start in time.";
                    // Best effort: it may still come up, so ask it to end once it does.
                    _thread = thread;
                    _threadId = threadId;
                    StopCore();
                    return false;
                }

                if (startError != null)
                {
                    message = startError;
                    thread.Join(2000);
                    ClearCallbacks();
                    return false;
                }

                _thread = thread;
                _threadId = threadId;
                return true;
            }
        }

        public void Stop()
        {
            lock (_lock)
                StopCore();
        }

        private void StopCore()
        {
            Thread thread = _thread;
            uint threadId = _threadId;
            _thread = null;
            _threadId = 0;
            if (thread == null)
                return;

            if (threadId != 0)
                NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            thread.Join(2000);
            ClearCallbacks();
        }

        private void ClearCallbacks()
        {
            _onWindow = null;
            _onDestroyed = null;
            _onFault = null;
            _callback = null;
        }

        private void Run(ManualResetEventSlim ready, Action<uint> publishThreadId, Action<string> publishError)
        {
            var hooks = new IntPtr[HookedEvents.Length];
            bool signaled = false;
            Action<string> onFault = _onFault;
            try
            {
                // Create this thread's message queue before anyone can post to it: a thread that
                // has not yet called a USER function has no queue, and a WM_QUIT posted to it
                // would be lost.
                NativeMethods.PeekMessage(out _, IntPtr.Zero, NativeMethods.WM_USER, NativeMethods.WM_USER, NativeMethods.PM_NOREMOVE);
                publishThreadId(NativeMethods.GetCurrentThreadId());

                for (int i = 0; i < HookedEvents.Length; i++)
                {
                    hooks[i] = NativeMethods.SetWinEventHook(HookedEvents[i], HookedEvents[i], IntPtr.Zero, _callback, 0, 0,
                        NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
                    if (hooks[i] == IntPtr.Zero)
                    {
                        publishError("SetWinEventHook failed (Win32 error " + Marshal.GetLastWin32Error() + "). "
                            + "Window events are not available in this session.");
                        return;
                    }
                }

                ready.Set();
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
                    Report(onFault, "The window event pump failed (Win32 error " + error + "); popups are no longer "
                        + "noticed through window events (the periodic scan, if on, still runs).");
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                string text = NeverThrowsGuard.Failure("Window event hook", ex);
                if (signaled)
                    Report(onFault, text);
                else
                    publishError(text);
            }
            finally
            {
                foreach (IntPtr hook in hooks)
                {
                    if (hook != IntPtr.Zero)
                        NativeMethods.UnhookWinEvent(hook);
                }
                // Every early exit still has to release Start, which is waiting for this signal;
                // once it has been given it is never given again.
                if (!signaled)
                {
                    try { ready.Set(); }
                    catch (ObjectDisposedException) { }
                }
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
}
