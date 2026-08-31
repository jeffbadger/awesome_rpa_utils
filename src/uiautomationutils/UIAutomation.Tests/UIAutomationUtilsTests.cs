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
            Assert.False(_uia.GetBoundingRectangleAsRectangle(null, out _, out string m5));
            Assert.False(string.IsNullOrEmpty(m5));
            Assert.False(_uia.IsEnabledSimple(null, out string m6));
            Assert.False(string.IsNullOrEmpty(m6));
            Assert.False(_uia.IsOffscreenSimple(null, out string m7));
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
            Assert.False(_uia.IsToggledSimple(null, out string m13));
            Assert.False(string.IsNullOrEmpty(m13));
            Assert.False(_uia.Expand(null, out string m14));
            Assert.False(string.IsNullOrEmpty(m14));
            Assert.False(_uia.Collapse(null, out string m15));
            Assert.False(string.IsNullOrEmpty(m15));
            Assert.False(_uia.Select(null, out string m16));
            Assert.False(string.IsNullOrEmpty(m16));
            Assert.False(_uia.IsSelectedSimple(null, out string m17));
            Assert.False(string.IsNullOrEmpty(m17));
            Assert.False(_uia.GetChildren(null, out _, out string m18));
            Assert.False(string.IsNullOrEmpty(m18));
            Assert.False(_uia.FindAllByControlType(null, UiControlType.Button, out _, out string m19));
            Assert.False(string.IsNullOrEmpty(m19));
            Assert.False(_uia.GetChildrenSummaryJson(null, out _, out string m20));
            Assert.False(string.IsNullOrEmpty(m20));
        }

        // --- New scalar/querySucceeded overloads: same null-element guard as their originals ---

        [Fact]
        public void NewScalarOverloads_NullElement_ReturnFalseWithMessage()
        {
            Assert.False(_uia.GetBoundingRectangle(null, out int _, out int _, out int _, out int _, out string m1));
            Assert.False(string.IsNullOrEmpty(m1));
            Assert.False(_uia.IsEnabled(null, out bool q1, out string m2));
            Assert.False(q1);
            Assert.False(string.IsNullOrEmpty(m2));
            Assert.False(_uia.IsOffscreen(null, out bool q2, out string m3));
            Assert.False(q2);
            Assert.False(string.IsNullOrEmpty(m3));
            Assert.False(_uia.IsToggled(null, out bool q3, out string m4));
            Assert.False(q3);
            Assert.False(string.IsNullOrEmpty(m4));
            Assert.False(_uia.IsSelected(null, out bool q4, out string m5));
            Assert.False(q4);
            Assert.False(string.IsNullOrEmpty(m5));
            Assert.False(_uia.HighlightElement(null, red: 255, green: 0, blue: 0, out string m6));
            Assert.False(string.IsNullOrEmpty(m6));
            Assert.False(_uia.HighlightElement(null, System.Drawing.Color.Red, out string m7));
            Assert.False(string.IsNullOrEmpty(m7));
        }

        // --- Element-list accessors: null list and out-of-range index guards ---

        [Fact]
        public void GetElementCount_NullList_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.GetElementCount(null, out int count, out string message));
            Assert.Equal(0, count);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetElementCount_EmptyList_ReturnsZero()
        {
            Assert.True(_uia.GetElementCount(new System.Collections.Generic.List<AutomationElement>(), out int count, out string message));
            Assert.Equal(0, count);
            Assert.Null(message);
        }

        [Fact]
        public void GetElementAt_NullList_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.GetElementAt(null, 0, out AutomationElement element, out string message));
            Assert.Null(element);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public void GetElementAt_IndexOutOfRangeForEmptyList_ReturnsFalseWithMessage(int index)
        {
            Assert.False(_uia.GetElementAt(new System.Collections.Generic.List<AutomationElement>(), index, out AutomationElement element, out string message));
            Assert.Null(element);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Wait guards: an argument error aborts the poll immediately (no timeout stall) ---

        [Fact]
        public void WaitForElementByAutomationIdSimple_NullParent_AbortsImmediatelyWithMessage()
        {
            Assert.False(_uia.WaitForElementByAutomationIdSimple(null, "ok", timeoutMs: 5000, pollIntervalMs: 10,
                out AutomationElement element, out string message));

            Assert.Null(element);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForElementByNameSimple_NullParent_AbortsImmediatelyWithMessage()
        {
            Assert.False(_uia.WaitForElementByNameSimple(null, "ok", exactMatch: true, timeoutMs: 5000, pollIntervalMs: 10,
                out AutomationElement element, out string message));

            Assert.Null(element);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForElementByAutomationIdWithTimedOut_NullParent_AbortsImmediatelyWithoutTimeout()
        {
            Assert.False(_uia.WaitForElementByAutomationId(null, "ok", timeoutMs: 5000, pollIntervalMs: 10,
                out AutomationElement element, out bool timedOut, out string message));

            Assert.Null(element);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForElementByNameWithTimedOut_NullParent_AbortsImmediatelyWithoutTimeout()
        {
            Assert.False(_uia.WaitForElementByName(null, "ok", exactMatch: true, timeoutMs: 5000, pollIntervalMs: 10,
                out AutomationElement element, out bool timedOut, out string message));

            Assert.Null(element);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- One-shot window-handle-scoped methods: a zero handle is false + message, never throws ---

        [Fact]
        public void GetChildrenFromWindowHandle_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.GetChildrenFromWindowHandle(IntPtr.Zero, out System.Collections.Generic.List<AutomationElement> children, out string message));
            Assert.Null(children);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetChildrenSummaryJsonFromWindowHandle_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.GetChildrenSummaryJsonFromWindowHandle(IntPtr.Zero, out string json, out string message));
            Assert.Null(json);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void InvokeByAutomationId_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.InvokeByAutomationId(IntPtr.Zero, "ok", out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SetValueByAutomationId_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.SetValueByAutomationId(IntPtr.Zero, "ok", "value", out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetValueByAutomationId_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.GetValueByAutomationId(IntPtr.Zero, "ok", out string value, out string message));
            Assert.Null(value);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void InvokeByName_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.InvokeByName(IntPtr.Zero, "ok", out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SetValueByName_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.SetValueByName(IntPtr.Zero, "ok", "value", out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetValueByName_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.GetValueByName(IntPtr.Zero, "ok", out string value, out string message));
            Assert.Null(value);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SelectListItemByName_ZeroHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_uia.SelectListItemByName(IntPtr.Zero, "Country", "United States", out string message));
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