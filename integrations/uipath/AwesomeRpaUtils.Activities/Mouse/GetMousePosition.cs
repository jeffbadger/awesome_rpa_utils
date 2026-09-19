using System.Activities;
using System.ComponentModel;
using MouseAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Mouse")]
    [DisplayName("Get Mouse Position")]
    [Description("Reads the cursor position in screen coordinates.")]
    public class GetMousePosition : CodeActivity
    {
        [RequiredArgument]
        public OutArgument<int> X { get; set; }

        [RequiredArgument]
        public OutArgument<int> Y { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new MouseUtils();
            ComponentCall.EnsureSuccess("Get Mouse Position",
                utils.GetPosition(out int x, out int y, out string message),
                message);
            X.Set(context, x);
            Y.Set(context, y);
        }
    }
}