using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;

namespace EventAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that watches Windows UI events via
    /// <c>SetWinEventHook</c> and exposes two consumption models over one engine:
    /// synchronous <c>WaitForX</c> calls, and background subscriptions whose
    /// events land in a per-subscription queue the robot polls with
    /// <c>GetNextEvent</c>. All public methods are thread-safe and never throw.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Initialize"/> then <see cref="Start(string)"/> before any
    /// <c>WaitForX</c> or <see cref="Subscribe"/> call. Wait methods block until
    /// a matching event or timeout; the timeout is reported through their
    /// <c>out bool timedOut</c> parameter.
    /// </remarks>
    [Description("Watches Windows UI events (window create/show/destroy, dialogs, " +
                 "titles, states, menus) and delivers them to waiters or subscriptions. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public partial class EventUtils : Component
    {
        private readonly object _gate = new object();
        private readonly SubscriptionManager _subscriptions = new SubscriptionManager();
        private readonly WaiterRegistry _waiters = new WaiterRegistry();
        private readonly ThrottleDebounce _debounce = new ThrottleDebounce();
        private readonly uint _hostPid = unchecked((uint)Environment.ProcessId);
        private WinEventEngine _engine;
        private volatile HashSet<EventCategory> _activeCategories = new HashSet<EventCategory>();
        private bool _disposed;

        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public EventUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public EventUtils(IContainer container)
        {
            container?.Add(this);
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts the background hook thread (idempotent). Call before
        /// <see cref="Start"/>. Returns True on success; never throws.
        /// </summary>
        public bool Initialize()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    Debug.WriteLine("EventUtils.Initialize: SetWinEventHook requires Windows.");
                    return false;
                }
                lock (_gate)
                {
                    if (_disposed)
                        return false;
                    if (_engine != null)
                        return true; // already initialized
                    _engine = new WinEventEngine(OnEventReceived);
                    _engine.StartThread();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.Initialize failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Activates the given categories and installs the WinEvent hook, e.g.
        /// <c>"Windows,Foreground,Dialogs"</c>. Idempotent; call after
        /// <see cref="Initialize"/>. Returns True on success; never throws.
        /// </summary>
        public bool Start(string categoriesCsv)
        {
            try
            {
                lock (_gate)
                {
                    if (_disposed)
                        return false;
                    if (_engine == null && !Initialize())
                        return false;
                    var cats = ParseCategories(categoriesCsv);
                    if (cats == null || cats.Count == 0)
                    {
                        Debug.WriteLine("EventUtils.Start: no valid categories in '" + categoriesCsv + "'.");
                        return false;
                    }
                    _activeCategories = cats;
                    _engine.RequestHook(true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.Start failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Unhooks the WinEvent hook and clears the active category set. Queues
        /// are preserved so they can still be drained. Returns True on success;
        /// never throws.
        /// </summary>
        public bool Stop()
        {
            try
            {
                lock (_gate)
                {
                    _activeCategories = new HashSet<EventCategory>();
                    _engine?.RequestHook(false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.Stop failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Full teardown: unhooks, stops the hook thread, clears subscriptions,
        /// waiters, and debounce state. Safe to call multiple times; a subsequent
        /// <see cref="Initialize"/> restarts the component.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                try
                {
                    _waiters.CancelAll();
                    _subscriptions.ClearAll();
                    _debounce.Clear();
                    _engine?.Dispose();
                    _engine = null;
                    _activeCategories = new HashSet<EventCategory>();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("EventUtils.Dispose failed: " + ex.Message);
                }
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------
        // Subscribe model
        // ------------------------------------------------------------------

        /// <summary>
        /// Registers a subscription: events whose category is in
        /// <paramref name="categoriesCsv"/> and whose fields match
        /// <paramref name="filterJson"/> are queued under
        /// <paramref name="subscriptionId"/>. A null/empty filter matches all
        /// events; malformed filter JSON returns False (never throws).
        /// </summary>
        public bool Subscribe(string categoriesCsv, string filterJson, string subscriptionId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(subscriptionId))
                {
                    Debug.WriteLine("EventUtils.Subscribe: subscriptionId is empty.");
                    return false;
                }
                var cats = ParseCategories(categoriesCsv);
                if (cats == null || cats.Count == 0)
                {
                    Debug.WriteLine("EventUtils.Subscribe: no valid categories in '" + categoriesCsv + "'.");
                    return false;
                }
                EventFilter filter = EventFilter.FromJson(filterJson);
                if (!string.IsNullOrEmpty(filterJson) && filter == null)
                {
                    Debug.WriteLine("EventUtils.Subscribe: malformed filter JSON for '" + subscriptionId + "'.");
                    return false;
                }
                filter = filter ?? EventFilter.Create(); // null/empty filter = match-all
                return _subscriptions.TryAdd(subscriptionId, cats, filter, out string message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.Subscribe failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Removes a subscription and drops its queue. Returns True if it existed.</summary>
        public bool Unsubscribe(string subscriptionId)
        {
            try
            {
                return _subscriptions.TryRemove(subscriptionId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.Unsubscribe failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Blocks up to <paramref name="timeoutMs"/> for the next queued event.
        /// <paramref name="hasEvent"/> is True when an event was returned.
        /// Returns null on timeout or unknown subscription; never throws.
        /// </summary>
        public EventData GetNextEvent(string subscriptionId, int timeoutMs, out bool hasEvent)
        {
            hasEvent = false;
            try
            {
                return _subscriptions.GetNextEvent(subscriptionId, timeoutMs, out hasEvent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.GetNextEvent failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Drains up to <paramref name="maxCount"/> queued events, waiting up to
        /// <paramref name="drainMs"/> for the first one. Never throws.
        /// </summary>
        public EventData[] GetNextEvents(string subscriptionId, int maxCount, int drainMs)
        {
            try
            {
                return _subscriptions.GetNextEvents(subscriptionId, maxCount, drainMs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.GetNextEvents failed: " + ex.Message);
                return Array.Empty<EventData>();
            }
        }

        /// <summary>Reports whether a subscription has queued events. Returns False for an unknown id.</summary>
        public bool HasEvents(string subscriptionId, out int count)
        {
            count = 0;
            try
            {
                return _subscriptions.HasEvents(subscriptionId, out count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.HasEvents failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Drops all queued events for a subscription.</summary>
        public void ClearQueue(string subscriptionId)
        {
            try
            {
                _subscriptions.ClearQueue(subscriptionId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.ClearQueue failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Tuning / ops
        // ------------------------------------------------------------------

        /// <summary>
        /// Sets the debounce window (ms) for an event name, e.g. "WindowShown".
        /// A second identical (hwnd, event-name) event within the window is
        /// dropped. Defaults: 150 ms for WindowShown/WindowHidden, 0 otherwise.
        /// </summary>
        public void SetDebounce(string eventName, int debounceMs)
        {
            try
            {
                _debounce.Set(eventName, debounceMs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.SetDebounce failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Sets the per-subscription queue limit and overflow policy
        /// ("DropOldest" | "DropNewest" | "Block"). Applies to existing and
        /// future subscriptions.
        /// </summary>
        public void SetQueueLimits(int maxEvents, string overflowPolicy)
        {
            try
            {
                _subscriptions.SetQueueLimits(maxEvents, overflowPolicy);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.SetQueueLimits failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Returns the last <paramref name="count"/> events (newest last) as a
        /// JSON array, regardless of subscriptions. Never throws.
        /// </summary>
        public string DumpRecentEvents(int count)
        {
            try
            {
                var engine = _engine;
                var events = engine != null ? engine.SnapshotRing(Math.Max(0, count)) : Array.Empty<EventData>();
                return JsonSerializer.Serialize(events, EventJson.Options);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.DumpRecentEvents failed: " + ex.Message);
                return "[]";
            }
        }

        /// <summary>
        /// Non-blocking lookback: True if a window-created event matching
        /// <paramref name="filterJson"/> was captured within the last
        /// <paramref name="withinLastMs"/> milliseconds. Never throws.
        /// </summary>
        public bool WasWindowCreated(string filterJson, int withinLastMs)
        {
            try
            {
                var engine = _engine;
                if (engine == null)
                    return false;
                var filter = EventFilter.FromJson(filterJson) ?? EventFilter.Create();
                long cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromMilliseconds(Math.Max(0, withinLastMs)).Ticks;
                foreach (var e in engine.SnapshotRing(500))
                {
                    if (e.Category == "WindowCreated" && e.Timestamp >= cutoff && filter.Matches(e, _hostPid))
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.WasWindowCreated failed: " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        /// <summary>Runs on the hook thread: debounce, then fan out to subscriptions and waiters.</summary>
        private void OnEventReceived(EventData data, List<EventCategory> cats)
        {
            var active = _activeCategories;
            if (active == null || active.Count == 0)
                return;
            bool anyActive = false;
            foreach (var c in cats)
            {
                if (active.Contains(c))
                {
                    anyActive = true;
                    break;
                }
            }
            if (!anyActive)
                return;
            if (_debounce.ShouldDrop(data.Hwnd, data.Category))
                return;
            _subscriptions.Deliver(data, cats, _hostPid);
            _waiters.Match(data);
        }

        private static HashSet<EventCategory> ParseCategories(string categoriesCsv)
        {
            var result = new HashSet<EventCategory>();
            if (string.IsNullOrWhiteSpace(categoriesCsv))
                return result;
            foreach (var part in categoriesCsv.Split(','))
            {
                string name = part.Trim();
                if (name.Length == 0)
                    continue;
                if (Enum.TryParse(name, true, out EventCategory cat))
                    result.Add(cat);
            }
            return result;
        }
    }
}
