using System.Activities;
using System.ComponentModel;
using MouseAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Mouse")]
    [DisplayName("Right Click At")]
    [Description("Moves the cursor to screen coordinates (x, y) and right-clicks.")]
    public class RightClickAt : CodeActivity
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
            ComponentCall.EnsureSuccess("Right Click At",
                utils.RightClickAt(X.Get(context), Y.Get(context), out string message),
                message);
        }
    }
}