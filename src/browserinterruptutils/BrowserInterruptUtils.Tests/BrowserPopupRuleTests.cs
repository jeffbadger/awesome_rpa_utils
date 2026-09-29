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
                ruleName: "", nameContains: "Cookies", automationIdContains: null, processName: null, roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.WatchOnly);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_RejectsRuleWithNoMatchCriteria()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "CookieBanner", nameContains: null, automationIdContains: null, processName: null, roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.WatchOnly);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_RejectsCloseWindowPatternForPageOverlay()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "CookieBanner", nameContains: "Cookies", automationIdContains: null, processName: null, roleContains: null,
                scope: BrowserPopupScope.PageOverlay, action: BrowserPopupAction.CloseWindowPattern);

            Assert.NotNull(message);
        }

        [Fact]
        public void ValidateCommon_AcceptsCloseWindowPatternForNativeDialog()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "JsAlert", nameContains: "Alert", automationIdContains: null, processName: null, roleContains: null,
                scope: BrowserPopupScope.NativeDialog, action: BrowserPopupAction.CloseWindowPattern);

            Assert.Null(message);
        }

        [Fact]
        public void ValidateCommon_AcceptsWellFormedRule()
        {
            string message = BrowserPopupRule.ValidateCommon(
                ruleName: "CookieBanner", nameContains: "Accept all", automationIdContains: null, processName: "chrome", roleContains: "dialog",
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
