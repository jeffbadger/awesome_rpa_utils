namespace EventAutomation
{
    /// <summary>
    /// A single filterable field, for
    /// <see cref="EventUtils.BuildFilterJson(EventFilterField, string, bool?)"/>'s
    /// designer-selectable single-field overload — the enum alternative to
    /// remembering which of <see cref="EventUtils.BuildFilterJson(string, string, string, string, string, bool?, bool?)"/>'s
    /// seven named parameters to fill in when only one field needs to be set.
    /// </summary>
    public enum EventFilterField
    {
        /// <summary>Match a single process name (case-insensitive; a trailing ".exe" is ignored).</summary>
        Process,
        /// <summary>Comma-separated process names to match any of (case-insensitive).</summary>
        ProcessesCsv,
        /// <summary>Match a window class name (case-insensitive), e.g. "#32770" for a dialog.</summary>
        ClassName,
        /// <summary>Require the window title to contain this text (case-insensitive).</summary>
        TitleContains,
        /// <summary>Require the window title to match this regex.</summary>
        TitleMatches
    }
}
