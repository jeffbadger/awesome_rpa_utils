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
        private readonly ConcurrentDictionary<string, int> _debounceMs = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<(uint hwnd, string eventName), long> _lastDelivered = new ConcurrentDictionary<(uint, string), long>();

        public ThrottleDebounce()
        {
            _debounceMs["WindowShown"] = 150;
            _debounceMs["WindowHidden"] = 150;
        }

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
        public bool ShouldDrop(uint hwnd, string eventName)
        {
            if (hwnd == 0 || string.IsNullOrEmpty(eventName))
                return false;
            if (!_debounceMs.TryGetValue(eventName, out int ms) || ms <= 0)
                return false;
            long now = Environment.TickCount64;
            if (_lastDelivered.TryGetValue((hwnd, eventName), out long last) && now - last < ms)
                return true; // drop subsequent, keep first
            _lastDelivered[(hwnd, eventName)] = now;
            return false;
        }

        /// <summary>Clears all debounce state (called on Stop/Dispose).</summary>
        public void Clear()
        {
            _lastDelivered.Clear();
        }
    }
}
