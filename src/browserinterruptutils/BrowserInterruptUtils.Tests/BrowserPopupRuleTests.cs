using BrowserInterruptAutomation;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    public class BrowserPopupRuleTests
    {
        [Fact]
        public void ValidateCommon_RejectsEmptyRuleName()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "", nameContains: "Cookies", messageContains: null, automationIdContains: null, processName: "chrome", roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.WatchOnly);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_RejectsRuleWithNoMatchCriteria()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "CookieBanner", nameContains: null, messageContains: null, automationIdContains: null, processName: "chrome", roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.WatchOnly);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_RejectsCloseWindowPatternForPageOverlay()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "CookieBanner", nameContains: "Cookies", messageContains: null, automationIdContains: null, processName: "chrome", roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.CloseWindowPattern);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_AcceptsCloseWindowPatternForNativeDialog()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "JsAlert", nameContains: "Alert", messageContains: null, automationIdContains: null, processName: "chrome", roleContains: null,
                scope: BrowserPopupScope.NativeDialog, action: BrowserPopupAction.CloseWindowPattern);

            Assert.Null(message);
        }

        [Theory]
        // process alone is never enough, in either scope
        [InlineData("NativeDialog", "WatchOnly", null, null, null, null, "chrome", false)]
        [InlineData("NativeDialog", "InvokeByName", null, null, null, null, "chrome", false)]
        [InlineData("PageOverlay", "WatchOnly", null, null, null, null, "chrome", false)]
        // overlay: message alone is not enough; role, name or id is
        [InlineData("PageOverlay", "WatchOnly", null, "cookies", null, null, "chrome", false)]
        [InlineData("PageOverlay", "WatchOnly", null, null, null, "dialog", "chrome", true)]
        [InlineData("PageOverlay", "WatchOnly", "Cookies", null, null, null, "chrome", true)]
        [InlineData("PageOverlay", "WatchOnly", null, null, "consent", null, "chrome", true)]
        // native: name, message or role is enough for a non-close rule
        [InlineData("NativeDialog", "WatchOnly", null, "sure?", null, null, "chrome", true)]
        [InlineData("NativeDialog", "InvokeByName", null, null, null, "dialog", "chrome", true)]
        // native close: needs name or message, role alone is not enough
        [InlineData("NativeDialog", "CloseWindowPattern", null, null, null, "dialog", "chrome", false)]
        [InlineData("NativeDialog", "CloseWindowPattern", "Alert", null, null, null, "chrome", true)]
        [InlineData("NativeDialog", "CloseWindowPattern", null, "sure?", null, null, "chrome", true)]
        public void ValidateCommon_RequiresAScopeSpecificCriterion(string scope, string action,
            string name, string message, string automationId, string role, string process, bool valid)
        {
            string result = BrowserPopupRule.ValidateCommon("Rule", name, message, automationId, process, role,
                (BrowserPopupScope)System.Enum.Parse(typeof(BrowserPopupScope), scope),
                (BrowserPopupAction)System.Enum.Parse(typeof(BrowserPopupAction), action));

            Assert.Equal(valid, result == null);
        }

        [Theory]
        [InlineData("NativeDialog", "InvokeByName", null)]
        [InlineData("NativeDialog", "InvokeByName", "")]
        [InlineData("NativeDialog", "InvokeByAutomationId", "   ")]
        [InlineData("NativeDialog", "CloseWindowPattern", ".exe")]
        [InlineData("NativeDialog", "WatchOnly", " .EXE ")]
        [InlineData("PageOverlay", "InvokeByName", null)]
        [InlineData("PageOverlay", "InvokeByAutomationId", "")]
        [InlineData("PageOverlay", "WatchOnly", ".exe")]
        [InlineData("PageOverlay", "WatchOnly", "  ")]
        public void ValidateCommon_RequiresAProcessName_ForEveryScopeAndAction(string scope, string action, string process)
        {
            // Otherwise well formed for the scope/action: only the process is missing.
            string result = BrowserPopupRule.ValidateCommon("Rule", "Alert", "sure?", null, process, "dialog",
                (BrowserPopupScope)System.Enum.Parse(typeof(BrowserPopupScope), scope),
                (BrowserPopupAction)System.Enum.Parse(typeof(BrowserPopupAction), action));

            Assert.Equal(BrowserPopupRule.ProcessRequiredMessage, result);
            Assert.Contains("processName is required", result);
        }

        [Theory]
        [InlineData("chrome")]
        [InlineData("chrome.exe")]
        [InlineData(" msedge.EXE ")]
        public void ValidateCommon_AcceptsAProcessNameWithOrWithoutExe(string process)
        {
            Assert.Null(BrowserPopupRule.ValidateCommon("Rule", "Alert", null, null, process, null,
                BrowserPopupScope.NativeDialog, BrowserPopupAction.InvokeByName));
        }

        [Fact]
        public void Matches_NeverMatchesWithoutAProcessName()
        {
            // Defense in depth: a rule that somehow has no process matches nothing, not everything.
            var rule = new BrowserPopupRule { RuleName = "r", Scope = BrowserPopupScope.NativeDialog, NameContains = "x" };
            var candidate = new BrowserElementInfo { Name = "x" };

            Assert.False(rule.Matches(candidate, processName: "chrome", messageText: null));
            Assert.False(rule.Matches(candidate, processName: null, messageText: null));
        }

        [Fact]
        public void ValidateCommon_ProcessOnlyMessagesExplainWhy()
        {
            string native = BrowserPopupRule.ValidateCommon("R", null, null, null, "chrome", null, BrowserPopupScope.NativeDialog, BrowserPopupAction.WatchOnly);
            string overlay = BrowserPopupRule.ValidateCommon("R", null, null, null, "chrome", null, BrowserPopupScope.PageOverlay, BrowserPopupAction.WatchOnly);

            Assert.Contains("main window", native);
            Assert.Contains("every element on the page", overlay);
        }

        [Fact]
        public void ValidateCommon_AcceptsWellFormedRule()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "CookieBanner", nameContains: "Accept all", messageContains: null, automationIdContains: null, processName: "chrome", roleContains: "dialog",
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.InvokeByName);

            Assert.Null(message);
        }

        [Fact]
        public void Matches_RequiresEveryNonEmptyCriterionToMatch()
        {
            var rule = new BrowserPopupRule
            {
                RuleName = "CookieBanner",
                Scope = BrowserPopupScope.PageOverlay,
                NameContains = "Accept",
                RoleContains = "dialog",
                ProcessName = "chrome.exe"
            };

            var candidate = new BrowserElementInfo
            {
                Name = "Accept all cookies",
                LocalizedControlType = "dialog",
                AutomationId = "cookie-consent"
            };

            Assert.True(rule.Matches(candidate, processName: "chrome", messageText: null));
            Assert.False(rule.Matches(candidate, processName: "firefox", messageText: null));
        }

        [Fact]
        public void Matches_ChecksMessageContainsOnlyWhenSet()
        {
            var rule = new BrowserPopupRule
            {
                RuleName = "SessionExpired",
                Scope = BrowserPopupScope.PageOverlay,
                NameContains = "Session",
                MessageContains = "expired",
                ProcessName = "chrome"
            };

            var candidate = new BrowserElementInfo { Name = "Session dialog" };

            Assert.True(rule.NeedsMessage);
            Assert.True(rule.Matches(candidate, processName: "chrome", messageText: "Your session has expired"));
            Assert.False(rule.Matches(candidate, processName: "chrome", messageText: "Everything is fine"));
        }

        [Fact]
        public void Matches_ReturnsFalseForNullCandidate()
        {
            var rule = new BrowserPopupRule { RuleName = "Any", NameContains = "x", ProcessName = "chrome" };

            Assert.False(rule.Matches(null, processName: null, messageText: null));
        }
    }
}
