using System;
using System.Windows.Automation;
using UIAutomation;
using Xunit;

namespace UIAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for UIAutomationUtils' input guards, plus the
    /// never-throw contract on the paths that return before any UIA/System call. These
    /// run anywhere (including non-Windows shells), so they deliberately avoid creating
    /// real UIA elements (impossible without a desktop session) — live element behavior
    /// is covered by the Pega Unit Test plan in the repo's TESTING.md.
    /// </summary>
    public class UIAutomationUtilsTests
    {
        private readonly UIAutomationUtils _uia = new UIAutomationUtils();

        // --- Find guards: only the null-parent guard is reachable without a real UIA element ---

        [Fact]
        public void FindByAutomationId_NullParent_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.FindByAutomationId(null, "ok", out _, out string parentMessage));
            Assert.False(string.IsNullOrEmpty(parentMessage));
        }

        [Fact]
        public void FindByClassName_NullParent_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.FindByClassName(null, "anything", out _, out string parentMessage));
            Assert.False(string.IsNullOrEmpty(parentMessage));
        }

        // --- Property/action guards: null element is false + message, never throws ---

        [Fact]
        public void NullElementGuards_ReturnFalseWithMessage()
        {
            Assert.False(_uia.GetName(null, out _, out string m1));
            Assert.False(string.IsNullOrEmpty(m1));
            Assert.False(_uia.GetAutomationId(null, out _, out string m2));
            Assert.False(string.IsNullOrEmpty(m2));
            Assert.False(_uia.GetClassName(null, out _, out string m3));
            Assert.False(string.IsNullOrEmpty(m3));
            Assert.False(_uia.GetControlTypeName(null, out _, out string m4));
            Assert.False(string.IsNullOrEmpty(m4));
            Assert.False(_uia.GetBoundingRectangle(null, out _, out string m5));
            Assert.False(string.IsNullOrEmpty(m5));
            Assert.False(_uia.IsEnabled(null, out string m6));
            Assert.False(string.IsNullOrEmpty(m6));
            Assert.False(_uia.IsOffscreen(null, out string m7));
            Assert.False(string.IsNullOrEmpty(m7));
            Assert.False(_uia.IsElementAvailable(null));
            Assert.False(_uia.Invoke(null, out string m8));
            Assert.False(string.IsNullOrEmpty(m8));
            Assert.False(_uia.SetValue(null, "v", out string m9));
            Assert.False(string.IsNullOrEmpty(m9));
            Assert.False(_uia.SetValue(null, null, out string m10));
            Assert.False(string.IsNullOrEmpty(m10));
            Assert.False(_uia.GetValue(null, out _, out string m11));
            Assert.False(string.IsNullOrEmpty(m11));
            Assert.False(_uia.Toggle(null, out string m12));
            Assert.False(string.IsNullOrEmpty(m12));
            Assert.False(_uia.IsToggled(null, out string m13));
            Assert.False(string.IsNullOrEmpty(m13));
            Assert.False(_uia.Expand(null, out string m14));
            Assert.False(string.IsNullOrEmpty(m14));
            Assert.False(_uia.Collapse(null, out string m15));
            Assert.False(string.IsNullOrEmpty(m15));
            Assert.False(_uia.Select(null, out string m16));
            Assert.False(string.IsNullOrEmpty(m16));
            Assert.False(_uia.IsSelected(null, out string m17));
            Assert.False(string.IsNullOrEmpty(m17));
            Assert.False(_uia.GetChildren(null, out _, out string m18));
            Assert.False(string.IsNullOrEmpty(m18));
            Assert.False(_uia.FindAllByControlType(null, UiControlType.Button, out _, out string m19));
            Assert.False(string.IsNullOrEmpty(m19));
        }

        // --- Wait guards: an argument error aborts the poll immediately (no timeout stall) ---

        [Fact]
        public void WaitForElementByAutomationId_NullParent_AbortsImmediatelyWithMessage()
        {
            Assert.False(_uia.WaitForElementByAutomationId(null, "ok", timeoutMs: 5000, pollIntervalMs: 10,
                out AutomationElement element, out string message));

            Assert.Null(element);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForElementByName_NullParent_AbortsImmediatelyWithMessage()
        {
            Assert.False(_uia.WaitForElementByName(null, "ok", exactMatch: true, timeoutMs: 5000, pollIntervalMs: 10,
                out AutomationElement element, out string message));

            Assert.Null(element);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- UiControlType mapping: every enum value maps, undefined values report a message ---

        [Fact]
        public void UiControlType_MapsEveryDefinedValue()
        {
            if (!OperatingSystem.IsWindows())
                return; // ControlType's static initialization requires the Windows UIA runtime

            foreach (UiControlType type in Enum.GetValues(typeof(UiControlType)))
            {
                Assert.True(UIAutomationUtils.TryToControlType(type, out _, out string message),
                    $"{type} should map to a UIA ControlType ({message}).");
                Assert.Null(message);
            }
        }

        [Fact]
        public void UiControlType_UndefinedValue_ReportsMessageAndFails()
        {
            if (!OperatingSystem.IsWindows())
                return;

            Assert.False(UIAutomationUtils.TryToControlType((UiControlType)9999, out _, out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}