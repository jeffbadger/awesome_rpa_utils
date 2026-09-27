using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ResourceLockAutomation
{
    internal enum LockKind { Lock, Slot }

    /// <summary>A lease owner that can be closed (a disposed component). Checked inside the lock table's lock, so an acquire can never add a
    /// lease to an owner whose disposal has already released everything it held.</summary>
    internal interface ILeaseOwner
    {
        bool IsClosed { get; }
    }

    /// <summary>The outcome of an acquire attempt: acquired with a token, or who and how many hold it.</summary>
    internal readonly struct AcquireResult
    {
        internal AcquireResult(bool acquired, string token, string currentHolder, int holderCount, string problem)
        {
            Acquired = acquired; Token = token; CurrentHolder = currentHolder; HolderCount = holderCount; Problem = problem;
        }

        internal bool Acquired { get; }
        internal string Token { get; }
        internal string CurrentHolder { get; }    // the holder when a lock was not acquired (the first holder for a full slot pool)
        internal int HolderCount { get; }         // holders after the attempt
        internal string Problem { get; }          // the call cannot be done (capacity mismatch, lock/slot mix, too many held); null otherwise
    }

    /// <summary>
    /// The Process scope: every lock and slot held in this Robot Runtime, shared by all component instances. A lease belongs to its token, not
    /// to a thread, so any thread can renew or release it. Time is measured with a monotonic clock, so changing the system clock neither expires
    /// nor extends a lease. Expired leases are removed lazily, by whichever call looks at the resource next. A release wakes waiting acquirers at
    /// once; a waiter also wakes when the earliest lease it is waiting on would expire.
    /// </summary>
    internal static class ProcessLocks
    {
        private sealed class Lease
        {
            internal string Token;
            internal string Holder;
            internal long ExpiresAtMs;             // monotonic milliseconds
            internal object Owner;                 // the component instance that acquired it (released when that instance is disposed)
        }

        private sealed class Entry
        {
            internal string Resource;              // as first spelled
            internal LockKind Kind;
            internal int Capacity;
            internal readonly List<Lease> Leases = new List<Lease>();
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<object, int> OwnerCounts = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);   // leases per owner
        private static long lastSweepMs = NowMs;

        /// <summary>How often every expired lease is dropped, so resources that are never visited again do not accumulate.</summary>
        internal const long SweepIntervalMs = 60000;

        internal static long NowMs => Environment.TickCount64;

        /// <summary>Removes the leases of an entry that match, keeping the per-owner counts, and the entry itself once it is empty.</summary>
        private static int Drop(Entry entry, Predicate<Lease> which)
        {
            int removed = 0;
            for (int i = entry.Leases.Count - 1; i >= 0; i--)
            {
                Lease lease = entry.Leases[i];
                if (!which(lease)) continue;
                entry.Leases.RemoveAt(i);
                removed++;
                if (OwnerCounts.TryGetValue(lease.Owner, out int count)) { if (count <= 1) OwnerCounts.Remove(lease.Owner); else OwnerCounts[lease.Owner] = count - 1; }
            }
            if (entry.Leases.Count == 0) Entries.Remove(entry.Resource);
            return removed;
        }

        /// <summary>The live leases of a resource, after dropping expired ones; null when nothing holds it (the entry is then removed).</summary>
        private static Entry Live(string resource, long now)
        {
            if (!Entries.TryGetValue(resource, out Entry entry)) return null;
            Drop(entry, l => l.ExpiresAtMs <= now);
            return entry.Leases.Count > 0 ? entry : null;
        }

        /// <summary>Drops every expired lease in the table.</summary>
        private static void Sweep(long now)
        {
            foreach (string resource in Entries.Keys.ToList()) Live(resource, now);
            lastSweepMs = now;
        }

        internal static int EntryCount { get { lock (Sync) return Entries.Count; } }

        internal static int HeldBy(object owner) { lock (Sync) return OwnerCounts.TryGetValue(owner, out int count) ? count : 0; }

        private static bool IsClosed(object owner) => owner is ILeaseOwner o && o.IsClosed;

        private static AcquireResult TryAcquireLocked(string resource, LockKind kind, int capacity, string holder, long leaseMs, object owner, long now)
        {
            if (IsClosed(owner)) return new AcquireResult(false, null, null, 0, "the component has been disposed");
            if (now - lastSweepMs >= SweepIntervalMs) Sweep(now);
            Entry entry = Live(resource, now);
            if (entry != null && entry.Kind != kind)
                return new AcquireResult(false, null, null, 0, kind == LockKind.Lock
                    ? "the resource is in use as a slot pool; use TryAcquireSlot or AcquireSlot, or another resource name"
                    : "the resource is in use as a lock; use TryAcquireLock or AcquireLock, or another resource name");
            if (entry != null && entry.Capacity != capacity)
                return new AcquireResult(false, null, null, 0, "the resource is in use with a capacity of " + entry.Capacity + "; every caller must give the same capacity");
            if (entry != null && entry.Leases.Count >= capacity)
                return new AcquireResult(false, null, entry.Leases[0].Holder, entry.Leases.Count, null);
            OwnerCounts.TryGetValue(owner, out int held);
            if (held >= LockLimits.MaxHeldPerInstance)
            {
                Sweep(now);                                                         // the count includes leases that expired unvisited
                entry = Live(resource, now);
                OwnerCounts.TryGetValue(owner, out held);
                if (held >= LockLimits.MaxHeldPerInstance)
                    return new AcquireResult(false, null, null, 0, "this component already holds " + LockLimits.MaxHeldPerInstance + " locks and slots; release some first");
            }
            if (entry == null) Entries[resource] = entry = new Entry { Resource = resource, Kind = kind, Capacity = capacity };
            var lease = new Lease { Token = Guid.NewGuid().ToString("N"), Holder = holder, ExpiresAtMs = now + leaseMs, Owner = owner };
            entry.Leases.Add(lease);
            OwnerCounts[owner] = held + 1;
            return new AcquireResult(true, lease.Token, null, entry.Leases.Count, null);
        }

        /// <summary>Takes a lock (capacity 1) or a slot now, or reports who holds it.</summary>
        internal static AcquireResult TryAcquire(string resource, LockKind kind, int capacity, string holder, long leaseMs, object owner)
        {
            lock (Sync) return TryAcquireLocked(resource, kind, capacity, holder, leaseMs, owner, NowMs);
        }

        /// <summary>Waits up to waitMs for a lock or slot. A release wakes the waiter at once; so does the earliest expiry of a lease it waits on.</summary>
        internal static AcquireResult Acquire(string resource, LockKind kind, int capacity, string holder, long leaseMs, long waitMs, object owner)
        {
            long deadline = NowMs + waitMs;
            lock (Sync)
            {
                while (true)
                {
                    long now = NowMs;
                    AcquireResult result = TryAcquireLocked(resource, kind, capacity, holder, leaseMs, owner, now);
                    if (result.Acquired || result.Problem != null || now >= deadline) return result;
                    long untilExpiry = Entries.TryGetValue(resource, out Entry entry) && entry.Leases.Count > 0 ? entry.Leases.Min(l => l.ExpiresAtMs) - now : long.MaxValue;
                    long sleep = Math.Max(1, Math.Min(deadline - now, untilExpiry));
                    Monitor.Wait(Sync, TimeSpan.FromMilliseconds(Math.Min(sleep, int.MaxValue)));
                }
            }
        }

        /// <summary>Extends a live lease; False (not renewed) when it expired, was released or was forced.</summary>
        internal static bool Renew(string resource, string token, long leaseMs, out long expiresInMs)
        {
            expiresInMs = 0;
            lock (Sync)
            {
                long now = NowMs;
                Lease lease = Live(resource, now)?.Leases.FirstOrDefault(l => string.Equals(l.Token, token, StringComparison.OrdinalIgnoreCase));
                if (lease == null) return false;
                lease.ExpiresAtMs = now + leaseMs;
                expiresInMs = leaseMs;
                return true;
            }
        }

        /// <summary>Releases a live lease; False (not released) when it had already expired, been released or been forced.</summary>
        internal static bool Release(string resource, string token)
        {
            lock (Sync)
            {
                Entry entry = Live(resource, NowMs);
                int removed = entry == null ? 0 : Drop(entry, l => string.Equals(l.Token, token, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) Monitor.PulseAll(Sync);
                return removed > 0;
            }
        }

        /// <summary>Removes every lease of a resource; returns how many holders lost theirs.</summary>
        internal static int ForceRelease(string resource)
        {
            lock (Sync)
            {
                Entry entry = Live(resource, NowMs);
                if (entry == null) return 0;
                int count = Drop(entry, l => true);
                Monitor.PulseAll(Sync);
                return count;
            }
        }

        /// <summary>
        /// Releases every lease an owner still holds (it is being disposed; it is already closed, so no acquire can add another), and wakes every
        /// waiter: the owner's own waiters then see it is closed and return.
        /// </summary>
        internal static void ReleaseAllOwnedBy(object owner)
        {
            lock (Sync)
            {
                if (OwnerCounts.ContainsKey(owner))
                    foreach (string resource in Entries.Keys.ToList())
                        if (Entries.TryGetValue(resource, out Entry entry)) Drop(entry, l => l.Owner == owner);
                Monitor.PulseAll(Sync);
            }
        }

        internal static void Status(string resource, out bool held, out string holders, out int expiresInSeconds, out int holderCount)
        {
            lock (Sync)
            {
                long now = NowMs;
                Entry entry = Live(resource, now);
                held = entry != null;
                holders = entry == null ? null : string.Join("|", entry.Leases.Select(l => l.Holder));
                expiresInSeconds = entry == null ? 0 : Seconds(entry.Leases.Min(l => l.ExpiresAtMs) - now);
                holderCount = entry?.Leases.Count ?? 0;
            }
        }

        /// <summary>Whole seconds left, rounded up, so a live lease never reports 0.</summary>
        internal static int Seconds(long ms) => ms <= 0 ? 0 : (int)Math.Min(int.MaxValue, (ms + 999) / 1000);

        /// <summary>Every held lock and slot, by resource. Tokens are never included: a token is what lets its holder release the lock.</summary>
        internal static string Json()
        {
            lock (Sync)
            {
                long now = NowMs;
                var live = Entries.Keys.ToList().Select(k => Live(k, now)).Where(e => e != null).OrderBy(e => e.Resource, StringComparer.OrdinalIgnoreCase).ToList();
                using var stream = new System.IO.MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) }))
                {
                    w.WriteStartObject();
                    w.WriteString("scope", "Process");
                    w.WriteStartArray("locks");
                    foreach (Entry e in live)
                    {
                        w.WriteStartObject();
                        w.WriteString("resource", e.Resource);
                        w.WriteString("kind", e.Kind.ToString());
                        w.WriteNumber("capacity", e.Capacity);
                        w.WriteStartArray("holders");
                        foreach (Lease l in e.Leases)
                        {
                            w.WriteStartObject();
                            w.WriteString("holder", l.Holder);
                            w.WriteNumber("expiresInSeconds", Seconds(l.ExpiresAtMs - now));
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
