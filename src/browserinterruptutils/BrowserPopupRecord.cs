using System;

namespace BrowserInterruptAutomation
{
    /// <summary>What kind of event a <see cref="BrowserPopupRecord"/> describes.</summary>
    internal enum BrowserPopupRecordKind
    {
        Detected,
        Dismissed,
        DismissFailed,
        Error
    }

    /// <summary>
    /// One entry in <see cref="BrowserPopupEngine"/>'s log: a popup detected, dismissed, or not
    /// dismissed, or a problem with the engine itself. Mirrors <c>InterruptUtils</c>' own
    /// <c>PopupRecord</c>, with two fields added because this component tracks two kinds of
    /// popup: <see cref="Scope"/> (<c>"NativeDialog"</c> or <c>"PageOverlay"</c>) and
    /// <see cref="Role"/> (the candidate's UIA localized control type, e.g. "dialog" or
    /// "button"), since a bare title is not enough to tell the two scopes' entries apart.
    /// Plain data - <see cref="BrowserPopupEngine"/> does no JSON work; that is the public API
    /// layer's job (<c>BrowserInterruptUtils.cs</c>, a later phase), exactly as in the sibling
    /// component.
    /// </summary>
    internal sealed class BrowserPopupRecord
    {
        public BrowserPopupRecordKind Kind { get; set; }
        public string RuleName { get; set; }

        /// <summary><c>"NativeDialog"</c> or <c>"PageOverlay"</c>; empty for an <see cref="BrowserPopupRecordKind.Error"/> record that concerns no particular popup.</summary>
        public string Scope { get; set; }

        /// <summary>The popup element's UIA name.</summary>
        public string Name { get; set; }

        /// <summary>The popup element's UIA localized control type (for example "dialog" or "pane").</summary>
        public string Role { get; set; }

        public string MessageText { get; set; }
        public string ProcessName { get; set; }
        public int ProcessId { get; set; }

        /// <summary>The name of the element invoked or closed, or <c>"(close)"</c> for a <see cref="BrowserPopupAction.CloseWindowPattern"/> dismissal.</summary>
        public string TargetInvoked { get; set; }

        public int Attempts { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Detail { get; set; }
    }
}
