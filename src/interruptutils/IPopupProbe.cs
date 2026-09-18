using System;
using System.Collections.Generic;

namespace InterruptAutomation
{
    /// <summary>What the engine needs to know about a top-level window to match it against the rules.</summary>
    internal sealed class PopupWindowInfo
    {
        public string ClassName { get; set; }
        public string Title { get; set; }
        public uint ProcessId { get; set; }
    }

    /// <summary>One button inside a popup.</summary>
    internal sealed class PopupButton
    {
        public IntPtr Handle { get; set; }
        public int Id { get; set; }

        /// <summary>The button's text with its <c>&amp;</c> access-key marker removed.</summary>
        public string Text { get; set; }
    }

    /// <summary>
    /// Everything the popup engine asks of the operating system. The real implementation
    /// is <c>Win32PopupProbe</c>; the tests substitute a fake so the engine's decisions can
    /// be checked without a desktop.
    /// </summary>
    internal interface IPopupProbe
    {
        /// <summary>The ID of the process the handler runs in; popups it owns are never touched.</summary>
        uint CurrentProcessId { get; }

        bool IsWindow(IntPtr hwnd);

        /// <summary>Class, title and owner of a window, or <c>null</c> if it no longer exists.</summary>
        PopupWindowInfo Describe(IntPtr hwnd);

        /// <summary>The name of the process with this ID (no <c>.exe</c>), or an empty string.</summary>
        string GetProcessName(uint processId);

        /// <summary>The popup's message: its first non-empty static text, or an empty string.</summary>
        string GetMessageText(IntPtr hwnd);

        /// <summary>The native buttons inside the popup (possibly none).</summary>
        IReadOnlyList<PopupButton> GetButtons(IntPtr hwnd);

        /// <summary>Clicks a button, waiting up to <paramref name="waitForEnabledMs"/> for it to be enabled; returns whether it was enabled when clicked.</summary>
        bool ClickButton(PopupButton button, int waitForEnabledMs);

        /// <summary>Asks the window to close (the same as its close box).</summary>
        void CloseWindow(IntPtr hwnd);

        /// <summary>Every visible top-level window.</summary>
        IReadOnlyList<IntPtr> EnumerateTopLevelWindows();
    }

    /// <summary>Delivers the handle of every top-level window that appears, as a background thread sees it.</summary>
    internal interface IPopupHookSource
    {
        /// <summary>Starts delivering window handles to <paramref name="onWindow"/> (called on a hook thread; it must return quickly).</summary>
        bool Start(Action<IntPtr> onWindow, out string message);

        /// <summary>Stops delivering; safe to call when not started.</summary>
        void Stop();
    }
}
