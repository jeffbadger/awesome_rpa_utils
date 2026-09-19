namespace ClipboardAutomation
{
    /// <summary>
    /// What the application that pastes a file list is asked to do with the files, as the
    /// clipboard's <c>Preferred DropEffect</c> format says it. The values are the Win32
    /// <c>DROPEFFECT_*</c> constants.
    /// </summary>
    public enum FileDropEffect
    {
        /// <summary>Copy the files (<c>DROPEFFECT_COPY</c>, 1). This is what a paste does when the clipboard does not say.</summary>
        Copy = 1,
        /// <summary>Move the files (<c>DROPEFFECT_MOVE</c>, 2), as after Cut in Explorer.</summary>
        Move = 2,
        /// <summary>Create shortcuts to the files (<c>DROPEFFECT_LINK</c>, 4).</summary>
        Link = 4
    }
}
