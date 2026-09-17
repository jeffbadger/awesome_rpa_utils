using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WinEventAutomation
{
    /// <summary>
    /// The Wait model: synchronous <c>WaitForX</c> calls that block until a
    /// matching event or timeout. These are thin sugar over the same engine as
    /// the Subscribe model — each call registers a matcher, not a new mechanism.
    /// Call <see cref="WinEventUtils.Start(WinEventCategory, out string)"/> or
    /// <see cref="WinEventUtils.StartCategories"/> first; if the engine is not
    /// running a wait returns False immediately with a message. A wait always
    /// resolves to exactly one event — the first match — so every method here
    /// returns a single <see cref="WinEventData"/>, not JSON or an array.
    /// Every method returns <c>bool</c> and never throws: any failure —
    /// including a timeout — returns False with a message via
    /// <c>out string message</c> explaining what happened.
    /// </summary>
    public partial class WinEventUtils
    {
        /// <summary>
        /// Waits for a window-created event matching the filter.
        /// </summary>
        public bool WaitForWindowCreated(string filterJson, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                return WaitFor(filterJson, timeoutMs, "WindowCreated", out eventData, out message,
                    e => e.Category == "WindowCreated");

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForWindowCreated", ex);
                return false;
            }
        }

        /// <summary>
        /// Waits for a window-destroyed event matching the filter.
        /// </summary>
        public bool WaitForWindowDestroyed(string filterJson, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                return WaitFor(filterJson, timeoutMs, "WindowDestroyed", out eventData, out message,
                    e => e.Category == "WindowDestroyed");

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForWindowDestroyed", ex);
                return false;
            }
        }

        /// <summary>
        /// Waits for a window-shown event matching the filter.
        /// </summary>
        public bool WaitForWindowShown(string filterJson, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                return WaitFor(filterJson, timeoutMs, "WindowShown", out eventData, out message,
                    e => e.Category == "WindowShown");

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForWindowShown", ex);
                return false;
            }
        }

        /// <summary>
        /// Waits for a foreground-change event matching the filter.
        /// </summary>
        public bool WaitForForegroundChanged(string filterJson, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                return WaitFor(filterJson, timeoutMs, "ForegroundChanged", out eventData, out message,
                    e => e.Category == "ForegroundChanged");

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForForegroundChanged", ex);
                return false;
            }
        }

        /// <summary>
        /// Waits for a title-change event matching the filter whose new title
        /// matches <paramref name="titleRegex"/> (null/empty matches any title).
        /// A regex that does not compile returns False with a message before any
        /// waiting begins.
        /// </summary>
        public bool WaitForTitleChanged(string filterJson, string titleRegex, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                var re = TryCompileRegex(titleRegex, "titleRegex", out message);
                if (message != null)
                {
                    eventData = null;
                    return false;
                }
                return WaitFor(filterJson, timeoutMs, "TitleChanged", out eventData, out message,
                    e => e.Category == "TitleChanged" && (re == null || re.IsMatch(e.Title ?? string.Empty)));

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForTitleChanged", ex);
                return false;
            }
        }

        /// <summary>
        /// Waits for a dialog event (SYSTEM_DIALOGSTART/END or a "#32770" window
        /// being created/shown) matching the filter.
        /// </summary>
        public bool WaitForDialogAppeared(string filterJson, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                return WaitFor(filterJson, timeoutMs, "DialogAppeared", out eventData, out message,
                    e => e.Category == "DialogAppeared" || e.Category == "DialogClosed" ||
                    (string.Equals(e.ClassName, "#32770", StringComparison.OrdinalIgnoreCase) &&
                     (e.Category == "WindowCreated" || e.Category == "WindowShown")));

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForDialogAppeared", ex);
                return false;
            }
        }

        /// <summary>
        /// Waits for a state-change event matching the filter whose normalized
        /// state matches <paramref name="stateRegex"/> (null/empty matches any).
        /// A regex that does not compile returns False with a message before any
        /// waiting begins.
        /// </summary>
        public bool WaitForStateChanged(string filterJson, string stateRegex, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                var re = TryCompileRegex(stateRegex, "stateRegex", out message);
                if (message != null)
                {
                    eventData = null;
                    return false;
                }
                return WaitFor(filterJson, timeoutMs, "StateChanged", out eventData, out message,
                    e => e.Category == "StateChanged" && (re == null || re.IsMatch(e.State ?? string.Empty)));

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForStateChanged", ex);
                return false;
            }
        }

        /// <summary>
        /// Waits for a menu-opened or menu-popup-opened event matching the filter.
        /// </summary>
        public bool WaitForMenuOpened(string filterJson, int timeoutMs, out WinEventData eventData, out string message)
        {
            eventData = default;
            message = default;
            try
            {
                return WaitFor(filterJson, timeoutMs, "MenuOpened", out eventData, out message,
                    e => e.Category == "MenuOpened" || e.Category == "MenuPopupOpened");

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForMenuOpened", ex);
                return false;
            }
        }

        /// <summary>
        /// Releases all pending waits immediately; each returns False and
        /// reports a timeout. Returns True on success;
        /// <paramref name="message"/> is null on success and a human-readable
        /// reason otherwise. Never throws.
        /// </summary>
        public bool CancelWaits(out string message)
        {
            message = default;
            try
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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CancelWaits", ex);
                return false;
            }
        }

        /// <summary>
        /// Shared wait core. Filter and timeout are validated before any waiting:
        /// a malformed filter JSON, an unusable timeout (0 or negative is an
        /// immediate timeout), or a stopped engine all return False with a
        /// message instead of waiting. A timeout is reported the same way as any
        /// other failure — False plus a message explaining it — rather than a
        /// separate output parameter.
        /// </summary>
        private bool WaitFor(string filterJson, int timeoutMs, string eventName, out WinEventData eventData, out string message, Func<WinEventData, bool> predicate)
        {
            eventData = null;
            message = null;
            try
            {
                if (!WinEventFilter.TryFromJson(filterJson, out var filter, out string filterError))
                {
                    message = filterError;
                    return false;
                }
                filter = filter ?? WinEventFilter.Create(); // null/empty filter = match-all
                if (_engine == null || _activeCategories.Count == 0)
                {
                    message = "Engine not started; call Initialize() and Start() first.";
                    return false;
                }
                var task = _waiters.Register(e => predicate(e) && filter.Matches(e, _hostPid), timeoutMs);
                var result = task.GetAwaiter().GetResult();
                if (result == null)
                {
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

        /// <summary>
        /// Compiles with IgnoreCase|Compiled and a 250 ms match timeout so a
        /// catastrophic-backtracking pattern cannot hang a wait. Null pattern is
        /// "match any"; an invalid pattern is rejected via
        /// <paramref name="message"/> rather than silently matching everything.
        /// </summary>
        private static Regex TryCompileRegex(string pattern, string paramName, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(pattern))
                return null;
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
            }
            catch (Exception ex)
            {
                message = "Invalid regular expression in " + paramName + " '" + pattern + "': " + ex.Message;
                return null;
            }
        }
    }
}
