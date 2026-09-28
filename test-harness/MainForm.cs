using System;
using System.Drawing;
using System.Runtime.InteropServices;
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
        public Label ClickDetailLabel;
        public Panel DragTargetPanel;
        public Label DragStatusLabel;
        public TextBox InputTextBox;
        public CheckBox OptionCheckBox;
        public ListBox ItemsListBox;
        public TreeView SampleTreeView;
        public Button ShowMessageBoxButton;
        public Button OpenChildWindowButton;
        public Button OpenSecondChildWindowButton;
        public Button ShowNonNativeDialogButton;
        public Button ShowDuplicateDialogsButton;
        public Button ShowDisabledButtonDialogButton;
        public Panel CursorHandPanel;
        public Panel CursorSizeAllPanel;
        public Panel CursorNoPanel;
        public Panel CursorCrossPanel;
        public Panel CursorWaitPanel;

        public MainForm()
        {
            Name = "MainForm";
            Text = "Awesome RPA Utils - Test Harness";
            Width = 460;
            Height = 610;

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
            ClickButton.MouseDown += (s, e) =>
            {
                ClickDetailLabel.Text = "Last button: " + e.Button;
            };

            // Drag target for DragAndDrop/DragAndHold/RubberBandSelect: records the
            // press and release points so a drag can be asserted programmatically.
            DragTargetPanel = new Panel
            {
                Name = "pnlDragTarget",
                Location = new Point(20, 260),
                Width = 280,
                Height = 60,
                BackColor = Color.LightSteelBlue,
                AllowDrop = false
            };
            DragStatusLabel = new Label
            {
                Name = "lblDragStatus",
                Text = "Drag: (none)",
                Location = new Point(5, 5),
                AutoSize = true
            };
            DragTargetPanel.Controls.Add(DragStatusLabel);
            DragTargetPanel.MouseDown += (s, e) =>
            {
                DragStatusLabel.Text = "Drag start " + e.Button + " at (" + e.X + "," + e.Y + ")";
            };
            DragTargetPanel.MouseUp += (s, e) =>
            {
                DragStatusLabel.Text = "Drag end " + e.Button + " at (" + e.X + "," + e.Y + ")";
            };

            ClickCountLabel = new Label
            {
                Name = "lblClickCount",
                Text = "Clicks: 0",
                Location = new Point(150, 25),
                Width = 150,
                AutoSize = false
            };

            // Records which button last fired, so LeftClick/RightClick/MiddleClick
            // (and the *At variants) can be distinguished programmatically.
            ClickDetailLabel = new Label
            {
                Name = "lblClickDetail",
                Text = "Last button: (none)",
                Location = new Point(150, 45),
                Width = 220,
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
                "Item 6", "Item 7", "Item 8", "Item 9", "Item 10",
                "Item 11", "Item 12", "Item 13", "Item 14", "Item 15",
                "Item 16", "Item 17", "Item 18", "Item 19", "Item 20",
                "Item 21", "Item 22", "Item 23", "Item 24", "Item 25",
                "Item 26", "Item 27", "Item 28", "Item 29", "Item 30"
            });

            SampleTreeView = new TreeView
            {
                Name = "treeSample",
                Location = new Point(20, 330),
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
                Location = new Point(20, 440),
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
                Location = new Point(170, 440),
                Width = 140
            };
            OpenChildWindowButton.Click += (s, e) =>
            {
                var child = new ChildForm();
                child.Show(this);
            };

            // A second, distinct top-level window type. These are owned windows
            // (Show(this), not WS_CHILD descendants), and WinForms does NOT derive a
            // distinct native window class per CLR Form subtype - every plain Form,
            // including ChildForm, shares the same generic "WindowsForms10.Window..."
            // class. SecondChildForm registers its own explicit class (see its
            // CreateParams override and EnsureNativeClassRegistered below) specifically
            // so it differs from ChildForm's. Covers TryFindWindowByRegex's
            // classNamePattern (nothing to wrongly match before this existed) and
            // GetTopLevelWindows/FindWindowByClass's duplicate-vs-distinct-class cases.
            OpenSecondChildWindowButton = new Button
            {
                Name = "btnOpenSecondChildWindow",
                Text = "Open 2nd Child Window",
                Location = new Point(320, 440),
                Width = 120
            };
            OpenSecondChildWindowButton.Click += (s, e) =>
            {
                var child = new SecondChildForm();
                child.Show(this);
            };

            // Dismiss control is a Label, never a real Button, so DialogUtils.CanDismissDialog
            // (which only ever looks for a native "Button"-classed child window) reliably
            // reports canDismiss = false, without depending on an actual WinUI3 dialog.
            ShowNonNativeDialogButton = new Button
            {
                Name = "btnShowNonNativeDialog",
                Text = "Show Non-Native Dialog",
                Location = new Point(20, 480),
                Width = 130
            };
            ShowNonNativeDialogButton.Click += (s, e) =>
            {
                var dialog = new NonNativeDialogForm();
                dialog.Show(this);
            };

            // Opens two windows with identical Text at once, for FindDialog's first-match
            // and FindAllDialogs' multi-match (returns both) behavior.
            ShowDuplicateDialogsButton = new Button
            {
                Name = "btnShowDuplicateDialogs",
                Text = "Show Duplicate Dialogs",
                Location = new Point(160, 480),
                Width = 130
            };
            ShowDuplicateDialogsButton.Click += (s, e) =>
            {
                new DuplicateDialogForm("Instance A").Show(this);
                new DuplicateDialogForm("Instance B").Show(this);
            };

            // btnConfirm starts disabled, so ClickDialogButtonByText/ById's wasEnabled
            // output can be asserted false; chkConfirmEnabled lets a tester enable it
            // mid-wait to also cover the true case.
            ShowDisabledButtonDialogButton = new Button
            {
                Name = "btnShowDisabledButtonDialog",
                Text = "Show Disabled Button Dialog",
                Location = new Point(300, 480),
                Width = 140
            };
            ShowDisabledButtonDialogButton.Click += (s, e) =>
            {
                var dialog = new DisabledButtonDialogForm();
                dialog.Show(this);
            };

            // Five zones with distinct Cursor settings, for MouseUtils.GetCurrentCursorType:
            // hover the pointer inside one and assert the matching CurrentCursorType.
            // Note Cursors.Cross maps to CurrentCursorType.Crosshair, not "Cross".
            CursorHandPanel = MakeCursorZonePanel("pnlCursorHand", "Hand", Cursors.Hand, new Point(20, 530), Color.MistyRose);
            CursorSizeAllPanel = MakeCursorZonePanel("pnlCursorSizeAll", "SizeAll", Cursors.SizeAll, new Point(100, 530), Color.Honeydew);
            CursorNoPanel = MakeCursorZonePanel("pnlCursorNo", "No", Cursors.No, new Point(180, 530), Color.LightYellow);
            CursorCrossPanel = MakeCursorZonePanel("pnlCursorCross", "Cross", Cursors.Cross, new Point(260, 530), Color.Lavender);
            CursorWaitPanel = MakeCursorZonePanel("pnlCursorWait", "Wait", Cursors.WaitCursor, new Point(340, 530), Color.PaleTurquoise);

            Controls.AddRange(new Control[]
            {
                ClickButton, ClickCountLabel, ClickDetailLabel, InputTextBox,
                OptionCheckBox, ItemsListBox, DragTargetPanel, SampleTreeView,
                ShowMessageBoxButton, OpenChildWindowButton, OpenSecondChildWindowButton,
                ShowNonNativeDialogButton, ShowDuplicateDialogsButton, ShowDisabledButtonDialogButton,
                CursorHandPanel, CursorSizeAllPanel, CursorNoPanel, CursorCrossPanel, CursorWaitPanel
            });
        }

        private static Panel MakeCursorZonePanel(string name, string label, Cursor cursor, Point location, Color backColor)
        {
            var panel = new Panel
            {
                Name = name,
                Cursor = cursor,
                Location = location,
                Width = 70,
                Height = 40,
                BackColor = backColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            panel.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = cursor
            });
            return panel;
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

    /// <summary>
    /// A second, differently-classed top-level (owned, not WS_CHILD) window, for
    /// WindowUtils scenarios that need more than one distinct window class on screen
    /// at once (e.g. proving TryFindWindowByRegex's classNamePattern actually
    /// excludes a non-matching class). A plain WinForms Form does not get its own
    /// native window class per CLR subtype - every Form in this app, ChildForm
    /// included, otherwise shares the same generic "WindowsForms10.Window..." class
    /// - so this registers its own via Win32's <c>RegisterClassEx</c> (setting
    /// <see cref="CreateParams.ClassName"/> alone does not register a class; an
    /// unregistered class name makes <c>CreateWindowEx</c> fail outright).
    /// </summary>
    public class SecondChildForm : Form
    {
        private const string NativeClassName = "TestHarnessSecondChildForm";
        private static bool _classRegistered;

        public SecondChildForm()
        {
            EnsureNativeClassRegistered();

            Name = "SecondChildForm";
            Text = "Second Child Window";
            Width = 300;
            Height = 200;

            var label = new Label
            {
                Name = "lblSecondChildContent",
                Text = "This is a second, differently-classed child window.",
                Location = new Point(20, 20),
                AutoSize = true
            };
            Controls.Add(label);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassName = NativeClassName;
                return cp;
            }
        }

        // Registers the class once, using the OS's own default window procedure
        // (user32's DefWindowProcW) so CreateWindowEx can create a window of this
        // class at all. WinForms then subclasses the window's WndProc after
        // creation - the same way it does for its own built-in
        // "WindowsForms10.Window..." class - so normal message handling and
        // control hosting still work.
        private static void EnsureNativeClassRegistered()
        {
            if (_classRegistered)
            {
                return;
            }

            IntPtr hUser32 = GetModuleHandle("user32.dll");
            IntPtr defWindowProc = GetProcAddress(hUser32, "DefWindowProcW");

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = defWindowProc,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                hbrBackground = (IntPtr)(COLOR_WINDOW + 1),
                lpszMenuName = null,
                lpszClassName = NativeClassName,
                hIconSm = IntPtr.Zero
            };

            if (RegisterClassEx(ref wc) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                const int ERROR_CLASS_ALREADY_EXISTS = 1410;
                if (error != ERROR_CLASS_ALREADY_EXISTS)
                {
                    throw new InvalidOperationException(
                        $"Failed to register the '{NativeClassName}' window class (Win32 error {error}).");
                }
            }

            _classRegistered = true;
        }

        private const int COLOR_WINDOW = 5;
        private const int IDC_ARROW = 32512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    }

    /// <summary>
    /// A dialog whose only dismiss control is a Label styled as a button, never a real
    /// Button. DialogUtils.CanDismissDialog only ever reports true for a native "Button"-
    /// classed child window, so this form deterministically drives canDismiss = false,
    /// covering the "non-native dialog" case without an actual WinUI3 dependency.
    /// </summary>
    public class NonNativeDialogForm : Form
    {
        public NonNativeDialogForm()
        {
            Name = "NonNativeDialogForm";
            Text = "Non-Native Dialog";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Width = 320;
            Height = 160;

            var pseudoButton = new Label
            {
                Name = "lblPseudoOkButton",
                Text = "OK",
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(110, 90),
                Width = 80,
                Height = 30
            };
            pseudoButton.Click += (s, e) => Close();
            Controls.Add(pseudoButton);
        }
    }

    /// <summary>
    /// A dialog with identical Text on every instance and a real Button to close it.
    /// Opening two at once (see MainForm.ShowDuplicateDialogsButton) exercises
    /// FindDialog's first-match and FindAllDialogs' multi-match (returns both) behavior.
    /// </summary>
    public class DuplicateDialogForm : Form
    {
        public DuplicateDialogForm(string instanceLabel)
        {
            Name = "DuplicateDialogForm";
            Text = "Duplicate Dialog";
            Width = 280;
            Height = 140;

            var instanceLabelControl = new Label
            {
                Name = "lblInstance",
                Text = instanceLabel,
                AutoSize = true,
                Location = new Point(20, 20)
            };
            var ok = new Button
            {
                Name = "btnDuplicateOk",
                Text = "OK",
                // System (not the WinForms default) renders via real native BS_PUSHBUTTON
                // painting rather than owner-drawing it, so DialogUtils' BM_CLICK-based
                // ClickButton/ClickDialogButtonByText can actually target it - a default
                // WinForms button is owner-drawn and DialogUtils' own README documents
                // that as possibly unresponsive to BM_CLICK.
                FlatStyle = FlatStyle.System,
                Location = new Point(90, 70),
                Width = 80
            };
            ok.Click += (s, e) => Close();
            Controls.Add(instanceLabelControl);
            Controls.Add(ok);
        }
    }

    /// <summary>
    /// A dialog whose Confirm button starts disabled, so ClickDialogButtonByText/ById's
    /// wasEnabled output can be asserted false; the checkbox lets a tester enable it
    /// mid-wait to also cover the true case.
    /// </summary>
    public class DisabledButtonDialogForm : Form
    {
        public DisabledButtonDialogForm()
        {
            Name = "DisabledButtonDialogForm";
            Text = "Disabled Button Dialog";
            Width = 300;
            Height = 160;

            var confirm = new Button
            {
                Name = "btnConfirm",
                Text = "Confirm",
                Enabled = false,
                // Same reasoning as DuplicateDialogForm's OK button: real native
                // BS_PUSHBUTTON painting, not WinForms' owner-drawn default, so
                // DialogUtils can find and click it via BM_CLICK.
                FlatStyle = FlatStyle.System,
                Location = new Point(90, 80),
                Width = 100
            };
            var enableToggle = new CheckBox
            {
                Name = "chkConfirmEnabled",
                Text = "Enable Confirm",
                Location = new Point(20, 20),
                AutoSize = true
            };
            enableToggle.CheckedChanged += (s, e) => confirm.Enabled = enableToggle.Checked;
            Controls.Add(enableToggle);
            Controls.Add(confirm);
        }
    }
}
