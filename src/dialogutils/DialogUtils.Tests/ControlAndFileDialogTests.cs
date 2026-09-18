using System;
using System.Collections.Generic;
using DialogAutomation;
using Xunit;
using Node = DialogAutomation.DialogUtils.ControlNode;

namespace DialogAutomation.Tests
{
    /// <summary>
    /// Tests for the set-text / check / combo / file-dialog features that need no live
    /// dialog: the classification and matching logic, the structural finders (run against
    /// control trees captured from real Windows Open, Save, and Font dialogs), and the
    /// never-throw contract on inputs that cannot be windows. What real dialogs do when driven
    /// is covered by the interactive harness - see TESTING.md.
    /// </summary>
    public class ControlAndFileDialogTests
    {
        private readonly DialogUtils _dialog = new DialogUtils();

        private static IntPtr H(int handle) => new IntPtr(handle);

        // WS_VISIBLE | WS_CHILD, plus the low-nibble type bits the real controls carried.
        private static Node N(int handle, int parent, int id, string cls, int style) =>
            new Node(H(handle), H(parent), id, cls, style);

        // ------------------------------------------------------------------
        // Captured control trees (trimmed to the controls that matter). Handles and styles
        // are the values Windows reported for the .NET SaveFileDialog/OpenFileDialog/FontDialog.
        // ------------------------------------------------------------------

        private const int Dlg = 0x250A76;

        // Save As: the File name box and file-type list are inside DirectUI hosts with ID 0.
        private static List<Node> SaveDialog() => new List<Node>
        {
            N(0x6007E0, Dlg,      0, "DUIViewWndClassName", 0x52000000),
            N(0x3A0824, 0x6007E0, 0, "DirectUIHWND",        0x56000000),
            N(0x4A059E, 0x3A0824, 0, "FloatNotifySink",     0x56000000),
            N(0x46065A, 0x4A059E, 0, "ComboBox",            0x50010242),   // File name (edit drop-down)
            N(0x0B0A6C, 0x46065A, 1001, "Edit",             0x50000380),   // ...its edit box
            N(0x0B0A0A, 0x2D0AB8, 1121, "SHELLDLL_DefView", 0x56010000),   // the folder view
            N(0x250606, 0x3A0824, 0, "FloatNotifySink",     0x56000000),
            N(0x190924, 0x250606, 0, "ComboBox",            0x50030203),   // Save as type (drop-down list)
            N(0x120A50, Dlg,      1, "Button",              0x50030001),   // &Save
            N(0x210A3A, Dlg,      2, "Button",              0x50030000),   // Cancel
            N(0x2B0AA4, Dlg,   1120, "ListBox",             0x40000303),
            // The Explorer address bar and search box - edit boxes that must never be picked.
            N(0x3C0A2C, Dlg,      0, "WorkerW",             0x56000000),
            N(0x6103F0, 0x3C0A2C, 40965, "ReBarWindow32",   0x5602EA69),
            N(0x1408A4, 0x6103F0, 41477, "Address Band Root", 0x56010000),
            N(0x0A09E8, 0x1408A4, 0, "msctls_progress32",   0x52000000),
            N(0x240598, 0x0A09E8, 41477, "ComboBoxEx32",    0x46010042),
            N(0x1208E0, 0x240598, 41477, "ComboBox",        0x56000413),
            N(0x49074C, 0x1208E0, 41477, "Edit",            0x54000080),
        };

        // Open: the File name box is a ComboBoxEx32 with a fixed ID, and the type list is a direct child.
        private static List<Node> OpenDialog() => new List<Node>
        {
            N(0x240A5C, 0x3E07EE, 0, "DUIViewWndClassName", 0x52000000),
            N(0x5B069C, 0x270896, 1121, "SHELLDLL_DefView", 0x56010000),
            N(0x470612, 0x3E07EE, 1090, "Static",           0x50020102),   // File &name:
            N(0x190876, 0x3E07EE, 1148, "ComboBoxEx32",     0x50010042),
            N(0x340892, 0x190876, 1148, "ComboBox",         0x56000413),
            N(0x22092C, 0x340892, 1148, "Edit",             0x54000080),   // the File name edit
            N(0x0D0954, 0x3E07EE, 1136, "ComboBox",         0x50010203),   // Files of &type
            N(0x160A5A, 0x3E07EE, 1, "Button",              0x50030001),
            N(0x150520, 0x3E07EE, 1120, "ListBox",          0x40000303),
            N(0x1D038A, 0x3E07EE, 0, "WorkerW",             0x56000000),
            N(0x0C0A1C, 0x1D038A, 40965, "ReBarWindow32",   0x5602EA69),
            N(0x7407D8, 0x0C0A1C, 41477, "Address Band Root", 0x56010000),
            N(0x60AB4,  0x7407D8, 41477, "ComboBoxEx32",    0x46010042),
            N(0x2E0AB8, 0x60AB4,  41477, "ComboBox",        0x56000413),
            N(0x0A0ABA, 0x2E0AB8, 41477, "Edit",            0x54000080),
        };

        // Font: combo boxes with edit boxes inside them, and ID 1136 - but not a file dialog.
        private static List<Node> FontDialog() => new List<Node>
        {
            N(0x170922, 0x2205FE, 1136, "ComboBox", 0x50010B51),
            N(0x1609DC, 0x170922, 1000, "ComboLBox", 0x54209053),
            N(0x6F01C4, 0x170922, 1001, "Edit",     0x50000380),
            N(0x4C074C, 0x2205FE, 1137, "ComboBox", 0x50010A51),
            N(0x510822, 0x4C074C, 1001, "Edit",     0x50000380),
            N(0x200430, 0x2205FE, 1, "Button",      0x50030001),
        };

        // A pre-Vista style dialog: the File name edit is a direct child at a fixed ID.
        private static List<Node> OldStyleDialog() => new List<Node>
        {
            N(0x1001, 0x9000, 1121, "SHELLDLL_DefView", 0x56010000),
            N(0x1002, 0x9000, 1152, "Edit",             0x50010080),
            N(0x1003, 0x9000, 1136, "ComboBox",         0x50010203),
        };

        // ------------------------------------------------------------------
        // Structural finders, against the captured trees
        // ------------------------------------------------------------------

        [Fact]
        public void FindFileNameControl_SaveDialog_PicksTheDirectUiEditNotTheAddressBar()
        {
            Assert.Equal(H(0x0B0A6C), DialogUtils.FindFileNameControl(SaveDialog()).Handle);
        }

        [Fact]
        public void FindFileNameControl_OpenDialog_PicksTheFixedIdEdit()
        {
            Assert.Equal(H(0x22092C), DialogUtils.FindFileNameControl(OpenDialog()).Handle);
        }

        [Fact]
        public void FindFileNameControl_OldStyleDialog_PicksIdEdt1()
        {
            Assert.Equal(H(0x1002), DialogUtils.FindFileNameControl(OldStyleDialog()).Handle);
        }

        [Fact]
        public void FindFileTypeCombo_SaveDialog_PicksTheDropDownListNotTheFileNameCombo()
        {
            Assert.Equal(H(0x190924), DialogUtils.FindFileTypeCombo(SaveDialog()).Handle);
        }

        [Fact]
        public void FindFileTypeCombo_OpenDialog_PicksTheFixedIdCombo()
        {
            Assert.Equal(H(0x0D0954), DialogUtils.FindFileTypeCombo(OpenDialog()).Handle);
        }

        [Fact]
        public void FindFileTypeCombo_OldStyleDialog_PicksTheFixedIdCombo()
        {
            Assert.Equal(H(0x1003), DialogUtils.FindFileTypeCombo(OldStyleDialog()).Handle);
        }

        [Fact]
        public void Finders_NeverPickAnythingInTheExplorerAddressBar()
        {
            // Strip the real File name box and type list, leaving only the address bar's
            // ComboBoxEx32/ComboBox/Edit: the answer must be "none", not the address bar.
            List<Node> nodes = SaveDialog();
            nodes.RemoveAll(n => n.Handle == H(0x46065A) || n.Handle == H(0x0B0A6C) || n.Handle == H(0x190924));

            Assert.Null(DialogUtils.FindFileNameControl(nodes));
            Assert.Null(DialogUtils.FindFileTypeCombo(nodes));
        }

        [Fact]
        public void FindFileNameControl_IgnoresAnInvisibleEdit()
        {
            List<Node> nodes = OldStyleDialog();
            nodes[1] = N(0x1002, 0x9000, 1152, "Edit", 0x40010080 & ~0x10000000); // same edit, not visible
            nodes.Add(N(0x1010, 0x1003, 1001, "Edit", 0x50000380));               // a visible edit inside a combo

            Assert.Equal(H(0x1010), DialogUtils.FindFileNameControl(nodes).Handle);
        }

        [Fact]
        public void Finders_RefuseADialogThatIsNotAFileDialog()
        {
            // A Font dialog has combo boxes with edit boxes in them and even a control with
            // ID 1136 - typing a path into its font-name box would be a silent misfire.
            Assert.False(DialogUtils.LooksLikeFileDialog(FontDialog()));
            Assert.Null(DialogUtils.FindFileNameControl(FontDialog()));
            Assert.Null(DialogUtils.FindFileTypeCombo(FontDialog()));
        }

        [Fact]
        public void LooksLikeFileDialog_TrueForTheRealOpenAndSaveDialogs()
        {
            Assert.True(DialogUtils.LooksLikeFileDialog(SaveDialog()));
            Assert.True(DialogUtils.LooksLikeFileDialog(OpenDialog()));
        }

        [Fact]
        public void LooksLikeFileDialog_TrueForTheLegacyListBoxAlone()
        {
            Assert.True(DialogUtils.LooksLikeFileDialog(new List<Node> { N(1, 2, 1120, "ListBox", 0x40000303) }));
        }

        [Fact]
        public void Finders_NullOrEmptyTree_ReturnNull()
        {
            Assert.Null(DialogUtils.FindFileNameControl(null));
            Assert.Null(DialogUtils.FindFileTypeCombo(null));
            Assert.Null(DialogUtils.FindFileNameControl(new List<Node>()));
            Assert.Null(DialogUtils.FindFileTypeCombo(new List<Node>()));
        }

        // ------------------------------------------------------------------
        // Check box / radio button classification
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0x50010003, "CheckBox")]      // the Font dialog's Strikeout: BS_AUTOCHECKBOX
        [InlineData(0x50010002, "CheckBox")]      // BS_CHECKBOX
        [InlineData(0x50010005, "ThreeState")]    // BS_3STATE
        [InlineData(0x50010006, "ThreeState")]    // BS_AUTO3STATE
        [InlineData(0x50010004, "Radio")]         // BS_RADIOBUTTON
        [InlineData(0x50010009, "Radio")]         // BS_AUTORADIOBUTTON
        [InlineData(0x50030001, "None")]          // BS_DEFPUSHBUTTON (the OK button)
        [InlineData(0x50030000, "None")]          // BS_PUSHBUTTON
        [InlineData(0x50020007, "None")]          // BS_GROUPBOX (the Effects frame)
        [InlineData(0x5001000B, "None")]          // BS_OWNERDRAW (WinForms buttons)
        public void ClassifyCheckable_ReadsTheButtonTypeBits(int style, string expected)
        {
            Assert.Equal(expected, DialogUtils.ClassifyCheckable(style).ToString());
        }

        [Theory]
        [InlineData("Button", true)]
        [InlineData("button", true)]
        [InlineData("WindowsForms10.Button.app.0.2360855_r3_ad1", true)]
        [InlineData("WindowsForms10.BUTTON.app.0.141b42a_r14_ad1", true)]
        [InlineData("ComboBox", false)]
        [InlineData("Edit", false)]
        [InlineData("Static", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsButtonClass_MatchesNativeAndWinFormsButtons(string className, bool expected)
        {
            Assert.Equal(expected, DialogUtils.IsButtonClass(className));
        }

        [Theory]
        [InlineData("ComboBox", true)]
        [InlineData("WindowsForms10.ComboBox.app.0.2360855_r3_ad1", true)]
        [InlineData("ComboBoxEx32", false)]   // its inner ComboBox is the one that answers CB_ messages
        [InlineData("ComboLBox", false)]
        [InlineData("Button", false)]
        [InlineData(null, false)]
        public void IsComboBoxClass_MatchesComboBoxesButNotComboBoxEx32(string className, bool expected)
        {
            Assert.Equal(expected, DialogUtils.IsComboBoxClass(className));
        }

        [Fact]
        public void ControlCheckState_ValuesMatchWin32BstConstants()
        {
            Assert.Equal(0, (int)ControlCheckState.Unchecked);      // BST_UNCHECKED
            Assert.Equal(1, (int)ControlCheckState.Checked);        // BST_CHECKED
            Assert.Equal(2, (int)ControlCheckState.Indeterminate);  // BST_INDETERMINATE
        }

        // ------------------------------------------------------------------
        // Combo item matching
        // ------------------------------------------------------------------

        private static readonly string[] FileTypes = { "Text (*.txt)", "CSV (*.csv)", "All files (*.*)" };

        [Theory]
        [InlineData("Text (*.txt)", true, 0)]
        [InlineData("csv (*.CSV)", true, 1)]     // case-insensitive
        [InlineData("All files (*.*)", true, 2)]
        [InlineData("CSV", true, -1)]            // exact means the whole text
        [InlineData("*.csv", false, 1)]          // substring
        [InlineData("files", false, 2)]
        [InlineData("(*.", false, 0)]            // first of several matches wins
        [InlineData("nope", false, -1)]
        [InlineData("", false, -1)]
        [InlineData(null, true, -1)]
        public void FindItemIndex_MatchesExactOrBySubstring(string text, bool exact, int expected)
        {
            Assert.Equal(expected, DialogUtils.FindItemIndex(FileTypes, text, exact));
        }

        [Fact]
        public void FindItemIndex_NullListOrNullItem_DoesNotThrow()
        {
            Assert.Equal(-1, DialogUtils.FindItemIndex(null, "x", true));
            Assert.Equal(1, DialogUtils.FindItemIndex(new string[] { null, "x" }, "x", true));
        }

        [Fact]
        public void DescribeItems_ListsAShortListInFull()
        {
            Assert.Equal("'Text (*.txt)', 'CSV (*.csv)', 'All files (*.*)'", DialogUtils.DescribeItems(FileTypes));
        }

        [Fact]
        public void DescribeItems_TruncatesALongListAndSaysHowManyThereAre()
        {
            var many = new List<string>();
            for (int i = 1; i <= 25; i++) many.Add("Item " + i);

            string text = DialogUtils.DescribeItems(many);

            Assert.Contains("'Item 10'", text);
            Assert.DoesNotContain("'Item 11'", text);
            Assert.EndsWith("... (25 in all)", text);
        }

        [Fact]
        public void DescribeItems_EmptyOrNull_SaysNone()
        {
            Assert.Equal("(none)", DialogUtils.DescribeItems(new string[0]));
            Assert.Equal("(none)", DialogUtils.DescribeItems(null));
        }

        // ------------------------------------------------------------------
        // Never-throws contract: handles that cannot be windows
        // ------------------------------------------------------------------

        [Fact]
        public void SetControlText_NullText_ReturnsFalseWithMessage()
        {
            Assert.False(_dialog.SetControlText(IntPtr.Zero, null, out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SetControlText_InvalidHandle_ReturnsFalseAndNeverEchoesTheText()
        {
            const string secret = "s3cr3t-Passw0rd";

            Assert.False(_dialog.SetControlText(IntPtr.Zero, secret, out string message));

            Assert.False(string.IsNullOrEmpty(message));
            Assert.DoesNotContain(secret, message);
        }

        [Fact]
        public void TryGetControlCheckState_InvalidHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_dialog.TryGetControlCheckState(IntPtr.Zero, out ControlCheckState state, out string message));
            Assert.Equal(ControlCheckState.Unchecked, state);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SetControlChecked_InvalidHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_dialog.SetControlChecked(IntPtr.Zero, true, out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void SelectComboItem_NoItemText_ReturnsFalseWithMessage(string itemText)
        {
            Assert.False(_dialog.SelectComboItem(IntPtr.Zero, itemText, out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SelectComboItem_InvalidHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_dialog.SelectComboItem(IntPtr.Zero, "Bold", out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void SetFileDialogPath_NoPath_ReturnsFalseWithMessage(string path)
        {
            Assert.False(_dialog.SetFileDialogPath(IntPtr.Zero, path, out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SetFileDialogPath_InvalidHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_dialog.SetFileDialogPath(IntPtr.Zero, @"C:\temp\report.csv", out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void SelectFileDialogFileType_NoText_ReturnsFalseWithMessage(string text)
        {
            Assert.False(_dialog.SelectFileDialogFileType(IntPtr.Zero, text, out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(300001)]
        public void SubmitFileDialog_TimeoutOutOfRange_ReturnsFalseWithMessage(int closeTimeoutMs)
        {
            Assert.False(_dialog.SubmitFileDialog(IntPtr.Zero, @"C:\temp\report.csv", out string message, closeTimeoutMs));
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SubmitFileDialog_InvalidHandle_ReturnsFalseWithMessage()
        {
            Assert.False(_dialog.SubmitFileDialog(IntPtr.Zero, @"C:\temp\report.csv", out string message));
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
