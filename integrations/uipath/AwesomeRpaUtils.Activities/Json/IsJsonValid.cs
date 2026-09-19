using System.Activities;
using System.ComponentModel;
using JsonAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Json")]
    [DisplayName("Is Json Valid")]
    [Description("Checks whether a string is valid JSON.")]
    public class IsJsonValid : CodeActivity<bool>
    {
        [RequiredArgument]
        [Description("Text to validate.")]
        public InArgument<string> Json { get; set; }

        protected override bool Execute(CodeActivityContext context)
        {
            var utils = new JsonUtils();
            return utils.IsValidJson(Json.Get(context), out string message);
        }
    }
}