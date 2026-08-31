using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace UIAutomation
{
    /// <summary>
    /// A common UI Automation control type, mapped internally to
    /// <see cref="System.Windows.Automation.ControlType"/>. Exposed as an enum (rather than
    /// <c>ControlType</c> directly, which is a class of static instances) so it works
    /// cleanly as a Pega Robot Studio designer parameter.
    /// </summary>
    public enum UiControlType
    {
        /// <summary>A push button (ControlType.Button).</summary>
        Button,
        /// <summary>A checkbox (ControlType.CheckBox).</summary>
        CheckBox,
        /// <summary>A combo box (ControlType.ComboBox).</summary>
        ComboBox,
        /// <summary>A text edit field (ControlType.Edit).</summary>
        Edit,
        /// <summary>A hyperlink (ControlType.Hyperlink).</summary>
        Hyperlink,
        /// <summary>An image (ControlType.Image).</summary>
        Image,
        /// <summary>A list (ControlType.List).</summary>
        List,
        /// <summary>A list item (ControlType.ListItem).</summary>
        ListItem,
        /// <summary>A menu (ControlType.Menu).</summary>
        Menu,
        /// <summary>A menu item (ControlType.MenuItem).</summary>
        MenuItem,
        /// <summary>A generic container pane (ControlType.Pane).</summary>
        Pane,
        /// <summary>A radio button (ControlType.RadioButton).</summary>
        RadioButton,
        /// <summary>A tab control (ControlType.Tab).</summary>
        Tab,
        /// <summary>A tab page/item (ControlType.TabItem).</summary>
        TabItem,
        /// <summary>A static text label (ControlType.Text).</summary>
        Text,
        /// <summary>A tree control (ControlType.Tree).</summary>
        Tree,
        /// <summary>A tree node/item (ControlType.TreeItem).</summary>
        TreeItem,
        /// <summary>A top-level window (ControlType.Window).</summary>
        Window,
        /// <summary>A data grid (ControlType.DataGrid).</summary>
        DataGrid,
        /// <summary>A data grid row/cell item (ControlType.DataItem).</summary>
        DataItem,
        /// <summary>A logical grouping of other controls (ControlType.Group).</summary>
        Group,
        /// <summary>A column/row header, e.g. in a grid (ControlType.Header).</summary>
        Header,
        /// <summary>A slider (ControlType.Slider).</summary>
        Slider,
        /// <summary>A spinner/numeric up-down control (ControlType.Spinner).</summary>
        Spinner,
        /// <summary>A toolbar (ControlType.ToolBar).</summary>
        ToolBar,
        /// <summary>An application-defined custom control (ControlType.Custom).</summary>
        Custom
    }

    /// <summary>
    /// Pega Robot Studio-ready component that finds and drives modern
    /// (WinUI3/UWP/WPF/browser-hosted) UI via Windows UI Automation (UIA) - the controls
    /// that <c>WindowUtils</c>/<c>DialogUtils</c> can't see, since those operate on native
    /// Win32 windows/controls by handle and window class.
    /// </summary>
    /// <remarks>
    /// Naming note: every other component in this repo follows a
    /// <c>&lt;Name&gt;Utils</c>/<c>&lt;Name&gt;Automation</c> pattern (e.g. <c>WindowUtils</c>/
    /// <c>WindowAutomation</c>). Applying that literally here would produce the redundant
    /// <c>UIAutomationUtils</c>/<c>UIAutomationAutomation</c> - so this component's assembly/
    /// namespace is just <c>UIAutomation</c> (no trailing "Automation"), a deliberate,
    /// documented one-off exception.
    /// </remarks>
    [Description("Finds and drives modern (WinUI3/UWP/WPF/browser-hosted) UI via Windows UI " +
                 "Automation. Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class UIAutomationUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public UIAutomationUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public UIAutomationUtils(IContainer container)
        {
            container?.Add(this);
        }

        /// <summary>
        /// Standard component cleanup override. UIAutomationUtils holds no unmanaged
        /// resources; implementing the pattern keeps the designer-generated teardown complete.
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

        #region Find

        /// <summary>Gets the desktop root element.</summary>
        [Category("UIAutomation - Find")]
        [Description("Gets the desktop root element.")]
        public AutomationElement GetRootElement()
        {
            return AutomationElement.RootElement;
        }

        /// <summary>
        /// Gets the UI Automation element for a window handle, bridging a handle obtained
        /// from <c>WindowUtils</c>/<c>DialogUtils</c> into UIA.
        /// </summary>
        /// <param name="hWnd">The window handle to wrap.</param>
        /// <returns>The corresponding <see cref="AutomationElement"/>, or <c>null</c> if <paramref name="hWnd"/> is zero or invalid.</returns>
        [Category("UIAutomation - Find")]
        [Description("Gets the UI Automation element for a window handle (bridges WindowUtils/DialogUtils), or null if invalid.")]
        public AutomationElement FromWindowHandle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return null;

            try
            {
                return AutomationElement.FromHandle(hWnd);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the UI Automation element at a screen point, bridging coordinates from
        /// <c>MouseUtils</c> into UIA.
        /// </summary>
        /// <param name="x">Screen X coordinate in pixels.</param>
        /// <param name="y">Screen Y coordinate in pixels.</param>
        /// <returns>The element at that point, or <c>null</c> on failure.</returns>
        [Category("UIAutomation - Find")]
        [Description("Gets the UI Automation element at a screen point (bridges MouseUtils coordinates), or null on failure.")]
        public AutomationElement FromPoint(int x, int y)
        {
            try
            {
                return AutomationElement.FromPoint(new System.Windows.Point(x, y));
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidCastException)
            {
                return null;
            }
        }

        /// <summary>
        /// Finds a descendant (or immediate child) element by its <c>AutomationId</c>.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="automationId">The AutomationId to match (exact).</param>
        /// <param name="element">The matching element, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> if the search completed (found or genuinely not found); otherwise a human-readable reason a real argument error prevented the search.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> if a matching element was found; <c>false</c> if it wasn't found, or if a real argument error (null <paramref name="parent"/>, empty <paramref name="automationId"/>) prevented the search (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Finds a descendant (or child) element by its AutomationId. Returns True if found; never throws.")]
        public bool FindByAutomationId(AutomationElement parent, string automationId, out AutomationElement element, out string message, bool descendantsOnly = true)
        {
            element = default;
            message = default;
            try
            {
                element = null;
                if (parent == null)
                {
                    message = "A parent element is required.";
                    return false;
                }
                if (string.IsNullOrEmpty(automationId))
                {
                    message = "An automation ID is required.";
                    return false;
                }

                var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
                var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;

                if (!TryUia(() => parent.FindFirst(scope, condition), out element, out message))
                    return false; // TryUia already set element/message

                message = null;
                return element != null;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("FindByAutomationId", ex);
                return false;
            }
        }

        /// <summary>
        /// Finds a descendant (or immediate child) element by its <c>Name</c>.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="name">The name to match.</param>
        /// <param name="element">The matching element, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> if the search completed (found or genuinely not found); otherwise a human-readable reason a real argument error prevented the search.</param>
        /// <param name="exactMatch">If <c>true</c> (default), requires an exact match. If <c>false</c>, matches any element whose name contains <paramref name="name"/> (case-insensitive).</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> if a matching element was found; <c>false</c> if it wasn't found, or if a real argument error (null <paramref name="parent"/>, empty <paramref name="name"/>) prevented the search (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Finds a descendant (or child) element by its Name (exact or substring match). Returns True if found; never throws.")]
        public bool FindByName(AutomationElement parent, string name, out AutomationElement element, out string message, bool exactMatch = true, bool descendantsOnly = true)
        {
            element = default;
            message = default;
            try
            {
                element = null;
                if (parent == null)
                {
                    message = "A parent element is required.";
                    return false;
                }
                if (string.IsNullOrEmpty(name))
                {
                    message = "A name is required.";
                    return false;
                }

                var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;

                if (exactMatch)
                {
                    var condition = new PropertyCondition(AutomationElement.NameProperty, name);
                    if (!TryUia(() => parent.FindFirst(scope, condition), out element, out message))
                        return false; // TryUia already set element/message
                }
                else
                {
                    // PropertyCondition only supports exact-value matches - UIA has no built-in
                    // substring condition, so enumerate candidates ourselves and filter, the same
                    // way WindowUtils.FindWindowByTitle does for its exactMatch=false case.
                    if (!TryUia(() => FindFirstMatching(parent, scope, el =>
                    {
                        // A candidate whose UI died mid-enumeration is simply skipped.
                        string elementName;
                        try
                        {
                            elementName = el.Current.Name;
                        }
                        catch (ElementNotAvailableException)
                        {
                            return false;
                        }
                        return elementName != null && elementName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
                    }), out element, out message))
                        return false; // TryUia already set element/message
                }

                message = null;
                return element != null;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("FindByName", ex);
                return false;
            }
        }

        /// <summary>
        /// Finds a descendant (or immediate child) element by its window class name.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="className">The class name to match (exact).</param>
        /// <param name="element">The matching element, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> if the search completed (found or genuinely not found); otherwise a human-readable reason a real argument error prevented the search.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> if a matching element was found; <c>false</c> if it wasn't found, or if a real argument error (null <paramref name="parent"/>, empty <paramref name="className"/>) prevented the search (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Finds a descendant (or child) element by its window class name. Returns True if found; never throws.")]
        public bool FindByClassName(AutomationElement parent, string className, out AutomationElement element, out string message, bool descendantsOnly = true)
        {
            element = default;
            message = default;
            try
            {
                element = null;
                if (parent == null)
                {
                    message = "A parent element is required.";
                    return false;
                }
                if (string.IsNullOrEmpty(className))
                {
                    message = "A class name is required.";
                    return false;
                }

                var condition = new PropertyCondition(AutomationElement.ClassNameProperty, className);
                var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;

                if (!TryUia(() => parent.FindFirst(scope, condition), out element, out message))
                    return false; // TryUia already set element/message

                message = null;
                return element != null;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("FindByClassName", ex);
                return false;
            }
        }

        /// <summary>
        /// Finds the first descendant (or immediate child) element of the given control type.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="controlType">The control type to match.</param>
        /// <param name="element">The matching element, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> if the search completed (found or genuinely not found); otherwise a human-readable reason a real argument error prevented the search.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> if a matching element was found; <c>false</c> if it wasn't found, or if a real argument error (null <paramref name="parent"/>, undefined <paramref name="controlType"/>) prevented the search (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Finds the first descendant (or child) element of the given control type. Returns True if found; never throws.")]
        public bool FindByControlType(AutomationElement parent, UiControlType controlType, out AutomationElement element, out string message, bool descendantsOnly = true)
        {
            element = default;
            message = default;
            try
            {
                element = null;
                if (parent == null)
                {
                    message = "A parent element is required.";
                    return false;
                }
                if (!TryToControlType(controlType, out ControlType nativeType, out message))
                    return false;

                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, nativeType);
                var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;

                if (!TryUia(() => parent.FindFirst(scope, condition), out element, out message))
                    return false; // TryUia already set element/message

                message = null;
                return element != null;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("FindByControlType", ex);
                return false;
            }
        }

        /// <summary>
        /// Finds every descendant (or immediate child) element of the given control type.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="controlType">The control type to match.</param>
        /// <param name="elements">Every matching element, in tree order (empty if none match), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the search failed.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="parent"/> is null or <paramref name="controlType"/> is undefined. Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Finds every descendant (or child) element of the given control type. Returns True on success; never throws.")]
        public bool FindAllByControlType(AutomationElement parent, UiControlType controlType, out List<AutomationElement> elements, out string message, bool descendantsOnly = true)
        {
            elements = default;
            message = default;
            try
            {
                elements = null;
                if (parent == null)
                {
                    message = "A parent element is required.";
                    return false;
                }
                if (!TryToControlType(controlType, out ControlType nativeType, out message))
                    return false;

                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, nativeType);
                var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;

                if (!TryUia(() =>
                {
                    var results = new List<AutomationElement>();
                    foreach (AutomationElement element in parent.FindAll(scope, condition))
                        results.Add(element);
                    return results;
                }, out elements, out message))
                    return false; // TryUia already set elements/message

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("FindAllByControlType", ex);
                return false;
            }
        }

        /// <summary>Gets all immediate children of an element.</summary>
        /// <param name="parent">The element whose children to enumerate.</param>
        /// <param name="children">The element's immediate children, in tree order (empty if it has none), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="parent"/> is null. Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Gets all immediate children of an element. Returns True on success; never throws.")]
        public bool GetChildren(AutomationElement parent, out List<AutomationElement> children, out string message)
        {
            children = default;
            message = default;
            try
            {
                children = null;
                if (parent == null)
                {
                    message = "A parent element is required.";
                    return false;
                }

                if (!TryUia(() =>
                {
                    var results = new List<AutomationElement>();
                    foreach (AutomationElement child in parent.FindAll(TreeScope.Children, Condition.TrueCondition))
                        results.Add(child);
                    return results;
                }, out children, out message))
                    return false; // TryUia already set children/message

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetChildren", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the number of elements in a list produced by <see cref="GetChildren"/> or
        /// <see cref="FindAllByControlType"/>, for designers who would rather loop by scalar
        /// index than iterate a collection proxy directly.
        /// </summary>
        /// <param name="elements">A list of elements, typically from <see cref="GetChildren"/> or <see cref="FindAllByControlType"/>.</param>
        /// <param name="count">The number of elements in the list, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="elements"/> is null. Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Gets the number of elements in a list from GetChildren/FindAllByControlType. Returns True on success; never throws.")]
        public bool GetElementCount(List<AutomationElement> elements, out int count, out string message)
        {
            count = default;
            message = default;
            try
            {
                if (elements == null)
                {
                    message = "An element list is required.";
                    return false;
                }
                count = elements.Count;
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetElementCount", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the element at a given index in a list produced by <see cref="GetChildren"/> or
        /// <see cref="FindAllByControlType"/>, for designers who would rather loop by scalar
        /// index than iterate a collection proxy directly. Pair with
        /// <see cref="GetElementCount"/> to drive the loop bound.
        /// </summary>
        /// <param name="elements">A list of elements, typically from <see cref="GetChildren"/> or <see cref="FindAllByControlType"/>.</param>
        /// <param name="index">The zero-based index of the element to get.</param>
        /// <param name="element">The element at <paramref name="index"/>, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the lookup failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="elements"/> is null or <paramref name="index"/> is out of range. Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Gets the element at a given index in a list from GetChildren/FindAllByControlType. Returns True on success; never throws.")]
        public bool GetElementAt(List<AutomationElement> elements, int index, out AutomationElement element, out string message)
        {
            element = default;
            message = default;
            try
            {
                element = null;
                if (elements == null)
                {
                    message = "An element list is required.";
                    return false;
                }
                if (index < 0 || index >= elements.Count)
                {
                    message = $"Index {index} is out of range for a list of {elements.Count} element(s).";
                    return false;
                }
                element = elements[index];
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetElementAt", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="GetChildren"/>, but summarizes each immediate child's Name,
        /// AutomationId, ClassName, control type, and bounding rectangle into a JSON string,
        /// for designers who cannot construct an <see cref="AutomationElement"/> collection
        /// proxy just to see what's there.
        /// </summary>
        /// <param name="parent">The element whose children to enumerate.</param>
        /// <param name="json">
        /// The children as JSON: <c>[{"name":"...","automationId":"...","className":"...","controlType":"...","bounds":{"left":0,"top":0,"width":0,"height":0}}]</c>.
        /// A child that has no on-screen bounding rectangle reports <c>"bounds":null</c>.
        /// <c>null</c> if this method returns <c>false</c>.
        /// </param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="parent"/> is null. Never throws.</returns>
        [Category("UIAutomation - Find")]
        [Description("Gets all immediate children of an element, summarized as a JSON array (name/automationId/className/controlType/bounds). Returns True on success; never throws.")]
        public bool GetChildrenSummaryJson(AutomationElement parent, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                json = null;
                if (!GetChildren(parent, out List<AutomationElement> children, out message))
                    return false;

                var payload = children.ConvertAll(child =>
                {
                    GetName(child, out string name, out _);
                    GetAutomationId(child, out string automationId, out _);
                    GetClassName(child, out string className, out _);
                    GetControlTypeName(child, out string controlType, out _);
                    object bounds = GetBoundingRectangleAsRectangle(child, out System.Drawing.Rectangle rc, out _)
                        ? (object)new { left = rc.Left, top = rc.Top, width = rc.Width, height = rc.Height }
                        : null;

                    return new
                    {
                        name,
                        automationId,
                        className,
                        controlType,
                        bounds
                    };
                });

                json = System.Text.Json.JsonSerializer.Serialize(payload);
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetChildrenSummaryJson", ex);
                return false;
            }
        }

        #endregion

        #region Properties

        /// <summary>Gets an element's <c>Name</c> property.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="name">The element's name, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null. Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's Name property. Returns True on success; never throws.")]
        public bool GetName(AutomationElement element, out string name, out string message)
        {
            name = default;
            message = default;
            try
            {
                name = null;
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.Current.Name, out name, out message))
                    return false; // TryUia already set name/message

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                name = NeverThrowsGuard.Failure("GetName", ex);
                return false;
            }
        }

        /// <summary>Gets an element's <c>AutomationId</c> property.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="automationId">The element's AutomationId, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null. Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's AutomationId property. Returns True on success; never throws.")]
        public bool GetAutomationId(AutomationElement element, out string automationId, out string message)
        {
            automationId = default;
            message = default;
            try
            {
                automationId = null;
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }
                if (!TryUia(() => element.Current.AutomationId, out automationId, out message))
                    return false; // TryUia already set automationId/message

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                automationId = NeverThrowsGuard.Failure("GetAutomationId", ex);
                return false;
            }
        }

        /// <summary>Gets an element's window class name.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="className">The element's window class name, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null. Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's window class name. Returns True on success; never throws.")]
        public bool GetClassName(AutomationElement element, out string className, out string message)
        {
            className = default;
            message = default;
            try
            {
                className = null;
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }
                if (!TryUia(() => element.Current.ClassName, out className, out message))
                    return false; // TryUia already set className/message

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                className = NeverThrowsGuard.Failure("GetClassName", ex);
                return false;
            }
        }

        /// <summary>Gets a friendly name for an element's control type (e.g. <c>"Button"</c>).</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="controlTypeName">The friendly control-type name, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null. Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Gets a friendly name for an element's control type (e.g. \"Button\"). Returns True on success; never throws.")]
        public bool GetControlTypeName(AutomationElement element, out string controlTypeName, out string message)
        {
            controlTypeName = default;
            message = default;
            try
            {
                controlTypeName = null;
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                // ProgrammaticName looks like "ControlType.Button" - strip the prefix so callers
                // get the same short form used by UiControlType (e.g. "Button").
                if (!TryUia(() =>
                {
                    ControlType controlType = element.Current.ControlType;
                    return controlType?.ProgrammaticName;
                }, out controlTypeName, out message))
                    return false; // TryUia already set controlTypeName/message

                if (controlTypeName == null)
                {
                    message = "This element does not report a ControlType.";
                    return false;
                }

                const string prefix = "ControlType.";
                controlTypeName = controlTypeName.StartsWith(prefix, StringComparison.Ordinal)
                    ? controlTypeName.Substring(prefix.Length)
                    : controlTypeName;
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                controlTypeName = NeverThrowsGuard.Failure("GetControlTypeName", ex);
                return false;
            }
        }

        /// <summary>Gets an element's screen-space bounding rectangle.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="bounds">The element's bounding rectangle, or <c>default</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null. Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's screen-space bounding rectangle. Returns True on success; never throws.")]
        public bool GetBoundingRectangleAsRectangle(AutomationElement element, out System.Drawing.Rectangle bounds, out string message)
        {
            bounds = default;
            message = default;
            try
            {
                bounds = default;
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.Current.BoundingRectangle, out System.Windows.Rect rect, out message))
                    return false; // TryUia already set bounds/message

                // UIA reports an empty (infinite) rectangle for elements with no on-screen
                // presence; the raw int casts would produce int.MinValue/MaxValue garbage.
                if (rect.IsEmpty || double.IsInfinity(rect.X) || double.IsInfinity(rect.Y)
                    || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height))
                {
                    bounds = default;
                    message = "The element has no on-screen bounding rectangle (it may be closed or not currently rendered).";
                    return false;
                }
                bounds = new System.Drawing.Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetBoundingRectangleAsRectangle", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="GetBoundingRectangleAsRectangle(AutomationElement, out System.Drawing.Rectangle, out string)"/>,
        /// but reports the bounds as scalar left/top/width/height outputs, for designers
        /// without a <c>Rectangle</c> proxy.
        /// </summary>
        /// <param name="element">The element to read.</param>
        /// <param name="left">Left edge of the bounding rectangle, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="top">Top edge of the bounding rectangle, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="width">Width of the bounding rectangle, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="height">Height of the bounding rectangle, or <c>0</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it has no on-screen bounding rectangle. Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's screen-space bounding rectangle as scalar left/top/width/height. Returns True on success; never throws.")]
        public bool GetBoundingRectangle(AutomationElement element, out int left, out int top, out int width, out int height, out string message)
        {
            left = default;
            top = default;
            width = default;
            height = default;
            bool ok = GetBoundingRectangleAsRectangle(element, out System.Drawing.Rectangle bounds, out message);
            left = bounds.Left;
            top = bounds.Top;
            width = bounds.Width;
            height = bounds.Height;
            return ok;
        }

        /// <summary>Returns <c>true</c> if the element is enabled.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely disabled element).</param>
        /// <returns><c>true</c> if the element is enabled; <c>false</c> if it isn't, or if <paramref name="element"/> is null (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is enabled. Never throws.")]
        public bool IsEnabledSimple(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }
                if (!TryUia(() => element.Current.IsEnabled, out bool enabled, out message))
                    return false; // stale element: reported via message, same as a disabled element

                message = null;
                return enabled;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsEnabledSimple", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="IsEnabledSimple(AutomationElement, out string)"/>, but separates
        /// "the query succeeded" from "the element is enabled" into two outputs, so the
        /// automation does not need to interpret <paramref name="message"/> for <c>null</c>.
        /// </summary>
        /// <param name="element">The element to read.</param>
        /// <param name="querySucceeded"><c>true</c> if the query itself completed (whether or not the element turned out to be enabled); <c>false</c> if <paramref name="element"/> is null or the query failed (check <paramref name="message"/>).</param>
        /// <param name="message"><c>null</c> whenever <paramref name="querySucceeded"/> is <c>true</c>; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if the element is enabled; <c>false</c> if it isn't, or if the query failed (check <paramref name="querySucceeded"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is enabled, plus whether the query itself succeeded. Never throws.")]
        public bool IsEnabled(AutomationElement element, out bool querySucceeded, out string message)
        {
            bool enabled = IsEnabledSimple(element, out message);
            querySucceeded = message == null;
            return enabled;
        }

        /// <summary>Returns <c>true</c> if the element is offscreen.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely onscreen element).</param>
        /// <returns><c>true</c> if the element is offscreen; <c>false</c> if it isn't, or if <paramref name="element"/> is null (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is offscreen. Never throws.")]
        public bool IsOffscreenSimple(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }
                if (!TryUia(() => element.Current.IsOffscreen, out bool offscreen, out message))
                    return false; // stale element: reported via message, same as a genuinely onscreen element

                message = null;
                return offscreen;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsOffscreenSimple", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="IsOffscreenSimple(AutomationElement, out string)"/>, but separates
        /// "the query succeeded" from "the element is offscreen" into two outputs, so the
        /// automation does not need to interpret <paramref name="message"/> for <c>null</c>.
        /// </summary>
        /// <param name="element">The element to read.</param>
        /// <param name="querySucceeded"><c>true</c> if the query itself completed (whether or not the element turned out to be offscreen); <c>false</c> if <paramref name="element"/> is null or the query failed (check <paramref name="message"/>).</param>
        /// <param name="message"><c>null</c> whenever <paramref name="querySucceeded"/> is <c>true</c>; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if the element is offscreen; <c>false</c> if it isn't, or if the query failed (check <paramref name="querySucceeded"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is offscreen, plus whether the query itself succeeded. Never throws.")]
        public bool IsOffscreen(AutomationElement element, out bool querySucceeded, out string message)
        {
            bool offscreen = IsOffscreenSimple(element, out message);
            querySucceeded = message == null;
            return offscreen;
        }

        /// <summary>
        /// Returns <c>true</c> if the element is still available (its underlying UI hasn't
        /// gone away). Unlike every other method here, a <c>null</c> <paramref name="element"/>
        /// returns <c>false</c> rather than throwing - this method's entire purpose is
        /// checking whether a reference is still good, and a null reference is definitionally
        /// not available.
        /// </summary>
        /// <param name="element">The element to check, or <c>null</c>.</param>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is still available (its underlying UI hasn't gone away).")]
        public bool IsElementAvailable(AutomationElement element)
        {
            if (element == null)
                return false;

            try
            {
                // Any Current property access throws ElementNotAvailableException if the
                // element's underlying UI has gone away - IsEnabled is as good as any.
                _ = element.Current.IsEnabled;
                return true;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
            catch (Exception)
            {
                // A provider that fails on any property read can't be treated as
                // reliably available either.
                return false;
            }
        }

        #endregion

        #region Actions

        /// <summary>Invokes an element (click-equivalent for buttons/menu items) via <c>InvokePattern</c>.</summary>
        /// <param name="element">The element to invoke.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the invoke failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support InvokePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Invokes an element (click-equivalent for buttons/menu items) via InvokePattern. Returns True on success; never throws.")]
        public bool Invoke(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(InvokePattern.Pattern, out object patternObj) ? patternObj : null,
                            out object invokeObj, out message))
                    return false; // TryUia already set message
                if (invokeObj == null)
                {
                    message = "This element does not support InvokePattern.";
                    return false;
                }

                if (!TryUiaAction(() => ((InvokePattern)invokeObj).Invoke(), out message))
                    return false; // element died between the pattern lookup and the invoke

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Invoke", ex);
                return false;
            }
        }

        /// <summary>Sets an element's value via <c>ValuePattern</c>.</summary>
        /// <param name="element">The element to set.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the set failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/>/<paramref name="value"/> are null, or the element does not support ValuePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Sets an element's value via ValuePattern. Returns True on success; never throws.")]
        public bool SetValue(AutomationElement element, string value, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }
                if (value == null)
                {
                    message = "A value is required.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObj) ? patternObj : null,
                            out object setValueObj, out message))
                    return false; // TryUia already set message
                if (setValueObj == null)
                {
                    message = "This element does not support ValuePattern.";
                    return false;
                }

                if (!TryUiaAction(() => ((ValuePattern)setValueObj).SetValue(value), out message))
                    return false; // e.g. the element died or is disabled (ElementNotEnabledException)

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetValue", ex);
                return false;
            }
        }

        /// <summary>Gets an element's value via <c>ValuePattern</c>.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="value">The element's value, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support ValuePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Gets an element's value via ValuePattern. Returns True on success; never throws.")]
        public bool GetValue(AutomationElement element, out string value, out string message)
        {
            value = default;
            message = default;
            try
            {
                value = null;
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObj) ? patternObj : null,
                            out object getValueObj, out message))
                    return false; // TryUia already set message
                if (getValueObj == null)
                {
                    message = "This element does not support ValuePattern.";
                    return false;
                }

                if (!TryUia(() => ((ValuePattern)getValueObj).Current.Value, out value, out message))
                    return false; // TryUia already set value/message

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                value = NeverThrowsGuard.Failure("GetValue", ex);
                return false;
            }
        }

        /// <summary>Toggles an element (e.g. a checkbox) via <c>TogglePattern</c>.</summary>
        /// <param name="element">The element to toggle.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the toggle failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support TogglePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Toggles an element (e.g. a checkbox) via TogglePattern. Returns True on success; never throws.")]
        public bool Toggle(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(TogglePattern.Pattern, out object patternObj) ? patternObj : null,
                            out object toggleObj, out message))
                    return false; // TryUia already set message
                if (toggleObj == null)
                {
                    message = "This element does not support TogglePattern.";
                    return false;
                }

                if (!TryUiaAction(() => ((TogglePattern)toggleObj).Toggle(), out message))
                    return false; // element died between the pattern lookup and the toggle

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Toggle", ex);
                return false;
            }
        }

        /// <summary>Returns <c>true</c> if a toggleable element is currently On.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely Off/indeterminate element).</param>
        /// <returns><c>true</c> if the element is On; <c>false</c> if it isn't, or if <paramref name="element"/> is null/does not support TogglePattern (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Returns True if a toggleable element is currently On. Never throws.")]
        public bool IsToggledSimple(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(TogglePattern.Pattern, out object patternObj) ? patternObj : null,
                            out object toggleStateObj, out message))
                    return false; // TryUia already set message
                if (toggleStateObj == null)
                {
                    message = "This element does not support TogglePattern.";
                    return false;
                }

                if (!TryUia(() => ((TogglePattern)toggleStateObj).Current.ToggleState == ToggleState.On, out bool isOn, out message))
                    return false; // TryUia already set message

                message = null;
                return isOn;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsToggledSimple", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="IsToggledSimple(AutomationElement, out string)"/>, but separates
        /// "the query succeeded" from "the element is On" into two outputs, so the automation
        /// does not need to interpret <paramref name="message"/> for <c>null</c>.
        /// </summary>
        /// <param name="element">The element to read.</param>
        /// <param name="querySucceeded"><c>true</c> if the query itself completed (whether or not the element turned out to be On); <c>false</c> if <paramref name="element"/> is null, it doesn't support TogglePattern, or the query otherwise failed (check <paramref name="message"/>).</param>
        /// <param name="message"><c>null</c> whenever <paramref name="querySucceeded"/> is <c>true</c>; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if the element is On; <c>false</c> if it isn't, or if the query failed (check <paramref name="querySucceeded"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Returns True if a toggleable element is currently On, plus whether the query itself succeeded. Never throws.")]
        public bool IsToggled(AutomationElement element, out bool querySucceeded, out string message)
        {
            bool isOn = IsToggledSimple(element, out message);
            querySucceeded = message == null;
            return isOn;
        }

        /// <summary>Expands an element (e.g. a combo box or tree node) via <c>ExpandCollapsePattern</c>.</summary>
        /// <param name="element">The element to expand.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the expand failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support ExpandCollapsePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Expands an element (e.g. a combo box or tree node) via ExpandCollapsePattern. Returns True on success; never throws.")]
        public bool Expand(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object patternObj) ? patternObj : null,
                            out object expandObj, out message))
                    return false; // TryUia already set message
                if (expandObj == null)
                {
                    message = "This element does not support ExpandCollapsePattern.";
                    return false;
                }

                if (!TryUiaAction(() => ((ExpandCollapsePattern)expandObj).Expand(), out message))
                    return false; // element died between the pattern lookup and the expand

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Expand", ex);
                return false;
            }
        }

        /// <summary>Collapses an element via <c>ExpandCollapsePattern</c>.</summary>
        /// <param name="element">The element to collapse.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the collapse failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support ExpandCollapsePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Collapses an element via ExpandCollapsePattern. Returns True on success; never throws.")]
        public bool Collapse(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object patternObj) ? patternObj : null,
                            out object collapseObj, out message))
                    return false; // TryUia already set message
                if (collapseObj == null)
                {
                    message = "This element does not support ExpandCollapsePattern.";
                    return false;
                }

                if (!TryUiaAction(() => ((ExpandCollapsePattern)collapseObj).Collapse(), out message))
                    return false; // element died between the pattern lookup and the collapse

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Collapse", ex);
                return false;
            }
        }

        /// <summary>Selects an element (e.g. a list item) via <c>SelectionItemPattern</c>.</summary>
        /// <param name="element">The element to select.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the selection failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support SelectionItemPattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Selects an element (e.g. a list item) via SelectionItemPattern. Returns True on success; never throws.")]
        public bool Select(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object patternObj) ? patternObj : null,
                            out object selectObj, out message))
                    return false; // TryUia already set message
                if (selectObj == null)
                {
                    message = "This element does not support SelectionItemPattern.";
                    return false;
                }

                if (!TryUiaAction(() => ((SelectionItemPattern)selectObj).Select(), out message))
                    return false; // element died between the pattern lookup and the select

                message = null;
                return true;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Select", ex);
                return false;
            }
        }

        /// <summary>Returns <c>true</c> if a selectable element is currently selected.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely unselected element).</param>
        /// <returns><c>true</c> if the element is selected; <c>false</c> if it isn't, or if <paramref name="element"/> is null/does not support SelectionItemPattern (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Returns True if a selectable element is currently selected. Never throws.")]
        public bool IsSelectedSimple(AutomationElement element, out string message)
        {
            message = default;
            try
            {
                if (element == null)
                {
                    message = "An element is required.";
                    return false;
                }

                if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object patternObj))
                {
                    message = "This element does not support SelectionItemPattern.";
                    return false;
                }

                if (!TryUia(() => element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object patternObj) ? patternObj : null,
                            out object selectedStateObj, out message))
                    return false; // TryUia already set message
                if (selectedStateObj == null)
                {
                    message = "This element does not support SelectionItemPattern.";
                    return false;
                }

                if (!TryUia(() => ((SelectionItemPattern)selectedStateObj).Current.IsSelected, out bool isSelected, out message))
                    return false; // TryUia already set message

                message = null;
                return isSelected;

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("IsSelectedSimple", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="IsSelectedSimple(AutomationElement, out string)"/>, but separates
        /// "the query succeeded" from "the element is selected" into two outputs, so the
        /// automation does not need to interpret <paramref name="message"/> for <c>null</c>.
        /// </summary>
        /// <param name="element">The element to read.</param>
        /// <param name="querySucceeded"><c>true</c> if the query itself completed (whether or not the element turned out to be selected); <c>false</c> if <paramref name="element"/> is null, it doesn't support SelectionItemPattern, or the query otherwise failed (check <paramref name="message"/>).</param>
        /// <param name="message"><c>null</c> whenever <paramref name="querySucceeded"/> is <c>true</c>; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if the element is selected; <c>false</c> if it isn't, or if the query failed (check <paramref name="querySucceeded"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Returns True if a selectable element is currently selected, plus whether the query itself succeeded. Never throws.")]
        public bool IsSelected(AutomationElement element, out bool querySucceeded, out string message)
        {
            bool isSelected = IsSelectedSimple(element, out message);
            querySucceeded = message == null;
            return isSelected;
        }

        #endregion

        #region One-Shot (Window-Handle-Scoped)

        /// <summary>
        /// Gets all immediate children of a top-level window, given its handle, without
        /// retaining an intermediate <see cref="AutomationElement"/> proxy for the window
        /// itself. Equivalent to <see cref="FromWindowHandle"/> followed by
        /// <see cref="GetChildren"/>.
        /// </summary>
        /// <param name="hWnd">Handle of the window to enumerate, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="children">The window's immediate children, in tree order (empty if it has none), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is zero or invalid. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Gets all immediate children of a top-level window from its handle, in one call. Returns True on success; never throws.")]
        public bool GetChildrenFromWindowHandle(IntPtr hWnd, out List<AutomationElement> children, out string message)
        {
            children = default;
            message = default;
            try
            {
                children = null;
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }

                return GetChildren(window, out children, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetChildrenFromWindowHandle", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="GetChildrenFromWindowHandle"/>, but summarizes the children as a
        /// JSON string via <see cref="GetChildrenSummaryJson"/>, for designers who cannot
        /// construct any <see cref="AutomationElement"/> proxy at all.
        /// </summary>
        /// <param name="hWnd">Handle of the window to enumerate, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="json">The children as JSON (see <see cref="GetChildrenSummaryJson"/> for the shape), or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is zero or invalid. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Gets all immediate children of a top-level window from its handle, summarized as JSON, in one call. Returns True on success; never throws.")]
        public bool GetChildrenSummaryJsonFromWindowHandle(IntPtr hWnd, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                json = null;
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }

                return GetChildrenSummaryJson(window, out json, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetChildrenSummaryJsonFromWindowHandle", ex);
                return false;
            }
        }

        /// <summary>
        /// Invokes a descendant of a top-level window matched by <c>AutomationId</c>, given the
        /// window's handle, without retaining an intermediate <see cref="AutomationElement"/>
        /// proxy. Equivalent to <see cref="FromWindowHandle"/>, <see cref="FindByAutomationId"/>,
        /// then <see cref="Invoke"/>.
        /// </summary>
        /// <param name="hWnd">Handle of the window to search within, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="automationId">The AutomationId to match (exact).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the invoke failed.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is invalid, no matching element is found, or it does not support InvokePattern. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Finds a descendant of a window by AutomationId and invokes it, in one call. Returns True on success; never throws.")]
        public bool InvokeByAutomationId(IntPtr hWnd, string automationId, out string message, bool descendantsOnly = true)
        {
            message = default;
            try
            {
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }
                if (!FindByAutomationId(window, automationId, out AutomationElement element, out message, descendantsOnly))
                {
                    if (message == null)
                        message = $"No element with AutomationId '{automationId}' was found.";
                    return false;
                }

                return Invoke(element, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("InvokeByAutomationId", ex);
                return false;
            }
        }

        /// <summary>
        /// Sets the value of a descendant of a top-level window matched by
        /// <c>AutomationId</c>, given the window's handle, without retaining an intermediate
        /// <see cref="AutomationElement"/> proxy. Equivalent to <see cref="FromWindowHandle"/>,
        /// <see cref="FindByAutomationId"/>, then <see cref="SetValue"/>.
        /// </summary>
        /// <param name="hWnd">Handle of the window to search within, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="automationId">The AutomationId to match (exact).</param>
        /// <param name="value">The value to set.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the set failed.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is invalid, no matching element is found, or it does not support ValuePattern. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Finds a descendant of a window by AutomationId and sets its value, in one call. Returns True on success; never throws.")]
        public bool SetValueByAutomationId(IntPtr hWnd, string automationId, string value, out string message, bool descendantsOnly = true)
        {
            message = default;
            try
            {
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }
                if (!FindByAutomationId(window, automationId, out AutomationElement element, out message, descendantsOnly))
                {
                    if (message == null)
                        message = $"No element with AutomationId '{automationId}' was found.";
                    return false;
                }

                return SetValue(element, value, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetValueByAutomationId", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the value of a descendant of a top-level window matched by
        /// <c>AutomationId</c>, given the window's handle, without retaining an intermediate
        /// <see cref="AutomationElement"/> proxy. Equivalent to <see cref="FromWindowHandle"/>,
        /// <see cref="FindByAutomationId"/>, then <see cref="GetValue"/>.
        /// </summary>
        /// <param name="hWnd">Handle of the window to search within, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="automationId">The AutomationId to match (exact).</param>
        /// <param name="value">The element's value, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is invalid, no matching element is found, or it does not support ValuePattern. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Finds a descendant of a window by AutomationId and gets its value, in one call. Returns True on success; never throws.")]
        public bool GetValueByAutomationId(IntPtr hWnd, string automationId, out string value, out string message, bool descendantsOnly = true)
        {
            value = default;
            message = default;
            try
            {
                value = null;
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }
                if (!FindByAutomationId(window, automationId, out AutomationElement element, out message, descendantsOnly))
                {
                    if (message == null)
                        message = $"No element with AutomationId '{automationId}' was found.";
                    return false;
                }

                return GetValue(element, out value, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetValueByAutomationId", ex);
                return false;
            }
        }

        /// <summary>
        /// Invokes a descendant of a top-level window matched by its visible <c>Name</c>, given
        /// the window's handle, without retaining an intermediate <see cref="AutomationElement"/>
        /// proxy. Equivalent to <see cref="FromWindowHandle"/>, <see cref="FindByName"/>, then
        /// <see cref="Invoke"/>. Use this over <see cref="InvokeByAutomationId"/> when the only
        /// identifier available is the control's on-screen text (e.g. a button's label), not an
        /// internal AutomationId.
        /// </summary>
        /// <param name="hWnd">Handle of the window to search within, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="name">The visible name/text to match.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the invoke failed.</param>
        /// <param name="exactMatch">If <c>true</c> (default), requires an exact match; if <c>false</c>, matches any element whose name contains <paramref name="name"/> (case-insensitive).</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is invalid, no matching element is found, or it does not support InvokePattern. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Finds a descendant of a window by its visible name and invokes it (e.g. clicks a button), in one call. Returns True on success; never throws.")]
        public bool InvokeByName(IntPtr hWnd, string name, out string message, bool exactMatch = true, bool descendantsOnly = true)
        {
            message = default;
            try
            {
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }
                if (!FindByName(window, name, out AutomationElement element, out message, exactMatch, descendantsOnly))
                {
                    if (message == null)
                        message = $"No element with name '{name}' was found.";
                    return false;
                }

                return Invoke(element, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("InvokeByName", ex);
                return false;
            }
        }

        /// <summary>
        /// Sets the value of a descendant of a top-level window matched by its visible
        /// <c>Name</c>, given the window's handle, without retaining an intermediate
        /// <see cref="AutomationElement"/> proxy. Equivalent to <see cref="FromWindowHandle"/>,
        /// <see cref="FindByName"/>, then <see cref="SetValue"/>. Use this over
        /// <see cref="SetValueByAutomationId"/> when the only identifier available is the
        /// field's visible label, not an internal AutomationId.
        /// </summary>
        /// <param name="hWnd">Handle of the window to search within, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="name">The visible name/text to match.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the set failed.</param>
        /// <param name="exactMatch">If <c>true</c> (default), requires an exact match; if <c>false</c>, matches any element whose name contains <paramref name="name"/> (case-insensitive).</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is invalid, no matching element is found, or it does not support ValuePattern. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Finds a descendant of a window by its visible name and sets its value, in one call. Returns True on success; never throws.")]
        public bool SetValueByName(IntPtr hWnd, string name, string value, out string message, bool exactMatch = true, bool descendantsOnly = true)
        {
            message = default;
            try
            {
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }
                if (!FindByName(window, name, out AutomationElement element, out message, exactMatch, descendantsOnly))
                {
                    if (message == null)
                        message = $"No element with name '{name}' was found.";
                    return false;
                }

                return SetValue(element, value, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetValueByName", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the value of a descendant of a top-level window matched by its visible
        /// <c>Name</c>, given the window's handle, without retaining an intermediate
        /// <see cref="AutomationElement"/> proxy. Equivalent to <see cref="FromWindowHandle"/>,
        /// <see cref="FindByName"/>, then <see cref="GetValue"/>. Use this over
        /// <see cref="GetValueByAutomationId"/> when the only identifier available is the
        /// field's visible label, not an internal AutomationId.
        /// </summary>
        /// <param name="hWnd">Handle of the window to search within, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="name">The visible name/text to match.</param>
        /// <param name="value">The element's value, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <param name="exactMatch">If <c>true</c> (default), requires an exact match; if <c>false</c>, matches any element whose name contains <paramref name="name"/> (case-insensitive).</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is invalid, no matching element is found, or it does not support ValuePattern. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Finds a descendant of a window by its visible name and gets its value, in one call. Returns True on success; never throws.")]
        public bool GetValueByName(IntPtr hWnd, string name, out string value, out string message, bool exactMatch = true, bool descendantsOnly = true)
        {
            value = default;
            message = default;
            try
            {
                value = null;
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }
                if (!FindByName(window, name, out AutomationElement element, out message, exactMatch, descendantsOnly))
                {
                    if (message == null)
                        message = $"No element with name '{name}' was found.";
                    return false;
                }

                return GetValue(element, out value, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetValueByName", ex);
                return false;
            }
        }

        /// <summary>
        /// Selects an item in a combo box, list box, or tree - any descendant of a top-level
        /// window that exposes <c>SelectionItemPattern</c> - by finding the container by its
        /// visible <c>Name</c>, expanding it, then finding and selecting the item by its own
        /// visible <c>Name</c> within it. Equivalent to <see cref="FromWindowHandle"/>,
        /// <see cref="FindByName"/> (container), <see cref="Expand"/>, <see cref="FindByName"/>
        /// (item), then <see cref="Select"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="Expand"/> is attempted but not required to succeed before searching for
        /// the item: some simple list-like controls already expose their items without an
        /// explicit expand, so an <see cref="Expand"/> failure (e.g. the container doesn't
        /// support <c>ExpandCollapsePattern</c>) doesn't abort the call - only a subsequent
        /// failure to find or select the item does. This method does not collapse the container
        /// afterward; most UI Automation implementations close the popup as a side effect of
        /// selecting an item, and forcing an unrelated collapse to gate success on a task that's
        /// already complete would be surprising.
        /// </remarks>
        /// <param name="hWnd">Handle of the window to search within, typically from <c>WindowUtils.FindWindowByTitle</c> or <c>DialogUtils</c>.</param>
        /// <param name="containerName">The visible name/text of the combo box, list box, or tree to select within.</param>
        /// <param name="itemName">The visible name/text of the item to select.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the selection failed.</param>
        /// <param name="exactMatch">If <c>true</c> (default), requires an exact match for both <paramref name="containerName"/> and <paramref name="itemName"/>; if <c>false</c>, matches any element whose name contains the given text (case-insensitive).</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree for the container; if <c>false</c>, searches only the window's immediate children.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="hWnd"/> is invalid, the container or item isn't found, or the item does not support SelectionItemPattern. Never throws.</returns>
        [Category("UIAutomation - One-Shot")]
        [Description("Finds a combo box/list/tree by name, expands it if needed, then finds and selects an item within it by name, in one call. Returns True on success; never throws.")]
        public bool SelectListItemByName(IntPtr hWnd, string containerName, string itemName, out string message, bool exactMatch = true, bool descendantsOnly = true)
        {
            message = default;
            try
            {
                AutomationElement window = FromWindowHandle(hWnd);
                if (window == null)
                {
                    message = "The window handle is zero or does not correspond to a live window.";
                    return false;
                }
                if (!FindByName(window, containerName, out AutomationElement container, out message, exactMatch, descendantsOnly))
                {
                    if (message == null)
                        message = $"No element with name '{containerName}' was found.";
                    return false;
                }

                // Best-effort: some controls already expose their items without an explicit
                // expand. Its failure is only fatal if the item can't subsequently be found/selected.
                Expand(container, out _);

                if (!FindByName(container, itemName, out AutomationElement item, out message, exactMatch, descendantsOnly: true))
                {
                    if (message == null)
                        message = $"No item with name '{itemName}' was found in '{containerName}'.";
                    return false;
                }

                return Select(item, out message);

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SelectListItemByName", ex);
                return false;
            }
        }

        #endregion

        #region Wait

        /// <summary>Polls for a descendant element matching the given <c>AutomationId</c> until it appears or the timeout elapses.</summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="automationId">The AutomationId to match (exact).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="element">The matching element, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> if the poll completed (found or genuinely timed out); otherwise a human-readable reason a real argument error aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if a matching element was found before the timeout; <c>false</c> if it timed out, or if a real argument error aborted the poll (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Wait")]
        [Description("Polls for a descendant element matching the given AutomationId until it appears or the timeout elapses. Never throws.")]
        public bool WaitForElementByAutomationIdSimple(AutomationElement parent, string automationId, int timeoutMs, int pollIntervalMs, out AutomationElement element, out string message)
        {
            return WaitForElementByAutomationId(parent, automationId, timeoutMs, pollIntervalMs, out element, out _, out message);
        }

        /// <summary>
        /// Same as <see cref="WaitForElementByAutomationIdSimple(AutomationElement, string, int, int, out AutomationElement, out string)"/>,
        /// but also reports whether the wait ended because the timeout elapsed, so the
        /// automation can branch on timeout vs. a real argument error without a null-message test.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="automationId">The AutomationId to match (exact).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="element">The matching element, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="timedOut"><c>true</c> if this method returned <c>false</c> because the timeout elapsed; <c>false</c> on success or on a real argument error (check <paramref name="message"/> for the latter).</param>
        /// <param name="message"><c>null</c> if the poll completed (found or genuinely timed out); otherwise a human-readable reason a real argument error aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if a matching element was found before the timeout; <c>false</c> if it timed out, or if a real argument error aborted the poll (check <paramref name="timedOut"/>/<paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Wait")]
        [Description("Polls for a descendant element matching the given AutomationId until it appears or the timeout elapses; reports whether the wait timed out. Never throws.")]
        public bool WaitForElementByAutomationId(AutomationElement parent, string automationId, int timeoutMs, int pollIntervalMs, out AutomationElement element, out bool timedOut, out string message)
        {
            element = default;
            timedOut = default;
            message = default;
            try
            {
                if (pollIntervalMs < 1) pollIntervalMs = 1;

                int start = Environment.TickCount;
                while (true)
                {
                    if (FindByAutomationId(parent, automationId, out element, out message))
                    {
                        message = null;
                        return true;
                    }
                    if (message != null)
                        return false; // real argument error aborted the poll

                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        element = null;
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForElementByAutomationId", ex);
                return false;
            }
        }

        /// <summary>Polls for a descendant element matching the given <c>Name</c> until it appears or the timeout elapses.</summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="name">The name to match.</param>
        /// <param name="exactMatch">If <c>true</c>, requires an exact match; if <c>false</c>, matches any element whose name contains <paramref name="name"/> (case-insensitive).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="element">The matching element, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> if the poll completed (found or genuinely timed out); otherwise a human-readable reason a real argument error aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if a matching element was found before the timeout; <c>false</c> if it timed out, or if a real argument error aborted the poll (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Wait")]
        [Description("Polls for a descendant element matching the given Name until it appears or the timeout elapses. Never throws.")]
        public bool WaitForElementByNameSimple(AutomationElement parent, string name, bool exactMatch, int timeoutMs, int pollIntervalMs, out AutomationElement element, out string message)
        {
            return WaitForElementByName(parent, name, exactMatch, timeoutMs, pollIntervalMs, out element, out _, out message);
        }

        /// <summary>
        /// Same as <see cref="WaitForElementByNameSimple(AutomationElement, string, bool, int, int, out AutomationElement, out string)"/>,
        /// but also reports whether the wait ended because the timeout elapsed, so the
        /// automation can branch on timeout vs. a real argument error without a null-message test.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="name">The name to match.</param>
        /// <param name="exactMatch">If <c>true</c>, requires an exact match; if <c>false</c>, matches any element whose name contains <paramref name="name"/> (case-insensitive).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="element">The matching element, or <c>null</c> if this method returns <c>false</c>.</param>
        /// <param name="timedOut"><c>true</c> if this method returned <c>false</c> because the timeout elapsed; <c>false</c> on success or on a real argument error (check <paramref name="message"/> for the latter).</param>
        /// <param name="message"><c>null</c> if the poll completed (found or genuinely timed out); otherwise a human-readable reason a real argument error aborted the poll early (in which case this method also returns <c>false</c>).</param>
        /// <returns><c>true</c> if a matching element was found before the timeout; <c>false</c> if it timed out, or if a real argument error aborted the poll (check <paramref name="timedOut"/>/<paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Wait")]
        [Description("Polls for a descendant element matching the given Name until it appears or the timeout elapses; reports whether the wait timed out. Never throws.")]
        public bool WaitForElementByName(AutomationElement parent, string name, bool exactMatch, int timeoutMs, int pollIntervalMs, out AutomationElement element, out bool timedOut, out string message)
        {
            element = default;
            timedOut = default;
            message = default;
            try
            {
                if (pollIntervalMs < 1) pollIntervalMs = 1;

                int start = Environment.TickCount;
                while (true)
                {
                    if (FindByName(parent, name, out element, out message, exactMatch))
                    {
                        message = null;
                        return true;
                    }
                    if (message != null)
                        return false; // real argument error aborted the poll

                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        element = null;
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }

            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForElementByName", ex);
                return false;
            }
        }

        #endregion

        #region Visual

        /// <summary>
        /// Flashes an inverting rectangle around an element to visually confirm which
        /// on-screen element it corresponds to. Uses the same XOR-drawing technique as
        /// <c>DialogUtils.HighlightControl</c> (drawing twice erases it exactly).
        /// </summary>
        /// <param name="element">The element to highlight.</param>
        /// <param name="flashes">Number of on/off flashes (default 3).</param>
        /// <param name="flashMs">Milliseconds each flash stays visible (default 200).</param>
        /// <param name="lineWidth">Pen width in pixels (default 3).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the highlight failed.</param>
        /// <param name="colorRef">RGB color for the rectangle as a 0xBBGGRR value (default 0x0000FF = red).</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or the GetDC call failed. Never throws.</returns>
        [Category("UIAutomation - Visual")]
        [Description("Flashes an inverting rectangle around an element to visually confirm which on-screen element it corresponds to. Returns True on success; never throws.")]
        public bool HighlightElement(AutomationElement element, out string message, int flashes = 3, int flashMs = 200, int lineWidth = 3, int colorRef = 0x0000FF)
        {
            message = default;
            try
            {
                if (!GetBoundingRectangleAsRectangle(element, out System.Drawing.Rectangle rc, out message))
                    return false;
                if (flashes < 1) flashes = 1;
                if (flashMs < 1) flashMs = 1;
                if (lineWidth < 1) lineWidth = 1;

                // Expand outward so the ring is visible just outside the element's edge
                // (drawing on the bounds hides half the pen under the element's own pixels).
                int left = rc.Left - lineWidth;
                int top = rc.Top - lineWidth;
                int right = rc.Right + lineWidth;
                int bottom = rc.Bottom + lineWidth;

                IntPtr hdc = GetDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero)
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), "GetDC(NULL) for the screen failed.").Message;
                    return false;
                }

                IntPtr hPen = CreatePen(PS_SOLID, lineWidth, (uint)colorRef);
                IntPtr hOldPen = IntPtr.Zero;
                IntPtr hOldBrush = IntPtr.Zero;

                try
                {
                    hOldPen = SelectObject(hdc, hPen);
                    hOldBrush = SelectObject(hdc, GetStockObject(NULL_BRUSH));
                    SetROP2(hdc, R2_NOTXORPEN);

                    for (int i = 0; i < flashes; i++)
                    {
                        // Draw (visible) - XOR
                        DrawRectangle(hdc, left, top, right, bottom);
                        Thread.Sleep(flashMs);
                        // Draw again (erases) - XOR XOR = original pixels
                        DrawRectangle(hdc, left, top, right, bottom);

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
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("HighlightElement", ex);
                return false;
            }
        }

        /// <summary>
        /// Same as <see cref="HighlightElement(AutomationElement, out string, int, int, int, int)"/>,
        /// but takes the color as RGB components (0-255 each, clamped) instead of a packed
        /// <c>0xBBGGRR</c> colorRef, for designers who find hexadecimal entry inconvenient.
        /// </summary>
        [Category("UIAutomation - Visual")]
        [Description("Flashes an inverting rectangle around an element using RGB color components. Returns True on success; never throws.")]
        public bool HighlightElement(AutomationElement element, int red, int green, int blue, out string message, int flashes = 3, int flashMs = 200, int lineWidth = 3)
        {
            return HighlightElement(element, out message, flashes, flashMs, lineWidth, PackColorRef(red, green, blue));
        }

        /// <summary>
        /// Same as <see cref="HighlightElement(AutomationElement, out string, int, int, int, int)"/>,
        /// but takes the color as a <see cref="System.Drawing.Color"/>, e.g. <c>Color.Red</c> or
        /// a named/system color, for designers with a <c>Color</c> proxy.
        /// </summary>
        [Category("UIAutomation - Visual")]
        [Description("Flashes an inverting rectangle around an element using a System.Drawing.Color. Returns True on success; never throws.")]
        public bool HighlightElement(AutomationElement element, System.Drawing.Color color, out string message, int flashes = 3, int flashMs = 200, int lineWidth = 3)
        {
            return HighlightElement(element, out message, flashes, flashMs, lineWidth, PackColorRef(color.R, color.G, color.B));
        }

        #endregion

        #region Internal Helpers

        private static readonly Dictionary<UiControlType, ControlType> ControlTypeMap = new Dictionary<UiControlType, ControlType>
        {
            [UiControlType.Button] = ControlType.Button,
            [UiControlType.CheckBox] = ControlType.CheckBox,
            [UiControlType.ComboBox] = ControlType.ComboBox,
            [UiControlType.Edit] = ControlType.Edit,
            [UiControlType.Hyperlink] = ControlType.Hyperlink,
            [UiControlType.Image] = ControlType.Image,
            [UiControlType.List] = ControlType.List,
            [UiControlType.ListItem] = ControlType.ListItem,
            [UiControlType.Menu] = ControlType.Menu,
            [UiControlType.MenuItem] = ControlType.MenuItem,
            [UiControlType.Pane] = ControlType.Pane,
            [UiControlType.RadioButton] = ControlType.RadioButton,
            [UiControlType.Tab] = ControlType.Tab,
            [UiControlType.TabItem] = ControlType.TabItem,
            [UiControlType.Text] = ControlType.Text,
            [UiControlType.Tree] = ControlType.Tree,
            [UiControlType.TreeItem] = ControlType.TreeItem,
            [UiControlType.Window] = ControlType.Window,
            [UiControlType.DataGrid] = ControlType.DataGrid,
            [UiControlType.DataItem] = ControlType.DataItem,
            [UiControlType.Group] = ControlType.Group,
            [UiControlType.Header] = ControlType.Header,
            [UiControlType.Slider] = ControlType.Slider,
            [UiControlType.Spinner] = ControlType.Spinner,
            [UiControlType.ToolBar] = ControlType.ToolBar,
            [UiControlType.Custom] = ControlType.Custom,
        };

        /// <summary>Maps our designer-friendly <see cref="UiControlType"/> to the real <see cref="ControlType"/>.</summary>
        internal static bool TryToControlType(UiControlType controlType, out ControlType result, out string message)
        {
            if (ControlTypeMap.TryGetValue(controlType, out result))
            {
                message = null;
                return true;
            }
            message = $"Unrecognized control type: {controlType}.";
            return false;
        }

        /// <summary>Packs RGB components (each clamped to 0-255) into a 0xBBGGRR colorRef value.</summary>
        private static int PackColorRef(int red, int green, int blue)
        {
            red = Math.Clamp(red, 0, 255);
            green = Math.Clamp(green, 0, 255);
            blue = Math.Clamp(blue, 0, 255);
            return red | (green << 8) | (blue << 16);
        }

        /// <summary>
        /// Runs a UIA property/pattern read and converts UIA's runtime failure modes into the
        /// never-throws contract. A UIA element whose underlying UI has died throws
        /// <see cref="ElementNotAvailableException"/> on any <c>Current</c> access - the defining
        /// failure mode of driving live UI, so actions/reads taken against a stale element
        /// report <c>false</c> + <paramref name="message"/> rather than throwing. UIA providers
        /// also throw <c>InvalidOperationException</c> subclasses for operations the target
        /// rejects (e.g. <see cref="ElementNotEnabledException"/>).
        /// </summary>
        private static bool TryUia<T>(Func<T> read, out T value, out string message)
        {
            try
            {
                value = read();
                message = null;
                return true;
            }
            catch (ElementNotAvailableException ex)
            {
                value = default;
                message = $"The element is no longer available (its underlying UI has gone away): {ex.Message}";
                return false;
            }
            catch (InvalidOperationException ex)
            {
                value = default;
                message = $"The UI Automation provider rejected the operation: {ex.Message}";
                return false;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                value = default;
                message = $"The UI Automation provider rejected the operation: {ex.Message}";
                return false;
            }
        }

        /// <summary>Void-action variant of <see cref="TryUia{T}"/> for pattern calls like <c>Invoke()</c>.</summary>
        private static bool TryUiaAction(Action action, out string message)
        {
            return TryUia<object>(() => { action(); return null; }, out _, out message);
        }

        /// <summary>Enumerates every element in <paramref name="scope"/> under <paramref name="parent"/> and returns the first one matching <paramref name="predicate"/>, or null.</summary>
        private static AutomationElement FindFirstMatching(AutomationElement parent, TreeScope scope, Func<AutomationElement, bool> predicate)
        {
            AutomationElementCollection candidates = parent.FindAll(scope, Condition.TrueCondition);
            foreach (AutomationElement candidate in candidates)
            {
                if (predicate(candidate))
                    return candidate;
            }
            return null;
        }

        private const int PS_SOLID = 0;
        private const int NULL_BRUSH = 5;
        private const int R2_NOTXORPEN = 10; // Draw = NOT (pen XOR dest)

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

        // Named DrawRectangle (not "Rectangle") to avoid colliding with the
        // System.Drawing.Rectangle type used elsewhere in this file - unlike
        // DialogUtils.HighlightControl's equivalent P/Invoke (which has no
        // System.Drawing usage to collide with).
        [DllImport("gdi32.dll", EntryPoint = "Rectangle")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DrawRectangle(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        #endregion
    }
}
