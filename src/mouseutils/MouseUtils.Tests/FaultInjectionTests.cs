using System;
using MouseAutomation;
using Xunit;

namespace MouseAutomation.Tests
{
    /// <summary>
    /// Tests that simulate native-call failures/exceptions via the internal
    /// SendInputOverride/GetCursorPosOverride/SetCursorPosOverride seams (see
    /// MouseUtils.cs, "P/Invoke - Cursor Position"/"P/Invoke - Input Injection"),
    /// rather than requiring a live desktop. xUnit creates a fresh instance of this
    /// class per test and calls Dispose() after each one, which resets all three
    /// static seams here - this is what keeps one test's injected fault from leaking
    /// into the next, since the seams are necessarily static (the P/Invoke wrappers
    /// they replace are static too).
    /// </summary>
    public class FaultInjectionTests : IDisposable
    {
        private readonly MouseUtils _mouse = new MouseUtils();

        public void Dispose()
        {
            MouseUtils.SendInputOverride = null;
            MouseUtils.GetCursorPosOverride = null;
            MouseUtils.SetCursorPosOverride = null;
            _mouse.Dispose();
        }

        // --- Recoverable exceptions at the native boundary convert to false + message,
        // never escape (the never-throws contract holds even at this seam). ---

        [Fact]
        public void Click_SendInputThrows_ReturnsFalseWithMessage()
        {
            MouseUtils.SendInputOverride = _ => throw new InvalidOperationException("simulated failure");

            bool ok = _mouse.Click(MouseButton.Left, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Compound operations still attempt (and surface the result of) their
        // release step even when the primary action failed or the release itself
        // fails - never silently drop a cleanup failure. ---

        [Fact]
        public void Click_ButtonUpFailsAfterButtonDownSucceeds_ReturnsFalseAndStillAttemptsRelease()
        {
            int callCount = 0;
            MouseUtils.SendInputOverride = inputs =>
            {
                callCount++;
                return callCount == 1 ? (uint)inputs.Length : 0u; // down succeeds, up fails
            };

            bool ok = _mouse.Click(MouseButton.Left, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal(2, callCount); // both the down and the up were attempted
        }

        [Fact]
        public void DragAndDrop_MoveFailsMidDrag_StillReleasesButton()
        {
            MouseUtils.GetCursorPosOverride = (out MouseUtils.POINT p) => { p = new MouseUtils.POINT { X = 0, Y = 0 }; return true; };
            int setCursorPosCalls = 0;
            MouseUtils.SetCursorPosOverride = (x, y) =>
            {
                setCursorPosCalls++;
                return setCursorPosCalls == 1; // the initial MoveTo succeeds, every later move fails
            };
            MouseUtils.SendInputOverride = inputs => (uint)inputs.Length; // button down/up always succeed

            bool ok = _mouse.DragAndDrop(0, 0, 100, 100, out string message);

            Assert.False(ok); // the move itself failed
            Assert.False(string.IsNullOrEmpty(message));
            // The button-up SendInput call must still have happened despite the move
            // failure - verified indirectly: MouseDown succeeded (tracked internally),
            // and Dispose (which the test fixture calls) would re-attempt a release if
            // MouseUp had never run. Directly confirm no release is pending by disposing
            // now and checking no further SendInput call occurs.
            int callsBeforeDispose = 0;
            MouseUtils.SendInputOverride = inputs => { callsBeforeDispose++; return (uint)inputs.Length; };
            _mouse.Dispose();
            Assert.Equal(0, callsBeforeDispose);
        }

        [Fact]
        public void ClickAndRestore_RestoreFails_ReturnsFalseAndNeverReportsSuccess()
        {
            MouseUtils.GetCursorPosOverride = (out MouseUtils.POINT p) => { p = new MouseUtils.POINT { X = 5, Y = 5 }; return true; };
            int setCursorPosCalls = 0;
            MouseUtils.SetCursorPosOverride = (x, y) =>
            {
                setCursorPosCalls++;
                return setCursorPosCalls != 2; // the move to the target succeeds; the restore (2nd call) fails
            };
            MouseUtils.SendInputOverride = inputs => (uint)inputs.Length; // the click itself always succeeds

            bool ok = _mouse.ClickAndRestore(10, 10, MouseButton.Left, out string message);

            Assert.False(ok);
            Assert.Contains("restored", message, StringComparison.OrdinalIgnoreCase);
        }

        // --- ClickWithModifiers/RubberBandSelect: a failed release must surface or be
        // recovered individually, never silently vanish. ---

        [Fact]
        public void ClickWithModifiers_UpBatchFailsTwice_FallsBackToIndividualReleases()
        {
            int callCount = 0;
            MouseUtils.SendInputOverride = inputs =>
            {
                callCount++;
                // Call 1: down batch (succeeds). Calls 2-3: up batch + its one retry (both
                // fail, forcing the individual-release fallback). Calls 4+: the individual
                // per-event releases (succeed).
                if (callCount is 2 or 3) return 0u;
                return (uint)inputs.Length;
            };

            bool ok = _mouse.ClickWithModifiers(MouseButton.Left, ModifierKeys.Control, out string message);

            // The original (non-fallback) up-batch attempt is what the return value
            // reflects, per this method's documented behavior - the individual fallback
            // is a best-effort recovery, not something that flips the result back to true.
            Assert.False(ok);
            // down(1) + up attempt(1) + up retry(1) + 2 individual up events = 5 calls.
            Assert.Equal(5, callCount);
        }

        [Fact]
        public void RubberBandSelect_ModifierReleaseFails_ReturnsFalseWithMessage()
        {
            MouseUtils.GetCursorPosOverride = (out MouseUtils.POINT p) => { p = new MouseUtils.POINT { X = 0, Y = 0 }; return true; };
            MouseUtils.SetCursorPosOverride = (x, y) => true;
            MouseUtils.SendInputOverride = inputs =>
            {
                // Identify the modifier-key-up batch by inspecting the events themselves
                // (type 1 = keyboard, dwFlags bit 0x0002 = KEYEVENTF_KEYUP) rather than by
                // call order, since that order depends on internal details (e.g. how many
                // intermediate moves SmoothMoveTo performs) this test shouldn't need to know.
                foreach (MouseUtils.INPUT input in inputs)
                {
                    if (input.type == 1 && (input.U.ki.dwFlags & 0x0002) != 0)
                        return 0u;
                }
                return (uint)inputs.Length;
            };

            bool ok = _mouse.RubberBandSelect(0, 0, 10, 10, ModifierKeys.Control, 1, 1, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- MouseDown/MouseUp: the one intentionally stateful pair in this class -
        // confirm each call sends exactly the one expected event, and that Dispose
        // (see PR 3) releases a button MouseDown left held. ---

        [Fact]
        public void MouseDown_ThenMouseUp_EachSendsExactlyOneEvent()
        {
            int callCount = 0;
            MouseUtils.SendInputOverride = inputs =>
            {
                callCount++;
                Assert.Single(inputs);
                return (uint)inputs.Length;
            };

            Assert.True(_mouse.MouseDown(MouseButton.Left, out string downMessage));
            Assert.Equal(1, callCount);

            Assert.True(_mouse.MouseUp(MouseButton.Left, out string upMessage));
            Assert.Equal(2, callCount);
        }

        [Fact]
        public void Dispose_ReleasesButtonHeldViaMouseDown()
        {
            MouseUtils.SendInputOverride = inputs => (uint)inputs.Length;
            Assert.True(_mouse.MouseDown(MouseButton.Left, out string message));

            int releaseCallCount = 0;
            bool releaseSawLeftUp = false;
            MouseUtils.SendInputOverride = inputs =>
            {
                releaseCallCount++;
                foreach (MouseUtils.INPUT input in inputs)
                {
                    if (input.type == 0 && (input.U.mi.dwFlags & 0x0004) != 0) // INPUT_MOUSE, MOUSEEVENTF_LEFTUP
                        releaseSawLeftUp = true;
                }
                return (uint)inputs.Length;
            };

            _mouse.Dispose();

            Assert.Equal(1, releaseCallCount);
            Assert.True(releaseSawLeftUp);
        }

        [Fact]
        public void Dispose_WithNoButtonHeld_SendsNoInput()
        {
            int callCount = 0;
            MouseUtils.SendInputOverride = inputs => { callCount++; return (uint)inputs.Length; };

            _mouse.Dispose();

            Assert.Equal(0, callCount);
        }
    }
}
