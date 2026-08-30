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
    /// <c>GetNextEvent</c>. All public methods are thread-safe and never throw:
    /// they return <c>bool</c> and report abnormal results through an
    /// <c>out string message</c>.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Initialize(out string)"/> then <see cref="Start(string, out string)"/>
    /// before any <c>WaitForX</c> or <see cref="Subscribe(string, string, string, out string)"/>
    /// call. Wait methods block until a matching event or timeout; the timeout is
    /// reported through their <c>out bool timedOut</c> parameter.
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
        /// <see cref="Start(string, out string)"/>. Returns True on success;
        /// <paramref name="message"/> is null on success and a human-readable
        /// reason otherwise. Never throws.
        /// </summary>
        public bool Initialize(out string message)
        {
            message = null;
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    message = "SetWinEventHook requires Windows.";
                    return false;
                }
                lock (_gate)
                {
                    if (_disposed)
                    {
                        message = "EventUtils is disposed; create a new instance.";
                        return false;
                    }
                    if (_engine != null)
                        return true; // already initialized
                    _engine = new WinEventEngine(OnEventReceived);
                    _engine.StartThread();
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = "Initialize failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Activates the given categories and installs the WinEvent hook, e.g.
        /// <c>"Windows,Foreground,Dialogs"</c>. Idempotent; call after
        /// <see cref="Initialize(out string)"/>. Returns True on success;
        /// <paramref name="message"/> is null on success and a human-readable
        /// reason otherwise. Never throws.
        /// </summary>
        public bool Start(string categoriesCsv, out string message)
        {
            message = null;
            try
            {
                lock (_gate)
                {
                    if (_disposed)
                    {
                        message = "EventUtils is disposed; create a new instance.";
                        return false;
                    }
                    if (_engine == null && !Initialize(out message))
                        return false;
                    if (!TryParseCategories(categoriesCsv, out var cats, out string parseError))
                    {
                        message = parseError;
                        return false;
                    }
                    if (cats.Count == 0)
                    {
                        message = "No categories in '" + categoriesCsv + "'. Pass a comma-separated list, e.g. 'Windows,Dialogs'.";
                        return false;
                    }
                    _activeCategories = cats;
                    _engine.RequestHook(true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = "Start failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Unhooks the WinEvent hook and clears the active category set. Queues
        /// are preserved so they can still be drained. Returns False with a
        /// message when the component was never initialized (nothing to stop).
        /// <paramref name="message"/> is null on success and a human-readable
        /// reason otherwise. Never throws.
        /// </summary>
        public bool Stop(out string message)
        {
            message = null;
            try
            {
                lock (_gate)
                {
                    if (_engine == null)
                    {
                        message = "EventUtils is not initialized; nothing to stop.";
                        return false;
                    }
                    _activeCategories = new HashSet<EventCategory>();
                    _engine.RequestHook(false);
                    _debounce.Clear();
                    Native.ProcessHelpers.Clear();
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = "Stop failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Full teardown: unhooks, stops the hook thread, clears subscriptions,
        /// waiters, and debounce state. Safe to call multiple times. After
        /// Dispose the instance is final: a subsequent
        /// <see cref="Initialize(out string)"/> returns False with a message —
        /// create a new instance instead.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            _waiters.CancelAll();
            _subscriptions.ClearAll();
            _debounce.Clear();

            var engine = (WinEventEngine)null;
            lock (_gate)
            {
                if (_disposed)
                {
                    base.Dispose(disposing);
                    return;
                }
                _disposed = true;
                engine = _engine;
                _engine = null;
                _activeCategories = new HashSet<EventCategory>();
            }
            // Dispose joins the hook thread (up to 2 s) — do it outside _gate so
            // other public methods are not blocked behind teardown.
            try
            {
                engine?.Dispose();
                Native.ProcessHelpers.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.Dispose failed: " + ex.Message);
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
        /// events. Returns True on success; <paramref name="message"/> is null
        /// on success and a human-readable reason otherwise (malformed filter
        /// JSON, invalid <c>titleMatches</c> regex, unknown category, duplicate
        /// id, empty id). Never throws.
        /// </summary>
        public bool Subscribe(string categoriesCsv, string filterJson, string subscriptionId, out string message)
        {
            message = null;
            try
            {
                if (string.IsNullOrWhiteSpace(subscriptionId))
                {
                    message = "subscriptionId is empty.";
                    return false;
                }
                if (!TryParseCategories(categoriesCsv, out var cats, out string parseError))
                {
                    message = parseError;
                    return false;
                }
                if (cats.Count == 0)
                {
                    message = "No categories in '" + categoriesCsv + "'. Pass a comma-separated list, e.g. 'Windows,Dialogs'.";
                    return false;
                }
                if (!EventFilter.TryFromJson(filterJson, out var filter, out string filterError))
                {
                    message = "Invalid filter for '" + subscriptionId + "': " + filterError;
                    return false;
                }
                filter = filter ?? EventFilter.Create(); // null/empty filter = match-all
                return _subscriptions.TryAdd(subscriptionId, cats, filter, out message);
            }
            catch (Exception ex)
            {
                message = "Subscribe failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Removes a subscription and drops its queue. Returns True if it
        /// existed; <paramref name="message"/> is null on success and a
        /// human-readable reason otherwise. Never throws.
        /// </summary>
        public bool Unsubscribe(string subscriptionId, out string message)
        {
            message = null;
            try
            {
                return _subscriptions.TryRemove(subscriptionId, out message);
            }
            catch (Exception ex)
            {
                message = "Unsubscribe failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Blocks up to <paramref name="timeoutMs"/> for the next queued event.
        /// Returns True when the call succeeded; <paramref name="hasEvent"/> is
        /// True when <paramref name="eventData"/> holds an event (False on
        /// timeout). <paramref name="message"/> is null on success and a
        /// human-readable reason otherwise (e.g. unknown subscription). Never
        /// throws.
        /// </summary>
        public bool GetNextEvent(string subscriptionId, int timeoutMs, out EventData eventData, out bool hasEvent, out string message)
        {
            eventData = null;
            hasEvent = false;
            message = null;
            try
            {
                eventData = _subscriptions.GetNextEvent(subscriptionId, timeoutMs, out hasEvent, out message);
                return message == null; // non-null message = unknown subscription
            }
            catch (Exception ex)
            {
                message = "GetNextEvent failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Drains up to <paramref name="maxCount"/> queued events, waiting up to
        /// <paramref name="drainMs"/> for the first one. Returns True on success;
        /// <paramref name="events"/> holds the drained events (possibly empty).
        /// <paramref name="message"/> is null on success and a human-readable
        /// reason otherwise. Never throws.
        /// </summary>
        public bool GetNextEvents(string subscriptionId, int maxCount, int drainMs, out EventData[] events, out string message)
        {
            events = Array.Empty<EventData>();
            message = null;
            try
            {
                events = _subscriptions.GetNextEvents(subscriptionId, maxCount, drainMs, out message);
                return message == null; // non-null message = unknown subscription
            }
            catch (Exception ex)
            {
                message = "GetNextEvents failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Reports the number of queued events for a subscription. Returns True
        /// when the subscription exists; <paramref name="count"/> is the queued
        /// count (0 when empty). <paramref name="message"/> is null on success
        /// and a human-readable reason otherwise (unknown subscription). Never
        /// throws.
        /// </summary>
        public bool HasEvents(string subscriptionId, out int count, out string message)
        {
            count = 0;
            message = null;
            try
            {
                return _subscriptions.HasEvents(subscriptionId, out count, out message);
            }
            catch (Exception ex)
            {
                message = "HasEvents failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Drops all queued events for a subscription. Returns True when the
        /// subscription exists; <paramref name="message"/> is null on success
        /// and a human-readable reason otherwise (unknown subscription). Never
        /// throws.
        /// </summary>
        public bool ClearQueue(string subscriptionId, out string message)
        {
            message = null;
            try
            {
                return _subscriptions.ClearQueue(subscriptionId, out message);
            }
            catch (Exception ex)
            {
                message = "ClearQueue failed: " + ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Tuning / ops
        // ------------------------------------------------------------------

        /// <summary>
        /// Sets the debounce window (ms) for an event name, e.g. "WindowShown".
        /// A second identical (hwnd, event-name) event within the window is
        /// dropped. Defaults: 150 ms for WindowShown/WindowHidden, 0 otherwise.
        /// Returns True on success; <paramref name="message"/> is null on
        /// success and a human-readable reason otherwise. Never throws.
        /// </summary>
        public bool SetDebounce(string eventName, int debounceMs, out string message)
        {
            message = null;
            try
            {
                _debounce.Set(eventName, debounceMs);
                return true;
            }
            catch (Exception ex)
            {
                message = "SetDebounce failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Sets the per-subscription queue limit and overflow policy
        /// ("DropOldest" | "DropNewest" | "Block"). "Block" is accepted for
        /// compatibility but behaves exactly like "DropNewest": the WinEvent
        /// hook thread must never block, so a full queue always drops the new
        /// event. Applies to existing and future subscriptions. Returns True on
        /// success; <paramref name="message"/> is null on success and a
        /// human-readable reason otherwise (invalid arguments). Never throws.
        /// </summary>
        public bool SetQueueLimits(int maxEvents, string overflowPolicy, out string message)
        {
            message = null;
            try
            {
                if (maxEvents < 1)
                {
                    message = "maxEvents must be positive.";
                    return false;
                }
                if (overflowPolicy != "DropOldest" && overflowPolicy != "DropNewest" && overflowPolicy != "Block")
                {
                    message = "overflowPolicy must be 'DropOldest', 'DropNewest', or 'Block'.";
                    return false;
                }
                _subscriptions.SetQueueLimits(maxEvents, overflowPolicy);
                return true;
            }
            catch (Exception ex)
            {
                message = "SetQueueLimits failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Returns the last <paramref name="count"/> events (newest last) as a
        /// JSON array in <paramref name="json"/>, regardless of subscriptions.
        /// Returns True on success; <paramref name="message"/> is null on
        /// success and a human-readable reason otherwise. Never throws.
        /// </summary>
        public bool DumpRecentEvents(int count, out string json, out string message)
        {
            json = "[]";
            message = null;
            try
            {
                var engine = _engine;
                var events = engine != null ? engine.SnapshotRing(Math.Max(0, count)) : Array.Empty<EventData>();
                json = JsonSerializer.Serialize(events, EventJson.Options);
                return true;
            }
            catch (Exception ex)
            {
                message = "DumpRecentEvents failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Non-blocking lookback: reports in <paramref name="wasCreated"/>
        /// whether a window-created event matching <paramref name="filterJson"/>
        /// was captured within the last <paramref name="withinLastMs"/>
        /// milliseconds. Returns True when the check succeeded;
        /// <paramref name="message"/> is null on success and a human-readable
        /// reason otherwise. Never throws.
        /// </summary>
        public bool WasWindowCreated(string filterJson, int withinLastMs, out bool wasCreated, out string message)
        {
            wasCreated = false;
            message = null;
            try
            {
                var engine = _engine;
                if (engine == null)
                {
                    message = "Engine not started; call Initialize() and Start() first.";
                    return false;
                }
                if (!EventFilter.TryFromJson(filterJson, out var filter, out string filterError))
                {
                    message = filterError;
                    return false;
                }
                filter = filter ?? EventFilter.Create();
                long cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromMilliseconds(Math.Max(0, withinLastMs)).Ticks;
                foreach (var e in engine.SnapshotRing(500))
                {
                    if (e.Category == "WindowCreated" && e.Timestamp >= cutoff && filter.Matches(e, _hostPid))
                    {
                        wasCreated = true;
                        return true;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                message = "WasWindowCreated failed: " + ex.Message;
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

        private static bool TryParseCategories(string categoriesCsv, out HashSet<EventCategory> cats, out string error)
        {
            cats = new HashSet<EventCategory>();
            error = null;
            if (string.IsNullOrWhiteSpace(categoriesCsv))
                return true; // caller treats an empty set as invalid input
            var unknown = new List<string>();
            foreach (var part in categoriesCsv.Split(','))
            {
                string name = part.Trim();
                if (name.Length == 0)
                    continue;
                if (Enum.TryParse(name, true, out EventCategory cat))
                    cats.Add(cat);
                else
                    unknown.Add(name);
            }
            if (unknown.Count > 0)
            {
                error = "Unknown event categories in '" + categoriesCsv + "': '" + string.Join("', '", unknown) +
                        "'. Valid categories: " + string.Join(", ", Enum.GetNames(typeof(EventCategory))) + ".";
                return false;
            }
            return true;
        }
    }
}
