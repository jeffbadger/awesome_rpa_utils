using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// A small thread-safe cache that holds STRONG references and bounds its size by generations
    /// instead of tracking each entry's age: two dictionaries, <c>current</c> and <c>previous</c>.
    /// A lookup checks <c>current</c>, then <c>previous</c> (promoting a hit into <c>current</c>, so
    /// anything still in use survives). A rotation makes <c>current</c> the new <c>previous</c>
    /// and drops the old <c>previous</c>, so an entry nobody touched is gone after two rotations.
    /// </summary>
    /// <remarks>
    /// UIA-independent on purpose: <c>UiaBrowserPopupProbe</c> keeps its <c>AutomationElement</c>s
    /// here (a weak reference is not enough, because the wrappers a subtree walk finds are
    /// otherwise unreferenced and a gen0 GC between discovery and the engine's re-check would
    /// collect them), and this logic gets real tests on any host. A rotation happens when the
    /// current generation has reached <c>maxEntriesPerGeneration</c> or has lived for
    /// <c>rotateAfterMs</c>, checked on every operation, so the cache never holds more than
    /// twice the cap and an idle cache empties itself within two intervals of the next use.
    /// One lock guards everything; every operation is a couple of dictionary calls.
    /// </remarks>
    internal sealed class GenerationalCache<TKey, TValue>
    {
        private readonly object _gate = new object();
        private readonly int _maxEntriesPerGeneration;
        private readonly long _rotateAfterMs;
        private readonly Func<long> _clockMs;
        private Dictionary<TKey, TValue> _current = new Dictionary<TKey, TValue>();
        private Dictionary<TKey, TValue> _previous = new Dictionary<TKey, TValue>();
        private long _generationStarted;

        /// <param name="maxEntriesPerGeneration">Rotate once the current generation holds this many entries (at least 1).</param>
        /// <param name="rotateAfterMs">Rotate once the current generation is this old; 0 or less turns the age trigger off.</param>
        /// <param name="clockMs">A monotonic millisecond clock; defaults to a <see cref="Stopwatch"/>. Tests inject one.</param>
        public GenerationalCache(int maxEntriesPerGeneration, long rotateAfterMs, Func<long> clockMs = null)
        {
            _maxEntriesPerGeneration = Math.Max(1, maxEntriesPerGeneration);
            _rotateAfterMs = rotateAfterMs;
            _clockMs = clockMs ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
            _generationStarted = _clockMs();
        }

        /// <summary>Entries in the current generation. For tests and diagnostics.</summary>
        public int CurrentCount
        {
            get { lock (_gate) return _current.Count; }
        }

        /// <summary>Entries in the previous generation. For tests and diagnostics.</summary>
        public int PreviousCount
        {
            get { lock (_gate) return _previous.Count; }
        }

        /// <summary>Stores or replaces an entry in the current generation (rotating first if one is due).</summary>
        public void Set(TKey key, TValue value)
        {
            lock (_gate)
            {
                RotateIfDue(adding: !_current.ContainsKey(key));
                _current[key] = value;
            }
        }

        /// <summary>Finds an entry, promoting one found in the previous generation into the current one.</summary>
        public bool TryGet(TKey key, out TValue value)
        {
            lock (_gate)
            {
                RotateIfDue(adding: false);
                if (_current.TryGetValue(key, out value))
                    return true;
                if (_previous.TryGetValue(key, out value))
                {
                    _previous.Remove(key);
                    RotateIfDue(adding: true);
                    _current[key] = value;
                    return true;
                }
                return false;
            }
        }

        /// <summary>Drops an entry from both generations.</summary>
        public void Remove(TKey key)
        {
            lock (_gate)
            {
                _current.Remove(key);
                _previous.Remove(key);
            }
        }

        /// <summary>Forces a rotation: current becomes previous, the old previous is dropped.</summary>
        public void Rotate()
        {
            lock (_gate)
                RotateCore();
        }

        private void RotateIfDue(bool adding)
        {
            bool full = adding && _current.Count >= _maxEntriesPerGeneration;
            bool old = _rotateAfterMs > 0 && _clockMs() - _generationStarted >= _rotateAfterMs;
            if (full || old)
                RotateCore();
        }

        private void RotateCore()
        {
            _previous = _current;
            _current = new Dictionary<TKey, TValue>();
            _generationStarted = _clockMs();
        }
    }
}
