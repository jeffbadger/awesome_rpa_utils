using System.ComponentModel;

namespace KeyboardAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that injects keyboard input (key presses,
    /// combos, text typing) using the Windows <c>SendInput</c> API, and reads
    /// keyboard/modifier state via <c>GetAsyncKeyState</c>.
    /// </summary>
    [Description("Injects keyboard input: key presses, combos, text typing, and " +
                 "clipboard-paste. Drag this component onto a Pega Robot Studio " +
                 "automation to use its methods.")]
    public class KeyboardUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public KeyboardUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public KeyboardUtils(IContainer container)
        {
            container?.Add(this);
        }
    }
}
