using System;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// Describes one in-page popup the interrupt handler saw, dismissed, or failed to dismiss
    /// (a native JS dialog or an ARIA modal overlay). Raised on a worker thread, not the
    /// automation's thread.
    /// </summary>
    public sealed class BrowserPopupEventArgs : EventArgs
    {
        internal BrowserPopupEventArgs(string ruleName, string scope, string name, string messageText, string role,
            string processName, int processId, string targetInvoked, int attempts, DateTime timestampUtc, string detail)
        {
            RuleName = ruleName;
            Scope = scope;
            Name = name;
            MessageText = messageText;
            Role = role;
            ProcessName = processName;
            ProcessId = processId;
            TargetInvoked = targetInvoked;
            Attempts = attempts;
            TimestampUtc = timestampUtc;
            Detail = detail;
        }

        /// <summary>The name of the rule that matched the popup.</summary>
        public string RuleName { get; }

        /// <summary>Which kind of popup this was: <c>"NativeDialog"</c> (a native JS alert/confirm/prompt) or <c>"PageOverlay"</c> (an in-page ARIA modal).</summary>
        public string Scope { get; }

        /// <summary>The popup element's UIA name.</summary>
        public string Name { get; }

        /// <summary>The popup's message text (its first non-empty text descendant), or an empty string if it has none or it could not be read.</summary>
        public string MessageText { get; }

        /// <summary>The popup element's UIA localized control type (for example "dialog" or "pane").</summary>
        public string Role { get; }

        /// <summary>The name of the process that owns the popup, without <c>.exe</c>; empty if it could not be determined.</summary>
        public string ProcessName { get; }

        /// <summary>The ID of the process that owns the popup.</summary>
        public int ProcessId { get; }

        /// <summary>The name of the element that was invoked or closed, or <c>null</c> for a watch-only rule or a failed dismissal.</summary>
        public string TargetInvoked { get; }

        /// <summary>How many times a dismissal was attempted; 0 for a watch-only detection.</summary>
        public int Attempts { get; }

        /// <summary>When it happened, in UTC.</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>For a failed dismissal, why; otherwise empty.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// Describes a problem in the interrupt handler itself (for example a rule that stopped
    /// auto-dismissing because its popup kept coming back). Raised on a worker thread, not the
    /// automation's thread.
    /// </summary>
    public sealed class BrowserInterruptErrorEventArgs : EventArgs
    {
        internal BrowserInterruptErrorEventArgs(string ruleName, string message, DateTime timestampUtc)
        {
            RuleName = ruleName;
            Message = message;
            TimestampUtc = timestampUtc;
        }

        /// <summary>The rule the problem concerns; empty if it concerns the handler as a whole.</summary>
        public string RuleName { get; }

        /// <summary>A human-readable description of the problem.</summary>
        public string Message { get; }

        /// <summary>When it happened, in UTC.</summary>
        public DateTime TimestampUtc { get; }
    }
}
