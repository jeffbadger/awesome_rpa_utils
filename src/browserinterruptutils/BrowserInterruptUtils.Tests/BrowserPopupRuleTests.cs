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
                ruleName: "", nameContains: "Cookies", messageContains: null, automationIdContains: null, processName: null, roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.WatchOnly);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_RejectsRuleWithNoMatchCriteria()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "CookieBanner", nameContains: null, messageContains: null, automationIdContains: null, processName: null, roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.WatchOnly);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_RejectsCloseWindowPatternForPageOverlay()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "CookieBanner", nameContains: "Cookies", messageContains: null, automationIdContains: null, processName: null, roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.CloseWindowPattern);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_AcceptsCloseWindowPatternForNativeDialog()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "JsAlert", nameContains: "Alert", messageContains: null, automationIdContains: null, processName: null, roleContains: null,
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
        [InlineData("PageOverlay", "WatchOnly", "Cookies", null, null, null, null, true)]
        [InlineData("PageOverlay", "WatchOnly", null, null, "consent", null, null, true)]
        // native: name, message or role is enough for a non-close rule
        [InlineData("NativeDialog", "WatchOnly", null, "sure?", null, null, null, true)]
        [InlineData("NativeDialog", "InvokeByName", null, null, null, "dialog", null, true)]
        // native close: needs name or message, role alone is not enough
        [InlineData("NativeDialog", "CloseWindowPattern", null, null, null, "dialog", "chrome", false)]
        [InlineData("NativeDialog", "CloseWindowPattern", "Alert", null, null, null, null, true)]
        [InlineData("NativeDialog", "CloseWindowPattern", null, "sure?", null, null, null, true)]
        public void ValidateCommon_RequiresAScopeSpecificCriterion(string scope, string action,
            string name, string message, string automationId, string role, string process, bool valid)
        {
            string result = BrowserPopupRule.ValidateCommon("Rule", name, message, automationId, process, role,
                (BrowserPopupScope)System.Enum.Parse(typeof(BrowserPopupScope), scope),
                (BrowserPopupAction)System.Enum.Parse(typeof(BrowserPopupAction), action));

            Assert.Equal(valid, result == null);
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
                MessageContains = "expired"
            };

            var candidate = new BrowserElementInfo { Name = "Session dialog" };

            Assert.True(rule.NeedsMessage);
            Assert.True(rule.Matches(candidate, processName: null, messageText: "Your session has expired"));
            Assert.False(rule.Matches(candidate, processName: null, messageText: "Everything is fine"));
        }

        [Fact]
        public void Matches_ReturnsFalseForNullCandidate()
        {
            var rule = new BrowserPopupRule { RuleName = "Any", NameContains = "x" };

            Assert.False(rule.Matches(null, processName: null, messageText: null));
        }
    }
}
