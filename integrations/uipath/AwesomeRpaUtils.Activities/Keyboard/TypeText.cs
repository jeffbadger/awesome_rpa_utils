using System.Activities;
using System.ComponentModel;
using KeyboardAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Keyboard")]
    [DisplayName("Type Text")]
    [Description("Types text into the focused control, character by character.")]
    public class TypeText : CodeActivity
    {
        [RequiredArgument]
        [Description("Text to type.")]
        public InArgument<string> Text { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new KeyboardUtils();
            ComponentCall.EnsureSuccess("Type Text",
                utils.TypeText(Text.Get(context), out string message),
                message);
        }
    }
}