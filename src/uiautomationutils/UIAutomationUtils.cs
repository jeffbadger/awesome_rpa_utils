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
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns>The matching element, or <c>null</c> if none matches.</returns>
        /// <exception cref="ArgumentException"><paramref name="parent"/> is null, or <paramref name="automationId"/> is null/empty.</exception>
        [Category("UIAutomation - Find")]
        [Description("Finds a descendant (or child) element by its AutomationId, or null if none matches.")]
        public AutomationElement FindByAutomationId(AutomationElement parent, string automationId, bool descendantsOnly = true)
        {
            if (parent == null)
                throw new ArgumentException("A parent element is required.", nameof(parent));
            if (string.IsNullOrEmpty(automationId))
                throw new ArgumentException("An automation ID is required.", nameof(automationId));

            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
            var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;
            return parent.FindFirst(scope, condition);
        }

        /// <summary>
        /// Finds a descendant (or immediate child) element by its <c>Name</c>.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="name">The name to match.</param>
        /// <param name="exactMatch">If <c>true</c> (default), requires an exact match. If <c>false</c>, matches any element whose name contains <paramref name="name"/> (case-insensitive).</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns>The matching element, or <c>null</c> if none matches.</returns>
        /// <exception cref="ArgumentException"><paramref name="parent"/> is null, or <paramref name="name"/> is null/empty.</exception>
        [Category("UIAutomation - Find")]
        [Description("Finds a descendant (or child) element by its Name (exact or substring match), or null if none matches.")]
        public AutomationElement FindByName(AutomationElement parent, string name, bool exactMatch = true, bool descendantsOnly = true)
        {
            if (parent == null)
                throw new ArgumentException("A parent element is required.", nameof(parent));
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A name is required.", nameof(name));

            var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;

            if (exactMatch)
            {
                var condition = new PropertyCondition(AutomationElement.NameProperty, name);
                return parent.FindFirst(scope, condition);
            }

            // PropertyCondition only supports exact-value matches - UIA has no built-in
            // substring condition, so enumerate candidates ourselves and filter, the same
            // way WindowUtils.FindWindowByTitle does for its exactMatch=false case.
            return FindFirstMatching(parent, scope, el =>
            {
                string elementName = el.Current.Name;
                return elementName != null && elementName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        /// <summary>
        /// Finds a descendant (or immediate child) element by its window class name.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="className">The class name to match (exact).</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns>The matching element, or <c>null</c> if none matches.</returns>
        /// <exception cref="ArgumentException"><paramref name="parent"/> is null, or <paramref name="className"/> is null/empty.</exception>
        [Category("UIAutomation - Find")]
        [Description("Finds a descendant (or child) element by its window class name, or null if none matches.")]
        public AutomationElement FindByClassName(AutomationElement parent, string className, bool descendantsOnly = true)
        {
            if (parent == null)
                throw new ArgumentException("A parent element is required.", nameof(parent));
            if (string.IsNullOrEmpty(className))
                throw new ArgumentException("A class name is required.", nameof(className));

            var condition = new PropertyCondition(AutomationElement.ClassNameProperty, className);
            var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;
            return parent.FindFirst(scope, condition);
        }

        /// <summary>
        /// Finds the first descendant (or immediate child) element of the given control type.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="controlType">The control type to match.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns>The matching element, or <c>null</c> if none matches.</returns>
        /// <exception cref="ArgumentException"><paramref name="parent"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="controlType"/> is not a defined value.</exception>
        [Category("UIAutomation - Find")]
        [Description("Finds the first descendant (or child) element of the given control type, or null if none matches.")]
        public AutomationElement FindByControlType(AutomationElement parent, UiControlType controlType, bool descendantsOnly = true)
        {
            if (parent == null)
                throw new ArgumentException("A parent element is required.", nameof(parent));

            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ToControlType(controlType));
            var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;
            return parent.FindFirst(scope, condition);
        }

        /// <summary>
        /// Finds every descendant (or immediate child) element of the given control type.
        /// </summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="controlType">The control type to match.</param>
        /// <param name="descendantsOnly">If <c>true</c> (default), searches the full subtree; if <c>false</c>, searches only immediate children.</param>
        /// <returns>Every matching element, in tree order (empty if none match).</returns>
        /// <exception cref="ArgumentException"><paramref name="parent"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="controlType"/> is not a defined value.</exception>
        [Category("UIAutomation - Find")]
        [Description("Finds every descendant (or child) element of the given control type.")]
        public List<AutomationElement> FindAllByControlType(AutomationElement parent, UiControlType controlType, bool descendantsOnly = true)
        {
            if (parent == null)
                throw new ArgumentException("A parent element is required.", nameof(parent));

            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ToControlType(controlType));
            var scope = descendantsOnly ? TreeScope.Descendants : TreeScope.Children;

            var results = new List<AutomationElement>();
            foreach (AutomationElement element in parent.FindAll(scope, condition))
                results.Add(element);
            return results;
        }

        /// <summary>Gets all immediate children of an element.</summary>
        /// <param name="parent">The element whose children to enumerate.</param>
        /// <returns>The element's immediate children, in tree order (empty if it has none).</returns>
        /// <exception cref="ArgumentException"><paramref name="parent"/> is null.</exception>
        [Category("UIAutomation - Find")]
        [Description("Gets all immediate children of an element.")]
        public List<AutomationElement> GetChildren(AutomationElement parent)
        {
            if (parent == null)
                throw new ArgumentException("A parent element is required.", nameof(parent));

            var results = new List<AutomationElement>();
            foreach (AutomationElement child in parent.FindAll(TreeScope.Children, Condition.TrueCondition))
                results.Add(child);
            return results;
        }

        #endregion

        #region Properties

        /// <summary>Gets an element's <c>Name</c> property.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's Name property.")]
        public string GetName(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));
            return element.Current.Name;
        }

        /// <summary>Gets an element's <c>AutomationId</c> property.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's AutomationId property.")]
        public string GetAutomationId(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));
            return element.Current.AutomationId;
        }

        /// <summary>Gets an element's window class name.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's window class name.")]
        public string GetClassName(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));
            return element.Current.ClassName;
        }

        /// <summary>Gets a friendly name for an element's control type (e.g. <c>"Button"</c>).</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        [Category("UIAutomation - Properties")]
        [Description("Gets a friendly name for an element's control type (e.g. \"Button\").")]
        public string GetControlTypeName(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            // ProgrammaticName looks like "ControlType.Button" - strip the prefix so callers
            // get the same short form used by UiControlType (e.g. "Button").
            string programmaticName = element.Current.ControlType.ProgrammaticName;
            const string prefix = "ControlType.";
            return programmaticName.StartsWith(prefix, StringComparison.Ordinal)
                ? programmaticName.Substring(prefix.Length)
                : programmaticName;
        }

        /// <summary>Gets an element's screen-space bounding rectangle.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        [Category("UIAutomation - Properties")]
        [Description("Gets an element's screen-space bounding rectangle.")]
        public System.Drawing.Rectangle GetBoundingRectangle(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            System.Windows.Rect rect = element.Current.BoundingRectangle;
            return new System.Drawing.Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        }

        /// <summary>Returns <c>true</c> if the element is enabled.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is enabled.")]
        public bool IsEnabled(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));
            return element.Current.IsEnabled;
        }

        /// <summary>Returns <c>true</c> if the element is offscreen.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        [Category("UIAutomation - Properties")]
        [Description("Returns True if the element is offscreen.")]
        public bool IsOffscreen(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));
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
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>InvokePattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Invokes an element (click-equivalent for buttons/menu items) via InvokePattern.")]
        public void Invoke(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support InvokePattern.");

            ((InvokePattern)patternObj).Invoke();
        }

        /// <summary>Sets an element's value via <c>ValuePattern</c>.</summary>
        /// <param name="element">The element to set.</param>
        /// <param name="value">The value to set.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null, or <paramref name="value"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>ValuePattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Sets an element's value via ValuePattern.")]
        public void SetValue(AutomationElement element, string value)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));
            if (value == null)
                throw new ArgumentException("A value is required.", nameof(value));

            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support ValuePattern.");

            ((ValuePattern)patternObj).SetValue(value);
        }

        /// <summary>Gets an element's value via <c>ValuePattern</c>.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>ValuePattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Gets an element's value via ValuePattern.")]
        public string GetValue(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support ValuePattern.");

            return ((ValuePattern)patternObj).Current.Value;
        }

        /// <summary>Toggles an element (e.g. a checkbox) via <c>TogglePattern</c>.</summary>
        /// <param name="element">The element to toggle.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>TogglePattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Toggles an element (e.g. a checkbox) via TogglePattern.")]
        public void Toggle(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support TogglePattern.");

            ((TogglePattern)patternObj).Toggle();
        }

        /// <summary>Returns <c>true</c> if a toggleable element is currently On.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>TogglePattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Returns True if a toggleable element is currently On.")]
        public bool IsToggled(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support TogglePattern.");

            return ((TogglePattern)patternObj).Current.ToggleState == ToggleState.On;
        }

        /// <summary>Expands an element (e.g. a combo box or tree node) via <c>ExpandCollapsePattern</c>.</summary>
        /// <param name="element">The element to expand.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>ExpandCollapsePattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Expands an element (e.g. a combo box or tree node) via ExpandCollapsePattern.")]
        public void Expand(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support ExpandCollapsePattern.");

            ((ExpandCollapsePattern)patternObj).Expand();
        }

        /// <summary>Collapses an element via <c>ExpandCollapsePattern</c>.</summary>
        /// <param name="element">The element to collapse.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>ExpandCollapsePattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Collapses an element via ExpandCollapsePattern.")]
        public void Collapse(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support ExpandCollapsePattern.");

            ((ExpandCollapsePattern)patternObj).Collapse();
        }

        /// <summary>Selects an element (e.g. a list item) via <c>SelectionItemPattern</c>.</summary>
        /// <param name="element">The element to select.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>SelectionItemPattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Selects an element (e.g. a list item) via SelectionItemPattern.")]
        public void Select(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support SelectionItemPattern.");

            ((SelectionItemPattern)patternObj).Select();
        }

        /// <summary>Returns <c>true</c> if a selectable element is currently selected.</summary>
        /// <param name="element">The element to read.</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> does not support <c>SelectionItemPattern</c>.</exception>
        [Category("UIAutomation - Actions")]
        [Description("Returns True if a selectable element is currently selected.")]
        public bool IsSelected(AutomationElement element)
        {
            if (element == null)
                throw new ArgumentException("An element is required.", nameof(element));

            if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object patternObj))
                throw new InvalidOperationException("This element does not support SelectionItemPattern.");

            return ((SelectionItemPattern)patternObj).Current.IsSelected;
        }

        #endregion

        #region Wait

        /// <summary>Polls for a descendant element matching the given <c>AutomationId</c> until it appears or the timeout elapses.</summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="automationId">The AutomationId to match (exact).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="element">The matching element, or <c>null</c> if not found in time.</param>
        /// <returns><c>true</c> if a matching element was found before the timeout.</returns>
        [Category("UIAutomation - Wait")]
        [Description("Polls for a descendant element matching the given AutomationId until it appears or the timeout elapses.")]
        public bool WaitForElementByAutomationId(AutomationElement parent, string automationId, int timeoutMs, int pollIntervalMs, out AutomationElement element)
        {
            throw new NotImplementedException();
        }

        /// <summary>Polls for a descendant element matching the given <c>Name</c> until it appears or the timeout elapses.</summary>
        /// <param name="parent">The element to search within.</param>
        /// <param name="name">The name to match.</param>
        /// <param name="exactMatch">If <c>true</c>, requires an exact match; if <c>false</c>, matches any element whose name contains <paramref name="name"/> (case-insensitive).</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">Delay between checks, in milliseconds; values below 1 are treated as 1.</param>
        /// <param name="element">The matching element, or <c>null</c> if not found in time.</param>
        /// <returns><c>true</c> if a matching element was found before the timeout.</returns>
        [Category("UIAutomation - Wait")]
        [Description("Polls for a descendant element matching the given Name until it appears or the timeout elapses.")]
        public bool WaitForElementByName(AutomationElement parent, string name, bool exactMatch, int timeoutMs, int pollIntervalMs, out AutomationElement element)
        {
            throw new NotImplementedException();
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
        /// <param name="colorRef">RGB color for the rectangle as a 0xBBGGRR value (default 0x0000FF = red).</param>
        /// <exception cref="ArgumentException"><paramref name="element"/> is null.</exception>
        /// <exception cref="Win32Exception"><c>GetDC</c> failed.</exception>
        [Category("UIAutomation - Visual")]
        [Description("Flashes an inverting rectangle around an element to visually confirm which on-screen element it corresponds to.")]
        public void HighlightElement(AutomationElement element, int flashes = 3, int flashMs = 200, int lineWidth = 3, int colorRef = 0x0000FF)
        {
            throw new NotImplementedException();
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
        private static ControlType ToControlType(UiControlType controlType)
        {
            if (ControlTypeMap.TryGetValue(controlType, out ControlType result))
                return result;
            throw new ArgumentOutOfRangeException(nameof(controlType), controlType, "Unrecognized control type.");
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

        #endregion
    }
}
