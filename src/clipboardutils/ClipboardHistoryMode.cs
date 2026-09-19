namespace ClipboardAutomation
{
    /// <summary>
    /// How much of each clipboard change the history keeps.
    /// </summary>
    public enum ClipboardHistoryMode
    {
        /// <summary>
        /// The text and the file list (with its copy/move effect), plus the names of the formats that were on the
        /// clipboard. Light: it never asks the copying application to produce an image, a table or any other format.
        /// Restoring an item puts back the text or the files, not the other formats.
        /// </summary>
        TextOnly = 0,

        /// <summary>
        /// Every format, as <c>SaveClipboard</c> keeps them, so restoring an item brings the whole clipboard back.
        /// Heavier: every copy makes the copying application produce all of its formats (Excel, for one, can take a
        /// while for a large range), and images count against <c>MaximumClipboardMegabytes</c>.
        /// </summary>
        AllFormats = 1
    }
}
