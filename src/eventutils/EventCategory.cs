namespace EventAutomation
{
    /// <summary>
    /// A family of WinEvents the engine can watch. A plain (non-flags) enum: the
    /// engine keeps an allowed-set of categories internally and an event can map
    /// to more than one category (e.g. a "#32770" dialog is both a window-created
    /// event and a dialog event).
    /// </summary>
    public enum EventCategory
    {
        /// <summary>Window create/destroy/show/hide (OBJECT_CREATE/DESTROY/SHOW/HIDE).</summary>
        Windows,
        /// <summary>Foreground and focus changes (SYSTEM_FOREGROUND, OBJECT_FOCUS).</summary>
        Foreground,
        /// <summary>Dialog start/end plus the "#32770" class heuristic.</summary>
        Dialogs,
        /// <summary>Window title changes (OBJECT_NAMECHANGE).</summary>
        Titles,
        /// <summary>State changes (OBJECT_STATECHANGE; OBJECT_VALUECHANGE is opt-in).</summary>
        States,
        /// <summary>Menu open/close and popup open/close.</summary>
        Menus,
        /// <summary>Minimize/restore and move/size operations.</summary>
        WindowOps,
        /// <summary>Alt+Tab session switches (SYSTEM_SWITCHSTART).</summary>
        Session
    }
}
