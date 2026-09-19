using System.Activities;
using System.ComponentModel;
using KeyboardAutomation;

namespace AwesomeRpaUtils.Activities
{
    [Category("Awesome RPA Utils > Keyboard")]
    [DisplayName("Press Key")]
    [Description("Presses and releases a virtual key (e.g. Enter, Escape).")]
    public class PressKey : CodeActivity
    {
        [RequiredArgument]
        [Description("Key to press and release.")]
        public VirtualKey Key { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var utils = new KeyboardUtils();
            ComponentCall.EnsureSuccess("Press Key",
                utils.PressKey(Key, out string message),
                message);
        }
    }
}