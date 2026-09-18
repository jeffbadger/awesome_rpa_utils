namespace InterruptAutomation
{
    /// <summary>
    /// A standard Windows dialog button, identified by its well-known control ID, for
    /// <see cref="InterruptUtils.AddDismissRuleById"/>. The values are the Win32
    /// <c>IDOK</c>, <c>IDCANCEL</c>, ... constants a <c>MessageBox</c> uses.
    /// </summary>
    public enum InterruptButton
    {
        /// <summary>The OK button (control ID 1).</summary>
        Ok = 1,
        /// <summary>The Cancel button (control ID 2).</summary>
        Cancel = 2,
        /// <summary>The Abort button (control ID 3).</summary>
        Abort = 3,
        /// <summary>The Retry button (control ID 4).</summary>
        Retry = 4,
        /// <summary>The Ignore button (control ID 5).</summary>
        Ignore = 5,
        /// <summary>The Yes button (control ID 6).</summary>
        Yes = 6,
        /// <summary>The No button (control ID 7).</summary>
        No = 7
    }
}
