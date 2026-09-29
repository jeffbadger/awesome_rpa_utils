namespace BrowserInterruptAutomation
{
    /// <summary>
    /// A small, purpose-scoped subset of UI Automation control types relevant to browser
    /// dialogs and in-page overlays. This is a standalone enum: it does not reference
    /// <c>UIAutomationUtils</c>' own control-type enum, because every component in this
    /// repository is fully standalone and shares no project references with any other.
    /// </summary>
    public enum BrowserControlType
    {
        /// <summary>A top-level window, such as a native JS dialog's own window (UIA <c>ControlType.Window</c>).</summary>
        Window,

        /// <summary>A generic container with no more specific role (UIA <c>ControlType.Pane</c>).</summary>
        Pane,

        /// <summary>A grouping of related controls (UIA <c>ControlType.Group</c>), typical of an ARIA <c>role="dialog"</c> or <c>role="alertdialog"</c> overlay.</summary>
        Group,

        /// <summary>A control with no built-in UIA role, exposed only through its ARIA attributes (UIA <c>ControlType.Custom</c>).</summary>
        Custom,

        /// <summary>A clickable button, such as a dialog's OK/Cancel button or an overlay's close control (UIA <c>ControlType.Button</c>).</summary>
        Button,

        /// <summary>Read-only text, typically a dialog or overlay's message (UIA <c>ControlType.Text</c>).</summary>
        Text,

        /// <summary>A page or document root (UIA <c>ControlType.Document</c>), the browser tab's rendered content area.</summary>
        Document
    }
}
