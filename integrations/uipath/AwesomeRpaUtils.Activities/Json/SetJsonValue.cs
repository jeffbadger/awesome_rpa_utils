using System.Activities;
using System.ComponentModel;
using JsonAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Json")]
    [DisplayName("Set Json Value")]
    [Description("Sets a value in a JSON document by path and outputs the updated document.")]
    public class SetJsonValue : CodeActivity<string>
    {
        [RequiredArgument]
        [Description("JSON document to update.")]
        public InArgument<string> Json { get; set; }

        [RequiredArgument]
        [Description("Path of the value to set.")]
        public InArgument<string> Path { get; set; }

        [RequiredArgument]
        [Description("Value to set (written as a JSON string).")]
        public InArgument<string> Value { get; set; }

        protected override string Execute(CodeActivityContext context)
        {
            var utils = new JsonUtils();
            ComponentCall.EnsureSuccess("Set Json Value",
                utils.TrySetValueInJson(Json.Get(context), Path.Get(context), Value.Get(context),
                    out string updatedJson, out string message),
                message);
            return updatedJson;
        }
    }
}