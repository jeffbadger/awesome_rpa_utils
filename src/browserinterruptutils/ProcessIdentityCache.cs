using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BrowserInterruptAutomation
{
    /// <summary>A process's name plus a key that changes when the process ID is reused (its creation time).</summary>
    internal readonly struct ProcessIdentity
    {
        /// <summary>Key value meaning "no creation time could be read"; such an entry cannot be cheaply re-validated.</summary>
        internal const long UnknownKey = long.MinValue;

        public ProcessIdentity(string name, long creationKey)
        {
            Name = name ?? string.Empty;
            CreationKey = creationKey;
        }

        /// <summary>The process name as <c>Process.ProcessName</c> reports it (no directory, no <c>.exe</c>).</summary>
        public string Name { get; }

        /// <summary>The process creation time (FILETIME ticks) or <see cref="UnknownKey"/>.</summary>
        public long CreationKey { get; }
    }

    /// <summary>
    /// A thread-safe pid to process-name cache that keeps the native sweep from paying an expensive
    /// name lookup for every window on every pass. UIA- and Win32-independent (the lookups are
    /// injected), so its logic is unit-tested on any host.
    /// </summary>
    /// <remarks>
    /// <b>Trust window.</b> A positive entry is returned as-is for <see cref="PositiveTtlMs"/>.
    /// After that it is re-validated by re-reading only the cheap creation-time key
    /// (<c>readKey</c>); the name is never re-read while the key is unchanged, and a different key
    /// (the PID was reused by another process) triggers a full re-resolve. A failed lookup
    /// (process gone, access denied) is cached as a negative entry for <see cref="NegativeTtlMs"/>
    /// so a failing pid is not retried on every sweep. The cache holds at most
    /// <see cref="MaxEntries"/> entries: expired ones are dropped first, then the least recently used.
    /// One lock guards the dictionary; the injected delegates are never called under it, so two
    /// threads racing on the same cold pid may both resolve it (bounded by the thread count, and the
    /// results are equivalent).
    /// </remarks>
    internal sealed class ProcessIdentityCache
    {
        /// <summary>How long a resolved name is trusted before the cheap key re-validation. Sweeps run every second; 5 s bounds the PID-reuse blind spot.</summary>
        internal const long PositiveTtlMs = 5000;

        /// <summary>How long a failed lookup is remembered before it is retried.</summary>
        internal const long NegativeTtlMs = 3000;

        /// <summary>Most pids remembered (a desktop with hundreds of windows fits comfortably).</summary>
        internal const int DefaultMaxEntries = 1024;

        private sealed class Entry
        {
            public string Name;        // null: negative entry
            public long Key;
            public long ValidUntil;
            public long LastUsed;      // use counter, not a clock: strictly ordered
        }

        private readonly object _gate = new object();
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly Func<int, ProcessIdentity?> _resolve;
        private readonly Func<int, long?> _readKey;
        private readonly Func<long> _clockMs;
        private long _useCounter;

        /// <param name="resolve">Full lookup (name and key), or null when the process is gone or inaccessible.</param>
        /// <param name="readKey">Cheap lookup of the creation-time key only, or null when it cannot be read.</param>
        /// <param name="clockMs">Monotonic millisecond clock; defaults to a <see cref="Stopwatch"/>.</param>
        /// <param name="maxEntries">Size cap (at least 1).</param>
        public ProcessIdentityCache(Func<int, ProcessIdentity?> resolve, Func<int, long?> readKey,
            Func<long> clockMs = null, int maxEntries = DefaultMaxEntries)
        {
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            _readKey = readKey ?? throw new ArgumentNullException(nameof(readKey));
            _clockMs = clockMs ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
            MaxEntries = Math.Max(1, maxEntries);
        }

        /// <summary>The size cap.</summary>
        public int MaxEntries { get; }

        /// <summary>Entries held (including negative ones). For tests and diagnostics.</summary>
        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        /// <summary>
        /// The name of the process with this ID. False (and an empty name) when the ID is invalid
        /// or the process is unknown; never throws.
        /// </summary>
        public bool TryGetName(int pid, out string name)
        {
            name = string.Empty;
            if (pid <= 0)
                return false;

            Entry existing;
            long now = _clockMs();
            lock (_gate)
            {
                if (_entries.TryGetValue(pid, out existing))
                {
                    if (now < existing.ValidUntil)
                    {
                        existing.LastUsed = ++_useCounter;
                        return Result(existing, out name);
                    }
                }
            }

            // Expired positive entry with a readable key: re-validate with the cheap read only.
            if (existing != null && existing.Name != null && existing.Key != ProcessIdentity.UnknownKey)
            {
                long? key = SafeReadKey(pid);
                if (key.HasValue && key.Value == existing.Key)
                {
                    lock (_gate)
                    {
                        // Refresh only if the entry is still the one validated (it may have been evicted or replaced).
                        if (_entries.TryGetValue(pid, out Entry current) && ReferenceEquals(current, existing))
                        {
                            current.ValidUntil = _clockMs() + PositiveTtlMs;
                            current.LastUsed = ++_useCounter;
                        }
                    }
                    name = existing.Name;
                    return existing.Name.Length > 0;
                }
                // Key changed (PID reuse) or unreadable (process gone): fall through to a full resolve.
            }

            ProcessIdentity? identity = SafeResolve(pid);
            var fresh = new Entry();
            if (identity.HasValue)
            {
                fresh.Name = identity.Value.Name;
                fresh.Key = identity.Value.CreationKey;
            }
            long after = _clockMs();
            fresh.ValidUntil = after + (identity.HasValue ? PositiveTtlMs : NegativeTtlMs);
            lock (_gate)
            {
                fresh.LastUsed = ++_useCounter;
                MakeRoomFor(pid, after);
                _entries[pid] = fresh;
            }
            return Result(fresh, out name);
        }

        /// <summary>Forgets one pid (for example after its window is known to be gone).</summary>
        public void Evict(int pid)
        {
            lock (_gate)
                _entries.Remove(pid);
        }

        /// <summary>Forgets everything.</summary>
        public void Clear()
        {
            lock (_gate)
                _entries.Clear();
        }

        private static bool Result(Entry entry, out string name)
        {
            name = entry.Name ?? string.Empty;
            return name.Length > 0;
        }

        // Called under _gate. Drops expired entries, then the least recently used, until adding pid fits.
        private void MakeRoomFor(int pid, long now)
        {
            if (_entries.ContainsKey(pid) || _entries.Count < MaxEntries)
                return;

            List<int> expired = null;
            foreach (KeyValuePair<int, Entry> pair in _entries)
            {
                if (now >= pair.Value.ValidUntil)
                    (expired ?? (expired = new List<int>())).Add(pair.Key);
            }
            if (expired != null)
            {
                foreach (int key in expired)
                    _entries.Remove(key);
            }

            while (_entries.Count >= MaxEntries)
            {
                int oldestPid = 0;
                long oldestUse = long.MaxValue;
                foreach (KeyValuePair<int, Entry> pair in _entries)
                {
                    if (pair.Value.LastUsed < oldestUse)
                    {
                        oldestUse = pair.Value.LastUsed;
                        oldestPid = pair.Key;
                    }
                }
                _entries.Remove(oldestPid);
            }
        }

        private ProcessIdentity? SafeResolve(int pid)
        {
            try
            {
                return _resolve(pid);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return null;
            }
        }

        private long? SafeReadKey(int pid)
        {
            try
            {
                return _readKey(pid);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return null;
            }
        }
    }
}
