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
        /// background thread, if someone else asks this holder to stop; it must end this holder's watch
        /// (and so call <see cref="Release"/>).
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
        private const int ReleaseJoinMs = 3000;

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
            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim ReleaseRequested = new ManualResetEventSlim(false);
            public Thread Thread;
            public volatile bool Acquired;
            public volatile bool Abandoned;
            public string Error;
        }

        public bool TryAcquire(Action stopRequested, out string message)
        {
            message = null;
            lock (_lock)
            {
                if (_hold != null)
                    return true; // already holding it

                var hold = new Hold();
                hold.Thread = new Thread(() => Run(hold, stopRequested))
                {
                    IsBackground = true,
                    Name = "InterruptUtils.InstanceGuard"
                };
                hold.Thread.Start();

                if (!hold.Ready.Wait(_acquireTimeoutMs + 2000))
                {
                    // Never got an answer: whatever the thread does later, it must not keep the guard.
                    hold.Abandoned = true;
                    hold.ReleaseRequested.Set();
                    message = TimedOutMessage();
                    return false;
                }
                if (!hold.Acquired)
                {
                    message = hold.Error ?? TimedOutMessage();
                    return false;
                }
                _hold = hold;
                return true;
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

            hold.ReleaseRequested.Set();
            // Release is also called from the holding thread itself (when asked to stop): it cannot wait for itself.
            if (!ReferenceEquals(Thread.CurrentThread, hold.Thread))
                hold.Thread.Join(ReleaseJoinMs);
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
