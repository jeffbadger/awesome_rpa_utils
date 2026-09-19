using System.Activities;
using System.ComponentModel;
using KeyboardAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Keyboard")]
    [DisplayName("Paste Text")]
    [Description("Pastes text into the focused control via the clipboard (Ctrl+V), faster and more reliable than typing for long strings.")]
    public class PasteText : CodeActivity
    {
        [RequiredArgument]
        [Description("Text to paste.")]
        public InArgument<string> Text { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new KeyboardUtils();
            ComponentCall.EnsureSuccess("Paste Text",
                utils.PasteText(Text.Get(context), out string message),
                message);
        }
    }
}