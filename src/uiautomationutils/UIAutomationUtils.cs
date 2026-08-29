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
            element = parent.FindFirst(scope, condition);
            message = null;
            return element != null;
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
                element = parent.FindFirst(scope, condition);
            }
            else
            {
                // PropertyCondition only supports exact-value matches - UIA has no built-in
                // substring condition, so enumerate candidates ourselves and filter, the same
                // way WindowUtils.FindWindowByTitle does for its exactMatch=false case.
                element = FindFirstMatching(parent, scope, el =>
                {
                    string elementName = el.Current.Name;
                    return elementName != null && elementName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            message = null;
            return element != null;
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
            element = parent.FindFirst(scope, condition);
            message = null;
            return element != null;
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
            element = parent.FindFirst(scope, condition);
            message = null;
            return element != null;
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

            var results = new List<AutomationElement>();
            foreach (AutomationElement element in parent.FindAll(scope, condition))
                results.Add(element);
            elements = results;
            message = null;
            return true;
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
            children = null;
            if (parent == null)
            {
                message = "A parent element is required.";
                return false;
            }

            var results = new List<AutomationElement>();
            foreach (AutomationElement child in parent.FindAll(TreeScope.Children, Condition.TrueCondition))
                results.Add(child);
            children = results;
            message = null;
            return true;
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
            name = null;
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }
            name = element.Current.Name;
            message = null;
            return true;
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
            automationId = null;
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }
            automationId = element.Current.AutomationId;
            message = null;
            return true;
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
            className = null;
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }
            className = element.Current.ClassName;
            message = null;
            return true;
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
            controlTypeName = null;
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }

            // ProgrammaticName looks like "ControlType.Button" - strip the prefix so callers
            // get the same short form used by UiControlType (e.g. "Button").
            string programmaticName = element.Current.ControlType.ProgrammaticName;
            const string prefix = "ControlType.";
            controlTypeName = programmaticName.StartsWith(prefix, StringComparison.Ordinal)
                ? programmaticName.Substring(prefix.Length)
                : programmaticName;
            message = null;
            return true;
        }

        /// <summary>Gets an element's screen-space bounding rectangle.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="bounds">The element's bounding rectangle, or <c>default</c> if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null. Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's screen-space bounding rectangle. Returns True on success; never throws.")]
        public bool GetBoundingRectangle(AutomationElement element, out System.Drawing.Rectangle bounds, out string message)
        {
            bounds = default;
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }

            System.Windows.Rect rect = element.Current.BoundingRectangle;
            bounds = new System.Drawing.Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
            message = null;
            return true;
        }

        /// <summary>Returns <c>true</c> if the element is enabled.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely disabled element).</param>
        /// <returns><c>true</c> if the element is enabled; <c>false</c> if it isn't, or if <paramref name="element"/> is null (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is enabled. Never throws.")]
        public bool IsEnabled(AutomationElement element, out string message)
        {
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }
            message = null;
            return element.Current.IsEnabled;
        }

        /// <summary>Returns <c>true</c> if the element is offscreen.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely onscreen element).</param>
        /// <returns><c>true</c> if the element is offscreen; <c>false</c> if it isn't, or if <paramref name="element"/> is null (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is offscreen. Never throws.")]
        public bool IsOffscreen(AutomationElement element, out string message)
        {
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }
            message = null;
            return element.Current.IsOffscreen;
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
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }

            if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out object patternObj))
            {
                message = "This element does not support InvokePattern.";
                return false;
            }

            ((InvokePattern)patternObj).Invoke();
            message = null;
            return true;
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

            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObj))
            {
                message = "This element does not support ValuePattern.";
                return false;
            }

            ((ValuePattern)patternObj).SetValue(value);
            message = null;
            return true;
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
            value = null;
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }

            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObj))
            {
                message = "This element does not support ValuePattern.";
                return false;
            }

            value = ((ValuePattern)patternObj).Current.Value;
            message = null;
            return true;
        }

        /// <summary>Toggles an element (e.g. a checkbox) via <c>TogglePattern</c>.</summary>
        /// <param name="element">The element to toggle.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the toggle failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support TogglePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Toggles an element (e.g. a checkbox) via TogglePattern. Returns True on success; never throws.")]
        public bool Toggle(AutomationElement element, out string message)
        {
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }

            if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out object patternObj))
            {
                message = "This element does not support TogglePattern.";
                return false;
            }

            ((TogglePattern)patternObj).Toggle();
            message = null;
            return true;
        }

        /// <summary>Returns <c>true</c> if a toggleable element is currently On.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely Off/indeterminate element).</param>
        /// <returns><c>true</c> if the element is On; <c>false</c> if it isn't, or if <paramref name="element"/> is null/does not support TogglePattern (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Returns True if a toggleable element is currently On. Never throws.")]
        public bool IsToggled(AutomationElement element, out string message)
        {
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }

            if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out object patternObj))
            {
                message = "This element does not support TogglePattern.";
                return false;
            }

            message = null;
            return ((TogglePattern)patternObj).Current.ToggleState == ToggleState.On;
        }

        /// <summary>Expands an element (e.g. a combo box or tree node) via <c>ExpandCollapsePattern</c>.</summary>
        /// <param name="element">The element to expand.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the expand failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support ExpandCollapsePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Expands an element (e.g. a combo box or tree node) via ExpandCollapsePattern. Returns True on success; never throws.")]
        public bool Expand(AutomationElement element, out string message)
        {
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }

            if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object patternObj))
            {
                message = "This element does not support ExpandCollapsePattern.";
                return false;
            }

            ((ExpandCollapsePattern)patternObj).Expand();
            message = null;
            return true;
        }

        /// <summary>Collapses an element via <c>ExpandCollapsePattern</c>.</summary>
        /// <param name="element">The element to collapse.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the collapse failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support ExpandCollapsePattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Collapses an element via ExpandCollapsePattern. Returns True on success; never throws.")]
        public bool Collapse(AutomationElement element, out string message)
        {
            if (element == null)
            {
                message = "An element is required.";
                return false;
            }

            if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object patternObj))
            {
                message = "This element does not support ExpandCollapsePattern.";
                return false;
            }

            ((ExpandCollapsePattern)patternObj).Collapse();
            message = null;
            return true;
        }

        /// <summary>Selects an element (e.g. a list item) via <c>SelectionItemPattern</c>.</summary>
        /// <param name="element">The element to select.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the selection failed.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="element"/> is null, or it does not support SelectionItemPattern. Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Selects an element (e.g. a list item) via SelectionItemPattern. Returns True on success; never throws.")]
        public bool Select(AutomationElement element, out string message)
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

            ((SelectionItemPattern)patternObj).Select();
            message = null;
            return true;
        }

        /// <summary>Returns <c>true</c> if a selectable element is currently selected.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed (in which case this method returns <c>false</c>, same as a genuinely unselected element).</param>
        /// <returns><c>true</c> if the element is selected; <c>false</c> if it isn't, or if <paramref name="element"/> is null/does not support SelectionItemPattern (check <paramref name="message"/> to tell them apart). Never throws.</returns>
        [Category("UIAutomation - Actions")]
        [Description("Returns True if a selectable element is currently selected. Never throws.")]
        public bool IsSelected(AutomationElement element, out string message)
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

            message = null;
            return ((SelectionItemPattern)patternObj).Current.IsSelected;
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
        public bool WaitForElementByAutomationId(AutomationElement parent, string automationId, int timeoutMs, int pollIntervalMs, out AutomationElement element, out string message)
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
                    message = null;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
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
        public bool WaitForElementByName(AutomationElement parent, string name, bool exactMatch, int timeoutMs, int pollIntervalMs, out AutomationElement element, out string message)
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
                    message = null;
                    return false;
                }
                Thread.Sleep(pollIntervalMs);
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
            if (!GetBoundingRectangle(element, out System.Drawing.Rectangle rc, out message))
                return false;
            if (flashes < 1) flashes = 1;
            if (flashMs < 1) flashMs = 1;
            if (lineWidth < 1) lineWidth = 1;

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
                    DrawRectangle(hdc, rc.Left, rc.Top, rc.Right, rc.Bottom);
                    Thread.Sleep(flashMs);
                    // Draw again (erases) - XOR XOR = original pixels
                    DrawRectangle(hdc, rc.Left, rc.Top, rc.Right, rc.Bottom);

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
            [UiControlType.Custom] = ControlType.Custom,
        };

        /// <summary>Maps our designer-friendly <see cref="UiControlType"/> to the real <see cref="ControlType"/>.</summary>
        private static bool TryToControlType(UiControlType controlType, out ControlType result, out string message)
        {
            if (ControlTypeMap.TryGetValue(controlType, out result))
            {
                message = null;
                return true;
            }
            message = $"Unrecognized control type: {controlType}.";
            return false;
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
