using System;
using System.Activities;
using System.ComponentModel;
using MouseAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Mouse")]
    [DisplayName("Scroll Mouse")]
    [Description("Scrolls the mouse wheel. Positive notches scroll up, negative scroll down.")]
    public class ScrollMouse : CodeActivity
    {
        [RequiredArgument]
        [Description("Number of wheel notches. Positive scrolls up, negative scrolls down.")]
        public InArgument<int> Notches { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new MouseUtils();
            int notches = Notches.Get(context);
            if (notches == 0)
                return;

            string message;
            bool succeeded = notches > 0
                ? utils.ScrollUp(notches, out message)
                : utils.ScrollDown(-notches, out message);
            ComponentCall.EnsureSuccess("Scroll Mouse", succeeded, message);
        }
    }
}