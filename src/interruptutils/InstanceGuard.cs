using System;
using System.Diagnostics;
using System.Threading;

namespace InterruptAutomation
{
    /// <summary>
    /// Lets only one <see cref="InterruptUtils"/> watch at a time, across every process in the
    /// user's session. Whoever wants to watch takes the guard; a current holder is asked to stop
    /// first.
    /// </summary>
    internal interface IInstanceGuard
    {
        /// <summary>
        /// Takes the right to watch, asking a current holder (in this or another process) to stop and
        /// waiting a bounded time for it to. <paramref name="stopRequested"/> is called, on a
        /// background thread, if someone else asks this holder to stop; it must end this holder's watch.
        /// <see cref="Release"/> is called once the watch has fully ended, which can be later than the
        /// callback returning (a worker may still be finishing a click); the guard stays held until then.
        /// </summary>
        /// <returns><c>true</c> if the right was taken; <c>false</c> (with a message) if the holder did not stop in time.</returns>
        bool TryAcquire(Action stopRequested, out string message);

        /// <summary>Gives the right back. Safe to call when not held, and from <c>stopRequested</c>.</summary>
        void Release();
    }

    /// <summary>A guard that never blocks anyone: used where there is only one instance by construction.</summary>
    internal sealed class NoInstanceGuard : IInstanceGuard
    {
        public bool TryAcquire(Action stopRequested, out string message)
        {
            message = null;
            return true;
        }

        public void Release()
        {
        }
    }

    /// <summary>
    /// The real guard: a named mutex says who is watching, and a named event is how a newcomer asks
    /// the holder to stop. Both live in the session's <c>Local\</c> namespace, because window events
    /// and popups belong to one desktop session. A mutex must be released by the thread that took it,
    /// so a small dedicated thread holds it for the whole run; if the holding process dies the mutex is
    /// abandoned, which the next taker treats as free.
    /// </summary>
    internal sealed class NamedInstanceGuard : IInstanceGuard
    {
        public const string DefaultName = "Local\\InterruptAutomation.Watcher";
        public const int DefaultAcquireTimeoutMs = 5000;

        private const int ResignalMs = 250;

        private readonly string _mutexName;
        private readonly string _requestName;
        private readonly int _acquireTimeoutMs;
        private readonly object _lock = new object();
        private Hold _hold;

        public NamedInstanceGuard(string name = DefaultName, int acquireTimeoutMs = DefaultAcquireTimeoutMs)
        {
            _mutexName = name + ".Mutex";
            _requestName = name + ".StopRequest";
            _acquireTimeoutMs = acquireTimeoutMs;
        }

        private sealed class Hold
        {
            // Ready never has its wait handle asked for, so it holds no native handle and is left to the GC
            // (disposing it could race a caller that is still about to wait on it). ReleaseRequested does
            // hand out a wait handle, so the guard thread disposes it when it exits.
            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim ReleaseRequested = new ManualResetEventSlim(false);
            public Thread Thread;
            public volatile bool Acquired;
            public volatile bool Abandoned;
            public string Error;

            /// <summary>Asks the guard thread to give up or let go. Safe after the thread has finished and cleaned up.</summary>
            public void SignalRelease()
            {
                try
                {
                    ReleaseRequested.Set();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        public bool TryAcquire(Action stopRequested, out string message)
        {
            message = null;
            lock (_lock)
            {
                if (_hold != null)
                    return true; // already holding it

                var hold = new Hold();
                bool succeeded = false;
                try
                {
                    hold.Thread = new Thread(() => Run(hold, stopRequested))
                    {
                        IsBackground = true,
                        Name = "InterruptUtils.InstanceGuard"
                    };
                    hold.Thread.Start();

                    if (!hold.Ready.Wait(_acquireTimeoutMs + 2000))
                    {
                        message = TimedOutMessage(); // never got an answer
                        return false;
                    }
                    if (!hold.Acquired)
                    {
                        message = hold.Error ?? TimedOutMessage();
                        return false;
                    }
                    _hold = hold;
                    succeeded = true;
                    return true;
                }
                finally
                {
                    // Any way out that is not "acquired and recorded" (a timeout, a refusal, or an exception
                    // such as the caller being interrupted while it waits) must not leave the hold's thread
                    // to acquire the mutex later and keep it forever: tell it to give up or let go.
                    if (!succeeded)
                    {
                        hold.Abandoned = true;
                        hold.SignalRelease();
                    }
                }
            }
        }

        public void Release()
        {
            Hold hold;
            lock (_lock)
            {
                hold = _hold;
                _hold = null;
            }
            if (hold == null)
                return;

            // Not waited for: the holding thread gives the mutex up as soon as it sees this, and anyone
            // waiting for it is woken then. Waiting here could deadlock, because Release is also called
            // from the worker that the holding thread's own stop is waiting to finish.
            hold.SignalRelease();
        }

        private string TimedOutMessage() =>
            "Another InterruptUtils (in this process or another) is watching and did not stop within "
            + (_acquireTimeoutMs / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " seconds.";

        private void Run(Hold hold, Action stopRequested)
        {
            Mutex mutex = null;
            EventWaitHandle request = null;
            bool owned = false;
            try
            {
                try
                {
                    request = new EventWaitHandle(false, EventResetMode.ManualReset, _requestName);
                    mutex = new Mutex(false, _mutexName);
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    // The named objects cannot be created or opened (for example another user or a
                    // higher-integrity process owns them): watching without the guard beats not
                    // watching at all.
                    Debug.WriteLine("InterruptUtils: instance guard unavailable: " + ex.Message);
                    hold.Acquired = true;
                    return;
                }

                long deadline = Environment.TickCount64 + _acquireTimeoutMs;
                owned = Take(mutex, 0);
                while (!owned)
                {
                    if (hold.Abandoned || Environment.TickCount64 >= deadline)
                        return;
                    // Ask again every time round: a request can be swallowed by a holder that is
                    // still starting up, or reset by another newcomer that got in first.
                    request.Set();
                    owned = Take(mutex, ResignalMs);
                }

                // Any request left over from before is out of date now that this instance holds the guard.
                request.Reset();
                if (hold.Abandoned)
                    return;
                hold.Acquired = true;
                hold.Ready.Set();

                int signalled = WaitHandle.WaitAny(new[] { hold.ReleaseRequested.WaitHandle, request });
                if (signalled == 1)
                {
                    try
                    {
                        stopRequested?.Invoke();
                    }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                    {
                        Debug.WriteLine("InterruptUtils: stopping on request failed: " + ex.Message);
                    }
                    // Keep the mutex until the holder says it has really finished (Release): stopping
                    // can return while a worker is still mid-click, and that worker must not overlap a
                    // newcomer. The newcomer's wait times out if that takes too long.
                    hold.ReleaseRequested.Wait();
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                Debug.WriteLine("InterruptUtils: instance guard failed: " + ex.Message);
                if (!hold.Acquired)
                    hold.Error = "The instance guard failed: " + ex.Message;
            }
            finally
            {
                if (owned)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                    {
                        Debug.WriteLine("InterruptUtils: releasing the instance guard failed: " + ex.Message);
                    }
                }
                mutex?.Dispose();
                request?.Dispose();
                hold.Ready.Set(); // every exit lets TryAcquire go on
                hold.ReleaseRequested.Dispose(); // the last use of it on this thread; late signals are tolerated
            }
        }

        private static bool Take(Mutex mutex, int timeoutMs)
        {
            try
            {
                return mutex.WaitOne(timeoutMs);
            }
            catch (AbandonedMutexException)
            {
                return true; // the previous holder's process ended without releasing: it is free
            }
        }
    }
}
