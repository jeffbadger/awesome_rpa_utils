using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EventAutomation
{
    /// <summary>
    /// Per-subscription bounded event queues with overflow policy, plus the
    /// registry of pending WaitForX matchers. Both are fed from the hook thread
    /// and drained from robot threads, so everything here is thread-safe.
    /// </summary>
    internal sealed class SubscriptionManager
    {
        private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new ConcurrentDictionary<string, Subscription>(StringComparer.OrdinalIgnoreCase);
        private int _defaultMaxEvents = 1000;
        private string _defaultOverflowPolicy = "DropOldest";

        public bool TryAdd(string id, HashSet<EventCategory> categories, EventFilter filter, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                message = "Subscription id cannot be empty.";
                return false;
            }
            var sub = new Subscription
            {
                Id = id,
                Categories = categories,
                Filter = filter,
                MaxEvents = _defaultMaxEvents,
                OverflowPolicy = _defaultOverflowPolicy
            };
            if (!_subscriptions.TryAdd(id, sub))
            {
                message = "A subscription with id '" + id + "' already exists.";
                return false;
            }
            return true;
        }

        public bool TryRemove(string id)
        {
            return _subscriptions.TryRemove(id, out _);
        }

        public void SetQueueLimits(int maxEvents, string overflowPolicy)
        {
            if (maxEvents > 0)
                _defaultMaxEvents = maxEvents;
            if (overflowPolicy == "DropOldest" || overflowPolicy == "DropNewest" || overflowPolicy == "Block")
                _defaultOverflowPolicy = overflowPolicy;
            foreach (var sub in _subscriptions.Values)
            {
                if (maxEvents > 0)
                    sub.MaxEvents = maxEvents;
                if (overflowPolicy == "DropOldest" || overflowPolicy == "DropNewest" || overflowPolicy == "Block")
                    sub.OverflowPolicy = overflowPolicy;
            }
        }

        public void ClearAll()
        {
            foreach (var sub in _subscriptions.Values)
                ClearQueue(sub.Id);
            _subscriptions.Clear();
        }

        /// <summary>Fans an event out to every subscription whose categories and filter match.</summary>
        public void Deliver(EventData data, List<EventCategory> cats, uint hostPid)
        {
            foreach (var sub in _subscriptions.Values)
            {
                if (!CategoriesOverlap(sub.Categories, cats))
                    continue;
                if (!sub.Filter.Matches(data, hostPid))
                    continue;
                Enqueue(sub, data);
            }
        }

        public EventData GetNextEvent(string id, int timeoutMs, out bool hasEvent)
        {
            hasEvent = false;
            if (!_subscriptions.TryGetValue(id, out var sub))
                return null;
            long deadline = Environment.TickCount64 + Math.Max(0, timeoutMs);
            lock (sub.Gate)
            {
                while (sub.Queue.IsEmpty)
                {
                    long remaining = deadline - Environment.TickCount64;
                    if (remaining <= 0)
                        return null;
                    Monitor.Wait(sub.Gate, (int)Math.Min(remaining, int.MaxValue));
                }
                if (sub.Queue.TryDequeue(out var e))
                {
                    Interlocked.Decrement(ref sub.Count);
                    hasEvent = true;
                    return e;
                }
            }
            return null;
        }

        public EventData[] GetNextEvents(string id, int maxCount, int drainMs)
        {
            if (!_subscriptions.TryGetValue(id, out var sub))
                return Array.Empty<EventData>();
            var result = new List<EventData>();
            long deadline = Environment.TickCount64 + Math.Max(0, drainMs);
            while (result.Count < maxCount)
            {
                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                    break;
                if (GetNextEvent(id, (int)Math.Min(remaining, int.MaxValue), out bool hasEvent) is EventData e)
                    result.Add(e);
                else
                    break;
            }
            return result.ToArray();
        }

        public bool HasEvents(string id, out int count)
        {
            count = 0;
            if (!_subscriptions.TryGetValue(id, out var sub))
                return false;
            count = sub.Count;
            return true;
        }

        public void ClearQueue(string id)
        {
            if (!_subscriptions.TryGetValue(id, out var sub))
                return;
            lock (sub.Gate)
            {
                while (sub.Queue.TryDequeue(out _))
                {
                }
                sub.Count = 0;
            }
        }

        private static bool CategoriesOverlap(HashSet<EventCategory> subCats, List<EventCategory> eventCats)
        {
            foreach (var c in eventCats)
                if (subCats.Contains(c))
                    return true;
            return false;
        }

        private static void Enqueue(Subscription sub, EventData e)
        {
            lock (sub.Gate)
            {
                if (sub.Count >= sub.MaxEvents)
                {
                    if (sub.OverflowPolicy == "DropNewest" || sub.OverflowPolicy == "Block")
                        return;
                    // DropOldest
                    sub.Queue.TryDequeue(out _);
                    Interlocked.Decrement(ref sub.Count);
                }
                sub.Queue.Enqueue(e);
                Interlocked.Increment(ref sub.Count);
                Monitor.Pulse(sub.Gate);
            }
        }

        private sealed class Subscription
        {
            public string Id;
            public HashSet<EventCategory> Categories;
            public EventFilter Filter;
            public ConcurrentQueue<EventData> Queue = new ConcurrentQueue<EventData>();
            public int MaxEvents;
            public string OverflowPolicy;
            public int Count;
            public readonly object Gate = new object();
        }
    }

    /// <summary>
    /// Pending WaitForX matchers. Each waiter is a predicate on <see cref="EventData"/>
    /// completed by a <see cref="TaskCompletionSource{TResult}"/>; a timeout via
    /// <see cref="CancellationTokenSource"/> completes it with null. Matching runs on
    /// the hook thread and must be fast.
    /// </summary>
    internal sealed class WaiterRegistry
    {
        private readonly object _gate = new object();
        private readonly List<Waiter> _waiters = new List<Waiter>();

        /// <summary>Registers a waiter and returns a task that completes with the matching event or null on timeout.</summary>
        public Task<EventData> Register(Func<EventData, bool> predicate, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<EventData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cts = new CancellationTokenSource();
            var waiter = new Waiter { Predicate = predicate, Tcs = tcs, Cts = cts };
            lock (_gate)
                _waiters.Add(waiter);
            if (timeoutMs > 0)
            {
                cts.CancelAfter(timeoutMs);
                cts.Token.Register(() =>
                {
                    lock (_gate)
                        _waiters.Remove(waiter);
                    tcs.TrySetResult(null);
                });
            }
            return tcs.Task;
        }

        /// <summary>Completes every waiter whose predicate matches (hook thread).</summary>
        public void Match(EventData data)
        {
            Waiter[] snapshot;
            lock (_gate)
                snapshot = _waiters.ToArray();
            foreach (var w in snapshot)
            {
                bool matched = false;
                try
                {
                    matched = w.Predicate(data);
                }
                catch
                {
                    // A bad predicate must never break the hook thread.
                }
                if (matched)
                {
                    lock (_gate)
                        _waiters.Remove(w);
                    // Set the result BEFORE cancelling: the timeout registration's
                    // TrySetResult(null) is then a no-op (TrySetResult is idempotent).
                    w.Tcs.TrySetResult(data);
                    w.Cts.Cancel();
                }
            }
        }

        /// <summary>Completes all pending waiters with null (CancelWaits / Dispose).</summary>
        public void CancelAll()
        {
            Waiter[] snapshot;
            lock (_gate)
            {
                snapshot = _waiters.ToArray();
                _waiters.Clear();
            }
            foreach (var w in snapshot)
            {
                w.Cts.Cancel();
                w.Tcs.TrySetResult(null);
            }
        }

        private sealed class Waiter
        {
            public Func<EventData, bool> Predicate;
            public TaskCompletionSource<EventData> Tcs;
            public CancellationTokenSource Cts;
        }
    }
}
