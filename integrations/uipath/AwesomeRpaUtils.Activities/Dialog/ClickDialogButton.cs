using System;
using System.Activities;
using System.ComponentModel;
using DialogAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Dialog")]
    [DisplayName("Click Dialog Button")]
    [Description("Finds a button in a dialog by its text and clicks it once enabled.")]
    public class ClickDialogButton : CodeActivity<bool>
    {
        [RequiredArgument]
        [Description("Handle of the dialog containing the button.")]
        public InArgument<IntPtr> WindowHandle { get; set; }

        [RequiredArgument]
        [Description("Text of the button to click.")]
        public InArgument<string> ButtonText { get; set; }

        [Description("Whether the button text must match exactly. Default true.")]
        public bool ExactMatch { get; set; } = true;

        // The returned value is the button's enabled state at click time.
        protected override bool Execute(CodeActivityContext context)
        {
            var utils = new DialogUtils();
            ComponentCall.EnsureSuccess("Click Dialog Button",
                utils.ClickDialogButtonByText(WindowHandle.Get(context), ButtonText.Get(context),
                    out bool wasEnabled, out string message, ExactMatch),
                message);
            return wasEnabled;
        }
    }
}