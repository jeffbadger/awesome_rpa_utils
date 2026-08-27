using System.ComponentModel;

namespace OcrAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that recognizes text from a screen region or an
    /// image file using <c>Windows.Media.Ocr</c>, with plain-text and structured
    /// (positioned) result shapes.
    /// </summary>
    [Description("Recognizes text from the screen or an image file via Windows OCR. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class OcrUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public OcrUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public OcrUtils(IContainer container)
        {
            container?.Add(this);
        }
    }
}
