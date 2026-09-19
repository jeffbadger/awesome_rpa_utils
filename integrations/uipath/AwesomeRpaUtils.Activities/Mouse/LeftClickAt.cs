using System.Activities;
using System.ComponentModel;
using MouseAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Mouse")]
    [DisplayName("Left Click At")]
    [Description("Moves the cursor to screen coordinates (x, y) and left-clicks.")]
    public class LeftClickAt : CodeActivity
    {
        [RequiredArgument]
        [Description("Screen x coordinate to click at.")]
        public InArgument<int> X { get; set; }

        [RequiredArgument]
        [Description("Screen y coordinate to click at.")]
        public InArgument<int> Y { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new MouseUtils();
            ComponentCall.EnsureSuccess("Left Click At",
                utils.LeftClickAt(X.Get(context), Y.Get(context), out string message),
                message);
        }
    }
}