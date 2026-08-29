using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EventAutomation
{
    /// <summary>
    /// The Wait model: synchronous <c>WaitForX</c> calls that block until a
    /// matching event or timeout. These are thin sugar over the same engine as
    /// the Subscribe model — each call registers a matcher, not a new mechanism.
    /// Call <see cref="EventUtils.Start(string, out string)"/> first; if the
    /// engine is not running a wait returns False immediately with a message.
    /// Every method returns <c>bool</c> and never throws: a timeout is reported
    /// through <c>out bool timedOut</c> and abnormal results through
    /// <c>out string message</c>.
    /// </summary>
    public partial class EventUtils
    {
        /// <summary>Waits for a window-created event matching the filter.</summary>
        public bool WaitForWindowCreated(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)
        {
            return WaitFor(filterJson, timeoutMs, "WindowCreated", out eventData, out timedOut, out message,
                e => e.Category == "WindowCreated");
        }

        /// <summary>Waits for a window-destroyed event matching the filter.</summary>
        public bool WaitForWindowDestroyed(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)
        {
            return WaitFor(filterJson, timeoutMs, "WindowDestroyed", out eventData, out timedOut, out message,
                e => e.Category == "WindowDestroyed");
        }

        /// <summary>Waits for a window-shown event matching the filter.</summary>
        public bool WaitForWindowShown(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)
        {
            return WaitFor(filterJson, timeoutMs, "WindowShown", out eventData, out timedOut, out message,
                e => e.Category == "WindowShown");
        }

        /// <summary>Waits for a foreground-change event matching the filter.</summary>
        public bool WaitForForegroundChanged(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)
        {
            return WaitFor(filterJson, timeoutMs, "ForegroundChanged", out eventData, out timedOut, out message,
                e => e.Category == "ForegroundChanged");
        }

        /// <summary>
        /// Waits for a title-change event matching the filter whose new title
        /// matches <paramref name="titleRegex"/> (null/empty matches any title).
        /// </summary>
        public bool WaitForTitleChanged(string filterJson, string titleRegex, int timeoutMs, out EventData eventData, out bool timedOut, out string message)
        {
            var re = CompileRegex(titleRegex);
            return WaitFor(filterJson, timeoutMs, "TitleChanged", out eventData, out timedOut, out message,
                e => e.Category == "TitleChanged" && (re == null || re.IsMatch(e.Title ?? string.Empty)));
        }

        /// <summary>
        /// Waits for a dialog event (SYSTEM_DIALOGSTART/END or a "#32770" window
        /// being created/shown) matching the filter.
        /// </summary>
        public bool WaitForDialogAppeared(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)
        {
            return WaitFor(filterJson, timeoutMs, "DialogAppeared", out eventData, out timedOut, out message,
                e => e.Category == "DialogAppeared" || e.Category == "DialogClosed" ||
                (string.Equals(e.ClassName, "#32770", StringComparison.OrdinalIgnoreCase) &&
                 (e.Category == "WindowCreated" || e.Category == "WindowShown")));
        }

        /// <summary>
        /// Waits for a state-change event matching the filter whose normalized
        /// state matches <paramref name="stateRegex"/> (null/empty matches any).
        /// </summary>
        public bool WaitForStateChanged(string filterJson, string stateRegex, int timeoutMs, out EventData eventData, out bool timedOut, out string message)
        {
            var re = CompileRegex(stateRegex);
            return WaitFor(filterJson, timeoutMs, "StateChanged", out eventData, out timedOut, out message,
                e => e.Category == "StateChanged" && (re == null || re.IsMatch(e.State ?? string.Empty)));
        }

        /// <summary>Waits for a menu-opened or menu-popup-opened event matching the filter.</summary>
        public bool WaitForMenuOpened(string filterJson, int timeoutMs, out EventData eventData, out bool timedOut, out string message)
        {
            return WaitFor(filterJson, timeoutMs, "MenuOpened", out eventData, out timedOut, out message,
                e => e.Category == "MenuOpened" || e.Category == "MenuPopupOpened");
        }

        /// <summary>
        /// Releases all pending waits immediately; each returns False and
        /// reports a timeout. Returns True on success;
        /// <paramref name="message"/> is null on success and a human-readable
        /// reason otherwise. Never throws.
        /// </summary>
        public bool CancelWaits(out string message)
        {
            message = null;
            try
            {
                _waiters.CancelAll();
                return true;
            }
            catch (Exception ex)
            {
                message = "CancelWaits failed: " + ex.Message;
                return false;
            }
        }

        private bool WaitFor(string filterJson, int timeoutMs, string eventName, out EventData eventData, out bool timedOut, out string message, Func<EventData, bool> predicate)
        {
            eventData = null;
            timedOut = false;
            message = null;
            try
            {
                if (_engine == null || _activeCategories.Count == 0)
                {
                    message = "Engine not started; call Initialize() and Start() first.";
                    return false;
                }
                var filter = EventFilter.FromJson(filterJson) ?? EventFilter.Create();
                var task = _waiters.Register(e => predicate(e) && filter.Matches(e, _hostPid), Math.Max(0, timeoutMs));
                var result = task.GetAwaiter().GetResult();
                if (result == null)
                {
                    timedOut = true;
                    message = "Timed out after " + Math.Max(0, timeoutMs) + " ms waiting for " + eventName + ".";
                    return false;
                }
                eventData = result;
                return true;
            }
            catch (Exception ex)
            {
                message = "WaitFor" + eventName + " failed: " + ex.Message;
                return false;
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
