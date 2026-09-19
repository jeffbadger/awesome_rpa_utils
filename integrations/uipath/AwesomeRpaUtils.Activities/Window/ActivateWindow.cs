using System;
using System.Activities;
using System.ComponentModel;
using WindowAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Window")]
    [DisplayName("Activate Window")]
    [Description("Brings a window to the foreground and gives it focus.")]
    public class ActivateWindow : CodeActivity
    {
        [RequiredArgument]
        [Description("Handle of the window to activate.")]
        public InArgument<IntPtr> WindowHandle { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new WindowUtils();
            ComponentCall.EnsureSuccess("Activate Window",
                utils.ActivateWindow(WindowHandle.Get(context), out string message),
                message);
        }
    }
}