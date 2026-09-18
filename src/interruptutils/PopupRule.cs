using System;
using System.Collections.Generic;

namespace InterruptAutomation
{
    /// <summary>What a rule does to a popup it matches.</summary>
    internal enum PopupAction
    {
        ClickButtonText,
        ClickButtonId,
        CloseWindow,
        WatchOnly
    }

    /// <summary>
    /// One "known popup" rule: how to recognize the popup and what to do about it.
    /// Matching is by case-insensitive substring, and every criterion that is set must
    /// match. Rules are immutable apart from <see cref="Enabled"/> and <see cref="Tripped"/>.
    /// </summary>
    internal sealed class PopupRule
    {
        /// <summary>The window class a rule matches when it does not name one: the standard dialog class.</summary>
        internal const string DialogClass = "#32770";

        /// <summary>A class name meaning "any window class".</summary>
        internal const string AnyClass = "*";

        internal const int MaxNameLength = 64;

        public string Name { get; set; }
        public string TitleContains { get; set; }
        public string MessageContains { get; set; }
        public string ProcessName { get; set; }
        public string ClassName { get; set; }
        public PopupAction Action { get; set; }
        public string ButtonText { get; set; }
        public bool ExactButtonText { get; set; }
        public int ButtonId { get; set; }

        /// <summary>Whether the rule is active. Cleared by <c>SetRuleEnabled</c>.</summary>
        public volatile bool Enabled = true;

        /// <summary>Set when the rule stopped itself for dismissing too many popups; cleared by re-enabling it.</summary>
        public volatile bool Tripped;

        /// <summary>When the rule recently dismissed popups (milliseconds on the engine clock); guarded by the engine's lock.</summary>
        internal readonly Queue<long> RecentDismissals = new Queue<long>();

        public bool NeedsMessage => !string.IsNullOrEmpty(MessageContains);

        /// <summary>
        /// Whether the window's class, title and owning process satisfy the rule. The
        /// message text is checked separately (<see cref="MatchesMessage"/>) because
        /// reading it costs a message to another application.
        /// </summary>
        public bool MatchesWindow(PopupWindowInfo info, Func<string> processName)
        {
            if (info == null)
                return false;
            if (ClassName != AnyClass && !string.Equals(ClassName, info.ClassName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(TitleContains) && !Contains(info.Title, TitleContains))
                return false;
            if (!string.IsNullOrEmpty(ProcessName) && !ProcessNamesMatch(ProcessName, processName()))
                return false;
            return true;
        }

        public bool MatchesMessage(string messageText) => Contains(messageText, MessageContains);

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

        /// <summary>Removes the <c>&amp;</c> access-key marker from button text ("&amp;Yes" becomes "Yes"; "&amp;&amp;" is a literal ampersand).</summary>
        internal static string StripMnemonic(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0)
                return text ?? string.Empty;

            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '&')
                {
                    if (i + 1 < text.Length && text[i + 1] == '&')
                    {
                        sb.Append('&');
                        i++;
                    }
                    continue;
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Checks the arguments common to every kind of rule. A rule must be named and must
        /// say at least one of title, message or process, so that no rule can mean "click
        /// whatever dialog appears".
        /// </summary>
        /// <returns>A message describing the problem, or <c>null</c> if the arguments are acceptable.</returns>
        internal static string ValidateCommon(string name, string titleContains, string messageContains, string processName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "ruleName may not be empty.";
            if (name.Trim().Length > MaxNameLength)
                return "ruleName may be at most " + MaxNameLength + " characters.";
            if (string.IsNullOrWhiteSpace(titleContains) && string.IsNullOrWhiteSpace(messageContains) && string.IsNullOrWhiteSpace(processName))
                return "A rule needs at least one of titleContains, messageContains or processName, so it can never match every dialog.";
            return null;
        }
    }
}
