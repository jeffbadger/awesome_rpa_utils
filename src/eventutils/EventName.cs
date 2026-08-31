namespace EventAutomation
{
    /// <summary>
    /// A specific WinEvent name, as reported in <see cref="EventData.Category"/> and
    /// mapped internally by <c>EventCategoryMap.EventName</c>. Exposed as an enum so
    /// Pega Robot Studio can select an event name (e.g. for <see cref="EventUtils.SetDebounce(EventName, int, out string)"/>)
    /// instead of typing a free-form, typo-prone string.
    /// </summary>
    public enum EventName
    {
        /// <summary>A window was created (EVENT_OBJECT_CREATE).</summary>
        WindowCreated,
        /// <summary>A window was destroyed (EVENT_OBJECT_DESTROY).</summary>
        WindowDestroyed,
        /// <summary>A window was shown (EVENT_OBJECT_SHOW).</summary>
        WindowShown,
        /// <summary>A window was hidden (EVENT_OBJECT_HIDE).</summary>
        WindowHidden,
        /// <summary>The foreground window changed (EVENT_SYSTEM_FOREGROUND).</summary>
        ForegroundChanged,
        /// <summary>Input focus changed (EVENT_OBJECT_FOCUS).</summary>
        FocusChanged,
        /// <summary>A dialog appeared (EVENT_SYSTEM_DIALOGSTART).</summary>
        DialogAppeared,
        /// <summary>A dialog closed (EVENT_SYSTEM_DIALOGEND).</summary>
        DialogClosed,
        /// <summary>A window's title changed (EVENT_OBJECT_NAMECHANGE).</summary>
        TitleChanged,
        /// <summary>A window's state changed (EVENT_OBJECT_STATECHANGE).</summary>
        StateChanged,
        /// <summary>A control's value changed (EVENT_OBJECT_VALUECHANGE; opt-in).</summary>
        ValueChanged,
        /// <summary>A menu opened (EVENT_SYSTEM_MENUSTART).</summary>
        MenuOpened,
        /// <summary>A menu closed (EVENT_SYSTEM_MENUEND).</summary>
        MenuClosed,
        /// <summary>A menu popup opened (EVENT_SYSTEM_MENUPOPUPSTART).</summary>
        MenuPopupOpened,
        /// <summary>A menu popup closed (EVENT_SYSTEM_MENUPOPUPEND).</summary>
        MenuPopupClosed,
        /// <summary>A window was minimized (EVENT_SYSTEM_MINIMIZESTART).</summary>
        WindowMinimized,
        /// <summary>A window was restored from minimized (EVENT_SYSTEM_MINIMIZEEND).</summary>
        WindowRestored,
        /// <summary>A window move/size operation started (EVENT_SYSTEM_MOVESIZE).</summary>
        WindowMoved,
        /// <summary>A window move/size operation ended (EVENT_SYSTEM_MOVESIZEEND).</summary>
        WindowMoveEnded,
        /// <summary>An Alt+Tab session switch occurred (EVENT_SYSTEM_SWITCHSTART).</summary>
        SessionSwitched
    }
}
