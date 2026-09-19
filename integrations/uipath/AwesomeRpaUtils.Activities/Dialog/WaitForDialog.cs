using System;
using System.Activities;
using System.ComponentModel;
using DialogAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Dialog")]
    [DisplayName("Wait For Dialog")]
    [Description("Waits for a dialog whose title matches the pattern, then outputs its handle. Throws on timeout.")]
    public class WaitForDialog : CodeActivity
    {
        [RequiredArgument]
        [Description("Regex pattern the dialog title must match.")]
        public InArgument<string> TitlePattern { get; set; }

        [RequiredArgument]
        [Description("How long to keep polling, in milliseconds.")]
        public InArgument<int> TimeoutMs { get; set; }

        [Description("Whether the title must match exactly instead of by regex. Default false.")]
        public bool ExactMatch { get; set; }

        [RequiredArgument]
        public OutArgument<IntPtr> WindowHandle { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new DialogUtils();
            utils.WaitForDialog(TitlePattern.Get(context), TimeoutMs.Get(context), 250,
                out IntPtr hWnd, ExactMatch);
            if (hWnd == IntPtr.Zero)
                throw new Exception($"Wait For Dialog timed out after {TimeoutMs.Get(context)} ms (pattern '{TitlePattern.Get(context)}').");
            WindowHandle.Set(context, hWnd);
        }
    }
}