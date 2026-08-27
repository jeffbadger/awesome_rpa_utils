using System.ComponentModel;

namespace WindowAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that enumerates, locates, moves/resizes,
    /// activates, and closes windows using the Win32 window APIs.
    /// </summary>
    [Description("Finds, moves, resizes, activates, and closes windows. Drag this " +
                 "component onto a Pega Robot Studio automation to use its methods.")]
    public class WindowUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public WindowUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public WindowUtils(IContainer container)
        {
            container?.Add(this);
        }
    }
}
