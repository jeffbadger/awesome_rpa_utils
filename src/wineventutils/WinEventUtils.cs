using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;

namespace WinEventAutomation
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
    /// Call <see cref="Initialize(out string)"/> then <see cref="Start(WinEventCategory, out string)"/>
    /// or <see cref="StartCategories"/> before any <c>WaitForX</c> or
    /// <see cref="Subscribe(WinEventCategory, string, string, out string)"/>
    /// call. Wait methods block until a matching event or timeout; a timeout
    /// returns False with a message explaining it, the same as any other
    /// failure.
    /// </remarks>
    [Description("Watches Windows UI events (window create/show/destroy, dialogs, " +
                 "titles, states, menus) and delivers them to waiters or subscriptions. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public partial class WinEventUtils : Component
    {
        private readonly object _gate = new object();
        private readonly SubscriptionManager _subscriptions = new SubscriptionManager();
        private readonly WaiterRegistry _waiters = new WaiterRegistry();
        private readonly ThrottleDebounce _debounce = new ThrottleDebounce();
        private readonly uint _hostPid = unchecked((uint)Environment.ProcessId);
        private WinEventEngine _engine;
        private volatile HashSet<WinEventCategory> _activeCategories = new HashSet<WinEventCategory>();
        private bool _disposed;

        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public WinEventUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public WinEventUtils(IContainer container)
        {
            container?.Add(this);
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts the background hook thread (idempotent). Call before
        /// <see cref="Start(WinEventCategory, out string)"/>/<see cref="StartCategories"/>. Returns True on success;
        /// <paramref name="message"/> is null on success and a human-readable
        /// reason otherwise. Never throws.
        /// </summary>
        public bool Initialize(out string message)
        {
            message = default;
            try
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
                            message = "WinEventUtils is disposed; create a new instance.";
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Initialize", ex);
                return false;
            }
        }

        /// <summary>
        /// Activates a single category and installs the WinEvent hook — the
        /// common case for a wait that only needs to watch one category; use
        /// <see cref="StartCategories"/> to watch more than one. Call after
        /// <see cref="Initialize(out string)"/>. Fails if categories are
        /// already active — call <see cref="Stop(out string)"/> first rather
        /// than silently replacing what is being watched. Returns True on
        /// success; <paramref name="message"/> is null on success and a
        /// human-readable reason otherwise. Never throws.
        /// </summary>
        public bool Start(WinEventCategory category, out string message)
        {
            message = default;
            try
            {
                message = null;
                try
                {
                    return StartCore(new HashSet<WinEventCategory> { category }, out message);
                }
                catch (Exception ex)
                {
                    message = "Start failed: " + ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Start", ex);
                return false;
            }
        }

        /// <summary>
        /// Activates the given categories and installs the WinEvent hook, with one
        /// Boolean checkbox per <see cref="WinEventCategory"/> instead of a
        /// comma-separated, typo-prone string. Each flag is nullable so an
        /// unwired port is treated the same as an explicit <c>False</c> — only
        /// <c>True</c> activates a category. Call after
        /// <see cref="Initialize(out string)"/>. Fails if categories are
        /// already active — call <see cref="Stop(out string)"/> first rather
        /// than silently replacing what is being watched. Returns True on
        /// success; <paramref name="message"/> is null on success and a
        /// human-readable reason otherwise. Never throws.
        /// </summary>
        public bool StartCategories(bool? windows, bool? foreground, bool? dialogs, bool? titles, bool? states, bool? menus, bool? windowOps, bool? session, out string message)
        {
            message = default;
            try
            {
                message = null;
                try
                {
                    var cats = new HashSet<WinEventCategory>();
                    if (windows == true) cats.Add(WinEventCategory.Windows);
                    if (foreground == true) cats.Add(WinEventCategory.Foreground);
                    if (dialogs == true) cats.Add(WinEventCategory.Dialogs);
                    if (titles == true) cats.Add(WinEventCategory.Titles);
                    if (states == true) cats.Add(WinEventCategory.States);
                    if (menus == true) cats.Add(WinEventCategory.Menus);
                    if (windowOps == true) cats.Add(WinEventCategory.WindowOps);
                    if (session == true) cats.Add(WinEventCategory.Session);
                    if (cats.Count == 0)
                    {
                        message = "No categories were selected. Set at least one category parameter to True.";
                        return false;
                    }
                    return StartCore(cats, out message);
                }
                catch (Exception ex)
                {
                    message = "StartCategories failed: " + ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("StartCategories", ex);
                return false;
            }
        }

        /// <summary>
        /// Shared installation logic for <see cref="Start(WinEventCategory, out string)"/> and
        /// <see cref="StartCategories"/>: initializes the engine if needed, activates
        /// the given category set, and installs the hook. Fails if categories are
        /// already active — call <see cref="Stop(out string)"/> first rather than
        /// silently replacing what is being watched.
        /// </summary>
        private bool StartCore(HashSet<WinEventCategory> cats, out string message)
        {
            message = null;
            lock (_gate)
            {
                if (_disposed)
                {
                    message = "WinEventUtils is disposed; create a new instance.";
                    return false;
                }
                if (_activeCategories.Count > 0)
                {
                    message = "WinEventUtils is already running; call Stop() before starting again with different categories.";
                    return false;
                }
                if (_engine == null && !Initialize(out message))
                    return false;
                _activeCategories = cats;
                _engine.RequestHook(true);
                return true;
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
            message = default;
            try
            {
                message = null;
                try
                {
                    lock (_gate)
                    {
                        if (_engine == null)
                        {
                            message = "WinEventUtils is not initialized; nothing to stop.";
                            return false;
                        }
                        _activeCategories = new HashSet<WinEventCategory>();
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Stop", ex);
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
                _activeCategories = new HashSet<WinEventCategory>();
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
                Debug.WriteLine("WinEventUtils.Dispose failed: " + ex.Message);
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------
        // Filter building
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a compact filter JSON string from scalar parameters, for
        /// <see cref="Subscribe"/>/the <c>WaitForX</c> methods/<see cref="WasWindowCreated"/>,
        /// so a Pega automation does not need to hand-author or escape JSON. Every
        /// parameter is optional (null/empty/default = not filtered on); omitted
        /// parameters are left out of the resulting JSON entirely. Never throws.
        /// </summary>
        /// <param name="process">Match a single process name (case-insensitive; a trailing ".exe" is ignored).</param>
        /// <param name="processesCsv">Comma-separated process names to match any of (case-insensitive) - the scalar alternative to <c>WinEventFilter.AnyOfProcesses(params string[])</c>, which is not Pega-friendly.</param>
        /// <param name="className">Match a window class name (case-insensitive), e.g. "#32770" for a dialog.</param>
        /// <param name="titleContains">Require the window title to contain this text (case-insensitive).</param>
        /// <param name="titleMatches">Require the window title to match this regex.</param>
        /// <param name="hasButtonChildren">Require (<c>true</c>) or exclude (<c>false</c>) a Button child window (dialog heuristic); pass null to not filter on this.</param>
        /// <param name="excludeSelf">Skip events whose process id equals this component's host process; pass null to not filter on this.</param>
        /// <returns>The filter JSON string. Returns <c>"{}"</c> (match-all) if every parameter is omitted.</returns>
        public string BuildFilterJson(string process = null, string processesCsv = null, string className = null, string titleContains = null, string titleMatches = null, bool? hasButtonChildren = null, bool? excludeSelf = null)
        {
            try
            {
                var fields = new Dictionary<string, object>();
                if (!string.IsNullOrWhiteSpace(process))
                    fields["process"] = process;
                if (!string.IsNullOrWhiteSpace(processesCsv))
                {
                    var names = new List<string>();
                    foreach (var part in processesCsv.Split(','))
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length > 0)
                            names.Add(trimmed);
                    }
                    if (names.Count > 0)
                        fields["processes"] = names;
                }
                if (!string.IsNullOrWhiteSpace(className))
                    fields["class"] = className;
                if (!string.IsNullOrWhiteSpace(titleContains))
                    fields["titleContains"] = titleContains;
                if (!string.IsNullOrWhiteSpace(titleMatches))
                    fields["titleMatches"] = titleMatches;
                if (hasButtonChildren.HasValue)
                    fields["hasButtonChildren"] = hasButtonChildren.Value;
                if (excludeSelf.HasValue)
                    fields["excludeSelf"] = excludeSelf.Value;

                return fields.Count == 0 ? "{}" : JsonSerializer.Serialize(fields);
            }
            catch
            {
                return "{}";
            }
        }

        /// <summary>
        /// Same as <see cref="BuildFilterJson(string, string, string, string, string, bool?, bool?)"/>,
        /// but takes a single designer-selectable <see cref="WinEventFilterField"/>
        /// instead of remembering which of its seven named parameters to fill
        /// in - the common case of matching on exactly one field, optionally
        /// combined with <paramref name="hasButtonChildren"/> (the one Boolean
        /// field common enough to pair with a single other field - it rarely
        /// narrows a search usefully on its own). Use the full overload
        /// directly for <c>excludeSelf</c>, or to combine more than one
        /// non-Boolean field in a single filter. Never throws.
        /// </summary>
        /// <param name="field">Which field <paramref name="value"/> applies to.</param>
        /// <param name="value">The value to match on <paramref name="field"/>. Null/empty = not filtered on this field.</param>
        /// <param name="hasButtonChildren">Also require a Button child window (dialog heuristic); pass null to not filter on this.</param>
        /// <returns>The filter JSON string. Returns <c>"{}"</c> (match-all) if <paramref name="value"/> is null/empty and <paramref name="hasButtonChildren"/> is null.</returns>
        public string BuildFilterJson(WinEventFilterField field, string value, bool? hasButtonChildren = null)
        {
            return field switch
            {
                WinEventFilterField.Process => BuildFilterJson(process: value, hasButtonChildren: hasButtonChildren),
                WinEventFilterField.ProcessesCsv => BuildFilterJson(processesCsv: value, hasButtonChildren: hasButtonChildren),
                WinEventFilterField.ClassName => BuildFilterJson(className: value, hasButtonChildren: hasButtonChildren),
                WinEventFilterField.TitleContains => BuildFilterJson(titleContains: value, hasButtonChildren: hasButtonChildren),
                WinEventFilterField.TitleMatches => BuildFilterJson(titleMatches: value, hasButtonChildren: hasButtonChildren),
                _ => "{}"
            };
        }

        // ------------------------------------------------------------------
        // Subscribe model
        // ------------------------------------------------------------------

        /// <summary>
        /// Registers a subscription for a single category — the common case;
        /// use <see cref="SubscribeCategories"/> to watch more than one.
        /// Events whose fields match <paramref name="filterJson"/> are queued
        /// under <paramref name="subscriptionId"/>. A null/empty filter
        /// matches all events. Returns True on success; <paramref name="message"/>
        /// is null on success and a human-readable reason otherwise (malformed
        /// filter JSON, invalid <c>titleMatches</c> regex, duplicate id, empty
        /// id). Never throws.
        /// </summary>
        public bool Subscribe(WinEventCategory category, string filterJson, string subscriptionId, out string message)
        {
            message = default;
            try
            {
                message = null;
                try
                {
                    return SubscribeCore(new HashSet<WinEventCategory> { category }, filterJson, subscriptionId, out message);
                }
                catch (Exception ex)
                {
                    message = "Subscribe failed: " + ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Subscribe", ex);
                return false;
            }
        }

        /// <summary>
        /// Registers a subscription across the given categories, with one
        /// Boolean checkbox per <see cref="WinEventCategory"/> instead of a
        /// comma-separated, typo-prone string. Each flag is nullable so an
        /// unwired port is treated the same as an explicit <c>False</c> — only
        /// <c>True</c> includes a category. Events whose fields match
        /// <paramref name="filterJson"/> are queued under
        /// <paramref name="subscriptionId"/>. A null/empty filter matches all
        /// events. Returns True on success; <paramref name="message"/> is null
        /// on success and a human-readable reason otherwise. Never throws.
        /// </summary>
        public bool SubscribeCategories(bool? windows, bool? foreground, bool? dialogs, bool? titles, bool? states, bool? menus, bool? windowOps, bool? session, string filterJson, string subscriptionId, out string message)
        {
            message = default;
            try
            {
                message = null;
                try
                {
                    var cats = new HashSet<WinEventCategory>();
                    if (windows == true) cats.Add(WinEventCategory.Windows);
                    if (foreground == true) cats.Add(WinEventCategory.Foreground);
                    if (dialogs == true) cats.Add(WinEventCategory.Dialogs);
                    if (titles == true) cats.Add(WinEventCategory.Titles);
                    if (states == true) cats.Add(WinEventCategory.States);
                    if (menus == true) cats.Add(WinEventCategory.Menus);
                    if (windowOps == true) cats.Add(WinEventCategory.WindowOps);
                    if (session == true) cats.Add(WinEventCategory.Session);
                    if (cats.Count == 0)
                    {
                        message = "No categories were selected. Set at least one category parameter to True.";
                        return false;
                    }
                    return SubscribeCore(cats, filterJson, subscriptionId, out message);
                }
                catch (Exception ex)
                {
                    message = "SubscribeCategories failed: " + ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SubscribeCategories", ex);
                return false;
            }
        }

        /// <summary>
        /// Shared registration logic for <see cref="Subscribe(WinEventCategory, string, string, out string)"/>
        /// and <see cref="SubscribeCategories"/>: validates the subscription id
        /// and filter, then registers the subscription.
        /// </summary>
        private bool SubscribeCore(HashSet<WinEventCategory> cats, string filterJson, string subscriptionId, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                message = "subscriptionId is empty.";
                return false;
            }
            if (!WinEventFilter.TryFromJson(filterJson, out var filter, out string filterError))
            {
                message = "Invalid filter for '" + subscriptionId + "': " + filterError;
                return false;
            }
            filter = filter ?? WinEventFilter.Create(); // null/empty filter = match-all
            return _subscriptions.TryAdd(subscriptionId, cats, filter, out message);
        }

        /// <summary>
        /// Removes a subscription and drops its queue. Returns True if it
        /// existed; <paramref name="message"/> is null on success and a
        /// human-readable reason otherwise. Never throws.
        /// </summary>
        public bool Unsubscribe(string subscriptionId, out string message)
        {
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Unsubscribe", ex);
                return false;
            }
        }

        /// <summary>
        /// Blocks up to <paramref name="timeoutMs"/> for the next queued event.
        /// Returns True when an event was dequeued into <paramref name="eventData"/>.
        /// Returns False either because the wait timed out with the queue still
        /// empty (a normal result — <paramref name="message"/> is null) or
        /// because of an operational failure such as an unknown subscription
        /// (<paramref name="message"/> is set). There is no separate
        /// <c>hasEvent</c> output. Never throws.
        /// </summary>
        public bool GetNextEvent(string subscriptionId, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                eventData = null;
                message = null;
                try
                {
                    eventData = _subscriptions.GetNextEvent(subscriptionId, timeoutMs, out bool hasEvent, out message);
                    return hasEvent;
                }
                catch (Exception ex)
                {
                    message = "GetNextEvent failed: " + ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetNextEvent", ex);
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
        public bool GetNextEvents(string subscriptionId, int maxCount, int drainMs, out WinEventData[] events, out string message)
        {
            events = default;
            message = default;
            try
            {
                events = Array.Empty<WinEventData>();
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetNextEvents", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="GetNextEvents(string, int, int, out WinEventData[], out string)"/>,
        /// but reports the drained events as a JSON array (the same shape
        /// <see cref="DumpRecentEvents"/> produces), for designers without an
        /// <see cref="WinEventData"/> array/collection proxy.
        /// </summary>
        /// <param name="subscriptionId">The subscription to drain.</param>
        /// <param name="maxCount">Maximum number of events to drain.</param>
        /// <param name="drainMs">Maximum time to wait for the first event, in milliseconds.</param>
        /// <param name="json">The drained events as a JSON array (possibly empty, <c>"[]"</c>), or <c>"[]"</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason (e.g. unknown subscription). Never throws.</param>
        /// <returns><c>true</c> on success; <c>false</c> on a real failure. Never throws.</returns>
        public bool GetNextEventsJson(string subscriptionId, int maxCount, int drainMs, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                json = "[]";
                bool ok = GetNextEvents(subscriptionId, maxCount, drainMs, out WinEventData[] events, out message);
                json = JsonSerializer.Serialize(events ?? Array.Empty<WinEventData>(), WinEventJson.Options);
                return ok;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                json = "[]";
                message = NeverThrowsGuard.Failure("GetNextEventsJson", ex);
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
            count = default;
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("HasEvents", ex);
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
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ClearQueue", ex);
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
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetDebounce", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="SetDebounce(string, int, out string)"/>, but takes the
        /// repository-owned <see cref="WinEventName"/> enum instead of a free-form,
        /// typo-prone string, for designer selection and validation.
        /// </summary>
        public bool SetDebounce(WinEventName eventName, int debounceMs, out string message)
        {
            return SetDebounce(eventName.ToString(), debounceMs, out message);
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
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetQueueLimits", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="SetQueueLimits(int, string, out string)"/>, but takes
        /// the repository-owned <see cref="WinEventOverflowPolicy"/> enum instead of a
        /// free-form string, for designer selection and validation.
        /// </summary>
        public bool SetQueueLimits(int maxEvents, WinEventOverflowPolicy overflowPolicy, out string message)
        {
            return SetQueueLimits(maxEvents, overflowPolicy.ToString(), out message);
        }

        /// <summary>
        /// Returns the last <paramref name="count"/> events (newest last) as a
        /// JSON array in <paramref name="json"/>, regardless of subscriptions.
        /// Returns True on success; <paramref name="message"/> is null on
        /// success and a human-readable reason otherwise. Never throws.
        /// </summary>
        public bool DumpRecentEvents(int count, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                json = "[]";
                message = null;
                try
                {
                    var engine = _engine;
                    var events = engine != null ? engine.SnapshotRing(Math.Max(0, count)) : Array.Empty<WinEventData>();
                    json = JsonSerializer.Serialize(events, WinEventJson.Options);
                    return true;
                }
                catch (Exception ex)
                {
                    message = "DumpRecentEvents failed: " + ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                json = NeverThrowsGuard.Failure("DumpRecentEvents", ex);
                return false;
            }
        }

        /// <summary>
        /// Non-blocking lookback: reports whether a window-created event matching
        /// <paramref name="filterJson"/> was captured within the last
        /// <paramref name="withinLastMs"/> milliseconds. Returns True when found,
        /// False when not found or when the check itself failed —
        /// <paramref name="message"/> distinguishes the two: null for "not
        /// found" (a normal result), non-null for an operational failure
        /// (engine not started, malformed filter). Never throws.
        /// </summary>
        public bool WasWindowCreated(string filterJson, int withinLastMs, out string message)
        {
            message = default;
            try
            {
                message = null;
                try
                {
                    var engine = _engine;
                    if (engine == null)
                    {
                        message = "Engine not started; call Initialize() and Start() first.";
                        return false;
                    }
                    if (!WinEventFilter.TryFromJson(filterJson, out var filter, out string filterError))
                    {
                        message = filterError;
                        return false;
                    }
                    filter = filter ?? WinEventFilter.Create();
                    long cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromMilliseconds(Math.Max(0, withinLastMs)).Ticks;
                    foreach (var e in engine.SnapshotRing(500))
                    {
                        if (e.Category == "WindowCreated" && e.Timestamp >= cutoff && filter.Matches(e, _hostPid))
                        {
                            return true;
                        }
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    message = "WasWindowCreated failed: " + ex.Message;
                    return false;
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WasWindowCreated", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        /// <summary>Runs on the hook thread: debounce, then fan out to subscriptions and waiters.</summary>
        private void OnEventReceived(WinEventData data, List<WinEventCategory> cats)
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
    }
}
