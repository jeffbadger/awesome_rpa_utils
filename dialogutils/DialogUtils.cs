using System.ComponentModel;

namespace DialogAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that finds and dismisses native dialogs
    /// (message boxes, common dialogs) by button text or control ID, via <c>BM_CLICK</c>
    /// — no cursor movement required, and it works even if the dialog is behind other
    /// windows.
    /// </summary>
    [Description("Finds and dismisses native dialogs by button text/control ID. Drag " +
                 "this component onto a Pega Robot Studio automation to use its methods.")]
    public class DialogUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public DialogUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public DialogUtils(IContainer container)
        {
            container?.Add(this);
        }
    }
}
