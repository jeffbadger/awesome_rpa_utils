namespace SessionAutomation
{
    /// <summary>
    /// A Windows Terminal Services session's connection state, mirroring the native
    /// <c>WTS_CONNECTSTATE_CLASS</c> enumeration - same member order/values, so the raw
    /// integer returned by <c>WTSQuerySessionInformation(WTSConnectState)</c> maps directly
    /// via <see cref="SessionUtils.TryToSessionConnectState"/> rather than requiring Pega to
    /// reference any Windows Terminal Services assembly.
    /// </summary>
    public enum SessionConnectState
    {
        /// <summary>A user is logged on to the session and it is the one currently receiving input.</summary>
        Active,
        /// <summary>A client is connected to the session.</summary>
        Connected,
        /// <summary>The session is in the process of connecting to a client.</summary>
        ConnectQuery,
        /// <summary>The session is shadowing another session.</summary>
        Shadow,
        /// <summary>The session is active, but the client has disconnected - the literal "RDP disconnect" state.</summary>
        Disconnected,
        /// <summary>The session is idle, waiting for a client to connect.</summary>
        Idle,
        /// <summary>The session is listening for a connection.</summary>
        Listen,
        /// <summary>The session is being reset.</summary>
        Reset,
        /// <summary>The session is down due to an error.</summary>
        Down,
        /// <summary>The session is initializing.</summary>
        Init
    }
}
