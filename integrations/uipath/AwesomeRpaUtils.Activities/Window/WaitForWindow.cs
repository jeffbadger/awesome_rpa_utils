using System;
using System.Activities;
using System.ComponentModel;
using WindowAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Window")]
    [DisplayName("Wait For Window")]
    [Description("Waits for a window whose title contains the given text to exist, then outputs its handle. Throws on timeout.")]
    public class WaitForWindow : CodeActivity
    {
        [RequiredArgument]
        [Description("Text the window title must contain.")]
        public InArgument<string> Title { get; set; }

        [RequiredArgument]
        [Description("How long to keep polling, in milliseconds.")]
        public InArgument<int> TimeoutMs { get; set; }

        [Description("Time between polls, in milliseconds. Default 250.")]
        public InArgument<int> PollIntervalMs { get; set; }

        [RequiredArgument]
        public OutArgument<IntPtr> WindowHandle { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new WindowUtils();
            int pollIntervalMs = PollIntervalMs.Get(context);
            if (pollIntervalMs <= 0)
                pollIntervalMs = 250;
            ComponentCall.EnsureSuccess("Wait For Window",
                utils.WaitForWindow(Title.Get(context), TimeoutMs.Get(context),
                    pollIntervalMs, out IntPtr hWnd, out string message),
                message);
            WindowHandle.Set(context, hWnd);
        }
    }
}