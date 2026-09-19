using System;
using System.Activities;
using System.ComponentModel;
using DialogAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Dialog")]
    [DisplayName("Get Dialog Text")]
    [Description("Reads a dialog's title text.")]
    public class GetDialogText : CodeActivity<string>
    {
        [RequiredArgument]
        [Description("Handle of the dialog to read.")]
        public InArgument<IntPtr> WindowHandle { get; set; }

        protected override string Execute(CodeActivityContext context)
        {
            return new DialogUtils().GetDialogText(WindowHandle.Get(context));
        }
    }
}