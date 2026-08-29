using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseAutomation
{
    /// <summary>
    /// Specifies which mouse button an action applies to.
    /// </summary>
    public enum MouseButton
    {
        /// <summary>The left (primary) mouse button.</summary>
        Left = 0,

        /// <summary>The right (secondary) mouse button.</summary>
        Right = 1,

        /// <summary>The middle mouse button (scroll wheel press).</summary>
        Middle = 2,

        /// <summary>The first extended button (X1, typically "back").</summary>
        XButton1 = 3,

        /// <summary>The second extended button (X2, typically "forward").</summary>
        XButton2 = 4
    }

    /// <summary>
    /// Identifies one of the standard Windows system cursors.
    /// The numeric values are the Windows OEM cursor resource IDs (OCR_*/IDC_*),
    /// which are used both when loading a cursor and when choosing which system
    /// cursor slot to replace with <see cref="MouseUtils.ReplaceSystemCursor"/>.
    /// </summary>
    public enum SystemCursorType
    {
        /// <summary>Standard arrow (IDC_ARROW / OCR_NORMAL, 32512). This is the slot shown most of the time.</summary>
        Arrow = 32512,

        /// <summary>Text-selection I-beam (IDC_IBEAM, 32513).</summary>
        IBeam = 32513,

        /// <summary>Busy / wait indicator - hourglass or spinning ring (IDC_WAIT, 32514).</summary>
        Wait = 32514,

        /// <summary>Precision crosshair (IDC_CROSS, 32515).</summary>
        Crosshair = 32515,

        /// <summary>Arrow pointing straight up (IDC_UPARROW, 32516).</summary>
        UpArrow = 32516,

        /// <summary>Diagonal resize, top-left / bottom-right (IDC_SIZENWSE, 32642).</summary>
        SizeNorthWestSouthEast = 32642,

        /// <summary>Diagonal resize, top-right / bottom-left (IDC_SIZENESW, 32643).</summary>
        SizeNorthEastSouthWest = 32643,

        /// <summary>Horizontal resize, left / right (IDC_SIZEWE, 32644).</summary>
        SizeWestEast = 32644,

        /// <summary>Vertical resize, up / down (IDC_SIZENS, 32645).</summary>
        SizeNorthSouth = 32645,

        /// <summary>Four-directional move arrows (IDC_SIZEALL, 32646).</summary>
        SizeAll = 32646,

        /// <summary>"Not allowed" circle-with-slash (IDC_NO, 32648).</summary>
        No = 32648,

        /// <summary>Pointing hand / link select (IDC_HAND, 32649).</summary>
        Hand = 32649,

        /// <summary>Arrow with small busy indicator - "working in background" (IDC_APPSTARTING, 32650).</summary>
        AppStarting = 32650
    }

    /// <summary>
    /// Modifier keys that can be held during a click with
    /// <see cref="MouseUtils.ClickWithModifiers(MouseButton, ModifierKeys, out string)"/>. Combinable flags.
    /// </summary>
    [Flags]
    public enum ModifierKeys
    {
        /// <summary>No modifier keys.</summary>
        None = 0,

        /// <summary>The Control key (left or right).</summary>
        Control = 1,

        /// <summary>The Shift key (left or right).</summary>
        Shift = 2,

        /// <summary>The Alt key (left or right). Note: Alt+Click can activate the menu bar in some classic Win32 applications.</summary>
        Alt = 4
    }

    /// <summary>
    /// Pega Robot Studio-ready component that moves, clicks, drags, and scrolls the mouse
    /// using the Windows <c>SendInput</c> / <c>SetCursorPos</c> APIs, and that can change the
    /// cursor's appearance, visibility, and confinement rectangle via <c>SetSystemCursor</c>,
    /// <c>ShowCursor</c>, and <c>ClipCursor</c>.
    /// </summary>
    /// <remarks>
    /// All coordinates are absolute screen pixels and work across multi-monitor setups
    /// (including negative coordinates on monitors left of the primary display).
    /// Input injection requires an interactive, unlocked desktop; it is blocked on the
    /// lock screen / secure desktop and when the target application runs at a higher
    /// integrity level (UIPI). Small delays are built into clicks and drags because many
    /// applications ignore zero-duration synthesized clicks.
    /// </remarks>
    [Description("Controls the mouse: move, click, double-click, drag, scroll, and set the " +
                 "cursor appearance/visibility. Drag this component onto a Pega Robot Studio " +
                 "automation to use its methods.")]
    public class MouseUtils : Component
    {
        #region Construction

        /// <summary>Tracks hide/show calls made through this component (Win32 ShowCursor uses a counter).</summary>
        private bool _cursorHidden;

        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public MouseUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public MouseUtils(IContainer container)
        {
            container?.Add(this);
        }

        /// <summary>
        /// Releases the resources used by the component and detaches it from its container.
        /// MouseUtils holds no unmanaged handles (<c>SetCursorPos</c> / <c>SendInput</c> /
        /// <c>GetAsyncKeyState</c> are all stateless Win32 calls), so there is nothing extra
        /// to release here - this override follows the standard component pattern and gives
        /// you a cleanup hook (e.g. if you later add a global mouse hook or polling timer,
        /// unhook/stop it below). Runs automatically when Pega Robot Studio tears down the
        /// automation's design-surface components.
        /// </summary>
        /// <param name="disposing">
        /// True when called from the public Dispose() method during teardown;
        /// false when called from the finalizer.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // No managed or unmanaged resources to release.
            }

            // Base Component.Dispose detaches this component from its container's site.
            base.Dispose(disposing);
        }

        #endregion

        #region Position

        /// <summary>
        /// Gets the current X coordinate of the cursor.
        /// </summary>
        /// <param name="x">The X coordinate in screen pixels, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the cursor position could not be read.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the underlying GetCursorPos call failed. Never throws.</returns>
        [Category("Mouse - Position")]
        [Description("Gets the current X coordinate of the cursor (screen pixels). Returns True on success; never throws.")]
        public bool GetX(out int x, out string message)
        {
            bool ok = TryGetPoint(out POINT p, out message);
            x = ok ? p.X : 0;
            return ok;
        }

        /// <summary>
        /// Gets the current Y coordinate of the cursor.
        /// </summary>
        /// <param name="y">The Y coordinate in screen pixels, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the cursor position could not be read.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the underlying GetCursorPos call failed. Never throws.</returns>
        [Category("Mouse - Position")]
        [Description("Gets the current Y coordinate of the cursor (screen pixels). Returns True on success; never throws.")]
        public bool GetY(out int y, out string message)
        {
            bool ok = TryGetPoint(out POINT p, out message);
            y = ok ? p.Y : 0;
            return ok;
        }

        /// <summary>
        /// Gets the current cursor position.
        /// </summary>
        /// <param name="position">The cursor's screen coordinates, or <c>default</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the cursor position could not be read.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the underlying GetCursorPos call failed. Never throws.</returns>
        [Category("Mouse - Position")]
        [Description("Gets the current cursor position as a System.Drawing.Point. Returns True on success; never throws.")]
        public bool GetPosition(out System.Drawing.Point position, out string message)
        {
            bool ok = TryGetPoint(out POINT p, out message);
            position = ok ? new System.Drawing.Point(p.X, p.Y) : default;
            return ok;
        }

        /// <summary>
        /// Instantly moves the cursor to the given screen coordinates.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the move failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if SetCursorPos failed. Never throws.</returns>
        [Category("Mouse - Position")]
        [Description("Instantly moves the cursor to the given screen coordinates. Returns True on success; never throws.")]
        public bool MoveTo(int x, int y, out string message)
        {
            return TrySetCursorPos(x, y, out message);
        }

        /// <summary>
        /// Moves the cursor by the given offsets relative to its current position.
        /// </summary>
        /// <param name="deltaX">Horizontal offset in pixels (positive = right).</param>
        /// <param name="deltaY">Vertical offset in pixels (positive = down).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the move failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call failed. Never throws.</returns>
        [Category("Mouse - Position")]
        [Description("Moves the cursor by the given offsets relative to its current position. Returns True on success; never throws.")]
        public bool MoveBy(int deltaX, int deltaY, out string message)
        {
            if (!TryGetPoint(out POINT p, out message))
                return false;
            return MoveTo(p.X + deltaX, p.Y + deltaY, out message);
        }

        /// <summary>
        /// Smoothly moves the cursor to the target position (25 steps, 5 ms per step)
        /// to simulate human movement.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the move failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call failed. Never throws.</returns>
        [Category("Mouse - Position")]
        [Description("Smoothly moves the cursor to the target position (25 steps, 5 ms per step) to simulate human movement. Returns True on success; never throws.")]
        public bool SmoothMoveTo(int x, int y, out string message)
        {
            return SmoothMoveTo(x, y, 25, 5, out message);
        }

        /// <summary>
        /// Smoothly moves the cursor to the target position using the given number of
        /// steps and delay between steps.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="steps">Number of intermediate move events; values below 1 are treated as 1.</param>
        /// <param name="delayMilliseconds">Delay between steps in milliseconds; 0 moves without pausing.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the move failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call failed. Never throws.</returns>
        [Category("Mouse - Position")]
        [Description("Smoothly moves the cursor to the target position using the given number of steps and delay between steps. Returns True on success; never throws.")]
        public bool SmoothMoveTo(int x, int y, int steps, int delayMilliseconds, out string message)
        {
            if (steps < 1) steps = 1;

            if (!TryGetPoint(out POINT start, out message))
                return false;

            for (int i = 1; i <= steps; i++)
            {
                int nx = start.X + (int)((x - start.X) * (double)i / steps);
                int ny = start.Y + (int)((y - start.Y) * (double)i / steps);
                if (!MoveTo(nx, ny, out message))
                    return false;
                if (delayMilliseconds > 0)
                    Thread.Sleep(delayMilliseconds);
            }
            return MoveTo(x, y, out message);
        }

        /// <summary>
        /// Nudges the cursor by a tiny amount and immediately back, leaving its
        /// position unchanged but generating real mouse-move input.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the nudge failed.</param>
        /// <param name="pixels">Distance to nudge in each direction; values below 1 are treated as 1.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call failed. Never throws.</returns>
        /// <remarks>
        /// Intended to be called periodically (e.g. from a Robot Studio loop) during a
        /// long unattended run to reset idle timers and prevent the screen from locking
        /// or a screensaver from starting, without visibly disturbing anything on screen.
        /// Windows clamps the nudge at the screen edges, so if the cursor was sitting
        /// on an edge the round trip would leave it displaced; this method detects
        /// that and puts the cursor back, keeping the "position unchanged" guarantee
        /// unconditional.
        /// </remarks>
        [Category("Mouse - Position")]
        [Description("Nudges the cursor by a tiny amount and back, to reset idle/screensaver timers without disturbing its position. Returns True on success; never throws.")]
        public bool JiggleMouse(out string message, int pixels = 1)
        {
            if (pixels < 1) pixels = 1;
            if (!TryGetPoint(out POINT original, out message))
                return false;
            if (!MoveBy(pixels, 0, out message))
                return false;
            if (!MoveBy(-pixels, 0, out message))
                return false;

            // Edge clamp: the first nudge may not have actually moved the cursor,
            // so the reverse nudge can leave it off the original position - put it back.
            if (!TryGetPoint(out POINT after, out message))
                return false;
            if (after.X != original.X || after.Y != original.Y)
                return TrySetCursorPos(original.X, original.Y, out message);
            return true;
        }

        #endregion

        #region Clicks

        /// <summary>
        /// Clicks the given button at the current cursor position
        /// (a short press/release cycle of ~20 ms).
        /// </summary>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed input injection (locked desktop, UAC/secure desktop, or integrity level). Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Clicks the given button at the current cursor position. Returns True on success; never throws.")]
        public bool Click(MouseButton button, out string message)
        {
            if (!MouseDown(button, out message))
                return false;
            Thread.Sleep(20);
            return MouseUp(button, out message);
        }

        /// <summary>
        /// Moves the cursor to the coordinates and clicks the given button.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Moves the cursor to the coordinates and clicks the given button. Returns True on success; never throws.")]
        public bool ClickAt(int x, int y, MouseButton button, out string message)
        {
            if (!MoveTo(x, y, out message))
                return false;
            Thread.Sleep(30);
            return Click(button, out message);
        }

        /// <summary>
        /// Double-clicks the given button at the current cursor position.
        /// The 50 ms gap between clicks stays within the system double-click time.
        /// </summary>
        /// <param name="button">The mouse button to double-click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Double-clicks the given button at the current cursor position. Returns True on success; never throws.")]
        public bool DoubleClick(MouseButton button, out string message)
        {
            if (!Click(button, out message))
                return false;
            Thread.Sleep(50);
            return Click(button, out message);
        }

        /// <summary>
        /// Moves the cursor to the coordinates and double-clicks the given button.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="button">The mouse button to double-click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Moves the cursor to the coordinates and double-clicks the given button. Returns True on success; never throws.")]
        public bool DoubleClickAt(int x, int y, MouseButton button, out string message)
        {
            if (!MoveTo(x, y, out message))
                return false;
            Thread.Sleep(30);
            return DoubleClick(button, out message);
        }

        /// <summary>
        /// Left-clicks at the current cursor position.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Left-clicks at the current cursor position. Returns True on success; never throws.")]
        public bool LeftClick(out string message) => Click(MouseButton.Left, out message);

        /// <summary>
        /// Right-clicks at the current cursor position.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Right-clicks at the current cursor position. Returns True on success; never throws.")]
        public bool RightClick(out string message) => Click(MouseButton.Right, out message);

        /// <summary>
        /// Middle-clicks at the current cursor position.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Middle-clicks at the current cursor position. Returns True on success; never throws.")]
        public bool MiddleClick(out string message) => Click(MouseButton.Middle, out message);

        /// <summary>
        /// Left-clicks at the given screen coordinates.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Left-clicks at the given screen coordinates. Returns True on success; never throws.")]
        public bool LeftClickAt(int x, int y, out string message) => ClickAt(x, y, MouseButton.Left, out message);

        /// <summary>
        /// Right-clicks at the given screen coordinates.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Right-clicks at the given screen coordinates. Returns True on success; never throws.")]
        public bool RightClickAt(int x, int y, out string message) => ClickAt(x, y, MouseButton.Right, out message);

        /// <summary>
        /// Double left-clicks at the current cursor position.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Double left-clicks at the current cursor position. Returns True on success; never throws.")]
        public bool LeftDoubleClick(out string message) => DoubleClick(MouseButton.Left, out message);

        /// <summary>
        /// Double right-clicks at the current cursor position.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Double right-clicks at the current cursor position. Returns True on success; never throws.")]
        public bool RightDoubleClick(out string message) => DoubleClick(MouseButton.Right, out message);

        /// <summary>
        /// Double left-clicks at the given screen coordinates.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Double left-clicks at the given screen coordinates. Returns True on success; never throws.")]
        public bool LeftDoubleClickAt(int x, int y, out string message) => DoubleClickAt(x, y, MouseButton.Left, out message);

        /// <summary>
        /// Presses and holds the given mouse button. Pair with <see cref="MouseUp"/>.
        /// </summary>
        /// <param name="button">The mouse button to press.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the press failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed input injection. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Presses and holds the given mouse button (pair with MouseUp). Returns True on success; never throws.")]
        public bool MouseDown(MouseButton button, out string message)
        {
            return TrySendMouseButton(button, true, out message);
        }

        /// <summary>
        /// Releases the given mouse button.
        /// </summary>
        /// <param name="button">The mouse button to release.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the release failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed input injection. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Releases the given mouse button. Returns True on success; never throws.")]
        public bool MouseUp(MouseButton button, out string message)
        {
            return TrySendMouseButton(button, false, out message);
        }

        /// <summary>
        /// Holds the given button down for the specified time, then releases it.
        /// </summary>
        /// <param name="button">The mouse button to hold.</param>
        /// <param name="holdMilliseconds">How long to hold the button; values below 0 are treated as 0.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the hold failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed input injection. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Holds the given button down for the specified time, then releases it. Returns True on success; never throws.")]
        public bool ClickAndHold(MouseButton button, int holdMilliseconds, out string message)
        {
            if (!MouseDown(button, out message))
                return false;
            Thread.Sleep(Math.Max(0, holdMilliseconds));
            return MouseUp(button, out message);
        }

        /// <summary>
        /// Clicks the given button while holding the given modifier keys - for example
        /// Control+Click to multi-select grid rows, or Shift+Click to extend a selection.
        /// The modifier presses and click down are injected as one atomic SendInput batch
        /// and the click up and modifier releases as a second, so real user input cannot
        /// interleave before the click lands; the ~20 ms gap between down and up keeps
        /// the click visible to applications that ignore zero-duration synthesized clicks.
        /// </summary>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="modifiers">Modifier keys to hold during the click; combinable flags.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed input injection (locked desktop, UAC/secure desktop, or integrity level). Never throws.</returns>
        /// <remarks>
        /// The click happens at the current cursor position - call <see cref="MoveTo"/> first
        /// to target it. Caveat: Alt+Click activates the menu bar in some classic Win32
        /// applications.
        /// </remarks>
        [Category("Mouse - Click")]
        [Description("Clicks a button while holding modifier keys (Control/Shift/Alt, combinable), injected as two atomic batches with a brief press duration. Returns True on success; never throws.")]
        public bool ClickWithModifiers(MouseButton button, ModifierKeys modifiers, out string message)
        {
            // Batch 1: press the modifiers and the button down, atomically.
            List<INPUT> downBatch = new List<INPUT>();

            if ((modifiers & ModifierKeys.Control) != 0) downBatch.Add(MakeKeyInput(VK_CONTROL, false));
            if ((modifiers & ModifierKeys.Shift)   != 0) downBatch.Add(MakeKeyInput(VK_SHIFT, false));
            if ((modifiers & ModifierKeys.Alt)     != 0) downBatch.Add(MakeKeyInput(VK_MENU, false));

            if (!TryGetButtonFlags(button, true, out uint downFlags, out int data, out message))
                return false;
            TryGetButtonFlags(button, false, out uint upFlags, out _, out _);
            downBatch.Add(MakeMouseInput(downFlags, data));

            if (!TrySendInputs(downBatch.ToArray(), out message))
                return false;

            Thread.Sleep(20); // press duration - see the summary; matches Click's press cycle

            // Batch 2: release the button, then the modifiers in reverse press order.
            List<INPUT> upBatch = new List<INPUT>
            {
                MakeMouseInput(upFlags, data)
            };
            if ((modifiers & ModifierKeys.Alt)     != 0) upBatch.Add(MakeKeyInput(VK_MENU, true));
            if ((modifiers & ModifierKeys.Shift)   != 0) upBatch.Add(MakeKeyInput(VK_SHIFT, true));
            if ((modifiers & ModifierKeys.Control) != 0) upBatch.Add(MakeKeyInput(VK_CONTROL, true));

            bool upOk = TrySendInputs(upBatch.ToArray(), out message);
            if (!upOk)
            {
                // Best-effort retry: a transient SendInput failure must not leave the
                // modifiers or button stuck down (mirrors RubberBandSelect's finally).
                TrySendInputs(upBatch.ToArray(), out _);
            }
            return upOk;
        }

        /// <summary>
        /// Clicks at the given coordinates, then jumps the cursor back to wherever it was.
        /// The round trip is near-instant, so the operator's pointer effectively never
        /// leaves its position - ideal for attended sessions where the automation must
        /// not steal the cursor.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed Win32 cursor/input-injection call. Never throws.</returns>
        /// <remarks>
        /// The cursor is restored even if the click is refused (locked desktop, UIPI), so
        /// the operator is never stranded. The brief visit still raises hover events at the
        /// target, which can trigger tooltips. If the restore itself fails (e.g. the desktop
        /// locked mid-click), the failure is reported in <paramref name="message"/>.
        /// </remarks>
        [Category("Mouse - Click")]
        [Description("Clicks at the given coordinates, then immediately returns the cursor to its original position. Returns True on success; never throws.")]
        public bool ClickAndRestore(int x, int y, MouseButton button, out string message)
        {
            if (!TryGetPoint(out POINT original, out message))
                return false;

            bool moved;
            bool clicked = false;
            string failureMessage = null;
            bool restored = true;
            try
            {
                moved = MoveTo(x, y, out failureMessage);
                if (moved)
                {
                    Thread.Sleep(30);
                    clicked = Click(button, out failureMessage);
                }
            }
            finally
            {
                // Restore even when the click failed - never strand the operator's cursor.
                restored = SetCursorPos(original.X, original.Y);
            }

            if (!restored)
            {
                message = "The cursor could not be restored to its original position after the click attempt.";
                return clicked;
            }

            message = clicked ? null : failureMessage;
            return clicked;
        }

        /// <summary>
        /// Clicks at the given coordinates, retrying on transient input-injection
        /// failures instead of giving up immediately.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="maxAttempts">Maximum number of attempts; values below 1 are treated as 1.</param>
        /// <param name="retryDelayMilliseconds">Delay between attempts in milliseconds.</param>
        /// <param name="message"><c>null</c> on success; otherwise the last attempt's failure reason.</param>
        /// <returns><c>true</c> if any attempt succeeded; <c>false</c> if every attempt failed. Never throws.</returns>
        /// <remarks>
        /// Useful for unattended runs where a momentary UAC flicker or timing hiccup can
        /// cause a single click attempt to fail even though the desktop is otherwise usable.
        /// </remarks>
        [Category("Mouse - Click")]
        [Description("Clicks at the given coordinates, retrying on failure up to maxAttempts times. Returns True if any attempt succeeded; never throws.")]
        public bool ClickWithRetry(int x, int y, MouseButton button, int maxAttempts, int retryDelayMilliseconds, out string message)
        {
            if (maxAttempts < 1) maxAttempts = 1;

            message = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (ClickAt(x, y, button, out message))
                {
                    message = null;
                    return true;
                }
                if (attempt < maxAttempts)
                    Thread.Sleep(Math.Max(0, retryDelayMilliseconds));
            }
            return false;
        }

        /// <summary>
        /// Triple-clicks the given button at the current cursor position - the
        /// select-whole-line/paragraph gesture recognized by most text editors.
        /// </summary>
        /// <param name="button">The mouse button to triple-click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed input injection. Never throws.</returns>
        [Category("Mouse - Click")]
        [Description("Triple-clicks the given button at the current cursor position (select-line/paragraph gesture). Returns True on success; never throws.")]
        public bool TripleClick(MouseButton button, out string message)
        {
            if (!Click(button, out message)) return false;
            Thread.Sleep(50);
            if (!Click(button, out message)) return false;
            Thread.Sleep(50);
            return Click(button, out message);
        }

        #endregion

        #region Drag & Drop

        /// <summary>
        /// Performs a left-button drag from the start coordinates to the end
        /// coordinates (30 steps, 10 ms per step).
        /// </summary>
        /// <param name="startX">Drag start X coordinate in screen pixels.</param>
        /// <param name="startY">Drag start Y coordinate in screen pixels.</param>
        /// <param name="endX">Drag end X coordinate in screen pixels.</param>
        /// <param name="endY">Drag end Y coordinate in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the drag failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Drag")]
        [Description("Performs a left-button drag from the start coordinates to the end coordinates (30 steps, 10 ms per step). Returns True on success; never throws.")]
        public bool DragAndDrop(int startX, int startY, int endX, int endY, out string message)
        {
            return DragAndDrop(startX, startY, endX, endY, 30, 10, out message);
        }

        /// <summary>
        /// Performs a left-button drag from the start coordinates to the end
        /// coordinates, moving smoothly in the given number of steps.
        /// </summary>
        /// <param name="startX">Drag start X coordinate in screen pixels.</param>
        /// <param name="startY">Drag start Y coordinate in screen pixels.</param>
        /// <param name="endX">Drag end X coordinate in screen pixels.</param>
        /// <param name="endY">Drag end Y coordinate in screen pixels.</param>
        /// <param name="steps">Number of intermediate move events during the drag.</param>
        /// <param name="stepDelayMilliseconds">Delay between drag steps in milliseconds.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the drag failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Drag")]
        [Description("Performs a left-button drag from the start coordinates to the end coordinates, moving smoothly in the given number of steps. Returns True on success; never throws.")]
        public bool DragAndDrop(int startX, int startY, int endX, int endY, int steps, int stepDelayMilliseconds, out string message)
        {
            if (!MoveTo(startX, startY, out message)) return false;
            Thread.Sleep(50);
            if (!MouseDown(MouseButton.Left, out message)) return false;
            Thread.Sleep(50);
            if (!SmoothMoveTo(endX, endY, steps, stepDelayMilliseconds, out message)) return false;
            Thread.Sleep(50);
            return MouseUp(MouseButton.Left, out message);
        }

        /// <summary>
        /// Performs a left-button rubber-band drag while holding the given modifier
        /// keys - for example Ctrl-drag to add a region to an existing multi-selection.
        /// (30 steps, 10 ms per step.)
        /// </summary>
        /// <param name="startX">Drag start X coordinate in screen pixels.</param>
        /// <param name="startY">Drag start Y coordinate in screen pixels.</param>
        /// <param name="endX">Drag end X coordinate in screen pixels.</param>
        /// <param name="endY">Drag end Y coordinate in screen pixels.</param>
        /// <param name="modifiers">Modifier keys to hold during the drag; combinable flags.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the drag failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Drag")]
        [Description("Performs a left-button rubber-band drag while holding modifier keys (e.g. Ctrl-drag to add to a selection). Returns True on success; never throws.")]
        public bool RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers, out string message)
        {
            return RubberBandSelect(startX, startY, endX, endY, modifiers, 30, 10, out message);
        }

        /// <summary>
        /// Performs a left-button rubber-band drag while holding the given modifier
        /// keys, moving smoothly in the given number of steps.
        /// </summary>
        /// <param name="startX">Drag start X coordinate in screen pixels.</param>
        /// <param name="startY">Drag start Y coordinate in screen pixels.</param>
        /// <param name="endX">Drag end X coordinate in screen pixels.</param>
        /// <param name="endY">Drag end Y coordinate in screen pixels.</param>
        /// <param name="modifiers">Modifier keys to hold during the drag; combinable flags.</param>
        /// <param name="steps">Number of intermediate move events during the drag.</param>
        /// <param name="stepDelayMilliseconds">Delay between drag steps in milliseconds.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the drag failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        /// <remarks>
        /// Modifier keys are released in a Finally block (best-effort), so a failed drag
        /// never leaves Ctrl/Shift/Alt stuck down.
        /// </remarks>
        [Category("Mouse - Drag")]
        [Description("Performs a left-button rubber-band drag while holding modifier keys, using a custom step count and delay. Returns True on success; never throws.")]
        public bool RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers, int steps, int stepDelayMilliseconds, out string message)
        {
            List<INPUT> downBatch = new List<INPUT>();
            if ((modifiers & ModifierKeys.Control) != 0) downBatch.Add(MakeKeyInput(VK_CONTROL, false));
            if ((modifiers & ModifierKeys.Shift)   != 0) downBatch.Add(MakeKeyInput(VK_SHIFT, false));
            if ((modifiers & ModifierKeys.Alt)     != 0) downBatch.Add(MakeKeyInput(VK_MENU, false));
            if (downBatch.Count > 0 && !TrySendInputs(downBatch.ToArray(), out message))
                return false;

            bool dragOk;
            string dragMessage = null;
            try
            {
                dragOk = DragAndDrop(startX, startY, endX, endY, steps, stepDelayMilliseconds, out dragMessage);
            }
            finally
            {
                List<INPUT> upBatch = new List<INPUT>();
                if ((modifiers & ModifierKeys.Alt)     != 0) upBatch.Add(MakeKeyInput(VK_MENU, true));
                if ((modifiers & ModifierKeys.Shift)   != 0) upBatch.Add(MakeKeyInput(VK_SHIFT, true));
                if ((modifiers & ModifierKeys.Control) != 0) upBatch.Add(MakeKeyInput(VK_CONTROL, true));
                // Best-effort modifier release - don't let a cleanup failure mask the
                // primary drag outcome already captured above.
                if (upBatch.Count > 0) TrySendInputs(upBatch.ToArray(), out _);
            }

            message = dragMessage;
            return dragOk;
        }

        /// <summary>
        /// Performs a left-button drag from the start coordinates to the end
        /// coordinates, then holds the button down at the destination for the given
        /// time before releasing - useful for targets that auto-expand or reveal a
        /// drop zone only after a brief hover-while-dragging.
        /// </summary>
        /// <param name="startX">Drag start X coordinate in screen pixels.</param>
        /// <param name="startY">Drag start Y coordinate in screen pixels.</param>
        /// <param name="endX">Drag end X coordinate in screen pixels.</param>
        /// <param name="endY">Drag end Y coordinate in screen pixels.</param>
        /// <param name="holdMilliseconds">How long to hold the button at the destination before releasing; values below 0 are treated as 0.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the drag failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Drag")]
        [Description("Drags from start to end, then holds the button down at the destination before releasing (for hover-to-expand drop targets). Returns True on success; never throws.")]
        public bool DragAndHold(int startX, int startY, int endX, int endY, int holdMilliseconds, out string message)
        {
            if (!MoveTo(startX, startY, out message)) return false;
            Thread.Sleep(50);
            if (!MouseDown(MouseButton.Left, out message)) return false;
            Thread.Sleep(50);
            if (!SmoothMoveTo(endX, endY, 30, 10, out message)) return false;
            Thread.Sleep(Math.Max(0, holdMilliseconds));
            return MouseUp(MouseButton.Left, out message);
        }

        #endregion

        #region Wheel / Scrolling

        /// <summary>
        /// Scrolls vertically at the current cursor position.
        /// </summary>
        /// <param name="wheelDelta">Scroll amount; positive scrolls up, negative scrolls down. 120 = one wheel notch.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls vertically. Positive values scroll up, negative scroll down. 120 = one wheel notch. Returns True on success; never throws.")]
        public bool Scroll(int wheelDelta, out string message)
        {
            return TrySendMouseEvent(MOUSEEVENTF_WHEEL, wheelDelta, out message);
        }

        /// <summary>
        /// Scrolls up one wheel notch.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls up one wheel notch. Returns True on success; never throws.")]
        public bool ScrollUp(out string message) => ScrollUp(1, out message);

        /// <summary>
        /// Scrolls up the given number of wheel notches.
        /// </summary>
        /// <param name="notches">Number of notches; the absolute value is used, clamped so the delta cannot overflow.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls up the given number of wheel notches. Returns True on success; never throws.")]
        public bool ScrollUp(int notches, out string message) => Scroll(WHEEL_DELTA * Math.Abs(ClampNotches(notches)), out message);

        /// <summary>
        /// Scrolls down one wheel notch.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls down one wheel notch. Returns True on success; never throws.")]
        public bool ScrollDown(out string message) => ScrollDown(1, out message);

        /// <summary>
        /// Scrolls down the given number of wheel notches.
        /// </summary>
        /// <param name="notches">Number of notches; the absolute value is used, clamped so the delta cannot overflow.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls down the given number of wheel notches. Returns True on success; never throws.")]
        public bool ScrollDown(int notches, out string message) => Scroll(-WHEEL_DELTA * Math.Abs(ClampNotches(notches)), out message);

        /// <summary>
        /// Scrolls horizontally at the current cursor position.
        /// </summary>
        /// <param name="wheelDelta">Scroll amount; positive scrolls right, negative scrolls left. 120 = one notch.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls horizontally. Positive values scroll right, negative scroll left. 120 = one notch. Returns True on success; never throws.")]
        public bool ScrollHorizontal(int wheelDelta, out string message)
        {
            return TrySendMouseEvent(MOUSEEVENTF_HWHEEL, wheelDelta, out message);
        }

        /// <summary>
        /// Scrolls right one wheel notch.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls right one wheel notch. Returns True on success; never throws.")]
        public bool ScrollRight(out string message) => ScrollRight(1, out message);

        /// <summary>
        /// Scrolls right the given number of wheel notches.
        /// </summary>
        /// <param name="notches">Number of notches; the absolute value is used, clamped so the delta cannot overflow.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls right the given number of wheel notches. Returns True on success; never throws.")]
        public bool ScrollRight(int notches, out string message) => ScrollHorizontal(WHEEL_DELTA * Math.Abs(ClampNotches(notches)), out message);

        /// <summary>
        /// Scrolls left one wheel notch.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls left one wheel notch. Returns True on success; never throws.")]
        public bool ScrollLeft(out string message) => ScrollLeft(1, out message);

        /// <summary>
        /// Scrolls left the given number of wheel notches.
        /// </summary>
        /// <param name="notches">Number of notches; the absolute value is used, clamped so the delta cannot overflow.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if input injection failed. Never throws.</returns>
        [Category("Mouse - Wheel")]
        [Description("Scrolls left the given number of wheel notches. Returns True on success; never throws.")]
        public bool ScrollLeft(int notches, out string message) => ScrollHorizontal(-WHEEL_DELTA * Math.Abs(ClampNotches(notches)), out message);

        /// <summary>
        /// Moves the cursor to the coordinates and scrolls horizontally there. Wheel
        /// messages go to the window under the cursor, so this targets the control that
        /// actually receives the scroll.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="wheelDelta">Scroll amount; positive scrolls right, negative scrolls left. 120 = one notch.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the scroll failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        /// <remarks>Some applications invert or ignore horizontal wheel input.</remarks>
        [Category("Mouse - Wheel")]
        [Description("Moves the cursor to the coordinates and scrolls horizontally there (positive = right, negative = left; 120 = one notch). Returns True on success; never throws.")]
        public bool ScrollHorizontalAt(int x, int y, int wheelDelta, out string message)
        {
            if (!MoveTo(x, y, out message))
                return false;
            Thread.Sleep(50); // let hover state land on the target before the wheel event arrives
            return ScrollHorizontal(wheelDelta, out message);
        }

        #endregion

        #region Cursor Appearance / Visibility / Confinement

        /// <summary>
        /// Changes the standard arrow cursor to the given system cursor - for example
        /// <see cref="SystemCursorType.Wait"/> while an automation is busy.
        /// Equivalent to ReplaceSystemCursor(SystemCursorType.Arrow, cursor).
        /// </summary>
        /// <param name="cursor">The system cursor to show in place of the normal arrow.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the change failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if loading or replacing the system cursor failed. Never throws.</returns>
        /// <remarks>
        /// This replaces the cursor <b>system-wide for the current session</b> (all
        /// applications), and it persists until <see cref="ResetSystemCursors"/> is called,
        /// the user signs out, or another process changes it. Applications that explicitly
        /// set their own cursor over their windows will override this for those windows.
        /// Always call <see cref="ResetSystemCursors"/> when your automation finishes
        /// (ideally in a Finally block) so the user's cursors are restored.
        /// </remarks>
        [Category("Mouse - Cursor")]
        [Description("Changes the normal arrow cursor to the given system cursor (e.g. Wait while the automation runs). Call ResetSystemCursors afterwards. Returns True on success; never throws.")]
        public bool SetCursor(SystemCursorType cursor, out string message)
        {
            return ReplaceSystemCursor(SystemCursorType.Arrow, cursor, out message);
        }

        /// <summary>
        /// Replaces any system cursor slot with another standard system cursor - e.g.
        /// swap the I-beam for a crosshair in addition to (or instead of) the arrow.
        /// </summary>
        /// <param name="slotToReplace">Which system cursor slot to overwrite.</param>
        /// <param name="newCursor">The standard cursor whose image is placed into that slot.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the change failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if loading or replacing the system cursor failed. Never throws.</returns>
        /// <inheritdoc cref="SetCursor(SystemCursorType, out string)" select="remarks"/>
        [Category("Mouse - Cursor")]
        [Description("Replaces a specific system cursor slot with another standard system cursor. Call ResetSystemCursors afterwards. Returns True on success; never throws.")]
        public bool ReplaceSystemCursor(SystemCursorType slotToReplace, SystemCursorType newCursor, out string message)
        {
            IntPtr hSource = LoadCursor(IntPtr.Zero, (int)newCursor); // shared system cursor - must NOT be destroyed
            if (hSource == IntPtr.Zero)
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "LoadCursor failed for system cursor " + newCursor + ".").Message;
                return false;
            }
            return TryApplySystemCursor(slotToReplace, hSource, out message);
        }

        /// <summary>
        /// Loads a custom cursor from a .cur or .ani file and installs it into the given
        /// system cursor slot.
        /// </summary>
        /// <param name="slotToReplace">Which system cursor slot to overwrite (usually <see cref="SystemCursorType.Arrow"/>).</param>
        /// <param name="filePath">Full path to a .cur or .ani file. The file is copied at load time, so it can be deleted afterwards.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the change failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for a missing/invalid <paramref name="filePath"/>, or if loading/replacing the system cursor failed. Never throws.</returns>
        /// <inheritdoc cref="SetCursor(SystemCursorType, out string)" select="remarks"/>
        [Category("Mouse - Cursor")]
        [Description("Loads a cursor from a .cur/.ani file into the given system cursor slot (usually Arrow). Call ResetSystemCursors afterwards. Returns True on success; never throws.")]
        public bool SetCursorFromFile(SystemCursorType slotToReplace, string filePath, out string message)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                message = "A cursor file path is required.";
                return false;
            }
            if (!File.Exists(filePath))
            {
                message = $"Cursor file not found: '{filePath}'.";
                return false;
            }

            IntPtr hFile = LoadCursorFromFile(filePath); // we own this handle
            if (hFile == IntPtr.Zero)
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(),
                    "LoadCursorFromFile failed for '" + filePath + "'. Expected a valid .cur or .ani file.").Message;
                return false;
            }

            try
            {
                return TryApplySystemCursor(slotToReplace, hFile, out message);
            }
            finally
            {
                DestroyCursor(hFile);
            }
        }

        /// <summary>
        /// Restores all system cursors to the user's configured Windows defaults,
        /// undoing every SetCursor / ReplaceSystemCursor / SetCursorFromFile change.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the reset failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the SystemParametersInfo(SPI_SETCURSORS) call failed. Never throws.</returns>
        [Category("Mouse - Cursor")]
        [Description("Restores all system cursors to the Windows defaults, undoing any SetCursor/SetCursorFromFile changes. Returns True on success; never throws.")]
        public bool ResetSystemCursors(out string message)
        {
            if (!SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "SystemParametersInfo(SPI_SETCURSORS) failed.").Message;
                return false;
            }
            message = null;
            return true;
        }

        /// <summary>
        /// Hides the cursor (until a matching <see cref="ShowCursor"/> call through this component).
        /// </summary>
        /// <remarks>
        /// Win32 ShowCursor uses an internal display counter rather than an on/off flag;
        /// this component tracks its own calls so repeated HideCursor calls here do not
        /// unbalance the counter. The counter is per-thread, so a matching
        /// <see cref="ShowCursor"/> call must be made on the same thread that hid the
        /// cursor (typically the automation's main thread). If another application hides
        /// the cursor independently, use <see cref="IsCursorVisible"/> only as an approximation.
        /// </remarks>
        [Category("Mouse - Cursor")]
        [Description("Hides the cursor. Counterbalanced by ShowCursor.")]
        public void HideCursor()
        {
            if (!_cursorHidden)
            {
                ShowCursorNative(false);
                _cursorHidden = true;
            }
        }

        /// <summary>
        /// Shows the cursor again after a <see cref="HideCursor"/> call made through this component.
        /// Must be called on the same thread that called <see cref="HideCursor"/> (the native
        /// display counter is per-thread); see that method's remarks.
        /// </summary>
        /// <inheritdoc cref="HideCursor" select="remarks"/>
        [Category("Mouse - Cursor")]
        [Description("Shows the cursor again after HideCursor.")]
        public void ShowCursor()
        {
            if (_cursorHidden)
            {
                ShowCursorNative(true);
                _cursorHidden = false;
            }
        }

        /// <summary>
        /// Indicates whether the cursor is considered visible based on this component's
        /// own HideCursor/ShowCursor calls.
        /// </summary>
        /// <returns>False after a <see cref="HideCursor"/> call through this component; otherwise true.</returns>
        /// <inheritdoc cref="HideCursor" select="remarks"/>
        [Category("Mouse - Cursor")]
        [Description("Returns False if the cursor was hidden via this component's HideCursor method.")]
        public bool IsCursorVisible()
        {
            return !_cursorHidden;
        }

        /// <summary>
        /// Confines the cursor to the given screen rectangle - the user (and synthesized
        /// moves) cannot leave it until <see cref="ReleaseCursorClip"/> is called.
        /// </summary>
        /// <param name="left">Left edge of the confinement rectangle in screen pixels.</param>
        /// <param name="top">Top edge of the confinement rectangle in screen pixels.</param>
        /// <param name="right">Right edge of the confinement rectangle in screen pixels.</param>
        /// <param name="bottom">Bottom edge of the confinement rectangle in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the confinement failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the rectangle is empty/inverted (right &lt;= left or bottom &lt;= top), or the ClipCursor call failed. Never throws.</returns>
        /// <remarks>
        /// Useful for demos/kiosks or to keep a script's clicks inside one monitor.
        /// The clip is released automatically by Windows when the session locks, but
        /// always pair with <see cref="ReleaseCursorClip"/> (ideally in a Finally block).
        /// </remarks>
        [Category("Mouse - Cursor")]
        [Description("Confines the cursor to the given screen rectangle until ReleaseCursorClip is called. Returns True on success; never throws.")]
        public bool ClipCursor(int left, int top, int right, int bottom, out string message)
        {
            if (right <= left || bottom <= top)
            {
                message = "Clip rectangle must be non-empty: right > left and bottom > top.";
                return false;
            }

            RECT rc = new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
            if (!ClipCursorRect(ref rc))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "ClipCursor failed.").Message;
                return false;
            }
            message = null;
            return true;
        }

        /// <summary>
        /// Removes any cursor confinement set by <see cref="ClipCursor"/>, allowing the
        /// cursor to move freely over all monitors again.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the release failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the ClipCursor call failed. Never throws.</returns>
        [Category("Mouse - Cursor")]
        [Description("Removes cursor confinement set by ClipCursor. Returns True on success; never throws.")]
        public bool ReleaseCursorClip(out string message)
        {
            if (!ClipCursorNull(IntPtr.Zero))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "ClipCursor(NULL) failed.").Message;
                return false;
            }
            message = null;
            return true;
        }

        /// <summary>
        /// Gets the rectangle the cursor is currently confined to (the full virtual
        /// screen when no clip is active).
        /// </summary>
        /// <param name="clip">The current clip rectangle, or <c>default</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the GetClipCursor call failed. Never throws.</returns>
        [Category("Mouse - Cursor")]
        [Description("Gets the rectangle the cursor is currently confined to (full virtual screen when unclipped). Returns True on success; never throws.")]
        public bool GetCursorClip(out System.Drawing.Rectangle clip, out string message)
        {
            if (!GetClipCursor(out RECT rc))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetClipCursor failed.").Message;
                clip = default;
                return false;
            }
            clip = new System.Drawing.Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
            message = null;
            return true;
        }

        #endregion

        #region Button State / Screen Info

        /// <summary>
        /// Indicates whether the left mouse button is currently held down.
        /// </summary>
        /// <returns>True while the left button is physically or synthetically held down.</returns>
        [Category("Mouse - State")]
        [Description("Returns True while the left mouse button is held down.")]
        public bool IsLeftButtonDown()
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        }

        /// <summary>
        /// Indicates whether the right mouse button is currently held down.
        /// </summary>
        /// <returns>True while the right button is physically or synthetically held down.</returns>
        [Category("Mouse - State")]
        [Description("Returns True while the right mouse button is held down.")]
        public bool IsRightButtonDown()
        {
            return (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        }

        /// <summary>
        /// Indicates whether the middle mouse button is currently held down.
        /// </summary>
        /// <returns>True while the middle button is physically or synthetically held down.</returns>
        [Category("Mouse - State")]
        [Description("Returns True while the middle mouse button is held down.")]
        public bool IsMiddleButtonDown()
        {
            return (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
        }

        /// <summary>
        /// Gets the system double-click time.
        /// </summary>
        /// <returns>The double-click time in milliseconds.</returns>
        [Category("Mouse - State")]
        [Description("Gets the system double-click time in milliseconds.")]
        public int GetDoubleClickTimeMs()
        {
            return (int)GetDoubleClickTime();
        }

        /// <summary>
        /// Sets the system double-click time in milliseconds.
        /// </summary>
        /// <param name="milliseconds">The new double-click time: 1..5000 ms, or 0 to restore the 500 ms Windows default.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the change failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="milliseconds"/> is out of range, or the SystemParametersInfo(SPI_SETDOUBLECLICKTIME) call failed. Never throws.</returns>
        /// <remarks>
        /// Attended-session tip: if the operator tightened their double-click time, widen
        /// it temporarily to make <see cref="DoubleClickAt"/> deterministic. Capture
        /// <see cref="GetDoubleClickTimeMs"/> first and restore it when the automation ends
        /// (ideally in a Finally block). The change applies immediately, per user session.
        /// </remarks>
        [Category("Mouse - State")]
        [Description("Sets the system double-click time in milliseconds (0 restores the 500 ms default; max 5000). Restore the previous value afterwards. Returns True on success; never throws.")]
        public bool SetDoubleClickTimeMs(int milliseconds, out string message)
        {
            if (milliseconds < 0 || milliseconds > 5000)
            {
                message = "Double-click time must be 0 (Windows default) or 1..5000 milliseconds.";
                return false;
            }

            if (!SystemParametersInfo(SPI_SETDOUBLECLICKTIME, (uint)milliseconds, IntPtr.Zero, SPIF_SENDCHANGE))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "SystemParametersInfo(SPI_SETDOUBLECLICKTIME) failed.").Message;
                return false;
            }
            message = null;
            return true;
        }

        /// <summary>
        /// Gets the width of the primary screen.
        /// </summary>
        /// <returns>Primary screen width in pixels.</returns>
        [Category("Mouse - Screen")]
        [Description("Gets the width of the primary screen in pixels.")]
        public int GetScreenWidth()
        {
            return GetSystemMetrics(SM_CXSCREEN);
        }

        /// <summary>
        /// Gets the height of the primary screen.
        /// </summary>
        /// <returns>Primary screen height in pixels.</returns>
        [Category("Mouse - Screen")]
        [Description("Gets the height of the primary screen in pixels.")]
        public int GetScreenHeight()
        {
            return GetSystemMetrics(SM_CYSCREEN);
        }

        /// <summary>
        /// Gets the bounding rectangle of the entire virtual screen (all monitors combined).
        /// </summary>
        /// <param name="left">Left edge; negative when monitors extend left of the primary display.</param>
        /// <param name="top">Top edge; negative when monitors extend above the primary display.</param>
        /// <param name="width">Total width in pixels.</param>
        /// <param name="height">Total height in pixels.</param>
        /// <remarks>
        /// Use these bounds to sanity-check a computed click target before
        /// <see cref="MoveTo"/> / <see cref="ClickAt"/> - coordinates outside the virtual
        /// screen indicate broken selector math (Windows would clamp the move to the
        /// nearest edge). Note this is the bounding box: on staggered monitor layouts it
        /// can contain areas no monitor actually covers.
        /// </remarks>
        [Category("Mouse - Screen")]
        [Description("Gets the bounding rectangle of the whole virtual screen (all monitors); left/top can be negative on multi-monitor setups.")]
        public void GetVirtualScreenBounds(out int left, out int top, out int width, out int height)
        {
            left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        }

        /// <summary>
        /// Indicates whether the given coordinates lie inside the virtual screen bounds.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        /// <returns>True if the point is within the virtual screen bounding rectangle.</returns>
        /// <inheritdoc cref="GetVirtualScreenBounds" select="remarks"/>
        [Category("Mouse - Screen")]
        [Description("Returns True if the coordinates lie inside the virtual screen bounds.")]
        public bool IsPointOnScreen(int x, int y)
        {
            GetVirtualScreenBounds(out int left, out int top, out int width, out int height);
            return x >= left && x < left + width && y >= top && y < top + height;
        }

        /// <summary>
        /// Clamps an X coordinate into the virtual screen's horizontal range.
        /// </summary>
        /// <param name="x">Any X coordinate in screen pixels.</param>
        /// <returns>The coordinate forced into [virtual-left, virtual-right - 1].</returns>
        /// <inheritdoc cref="GetVirtualScreenBounds" select="remarks"/>
        [Category("Mouse - Screen")]
        [Description("Clamps an X coordinate into the virtual screen's horizontal range.")]
        public int ClampToScreenX(int x)
        {
            GetVirtualScreenBounds(out int left, out _, out int width, out _);
            return Math.Max(left, Math.Min(left + width - 1, x));
        }

        /// <summary>
        /// Clamps a Y coordinate into the virtual screen's vertical range.
        /// </summary>
        /// <param name="y">Any Y coordinate in screen pixels.</param>
        /// <returns>The coordinate forced into [virtual-top, virtual-bottom - 1].</returns>
        /// <inheritdoc cref="GetVirtualScreenBounds" select="remarks"/>
        [Category("Mouse - Screen")]
        [Description("Clamps a Y coordinate into the virtual screen's vertical range.")]
        public int ClampToScreenY(int y)
        {
            GetVirtualScreenBounds(out _, out int top, out _, out int height);
            return Math.Max(top, Math.Min(top + height - 1, y));
        }

        #endregion

        #region Input Blocking (Attended Sessions)

        /// <summary>
        /// Blocks ALL real keyboard and mouse input system-wide: the operator cannot type
        /// or click until <see cref="UnblockUserInput"/> is called. Input injected by this
        /// component (SendInput/SetCursorPos based actions) is NOT affected - which is the
        /// point: wrap critical click sequences so the human cannot interleave mid-action.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the block was refused.</param>
        /// <returns>
        /// <c>true</c> on success; <c>false</c> if Windows refused the block - input is
        /// already blocked by another thread, or the active desktop is secure (UAC prompt,
        /// lock screen). Never throws.
        /// </returns>
        /// <remarks>
        /// Rules of engagement:
        ///  - ALWAYS pair with <see cref="UnblockUserInput"/> in a Finally block. Only the
        ///    thread that blocked can unblock; a stranded block means no working input.
        ///  - Ctrl+Alt+Del always breaks the block (a Windows safety hatch).
        ///  - If this process exits while blocked, Windows releases the block with the thread.
        ///  - Requires an interactive desktop; fails on the secure desktop and against
        ///    higher-integrity desktops (UIPI).
        ///  - Microsoft marks BlockInput as deprecated; use it only for short, critical
        ///    sequences, never as a long-lived input lock.
        /// </remarks>
        [Category("Mouse - Input Blocking")]
        [Description("Blocks all real keyboard/mouse input system-wide until UnblockUserInput (injected input still works). MUST be paired with UnblockUserInput in a Finally block. Returns True on success; never throws.")]
        public bool BlockUserInput(out string message)
        {
            if (!BlockInputNative(true))
            {
                message = "BlockInput was refused - input is already blocked, or the desktop is secure/locked.";
                return false;
            }
            message = null;
            return true;
        }

        /// <summary>
        /// Re-enables real keyboard and mouse input after <see cref="BlockUserInput"/>.
        /// Safe to call when nothing is blocked, so call it unconditionally from a Finally block.
        /// </summary>
        /// <inheritdoc cref="BlockUserInput" select="remarks"/>
        [Category("Mouse - Input Blocking")]
        [Description("Re-enables real keyboard/mouse input after BlockUserInput. Safe to call even when nothing is blocked.")]
        public void UnblockUserInput()
        {
            // BlockInput(false) returns False when nothing is blocked; that is not an
            // error for cleanup purposes, so the result is deliberately ignored.
            BlockInputNative(false);
        }

        #endregion

        // ====================================================================
        //  Background Window Clicks (PostMessage)
        // ====================================================================

        #region Background Clicks (PostMessage)

        /// <summary>
        /// Posts a mouse-click message directly to a window's handle without moving
        /// the cursor or stealing focus. The window can be covered, partially obscured,
        /// or even minimized (depending on the application's message handling).
        /// </summary>
        /// <param name="hWnd">Handle of the target window (ideally the raw child control, not the root).</param>
        /// <param name="button">The mouse button to simulate.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed PostMessage/GetWindowRect call (e.g. UIPI blocked the message to a higher-integrity target). Never throws.</returns>
        /// <remarks>
        /// Caveats:
        ///  - UIPI blocks messages to windows at a higher integrity level (elevated apps);
        ///    this surfaces as a <c>false</c> return with a message rather than silent failure.
        ///  - Browsers, DirectX games, and some modern frameworks often ignore posted
        ///    mouse messages because they read raw input or use a different input pipeline.
        ///  - For best results, target the raw child control handle (from Spy++ or
        ///    WindowFromPoint), not the top-level root window.
        ///  - The center is computed from GetWindowRect, which returns the minimized
        ///    position for minimized windows - restore the window before calling.
        /// </remarks>
        [Category("Mouse - Background Click")]
        [Description("Posts a click directly to a window handle without moving the cursor or stealing focus. Returns True on success; never throws.")]
        public bool ClickWindow(IntPtr hWnd, MouseButton button, out string message)
        {
            if (!TryGetWindowRect(hWnd, out int left, out int top, out int width, out int height, out message))
                return false;

            // The window rect includes the non-client area (title bar, borders),
            // but the posted message expects client coordinates - convert the
            // window-rect center through ScreenToClient instead of using the
            // window-relative center directly, which would land the click off
            // target by the non-client offset.
            POINT center = new POINT { X = left + width / 2, Y = top + height / 2 };
            if (!ScreenToClient(hWnd, ref center))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "ScreenToClient failed.").Message;
                return false;
            }

            return ClickWindowAtClientPoint(hWnd, center.X, center.Y, button, out message);
        }

        /// <summary>
        /// Posts a click to a window at the given screen coordinates. The coordinates
        /// are converted to client coordinates via <c>ScreenToClient</c> before posting.
        /// </summary>
        /// <param name="hWnd">Handle of the target window.</param>
        /// <param name="screenX">Screen-space X coordinate.</param>
        /// <param name="screenY">Screen-space Y coordinate.</param>
        /// <param name="button">The mouse button to simulate.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if ScreenToClient or PostMessage failed. Never throws.</returns>
        /// <inheritdoc cref="ClickWindow"/>
        [Category("Mouse - Background Click")]
        [Description("Posts a click to a window at the given screen coordinates (converted to client coords). Returns True on success; never throws.")]
        public bool ClickWindowAtPoint(IntPtr hWnd, int screenX, int screenY, MouseButton button, out string message)
        {
            POINT screenPt = new POINT { X = screenX, Y = screenY };
            if (!ScreenToClient(hWnd, ref screenPt))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "ScreenToClient failed.").Message;
                return false;
            }

            return ClickWindowAtClientPoint(hWnd, screenPt.X, screenPt.Y, button, out message);
        }

        /// <summary>
        /// Posts a click to a window at the given client-area coordinates — the form
        /// used by Spy++ and most window-spy tools. Supports all five mouse buttons;
        /// X1/X2 identify themselves via the HIWORD of wParam.
        /// </summary>
        /// <param name="hWnd">Handle of the target window.</param>
        /// <param name="clientX">Client-space X coordinate.</param>
        /// <param name="clientY">Client-space Y coordinate.</param>
        /// <param name="button">The mouse button to simulate.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/> or a failed PostMessage. Never throws.</returns>
        /// <inheritdoc cref="ClickWindow"/>
        [Category("Mouse - Background Click")]
        [Description("Posts a click to a window at client-area coordinates (Spy++ style). Supports all buttons. Returns True on success; never throws.")]
        public bool ClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button, out string message)
        {
            if (!TryGetWindowMessageParams(button, out uint downMsg, out uint upMsg, out uint wParam, out message))
                return false;

            IntPtr lParam = MAKELPARAM(clientX, clientY);

            if (!TryPostMessage(hWnd, downMsg, wParam, lParam, out message))
                return false;

            Thread.Sleep(20);

            return TryPostMessage(hWnd, upMsg, wParam, lParam, out message);
        }

        /// <summary>
        /// Posts a double-click message sequence (DOWN, UP, DBLCLK, UP) to a window
        /// at the given client coordinates.
        /// </summary>
        /// <param name="hWnd">Handle of the target window.</param>
        /// <param name="clientX">Client-space X coordinate.</param>
        /// <param name="clientY">Client-space Y coordinate.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if any PostMessage in the sequence failed. Never throws.</returns>
        /// <remarks>
        /// The target must have <c>CS_DBLCLKS</c> in its window class style for the
        /// DBLCLK message to register; most standard Win32 controls do.
        /// </remarks>
        [Category("Mouse - Background Click")]
        [Description("Posts a double-click sequence (DOWN/UP/DBLCLK/UP) to a window at client coordinates. Returns True on success; never throws.")]
        public bool DoubleClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY, out string message)
        {
            IntPtr lParam = MAKELPARAM(clientX, clientY);

            if (!TryPostMessage(hWnd, WM_LBUTTONDOWN, MK_LBUTTON, lParam, out message)) return false;
            Thread.Sleep(20);
            if (!TryPostMessage(hWnd, WM_LBUTTONUP, MK_LBUTTON, lParam, out message)) return false;
            Thread.Sleep(20);
            if (!TryPostMessage(hWnd, WM_LBUTTONDBLCLK, MK_LBUTTON, lParam, out message)) return false;
            Thread.Sleep(20);
            return TryPostMessage(hWnd, WM_LBUTTONUP, MK_LBUTTON, lParam, out message);
        }

        // ---- Background-click helpers ----

        private static bool TryGetWindowMessageParams(MouseButton button, out uint downMsg, out uint upMsg, out uint wParam, out string message)
        {
            downMsg = upMsg = wParam = 0;
            switch (button)
            {
                case MouseButton.Left:
                    downMsg = WM_LBUTTONDOWN; upMsg = WM_LBUTTONUP; wParam = MK_LBUTTON; break;
                case MouseButton.Right:
                    downMsg = WM_RBUTTONDOWN; upMsg = WM_RBUTTONUP; wParam = MK_RBUTTON; break;
                case MouseButton.Middle:
                    downMsg = WM_MBUTTONDOWN; upMsg = WM_MBUTTONUP; wParam = MK_MBUTTON; break;
                case MouseButton.XButton1:
                    downMsg = WM_XBUTTONDOWN; upMsg = WM_XBUTTONUP; wParam = MAKEWPARAM(MK_XBUTTON1, XBUTTON1_HI); break;
                case MouseButton.XButton2:
                    downMsg = WM_XBUTTONDOWN; upMsg = WM_XBUTTONUP; wParam = MAKEWPARAM(MK_XBUTTON2, XBUTTON2_HI); break;
                default:
                    message = $"Unsupported mouse button: {button}.";
                    return false;
            }
            message = null;
            return true;
        }

        private static bool TryPostMessage(IntPtr hWnd, uint msg, uint wParam, IntPtr lParam, out string message)
        {
            if (!PostMessage(hWnd, msg, (IntPtr)wParam, lParam))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(),
                    "PostMessage failed for message 0x" + msg.ToString("X") +
                    ". The target may be elevated (UIPI), or the handle is invalid.").Message;
                return false;
            }
            message = null;
            return true;
        }

        private static bool TryGetWindowRect(IntPtr hWnd, out int left, out int top, out int width, out int height, out string message)
        {
            left = top = width = height = 0;
            if (!GetWindowRect(hWnd, out RECT rc))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.").Message;
                return false;
            }
            left = rc.Left;
            top = rc.Top;
            width = rc.Right - rc.Left;
            height = rc.Bottom - rc.Top;
            message = null;
            return true;
        }

        private static IntPtr MAKELPARAM(int low, int high)
        {
            return (IntPtr)((high << 16) | (low & 0xFFFF));
        }

        private static uint MAKEWPARAM(uint low, uint high)
        {
            return ((high & 0xFFFF) << 16) | (low & 0xFFFF);
        }

        #endregion

        // ====================================================================
        //  Window-Relative Targeting
        // ====================================================================

        #region Window-Relative Targeting

        /// <summary>
        /// Gets the screen-space bounding rectangle of a window.
        /// </summary>
        /// <param name="hWnd">Handle of the window to measure.</param>
        /// <param name="bounds">The window's bounds, or <c>default</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if GetWindowRect failed (e.g. an invalid handle). Never throws.</returns>
        [Category("Mouse - Window Targeting")]
        [Description("Gets the screen-space bounding rectangle of a window. Returns True on success; never throws.")]
        public bool GetWindowBounds(IntPtr hWnd, out System.Drawing.Rectangle bounds, out string message)
        {
            bool ok = TryGetWindowRect(hWnd, out int left, out int top, out int width, out int height, out message);
            bounds = ok ? new System.Drawing.Rectangle(left, top, width, height) : default;
            return ok;
        }

        /// <summary>
        /// Converts a point in a window's client area to screen coordinates.
        /// </summary>
        /// <param name="hWnd">Handle of the window the client point is relative to.</param>
        /// <param name="clientX">Client-space X coordinate.</param>
        /// <param name="clientY">Client-space Y coordinate.</param>
        /// <param name="screenX">Receives the equivalent screen-space X coordinate, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="screenY">Receives the equivalent screen-space Y coordinate, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the conversion failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if ClientToScreen failed. Never throws.</returns>
        [Category("Mouse - Window Targeting")]
        [Description("Converts a point in a window's client area to screen coordinates. Returns True on success; never throws.")]
        public bool ClientPointToScreen(IntPtr hWnd, int clientX, int clientY, out int screenX, out int screenY, out string message)
        {
            POINT pt = new POINT { X = clientX, Y = clientY };
            if (!ClientToScreen(hWnd, ref pt))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "ClientToScreen failed.").Message;
                screenX = 0;
                screenY = 0;
                return false;
            }
            screenX = pt.X;
            screenY = pt.Y;
            message = null;
            return true;
        }

        /// <summary>
        /// Converts a screen coordinate to a point relative to a window's client area.
        /// </summary>
        /// <param name="hWnd">Handle of the window the result is relative to.</param>
        /// <param name="screenX">Screen-space X coordinate.</param>
        /// <param name="screenY">Screen-space Y coordinate.</param>
        /// <param name="clientX">Receives the equivalent client-space X coordinate, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="clientY">Receives the equivalent client-space Y coordinate, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the conversion failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if ScreenToClient failed. Never throws.</returns>
        [Category("Mouse - Window Targeting")]
        [Description("Converts a screen coordinate to a point relative to a window's client area. Returns True on success; never throws.")]
        public bool ScreenPointToClient(IntPtr hWnd, int screenX, int screenY, out int clientX, out int clientY, out string message)
        {
            POINT pt = new POINT { X = screenX, Y = screenY };
            if (!ScreenToClient(hWnd, ref pt))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "ScreenToClient failed.").Message;
                clientX = 0;
                clientY = 0;
                return false;
            }
            clientX = pt.X;
            clientY = pt.Y;
            message = null;
            return true;
        }

        /// <summary>
        /// Moves the real cursor to a point relative to a window's client area and
        /// clicks the given button there - unlike <see cref="ClickWindowAtClientPoint"/>,
        /// this is a genuine cursor click rather than a posted message, so it also works
        /// against WPF, Electron/Chromium, and other frameworks that ignore PostMessage.
        /// </summary>
        /// <param name="hWnd">Handle of the window the client point is relative to.</param>
        /// <param name="clientX">Client-space X coordinate.</param>
        /// <param name="clientY">Client-space Y coordinate.</param>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/>, or if ClientToScreen/input injection failed. Never throws.</returns>
        /// <remarks>
        /// Because this moves the real cursor, the target window does not need to be
        /// covered or minimized (unlike the PostMessage variants), but it must be
        /// visible and unobstructed at the computed screen point.
        /// </remarks>
        [Category("Mouse - Window Targeting")]
        [Description("Moves the real cursor to a window-relative client point and clicks there (works where PostMessage-based clicks are ignored). Returns True on success; never throws.")]
        public bool ClickAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button, out string message)
        {
            if (!ClientPointToScreen(hWnd, clientX, clientY, out int screenX, out int screenY, out message))
                return false;
            return ClickAt(screenX, screenY, button, out message);
        }

        /// <summary>
        /// Clicks at a position expressed as a fraction of a window's client area -
        /// e.g. (0.5, 0.9) for "horizontally centered, near the bottom" - so the click
        /// target survives minor resizes or resolution differences across machines.
        /// </summary>
        /// <param name="hWnd">Handle of the window to click within.</param>
        /// <param name="xFraction">Horizontal position as a fraction of the client area's width, from 0.0 (left edge) to 1.0 (right edge).</param>
        /// <param name="yFraction">Vertical position as a fraction of the client area's height, from 0.0 (top edge) to 1.0 (bottom edge).</param>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="xFraction"/>/<paramref name="yFraction"/> are outside [0.0, 1.0], <paramref name="button"/> is undefined, or GetClientRect/ClientToScreen/input injection failed. Never throws.</returns>
        [Category("Mouse - Window Targeting")]
        [Description("Clicks at a fractional position within a window's client area (e.g. 0.5, 0.9), resilient to minor resizes across machines. Returns True on success; never throws.")]
        public bool ClickAtRelativePosition(IntPtr hWnd, double xFraction, double yFraction, MouseButton button, out string message)
        {
            if (xFraction < 0.0 || xFraction > 1.0)
            {
                message = "xFraction must be between 0.0 and 1.0.";
                return false;
            }
            if (yFraction < 0.0 || yFraction > 1.0)
            {
                message = "yFraction must be between 0.0 and 1.0.";
                return false;
            }

            // Fractions are relative to the client area, not the full window rect (which
            // includes the title bar/borders), so (0.5, 0.5) is the client center.
            if (!GetClientRect(hWnd, out RECT client))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetClientRect failed.").Message;
                return false;
            }

            POINT pt = new POINT
            {
                X = client.Left + (int)Math.Round((client.Right - client.Left) * xFraction),
                Y = client.Top + (int)Math.Round((client.Bottom - client.Top) * yFraction)
            };
            if (!ClientToScreen(hWnd, ref pt))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "ClientToScreen failed.").Message;
                return false;
            }

            return ClickAt(pt.X, pt.Y, button, out message);
        }

        /// <summary>
        /// Gets the handle of the (topmost, visible) window at the given screen point.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        /// <returns>The window handle at that point, or <see cref="IntPtr.Zero"/> if no window is there.</returns>
        [Category("Mouse - Window Targeting")]
        [Description("Gets the handle of the window at the given screen point (WindowFromPoint).")]
        public IntPtr GetWindowAtPoint(int x, int y)
        {
            return WindowFromPoint(new POINT { X = x, Y = y });
        }

        /// <summary>
        /// Clicks at the given coordinates only if the window under that point is the
        /// expected window (or a descendant of it) - a misclick guard for when a
        /// computed click target might have drifted because the screen layout changed.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="expectedWindowHandle">The window handle expected to own the point (its top-level/root window).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click was refused or failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the window under the point is not <paramref name="expectedWindowHandle"/> or one of its descendants (the misclick guard), <paramref name="button"/> is undefined, or a Win32 cursor call/input injection failed. Never throws.</returns>
        [Category("Mouse - Window Targeting")]
        [Description("Clicks only if the window under the point matches the expected window (or a descendant) - guards against misclicks from a shifted layout. Returns True on success; never throws.")]
        public bool SafeClickAt(int x, int y, MouseButton button, IntPtr expectedWindowHandle, out string message)
        {
            IntPtr actual = GetWindowAtPoint(x, y);
            if (actual != expectedWindowHandle && GetAncestor(actual, GA_ROOT) != expectedWindowHandle)
            {
                message = $"Refusing to click ({x},{y}): the window under that point (handle {actual}) " +
                          $"is not the expected window (handle {expectedWindowHandle}) or one of its descendants.";
                return false;
            }

            return ClickAt(x, y, button, out message);
        }

        #endregion

        // ====================================================================
        //  DPI & Physical Coordinates
        // ====================================================================

        #region DPI / Physical Coordinates

        /// <summary>
        /// Gets the current cursor X coordinate in physical pixels, regardless of the
        /// process's DPI-awareness setting. On a high-DPI monitor where the logical
        /// coordinate (from <see cref="GetX"/>) is scaled, this returns the true
        /// hardware pixel position.
        /// </summary>
        /// <param name="x">The cursor X in physical screen pixels, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if GetPhysicalCursorPos failed. Never throws.</returns>
        [Category("Mouse - DPI")]
        [Description("Gets the cursor X in physical pixels (unaffected by DPI scaling). Returns True on success; never throws.")]
        public bool GetPhysicalCursorX(out int x, out string message)
        {
            bool ok = TryGetPhysicalPoint(out POINT p, out message);
            x = ok ? p.X : 0;
            return ok;
        }

        /// <summary>
        /// Gets the current cursor Y coordinate in physical pixels, regardless of the
        /// process's DPI-awareness setting.
        /// </summary>
        /// <param name="y">The cursor Y in physical screen pixels, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if GetPhysicalCursorPos failed. Never throws.</returns>
        [Category("Mouse - DPI")]
        [Description("Gets the cursor Y in physical pixels (unaffected by DPI scaling). Returns True on success; never throws.")]
        public bool GetPhysicalCursorY(out int y, out string message)
        {
            bool ok = TryGetPhysicalPoint(out POINT p, out message);
            y = ok ? p.Y : 0;
            return ok;
        }

        /// <summary>
        /// Indicates whether the current process is DPI-aware. Returns false when the
        /// process is DPI-unaware (the OS virtualizes coordinates), true when it is
        /// per-monitor, per-monitor-v2, or system-DPI-aware.
        /// </summary>
        /// <returns>True if the process is any level of DPI-aware; false if DPI-unaware.</returns>
        /// <remarks>
        /// This is the quick diagnostic for the classic "clicks land offset on scaled
        /// monitors" bug: a DPI-unaware automation process receives virtualized
        /// coordinates, so <see cref="MoveTo"/> targets the wrong physical location.
        /// Note that a <c>false</c> return can also mean the awareness query itself
        /// failed - <c>GetDpiAwarenessContext</c> requires Windows 10 1607 or later,
        /// and on older systems this method reports false even for a DPI-aware process
        /// (the missing entry point is caught, so the never-throws contract holds).
        /// </remarks>
        [Category("Mouse - DPI")]
        [Description("Returns True if the process is DPI-aware (any level); false if DPI-unaware.")]
        public bool IsProcessDpiAware()
        {
            IntPtr ctx;
            try
            {
                ctx = GetDpiAwarenessContext();
            }
            catch (EntryPointNotFoundException)
            {
                // GetDpiAwarenessContext requires Windows 10 1607+; on older systems
                // report DPI-unaware rather than throwing (never-throws contract).
                return false;
            }
            if (ctx == IntPtr.Zero)
                return false;

            // DPI_AWARENESS_CONTEXT_UNAWARE is -1 as a handle; compare against the
            // known-aware contexts. If it matches UNAWARE, return false.
            return !AreDpiAwarenessContextsEqual(ctx, DPI_AWARENESS_CONTEXT_UNAWARE);
        }

        private static bool TryGetPhysicalPoint(out POINT p, out string message)
        {
            if (!GetPhysicalCursorPos(out p))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetPhysicalCursorPos failed.").Message;
                return false;
            }
            message = null;
            return true;
        }

        #endregion

        // ====================================================================
        //  Cursor Highlight Ring (for demos / recordings)
        // ====================================================================

        #region Cursor Highlight

        /// <summary>
        /// Flashes an inverting ring around the cursor to highlight its position —
        /// useful for demos, recordings, and attended-RPA operator guidance. The ring
        /// is drawn on the screen DC with <c>R2_NOTXORPEN</c> so drawing the same ring
        /// twice erases it exactly (no permanent pixels left behind). All GDI objects
        /// and the DC are restored/released in a <c>finally</c> block.
        /// </summary>
        /// <param name="radius">Ring radius in pixels (default 30).</param>
        /// <param name="flashes">Number of on/off flashes (default 3).</param>
        /// <param name="flashMs">Milliseconds each flash stays visible (default 200).</param>
        /// <param name="ringWidth">Pen width in pixels (default 3).</param>
        /// <param name="colorRef">
        /// RGB color for the ring as a 0xBBGGRR value (e.g. 0x0000FF = red).
        /// Because the ring uses XOR drawing, the visible color depends on what is
        /// under the cursor. Default is 0x0000FF (red).
        /// </param>
        /// <remarks>
        /// Caveats:
        ///  - If a window repaints while the ring is visible, the ring pixels may
        ///    leave artifacts that the second XOR pass cannot fully erase.
        ///  - Does not draw over exclusive fullscreen (DirectX) applications.
        ///  - The XOR blend means the apparent color varies by background.
        /// </remarks>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the highlight failed.</param>
        [Category("Mouse - Highlight")]
        [Description("Flashes an inverting ring around the cursor for demos/recordings. Erases itself exactly via XOR drawing. Returns True on success; never throws.")]
        public bool FlashCursorHighlight(out string message, int radius = 30, int flashes = 3, int flashMs = 200, int ringWidth = 3, int colorRef = 0x0000FF)
        {
            if (radius < 1) radius = 1;
            if (flashes < 1) flashes = 1;
            if (flashMs < 1) flashMs = 1;
            if (ringWidth < 1) ringWidth = 1;

            if (!TryGetPhysicalPoint(out POINT pos, out message))
                return false;

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetDC(NULL) for the screen failed.").Message;
                return false;
            }

            IntPtr hPen = CreatePen(PS_SOLID, ringWidth, (uint)colorRef);
            if (hPen == IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, hdc);
                message = new Win32Exception(Marshal.GetLastWin32Error(), "CreatePen failed for the highlight ring.").Message;
                return false;
            }
            IntPtr hOldPen = IntPtr.Zero;
            IntPtr hOldBrush = IntPtr.Zero;

            try
            {
                hOldPen = SelectObject(hdc, hPen);
                if (hOldPen == IntPtr.Zero)
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "SelectObject failed for the highlight pen.").Message;
                    return false;
                }
                hOldBrush = SelectObject(hdc, GetStockObject(NULL_BRUSH));
                if (hOldBrush == IntPtr.Zero)
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "SelectObject failed for the highlight brush.").Message;
                    return false;
                }
                SetROP2(hdc, R2_NOTXORPEN);

                for (int i = 0; i < flashes; i++)
                {
                    // Draw (visible) — XOR
                    Ellipse(hdc, pos.X - radius, pos.Y - radius, pos.X + radius, pos.Y + radius);
                    Thread.Sleep(flashMs);
                    // Draw again (erases) — XOR XOR = original pixels
                    Ellipse(hdc, pos.X - radius, pos.Y - radius, pos.X + radius, pos.Y + radius);

                    if (i < flashes - 1)
                        Thread.Sleep(flashMs);
                }
            }
            finally
            {
                if (hOldPen != IntPtr.Zero) SelectObject(hdc, hOldPen);
                if (hOldBrush != IntPtr.Zero) SelectObject(hdc, hOldBrush);
                if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
                if (hPen != IntPtr.Zero) DeleteObject(hPen);
            }

            message = null;
            return true;
        }

        #endregion

        // ====================================================================
        //  Human-like Bezier Movement
        // ====================================================================

        #region Bezier Movement

        /// <summary>
        /// Moves the cursor to the target along a randomized cubic Bezier curve with
        /// ease-in-out timing, simulating human-like cursor movement. Control points
        /// are offset perpendicular to the start→end line by a random amount, and
        /// ±1 pixel jitter is added to each intermediate step (except the final
        /// landing, which is exact).
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="durationMs">Total movement time in milliseconds (default 500).</param>
        /// <remarks>
        /// The curve, timing, and jitter vary on every call, so repeated movements to
        /// the same target do not look identical — useful for anti-detection and for
        /// making demo recordings look natural. The endpoint is always exact (no
        /// jitter on the final step), so the click lands precisely where intended.
        /// </remarks>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the move failed.</param>
        [Category("Mouse - Movement")]
        [Description("Moves the cursor to the target along a randomized Bezier curve with ease-in-out timing (human-like). Returns True on success; never throws.")]
        public bool MoveMouseBezier(int x, int y, out string message, int durationMs = 500)
        {
            if (durationMs < 1) durationMs = 1;

            if (!TryGetPoint(out POINT start, out message))
                return false;

            Random rng = Random.Shared;

            // Direction of the start→end line.
            double dx = x - start.X;
            double dy = y - start.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < 2.0)
            {
                // Already there (or very close) — just snap.
                return MoveTo(x, y, out message);
            }

            // Perpendicular unit vector to the line.
            double perpX = -dy / dist;
            double perpY = dx / dist;

            // Control points at 1/3 and 2/3 along the line, offset perpendicular.
            double offset1 = (rng.NextDouble() * 2.0 - 1.0) * (dist * 0.3);
            double offset2 = (rng.NextDouble() * 2.0 - 1.0) * (dist * 0.3);

            double cp1x = start.X + dx / 3.0 + perpX * offset1;
            double cp1y = start.Y + dy / 3.0 + perpY * offset1;
            double cp2x = start.X + 2.0 * dx / 3.0 + perpX * offset2;
            double cp2y = start.Y + 2.0 * dy / 3.0 + perpY * offset2;

            int steps = Math.Max(10, durationMs / 10); // ~10 ms per step
            int stepDelay = durationMs / steps;

            for (int i = 1; i <= steps; i++)
            {
                // Normalize t to [0,1].
                double t = (double)i / steps;

                // Ease-in-out cubic: 3t² - 2t³ (smooth acceleration/deceleration).
                double eased = t * t * (3.0 - 2.0 * t);

                // Cubic Bezier: B(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3
                double u = 1.0 - eased;
                double bx = u * u * u * start.X + 3.0 * u * u * eased * cp1x + 3.0 * u * eased * eased * cp2x + eased * eased * eased * x;
                double by = u * u * u * start.Y + 3.0 * u * u * eased * cp1y + 3.0 * u * eased * eased * cp2y + eased * eased * eased * y;

                int px = (int)Math.Round(bx);
                int py = (int)Math.Round(by);

                // Add ±1px jitter on intermediate steps for human-like noise.
                // Skip jitter on the final step so the endpoint is exact.
                if (i < steps)
                {
                    px += rng.Next(-1, 2);
                    py += rng.Next(-1, 2);
                }

                if (!MoveTo(px, py, out message))
                    return false;

                if (stepDelay > 0 && i < steps)
                    Thread.Sleep(stepDelay);
            }

            // Guarantee exact endpoint.
            return MoveTo(x, y, out message);
        }

        /// <summary>
        /// Moves the cursor to the target along a randomized Bezier curve, then clicks
        /// the given button - the human-like counterpart to <see cref="ClickAt"/>.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="button">The mouse button to click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <param name="durationMs">Total movement time in milliseconds (default 500).</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/>, or if a Win32 cursor call/input injection failed. Never throws.</returns>
        [Category("Mouse - Movement")]
        [Description("Moves along a randomized Bezier curve to the target, then clicks - the human-like counterpart to ClickAt. Returns True on success; never throws.")]
        public bool BezierClickAt(int x, int y, MouseButton button, out string message, int durationMs = 500)
        {
            if (!MoveMouseBezier(x, y, out message, durationMs))
                return false;
            Thread.Sleep(30);
            return Click(button, out message);
        }

        /// <summary>
        /// Moves the cursor to the target along a randomized Bezier curve, then
        /// double-clicks the given button.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <param name="button">The mouse button to double-click.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the click failed.</param>
        /// <param name="durationMs">Total movement time in milliseconds (default 500).</param>
        /// <returns><c>true</c> on success; <c>false</c> for an undefined <paramref name="button"/>, or if a Win32 cursor call/input injection failed. Never throws.</returns>
        [Category("Mouse - Movement")]
        [Description("Moves along a randomized Bezier curve to the target, then double-clicks. Returns True on success; never throws.")]
        public bool BezierDoubleClickAt(int x, int y, MouseButton button, out string message, int durationMs = 500)
        {
            if (!MoveMouseBezier(x, y, out message, durationMs))
                return false;
            Thread.Sleep(30);
            return DoubleClick(button, out message);
        }

        /// <summary>
        /// Performs a left-button drag from the start coordinates to the end
        /// coordinates, moving along a randomized Bezier curve instead of a
        /// straight/eased line - the human-like counterpart to <see cref="DragAndDrop(int, int, int, int, out string)"/>.
        /// </summary>
        /// <param name="startX">Drag start X coordinate in screen pixels.</param>
        /// <param name="startY">Drag start Y coordinate in screen pixels.</param>
        /// <param name="endX">Drag end X coordinate in screen pixels.</param>
        /// <param name="endY">Drag end Y coordinate in screen pixels.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the drag failed.</param>
        /// <param name="durationMs">Total movement time in milliseconds (default 500).</param>
        /// <returns><c>true</c> on success; <c>false</c> if a Win32 cursor call or input injection failed. Never throws.</returns>
        [Category("Mouse - Movement")]
        [Description("Performs a left-button drag along a randomized Bezier curve instead of a straight line - the human-like counterpart to DragAndDrop. Returns True on success; never throws.")]
        public bool BezierDragAndDrop(int startX, int startY, int endX, int endY, out string message, int durationMs = 500)
        {
            if (!MoveTo(startX, startY, out message)) return false;
            Thread.Sleep(50);
            if (!MouseDown(MouseButton.Left, out message)) return false;
            Thread.Sleep(50);
            if (!MoveMouseBezier(endX, endY, out message, durationMs)) return false;
            Thread.Sleep(50);
            return MouseUp(MouseButton.Left, out message);
        }

        #endregion

        // ====================================================================
        //  Verification & Synchronization
        // ====================================================================

        #region Verification & Synchronization

        /// <summary>
        /// Reads the color of the screen pixel at the given coordinates.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        /// <param name="color">The pixel color as a COLORREF (0x00BBGGRR), the same format used by <see cref="FlashCursorHighlight"/>'s colorRef parameter, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the read failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if GetDC or GetPixel failed (e.g. the coordinates are outside every monitor's clipping region). Never throws.</returns>
        /// <remarks>
        /// A lightweight verification primitive for flows that can't use full OCR/image
        /// recognition - e.g. confirming a button changed color after being clicked.
        /// On systems with GPU-accelerated compositing (DWM), GetPixel on the screen DC
        /// can return stale or incorrect colors, so treat this as a best-effort heuristic.
        /// </remarks>
        [Category("Mouse - Verification")]
        [Description("Reads the color of the screen pixel at the given coordinates, as a 0x00BBGGRR COLORREF value. Returns True on success; never throws.")]
        public bool GetPixelColor(int x, int y, out int color, out string message)
        {
            color = 0;
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetDC(NULL) for the screen failed.").Message;
                return false;
            }

            try
            {
                uint colorRef = GetPixel(hdc, x, y);
                if (colorRef == CLR_INVALID)
                {
                    message = "GetPixel failed - the coordinates may be outside every monitor's clipping region.";
                    return false;
                }
                color = unchecked((int)colorRef);
                message = null;
                return true;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        /// <summary>
        /// Polls a screen pixel until it matches the expected color or the timeout elapses.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        /// <param name="expectedColorRef">The color to wait for, as a 0x00BBGGRR COLORREF (see <see cref="GetPixelColor"/>).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="message"><c>null</c> if the poll completed (matched or genuinely timed out); otherwise a human-readable reason a Win32 failure aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the pixel matched before the timeout; <c>false</c> if it timed out, or if a Win32 failure aborted the poll (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("Mouse - Verification")]
        [Description("Polls a screen pixel until it matches the expected COLORREF or the timeout elapses. Returns True if it matched in time; never throws.")]
        public bool WaitForPixelColor(int x, int y, int expectedColorRef, int timeoutMs, int pollIntervalMs, out string message)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (true)
            {
                if (!GetPixelColor(x, y, out int color, out message))
                    return false;
                if (color == expectedColorRef)
                {
                    message = null;
                    return true;
                }
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                {
                    message = null;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
            }
        }

        /// <summary>
        /// Polls a screen pixel until its color changes from what it was when this
        /// method was called, or the timeout elapses.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="message"><c>null</c> if the poll completed (changed or genuinely timed out); otherwise a human-readable reason a Win32 failure aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the pixel changed before the timeout; <c>false</c> if it timed out, or if a Win32 failure aborted the poll (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("Mouse - Verification")]
        [Description("Polls a screen pixel until its color changes from its value at call time, or the timeout elapses. Returns True if it changed in time; never throws.")]
        public bool WaitForPixelChange(int x, int y, int timeoutMs, int pollIntervalMs, out string message)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            if (!GetPixelColor(x, y, out int baseline, out message))
                return false;

            int start = Environment.TickCount;
            while (true)
            {
                if (!GetPixelColor(x, y, out int color, out message))
                    return false;
                if (color != baseline)
                {
                    message = null;
                    return true;
                }
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                {
                    message = null;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
            }
        }

        /// <summary>
        /// Indicates whether the system is currently showing a busy cursor (the Wait
        /// hourglass or the AppStarting "working in background" arrow).
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely non-busy cursor).</param>
        /// <returns><c>true</c> if the current cursor is the Wait or AppStarting system cursor; <c>false</c> if it isn't, or if GetCursorInfo failed (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        /// <remarks>
        /// Many applications show one of these cursors while processing, so this is a
        /// more reliable "is the app still working" signal than a fixed delay. It is
        /// only a heuristic: an application can be busy without changing the cursor,
        /// or another window under the cursor can show it for unrelated reasons.
        /// </remarks>
        [Category("Mouse - Verification")]
        [Description("Returns True if the current system cursor is the Wait or AppStarting busy indicator. Never throws.")]
        public bool IsBusyCursorActive(out string message)
        {
            CURSORINFO info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(out info))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetCursorInfo failed.").Message;
                return false;
            }

            message = null;
            return info.hCursor == WaitCursorHandle.Value || info.hCursor == AppStartingCursorHandle.Value;
        }

        /// <summary>
        /// Waits until the system busy cursor (Wait/AppStarting) is no longer showing,
        /// or the timeout elapses.
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="message"><c>null</c> if the poll completed (idle or genuinely timed out); otherwise a human-readable reason a Win32 failure aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if the cursor became idle before the timeout; <c>false</c> if it timed out still busy, or if a Win32 failure aborted the poll (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        /// <inheritdoc cref="IsBusyCursorActive" select="remarks"/>
        [Category("Mouse - Verification")]
        [Description("Waits until the busy cursor (Wait/AppStarting) clears, or the timeout elapses. Returns True if it became idle in time; never throws.")]
        public bool WaitForIdleCursor(int timeoutMs, int pollIntervalMs, out string message)
        {
            if (pollIntervalMs < 1) pollIntervalMs = 1;

            int start = Environment.TickCount;
            while (true)
            {
                if (!IsBusyCursorActive(out message))
                {
                    if (message != null)
                        return false; // aborted due to a Win32 failure
                    return true; // not busy - idle
                }
                if (unchecked(Environment.TickCount - start) >= timeoutMs)
                {
                    message = null;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
            }
        }

        #endregion

        #region Internal Helpers

        private static bool TryGetPoint(out POINT p, out string message)
        {
            if (!GetCursorPos(out p))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "GetCursorPos failed.").Message;
                return false;
            }
            message = null;
            return true;
        }

        private static bool TrySetCursorPos(int x, int y, out string message)
        {
            if (!SetCursorPos(x, y))
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "SetCursorPos failed.").Message;
                return false;
            }
            message = null;
            return true;
        }

        /// <summary>
        /// Copies the source cursor and installs the copy into the given system slot,
        /// following the ownership rules of SetSystemCursor (which destroys the handle
        /// it is given - hence the copy; the caller keeps ownership of hSource).
        /// </summary>
        private static bool TryApplySystemCursor(SystemCursorType slot, IntPtr hSource, out string message)
        {
            IntPtr hCopy = CopyIcon(hSource); // CopyCursor is a macro for CopyIcon
            if (hCopy == IntPtr.Zero)
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "CopyIcon/CopyCursor of the cursor failed.").Message;
                return false;
            }

            if (!SetSystemCursor(hCopy, (uint)slot))
            {
                int err = Marshal.GetLastWin32Error();
                DestroyCursor(hCopy);
                message = new Win32Exception(err, "SetSystemCursor failed for slot " + slot + ".").Message;
                return false;
            }
            // hCopy is now owned by the system - do not destroy it.
            message = null;
            return true;
        }

        private static bool TrySendMouseButton(MouseButton button, bool isDown, out string message)
        {
            if (!TryGetButtonFlags(button, isDown, out uint flags, out int data, out message))
                return false;
            return TrySendMouseEvent(flags, data, out message);
        }

        /// <summary>
        /// Maps a button + direction to the matching MOUSEEVENTF_* flags and mouseData
        /// value. The X buttons share XDOWN/XUP and identify themselves via mouseData.
        /// </summary>
        private static bool TryGetButtonFlags(MouseButton button, bool isDown, out uint flags, out int data, out string message)
        {
            flags = 0;
            data = 0;

            switch (button)
            {
                case MouseButton.Left:
                    flags = isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                    break;
                case MouseButton.Right:
                    flags = isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                    break;
                case MouseButton.Middle:
                    flags = isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                    break;
                case MouseButton.XButton1:
                    flags = isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
                    data = XBUTTON1;
                    break;
                case MouseButton.XButton2:
                    flags = isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
                    data = XBUTTON2;
                    break;
                default:
                    message = $"Unsupported mouse button: {button}.";
                    return false;
            }
            message = null;
            return true;
        }

        /// <summary>Builds a single INPUT structure for a mouse event.</summary>
        private static INPUT MakeMouseInput(uint flags, int data)
        {
            INPUT input = new INPUT();
            input.type = INPUT_MOUSE;
            input.U.mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = unchecked((uint)data),
                dwFlags = flags,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            };
            return input;
        }

        /// <summary>Builds a single INPUT structure for a key press or release.</summary>
        private static INPUT MakeKeyInput(int virtualKey, bool keyUp)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U.ki = new KEYBDINPUT
            {
                wVk = (ushort)virtualKey,
                wScan = 0,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            };
            return input;
        }

        /// <summary>
        /// Clamps a notch count into a range where |notches| * WHEEL_DELTA cannot
        /// overflow, so Math.Abs(int.MinValue) cannot throw and the computed wheel
        /// delta cannot wrap into a garbage value.
        /// </summary>
        private static int ClampNotches(int notches)
        {
            return Math.Clamp(notches, -MAX_WHEEL_NOTCHES, MAX_WHEEL_NOTCHES);
        }

        private static bool TrySendMouseEvent(uint flags, int data, out string message)
        {
            return TrySendInputs(new INPUT[] { MakeMouseInput(flags, data) }, out message);
        }

        /// <summary>
        /// Injects a batch of input events atomically (in order) and reports when
        /// Windows refuses them (locked / secure desktop, UAC prompt, or a target app
        /// running at a higher integrity level - UIPI blocks the injection).
        /// </summary>
        private static bool TrySendInputs(INPUT[] inputs, out string message)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != (uint)inputs.Length)
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(),
                    "SendInput failed to inject the input event(s). (Desktop locked, UAC/secure desktop, or insufficient privileges?)").Message;
                return false;
            }
            message = null;
            return true;
        }

        #endregion

        #region Win32 Interop

        #region Constants

        private const uint INPUT_MOUSE    = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
        private const uint MOUSEEVENTF_XDOWN      = 0x0080;
        private const uint MOUSEEVENTF_XUP        = 0x0100;
        private const uint MOUSEEVENTF_WHEEL      = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL     = 0x1000;

        private const uint KEYEVENTF_KEYUP        = 0x0002;

        private const int WHEEL_DELTA = 120;

        // Largest |notches| whose WHEEL_DELTA product cannot overflow int.
        private const int MAX_WHEEL_NOTCHES = int.MaxValue / WHEEL_DELTA;

        private const int XBUTTON1 = 0x0001;
        private const int XBUTTON2 = 0x0002;

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        private const int VK_SHIFT   = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU    = 0x12; // Alt

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        private const int SM_XVIRTUALSCREEN  = 76;
        private const int SM_YVIRTUALSCREEN  = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        private const uint SPI_SETCURSORS         = 0x0057;
        private const uint SPI_SETDOUBLECLICKTIME = 0x0021;
        private const uint SPIF_SENDCHANGE        = 0x0002;

        #endregion

        #region Constants - Background Clicks / DPI / Highlight

        // Window messages for mouse buttons.
        private const uint WM_LBUTTONDOWN  = 0x0201;
        private const uint WM_LBUTTONUP    = 0x0202;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONDOWN  = 0x0204;
        private const uint WM_RBUTTONUP    = 0x0205;
        private const uint WM_RBUTTONDBLCLK = 0x0206;
        private const uint WM_MBUTTONDOWN  = 0x0207;
        private const uint WM_MBUTTONUP    = 0x0208;
        private const uint WM_MBUTTONDBLCLK = 0x0209;
        private const uint WM_XBUTTONDOWN  = 0x020B;
        private const uint WM_XBUTTONUP    = 0x020C;
        private const uint WM_XBUTTONDBLCLK = 0x020D;

        // Mouse-key state flags (wParam for posted mouse messages).
        private const uint MK_LBUTTON  = 0x0001;
        private const uint MK_RBUTTON  = 0x0002;
        private const uint MK_MBUTTON   = 0x0010;
        private const uint MK_XBUTTON1  = 0x0020;
        private const uint MK_XBUTTON2  = 0x0040;

        // HIWORD values for X-button messages.
        private const uint XBUTTON1_HI = 0x0001;
        private const uint XBUTTON2_HI = 0x0002;

        // GDI constants for the highlight ring.
        private const int PS_SOLID    = 0;
        private const int NULL_BRUSH  = 5;
        private const int R2_NOTXORPEN = 10; // Draw = NOT (pen XOR dest)

        // DPI awareness context handles (passed as IntPtr).
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_UNAWARE = new IntPtr(-1);

        #endregion

        #region Constants - Verification & Window Targeting

        // GetPixel's documented failure return value.
        private const uint CLR_INVALID = 0xFFFFFFFF;

        // GetAncestor flag: retrieve the root (top-level) window.
        private const uint GA_ROOT = 2;

        // Shared system-cursor handles for the busy-cursor check, loaded once and never
        // destroyed (LoadCursor returns shared handles). Lazy so the type can be
        // instantiated on non-Windows test hosts without touching user32.
        private static readonly Lazy<IntPtr> WaitCursorHandle =
            new Lazy<IntPtr>(() => LoadCursor(IntPtr.Zero, (int)SystemCursorType.Wait));
        private static readonly Lazy<IntPtr> AppStartingCursorHandle =
            new Lazy<IntPtr>(() => LoadCursor(IntPtr.Zero, (int)SystemCursorType.AppStarting));

        #endregion

        #region P/Invoke - Cursor Position

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        #endregion

        #region P/Invoke - Input Injection

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // BlockInput has no useful last-error story; SetLastError is declared only for
        // symmetry. A False return practically always means "already blocked" or "secure
        // desktop". Only the blocking thread can reverse it; Ctrl+Alt+Del also reverses it.
        [DllImport("user32.dll", EntryPoint = "BlockInput")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BlockInputNative([MarshalAs(UnmanagedType.Bool)] bool fBlockIt);

        #endregion

        #region P/Invoke - Metrics

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        #endregion

        #region P/Invoke - Cursor Appearance / Visibility / Confinement

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadCursorFromFile(string lpFileName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyCursor(IntPtr hCursor);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", EntryPoint = "ShowCursor")]
        private static extern int ShowCursorNative([MarshalAs(UnmanagedType.Bool)] bool bShow);

        [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
        private static extern bool ClipCursorRect(ref RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
        private static extern bool ClipCursorNull(IntPtr lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClipCursor(out RECT lpRect);

        #endregion

        #region P/Invoke - PostMessage & Window Rect

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        #endregion

        #region P/Invoke - Physical Cursor Position

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicalCursorPos(out POINT lpPoint);

        #endregion

        #region P/Invoke - DPI Awareness

        [DllImport("user32.dll")]
        private static extern IntPtr GetDpiAwarenessContext();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AreDpiAwarenessContextsEqual(IntPtr dpiAwarenessContextA, IntPtr dpiAwarenessContextB);

        #endregion

        #region P/Invoke - GDI (Highlight Ring)

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int fnObject);

        [DllImport("gdi32.dll")]
        private static extern int SetROP2(IntPtr hdc, int fnDrawMode);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Ellipse(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        #endregion

        #region P/Invoke - Verification & Window Targeting

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION U;
        }

        // The union must include all input types so Marshal.SizeOf(INPUT) matches
        // what Windows expects (40 bytes on x64); otherwise SendInput fails.
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        #endregion

        #endregion
    }
}
