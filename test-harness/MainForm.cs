using System;
using System.Drawing;
using System.Windows.Forms;

namespace TestHarness
{
    /// <summary>
    /// Minimal WinForms harness with fixed, known control names/IDs for manually
    /// testing DialogUtils, KeyboardUtils, MouseUtils, WindowUtils, and
    /// UIAutomationUtils against a real target, per TESTING.md's Phase 0 Test Harness.
    /// Each control's Name doubles as its Win32 window text lookup key and (for
    /// most standard WinForms controls) its UI Automation AutomationId.
    /// </summary>
    public class MainForm : Form
    {
        private int _clickCount;

        public Button ClickButton;
        public Label ClickCountLabel;
        public TextBox InputTextBox;
        public CheckBox OptionCheckBox;
        public ListBox ItemsListBox;
        public TreeView SampleTreeView;
        public Button ShowMessageBoxButton;
        public Button OpenChildWindowButton;

        public MainForm()
        {
            Name = "MainForm";
            Text = "Awesome RPA Utils - Test Harness";
            Width = 420;
            Height = 480;

            ClickButton = new Button
            {
                Name = "btnClick",
                Text = "Click Me",
                Location = new Point(20, 20),
                Width = 120
            };
            ClickButton.Click += (s, e) =>
            {
                _clickCount++;
                ClickCountLabel.Text = "Clicks: " + _clickCount;
            };

            ClickCountLabel = new Label
            {
                Name = "lblClickCount",
                Text = "Clicks: 0",
                Location = new Point(150, 25),
                Width = 150,
                AutoSize = false
            };

            InputTextBox = new TextBox
            {
                Name = "txtInput",
                Location = new Point(20, 60),
                Width = 280
            };

            OptionCheckBox = new CheckBox
            {
                Name = "chkOption",
                Text = "Enable Option",
                Location = new Point(20, 100),
                Width = 200
            };

            ItemsListBox = new ListBox
            {
                Name = "lstItems",
                Location = new Point(20, 140),
                Width = 280,
                Height = 100,
                SelectionMode = SelectionMode.MultiExtended
            };
            ItemsListBox.Items.AddRange(new object[]
            {
                "Item 1", "Item 2", "Item 3", "Item 4", "Item 5",
                "Item 6", "Item 7", "Item 8", "Item 9", "Item 10"
            });

            SampleTreeView = new TreeView
            {
                Name = "treeSample",
                Location = new Point(20, 250),
                Width = 280,
                Height = 100
            };
            var rootNode = new TreeNode("Documents");
            rootNode.Nodes.Add(new TreeNode("Report.docx"));
            rootNode.Nodes.Add(new TreeNode("Budget.xlsx"));
            SampleTreeView.Nodes.Add(rootNode);

            ShowMessageBoxButton = new Button
            {
                Name = "btnShowMessageBox",
                Text = "Show MessageBox",
                Location = new Point(20, 360),
                Width = 140
            };
            ShowMessageBoxButton.Click += (s, e) =>
            {
                MessageBox.Show(this, "This is a test dialog.", "Test Harness",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            };

            OpenChildWindowButton = new Button
            {
                Name = "btnOpenChildWindow",
                Text = "Open Child Window",
                Location = new Point(170, 360),
                Width = 140
            };
            OpenChildWindowButton.Click += (s, e) =>
            {
                var child = new ChildForm();
                child.Show(this);
            };

            Controls.AddRange(new Control[]
            {
                ClickButton, ClickCountLabel, InputTextBox, OptionCheckBox,
                ItemsListBox, SampleTreeView, ShowMessageBoxButton, OpenChildWindowButton
            });
        }
    }

    /// <summary>A trivial child window for WindowUtils child-window/enumeration tests.</summary>
    public class ChildForm : Form
    {
        public ChildForm()
        {
            Name = "ChildForm";
            Text = "Child Window";
            Width = 300;
            Height = 200;

            var label = new Label
            {
                Name = "lblChildContent",
                Text = "This is a child window.",
                Location = new Point(20, 20),
                AutoSize = true
            };
            Controls.Add(label);
        }
    }
}
