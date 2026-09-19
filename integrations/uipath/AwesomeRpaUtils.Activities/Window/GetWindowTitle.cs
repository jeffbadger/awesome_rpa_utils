using System;
using System.Activities;
using System.ComponentModel;
using WindowAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Window")]
    [DisplayName("Get Window Title")]
    [Description("Reads a window's title text.")]
    public class GetWindowTitle : CodeActivity<string>
    {
        [RequiredArgument]
        [Description("Handle of the window to read.")]
        public InArgument<IntPtr> WindowHandle { get; set; }

        protected override string Execute(CodeActivityContext context)
        {
            var utils = new WindowUtils();
            ComponentCall.EnsureSuccess("Get Window Title",
                utils.TryGetWindowTitle(WindowHandle.Get(context), out string title, out string message),
                message);
            return title;
        }
    }
}