using System.Activities;
using System.ComponentModel;
using JsonAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Json")]
    [DisplayName("Get Json Value")]
    [Description("Reads a string value from a JSON document by path.")]
    public class GetJsonValue : CodeActivity<string>
    {
        [RequiredArgument]
        [Description("JSON document to read from.")]
        public InArgument<string> Json { get; set; }

        [RequiredArgument]
        [Description("Path of the value to read.")]
        public InArgument<string> Path { get; set; }

        protected override string Execute(CodeActivityContext context)
        {
            var utils = new JsonUtils();
            ComponentCall.EnsureSuccess("Get Json Value",
                utils.TryGetStringValue(Json.Get(context), Path.Get(context),
                    out string value, out string message),
                message);
            return value;
        }
    }
}