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

        /// <summary>Close the popup's own window (its UIA Window pattern). Valid only for <see cref="BrowserPopupScope.NativeDialog"/>.</summary>
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
        /// Whether the candidate satisfies every criterion the rule sets. <paramref name="processName"/>
        /// and <paramref name="messageText"/> are passed in, rather than resolved here, because both cost
        /// a call into the probe; the engine resolves <paramref name="messageText"/> only when
        /// <see cref="NeedsMessage"/> says it is worth the cost.
        /// </summary>
        public bool Matches(BrowserElementInfo candidate, string processName, string messageText)
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
            if (!string.IsNullOrEmpty(MessageContains) && !Contains(messageText, MessageContains))
                return false;
            return true;
        }

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
        /// Checks the arguments common to every kind of rule. A rule must be named, must say at
        /// least one of name, automation ID, process or role (so no rule can mean "dismiss
        /// whatever popup appears"), and must not pair <see cref="BrowserPopupAction.CloseWindowPattern"/>
        /// with <see cref="BrowserPopupScope.PageOverlay"/>: closing a window makes no sense for an
        /// element that has none. This is the one place scope/action compatibility is enforced
        /// defensively; the public API (a later phase) only exposes the valid combinations as
        /// distinct methods in the first place.
        /// </summary>
        /// <returns>A message describing the problem, or <c>null</c> if the arguments are acceptable.</returns>
        internal static string ValidateCommon(string ruleName, string nameContains, string automationIdContains,
            string processName, string roleContains, BrowserPopupScope scope, BrowserPopupAction action)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                return "ruleName may not be empty.";
            if (ruleName.Trim().Length > MaxNameLength)
                return "ruleName may be at most " + MaxNameLength + " characters.";
            if (string.IsNullOrWhiteSpace(nameContains) && string.IsNullOrWhiteSpace(automationIdContains)
                && string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(roleContains))
                return "A rule needs at least one of nameContains, automationIdContains, processName or roleContains, so it can never match every popup.";
            if (action == BrowserPopupAction.CloseWindowPattern && scope != BrowserPopupScope.NativeDialog)
                return "CloseWindowPattern is only valid for a NativeDialog rule.";
            return null;
        }
    }
}
