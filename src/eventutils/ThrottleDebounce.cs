using System;
using System.Collections.Concurrent;

namespace EventAutomation
{
    /// <summary>
    /// Per-(hwnd, event-name) debounce. A second identical-key event arriving
    /// within the debounce window is dropped (keep-first). Defaults: 150 ms for
    /// SHOW/HIDE (Save dialogs fire 4-6 SHOWs in ~200 ms and must coalesce to 1),
    /// 0 for everything else. Thread-safe; called from the hook thread.
    /// </summary>
    internal sealed class ThrottleDebounce
    {
        private const int PruneThreshold = 512;

        private readonly ConcurrentDictionary<string, int> _debounceMs = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<(long hwnd, string eventName), long> _lastDelivered = new ConcurrentDictionary<(long, string), long>();

        public ThrottleDebounce()
        {
            _debounceMs["WindowShown"] = 150;
            _debounceMs["WindowHidden"] = 150;
        }

        /// <summary>Test/introspection hook: number of tracked (hwnd, event) keys.</summary>
        internal int CachedKeys => _lastDelivered.Count;

        /// <summary>Sets the debounce window for an event name (0 disables).</summary>
        public void Set(string eventName, int debounceMs)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                return;
            _debounceMs[eventName] = Math.Max(0, debounceMs);
        }

        /// <summary>
        /// Returns true when this event should be dropped because an identical
        /// (hwnd, event-name) event was delivered within the debounce window.
        /// </summary>
        public bool ShouldDrop(long hwnd, string eventName)
        {
            if (hwnd == 0 || string.IsNullOrEmpty(eventName))
                return false;
            if (!_debounceMs.TryGetValue(eventName, out int ms) || ms <= 0)
                return false;
            long now = Environment.TickCount64;
            if (_lastDelivered.TryGetValue((hwnd, eventName), out long last) && now - last < ms)
                return true; // drop subsequent, keep first
            _lastDelivered[(hwnd, eventName)] = now;
            if (_lastDelivered.Count > PruneThreshold)
                PruneStale(now);
            return false;
        }

        /// <summary>
        /// Bounds growth: over a long robot session every distinct (hwnd, event)
        /// pair leaves a timestamp here, so once past the threshold drop entries
        /// older than the largest configured debounce window (with slack) — they
        /// can no longer cause a drop.
        /// </summary>
        private void PruneStale(long now)
        {
            int maxWindow = 0;
            foreach (var window in _debounceMs.Values)
                if (window > maxWindow)
                    maxWindow = window;
            foreach (var pair in _lastDelivered)
            {
                if (now - pair.Value > maxWindow)
                    _lastDelivered.TryRemove(pair.Key, out _);
            }
        }

        /// <summary>Clears all debounce state (called on Stop and Dispose).</summary>
        public void Clear()
        {
            _lastDelivered.Clear();
        }
    }
}
