using System;

namespace InterruptAutomation
{
    /// <summary>
    /// Describes one popup the interrupt handler saw, dismissed, or failed to dismiss.
    /// Raised on a worker thread, not the automation's thread.
    /// </summary>
    public sealed class InterruptPopupEventArgs : EventArgs
    {
        internal InterruptPopupEventArgs(string ruleName, string title, string messageText, string processName,
            uint processId, string buttonClicked, int attempts, DateTime timestampUtc, string detail)
        {
            RuleName = ruleName;
            Title = title;
            MessageText = messageText;
            ProcessName = processName;
            ProcessId = processId;
            ButtonClicked = buttonClicked;
            Attempts = attempts;
            TimestampUtc = timestampUtc;
            Detail = detail;
        }

        /// <summary>The name of the rule that matched the popup.</summary>
        public string RuleName { get; }

        /// <summary>The popup's title bar text.</summary>
        public string Title { get; }

        /// <summary>The popup's message text (its first non-empty static text), or an empty string if it has none or it could not be read.</summary>
        public string MessageText { get; }

        /// <summary>The name of the process that owns the popup, without <c>.exe</c>; empty if it could not be determined.</summary>
        public string ProcessName { get; }

        /// <summary>The ID of the process that owns the popup.</summary>
        public uint ProcessId { get; }

        /// <summary>The text of the button that was clicked (for a close rule, <c>(close)</c>); empty for a watch-only rule or a failed dismissal.</summary>
        public string ButtonClicked { get; }

        /// <summary>How many times a dismissal was attempted; 0 for a watch-only detection.</summary>
        public int Attempts { get; }

        /// <summary>When it happened, in UTC.</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>For a failed dismissal, why; otherwise empty.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// Describes a problem in the interrupt handler itself (for example a rule that
    /// stopped auto-dismissing because its popup kept coming back). Raised on a worker
    /// thread, not the automation's thread.
    /// </summary>
    public sealed class InterruptErrorEventArgs : EventArgs
    {
        internal InterruptErrorEventArgs(string ruleName, string message, DateTime timestampUtc)
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
