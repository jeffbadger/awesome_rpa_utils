namespace SessionAutomation
{
    /// <summary>
    /// How a Windows session is connected. Distinguishing these is central to diagnosing
    /// unattended-automation failures: a <see cref="Service"/> session has no interactive
    /// desktop at all (Session 0 isolation), regardless of anything <c>SessionUtils</c>
    /// reports about lock state or desktop availability.
    /// </summary>
    public enum SessionKind
    {
        /// <summary>Logged on at the machine's physical console.</summary>
        Console,
        /// <summary>Connected via Remote Desktop (RDP).</summary>
        Rdp,
        /// <summary>Session 0 - an isolated Windows service session with no interactive desktop.</summary>
        Service,
        /// <summary>A connection protocol other than console or RDP (e.g. Citrix ICA) - rare on stock Windows.</summary>
        Other
    }
}
