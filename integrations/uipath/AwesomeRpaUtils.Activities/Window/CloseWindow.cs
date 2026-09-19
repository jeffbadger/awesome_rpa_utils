using System;
using System.Activities;
using System.ComponentModel;
using WindowAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Window")]
    [DisplayName("Close Window")]
    [Description("Sends a close request (WM_CLOSE) to a window.")]
    public class CloseWindow : CodeActivity
    {
        [RequiredArgument]
        [Description("Handle of the window to close.")]
        public InArgument<IntPtr> WindowHandle { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new WindowUtils();
            ComponentCall.EnsureSuccess("Close Window",
                utils.CloseWindow(WindowHandle.Get(context), out string message),
                message);
        }
    }
}