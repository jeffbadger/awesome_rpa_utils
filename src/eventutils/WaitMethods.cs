using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EventAutomation
{
    /// <summary>
    /// The Wait model: synchronous <c>WaitForX</c> calls that block until a
    /// matching event or timeout. These are thin sugar over the same engine as
    /// the Subscribe model — each call registers a matcher, not a new mechanism.
    /// Call <see cref="EventUtils.Start"/> first; if the engine is not running a
    /// wait returns immediately and reports a timeout.
    /// </summary>
    public partial class EventUtils
    {
        /// <summary>Waits for a window-created event matching the filter.</summary>
        public EventData WaitForWindowCreated(string filterJson, int timeoutMs, out bool timedOut)
        {
            return WaitFor(filterJson, timeoutMs, out timedOut, e => e.Category == "WindowCreated");
        }

        /// <summary>Waits for a window-destroyed event matching the filter.</summary>
        public EventData WaitForWindowDestroyed(string filterJson, int timeoutMs, out bool timedOut)
        {
            return WaitFor(filterJson, timeoutMs, out timedOut, e => e.Category == "WindowDestroyed");
        }

        /// <summary>Waits for a window-shown event matching the filter.</summary>
        public EventData WaitForWindowShown(string filterJson, int timeoutMs, out bool timedOut)
        {
            return WaitFor(filterJson, timeoutMs, out timedOut, e => e.Category == "WindowShown");
        }

        /// <summary>Waits for a foreground-change event matching the filter.</summary>
        public EventData WaitForForegroundChanged(string filterJson, int timeoutMs, out bool timedOut)
        {
            return WaitFor(filterJson, timeoutMs, out timedOut, e => e.Category == "ForegroundChanged");
        }

        /// <summary>
        /// Waits for a title-change event matching the filter whose new title
        /// matches <paramref name="titleRegex"/> (null/empty matches any title).
        /// </summary>
        public EventData WaitForTitleChanged(string filterJson, string titleRegex, int timeoutMs, out bool timedOut)
        {
            var re = CompileRegex(titleRegex);
            return WaitFor(filterJson, timeoutMs, out timedOut, e =>
                e.Category == "TitleChanged" && (re == null || re.IsMatch(e.Title ?? string.Empty)));
        }

        /// <summary>
        /// Waits for a dialog event (SYSTEM_DIALOGSTART/END or a "#32770" window
        /// being created/shown) matching the filter.
        /// </summary>
        public EventData WaitForDialogAppeared(string filterJson, int timeoutMs, out bool timedOut)
        {
            return WaitFor(filterJson, timeoutMs, out timedOut, e =>
                e.Category == "DialogAppeared" || e.Category == "DialogClosed" ||
                (string.Equals(e.ClassName, "#32770", StringComparison.OrdinalIgnoreCase) &&
                 (e.Category == "WindowCreated" || e.Category == "WindowShown")));
        }

        /// <summary>
        /// Waits for a state-change event matching the filter whose normalized
        /// state matches <paramref name="stateRegex"/> (null/empty matches any).
        /// </summary>
        public EventData WaitForStateChanged(string filterJson, string stateRegex, int timeoutMs, out bool timedOut)
        {
            var re = CompileRegex(stateRegex);
            return WaitFor(filterJson, timeoutMs, out timedOut, e =>
                e.Category == "StateChanged" && (re == null || re.IsMatch(e.State ?? string.Empty)));
        }

        /// <summary>Waits for a menu-opened or menu-popup-opened event matching the filter.</summary>
        public EventData WaitForMenuOpened(string filterJson, int timeoutMs, out bool timedOut)
        {
            return WaitFor(filterJson, timeoutMs, out timedOut, e =>
                e.Category == "MenuOpened" || e.Category == "MenuPopupOpened");
        }

        /// <summary>
        /// Releases all pending waits immediately; each returns null and reports
        /// a timeout. Never throws.
        /// </summary>
        public void CancelWaits()
        {
            try
            {
                _waiters.CancelAll();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.CancelWaits failed: " + ex.Message);
            }
        }

        private EventData WaitFor(string filterJson, int timeoutMs, out bool timedOut, Func<EventData, bool> predicate)
        {
            timedOut = false;
            try
            {
                if (_engine == null || _activeCategories.Count == 0)
                {
                    Debug.WriteLine("EventUtils.WaitFor: engine not started; call Start() first.");
                    timedOut = true;
                    return null;
                }
                var filter = EventFilter.FromJson(filterJson) ?? EventFilter.Create();
                var task = _waiters.Register(e => predicate(e) && filter.Matches(e, _hostPid), Math.Max(0, timeoutMs));
                var result = task.GetAwaiter().GetResult();
                if (result == null)
                    timedOut = true;
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventUtils.WaitFor failed: " + ex.Message);
                timedOut = true;
                return null;
            }
        }

        private static Regex CompileRegex(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch
            {
                return null;
            }
        }
    }
}
