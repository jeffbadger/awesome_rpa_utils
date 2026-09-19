using System.Activities;
using System.ComponentModel;
using MouseAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Mouse")]
    [DisplayName("Move Mouse")]
    [Description("Moves the cursor to screen coordinates (x, y).")]
    public class MoveMouse : CodeActivity
    {
        [RequiredArgument]
        [Description("Target screen x coordinate.")]
        public InArgument<int> X { get; set; }

        [RequiredArgument]
        [Description("Target screen y coordinate.")]
        public InArgument<int> Y { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new MouseUtils();
            ComponentCall.EnsureSuccess("Move Mouse",
                utils.MoveTo(X.Get(context), Y.Get(context), out string message),
                message);
        }
    }
}