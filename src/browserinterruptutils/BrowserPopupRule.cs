using System;
using System.Collections.Generic;

namespace BrowserInterruptAutomation
{
    /// <summary>Which kind of in-page popup a rule matches.</summary>
    internal enum BrowserPopupScope
    {
        /// <summary>A native JS <c>alert</c>/<c>confirm</c>/<c>prompt</c> dialog: its own top-level window.</summary>
        NativeDialog,

        /// <summary>An in-page ARIA modal overlay: an element inside the browser window's own page, with no window of its own.</summary>
        PageOverlay
    }

    /// <summary>What a rule does to a popup it matches.</summary>
    internal enum BrowserPopupAction
    {
        /// <summary>Invoke a descendant element found by its name (<see cref="BrowserPopupRule.TargetElementName"/>).</summary>
        InvokeByName,

        /// <summary>Invoke a descendant element found by its automation ID (<see cref="BrowserPopupRule.TargetAutomationId"/>).</summary>
        InvokeByAutomationId,

        /// <summary>
        /// Close the popup's own window (its UIA Window pattern). Valid only for <see cref="BrowserPopupScope.NativeDialog"/>.
        /// The engine refuses (one <c>DismissFailed</c>, no close attempted) when the window looks like a
        /// main application window (<see cref="BrowserElementInfo.IsMainWindowLike"/>), since a name
        /// substring can match the browser's own main window. The same refusal applies to the invoke
        /// actions of a NativeDialog rule (their subtree walk would cover the page); only
        /// <see cref="WatchOnly"/> still reports such a window.
        /// </summary>
        CloseWindowPattern,

        /// <summary>Only report the popup; never act on it.</summary>
        WatchOnly
    }

    /// <summary>
    /// One "known popup" rule: how to recognize an in-page popup and what to do about it.
    /// Matching is by case-insensitive substring, and every criterion that is set must match.
    /// Mirrors <c>InterruptUtils</c>' own <c>PopupRule</c>, adapted from Win32 window/button
    /// concepts to UIA element concepts. Rules are immutable apart from <see cref="Enabled"/>.
    /// </summary>
    internal sealed class BrowserPopupRule
    {
        /// <summary>The most rules a caller may register at once (mirrors <c>PopupRule.MaxRules</c>).</summary>
        internal const int MaxRules = 100;

        internal const int MaxNameLength = 64;

        public string RuleName { get; set; }
        public BrowserPopupScope Scope { get; set; }
        public string NameContains { get; set; }
        public string MessageContains { get; set; }
        public string ProcessName { get; set; }
        public string RoleContains { get; set; }

        /// <summary>
        /// Matched against a candidate's automation ID. Nullable; meaningful mainly for a
        /// <see cref="BrowserPopupScope.PageOverlay"/> rule, since a page's own markup is far
        /// more likely to carry a stable <c>id</c>/automation ID than a native dialog is.
        /// </summary>
        public string AutomationIdContains { get; set; }

        public BrowserPopupAction Action { get; set; }

        /// <summary>The name of the element to invoke, for <see cref="BrowserPopupAction.InvokeByName"/>.</summary>
        public string TargetElementName { get; set; }

        /// <summary>The automation ID of the element to invoke, for <see cref="BrowserPopupAction.InvokeByAutomationId"/>.</summary>
        public string TargetAutomationId { get; set; }

        public bool ExactTargetElementName { get; set; }

        /// <summary>Whether the rule is active. Cleared by a caller wanting to pause a rule without removing it.</summary>
        public volatile bool Enabled = true;

        /// <summary>Whether this rule needs the popup's message text fetched before <see cref="Matches"/> can decide.</summary>
        public bool NeedsMessage => !string.IsNullOrEmpty(MessageContains);

        /// <summary>
        /// Whether the candidate satisfies every criterion the rule sets except the message. This is
        /// the cheap phase: it reads only what the candidate already carries, so the engine runs it
        /// first and fetches message text (a bounded subtree walk) only for a candidate that passes.
        /// <paramref name="processName"/> is passed in, rather than resolved here, because resolving it
        /// costs a call into the probe.
        /// </summary>
        public bool MatchesCheap(BrowserElementInfo candidate, string processName)
        {
            if (candidate == null)
                return false;
            if (!string.IsNullOrEmpty(NameContains) && !Contains(candidate.Name, NameContains))
                return false;
            if (!string.IsNullOrEmpty(AutomationIdContains) && !Contains(candidate.AutomationId, AutomationIdContains))
                return false;
            if (!string.IsNullOrEmpty(RoleContains) && !Contains(candidate.LocalizedControlType, RoleContains))
                return false;
            if (!string.IsNullOrEmpty(ProcessName) && !ProcessNamesMatch(ProcessName, processName))
                return false;
            return true;
        }

        /// <summary>The message phase: true when the rule sets no message criterion, or the text contains it.</summary>
        public bool MatchesMessage(string messageText) =>
            string.IsNullOrEmpty(MessageContains) || Contains(messageText, MessageContains);

        /// <summary>
        /// Whether the candidate satisfies every criterion the rule sets: <see cref="MatchesCheap"/>
        /// then <see cref="MatchesMessage"/>. The engine calls the two phases separately so it can
        /// skip fetching <paramref name="messageText"/> when <see cref="NeedsMessage"/> is false or
        /// the cheap phase already failed.
        /// </summary>
        public bool Matches(BrowserElementInfo candidate, string processName, string messageText) =>
            MatchesCheap(candidate, processName) && MatchesMessage(messageText);

        internal static bool Contains(string haystack, string needle) =>
            haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Compares two process names ignoring case and a trailing <c>.exe</c> on either.</summary>
        internal static bool ProcessNamesMatch(string ruleName, string actualName)
        {
            if (string.IsNullOrEmpty(actualName))
                return false;
            return string.Equals(TrimExe(ruleName), TrimExe(actualName), StringComparison.OrdinalIgnoreCase);
        }

        internal static string TrimExe(string name)
        {
            name = (name ?? string.Empty).Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        /// <summary>
        /// Checks the arguments common to every kind of rule. A rule must be named, must not pair
        /// <see cref="BrowserPopupAction.CloseWindowPattern"/> with <see cref="BrowserPopupScope.PageOverlay"/>
        /// (closing a window makes no sense for an element that has none), and must say enough to be
        /// specific to its scope; a process name alone is never enough:
        /// <list type="bullet">
        /// <item><see cref="BrowserPopupScope.NativeDialog"/>: at least one of name, message or role. A
        /// process-only rule matches every top-level window of that process, including the user's
        /// main browser window. A <see cref="BrowserPopupAction.CloseWindowPattern"/> rule needs name or
        /// message specifically, since closing the wrong window is the costly mistake.</item>
        /// <item><see cref="BrowserPopupScope.PageOverlay"/>: at least one of role, name or automation
        /// ID. Every element on the page belongs to the browser process, so a process-only (or
        /// message-only) rule would match every element on the page.</item>
        /// </list>
        /// The public API only exposes the valid scope/action combinations as distinct methods;
        /// this is the one place compatibility is enforced defensively.
        /// </summary>
        /// <returns>A message describing the problem, or <c>null</c> if the arguments are acceptable.</returns>
        internal static string ValidateCommon(string ruleName, string nameContains, string messageContains, string automationIdContains,
            string processName, string roleContains, BrowserPopupScope scope, BrowserPopupAction action)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                return "ruleName may not be empty.";
            if (ruleName.Trim().Length > MaxNameLength)
                return "ruleName may be at most " + MaxNameLength + " characters.";
            if (action == BrowserPopupAction.CloseWindowPattern && scope != BrowserPopupScope.NativeDialog)
                return "CloseWindowPattern is only valid for a NativeDialog rule.";

            bool hasName = !string.IsNullOrWhiteSpace(nameContains);
            bool hasMessage = !string.IsNullOrWhiteSpace(messageContains);
            bool hasId = !string.IsNullOrWhiteSpace(automationIdContains);
            bool hasRole = !string.IsNullOrWhiteSpace(roleContains);

            if (scope == BrowserPopupScope.PageOverlay)
            {
                if (!hasRole && !hasName && !hasId)
                    return "A PageOverlay rule needs at least one of roleContains, nameContains or automationIdContains "
                        + "(a process name or message alone is not enough): every element on the page belongs to the browser "
                        + "process, so such a rule would match every element on the page.";
                return null;
            }

            if (!hasName && !hasMessage && !hasRole)
                return "A NativeDialog rule needs at least one of nameContains, messageContains or roleContains "
                    + "(a process name alone is not enough): a process-only rule would match every window of that process, "
                    + "including the browser's main window.";
            if (action == BrowserPopupAction.CloseWindowPattern && !hasName && !hasMessage)
                return "A NativeDialog close rule needs nameContains or messageContains: closing a window on a role or "
                    + "process match alone could close the browser's main window.";
            return null;
        }
    }
}
