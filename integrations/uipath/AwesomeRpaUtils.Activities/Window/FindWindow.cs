using System;
using System.Activities;
using System.ComponentModel;
using WindowAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Window")]
    [DisplayName("Find Window")]
    [Description("Finds a top-level window whose title and/or class name matches regex patterns. Throws if no window matches.")]
    public class FindWindow : CodeActivity
    {
        [Description("Regex pattern the window title must match. Null/empty skips the title check.")]
        public InArgument<string> TitlePattern { get; set; }

        [Description("Regex pattern the window class name must match. Null/empty skips the class check.")]
        public InArgument<string> ClassNamePattern { get; set; }

        [Description("Whether the patterns match case-insensitively. Default true.")]
        public bool IgnoreCase { get; set; } = true;

        [RequiredArgument]
        public OutArgument<IntPtr> WindowHandle { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new WindowUtils();
            ComponentCall.EnsureSuccess("Find Window",
                utils.TryFindWindowByRegex(TitlePattern.Get(context), ClassNamePattern.Get(context),
                    out IntPtr hWnd, out string message, IgnoreCase),
                message);
            WindowHandle.Set(context, hWnd);
        }
    }
}