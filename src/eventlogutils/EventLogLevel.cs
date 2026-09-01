namespace EventLogAutomation
{
    /// <summary>
    /// A Windows Event Log severity/level, unifying the classic write-side
    /// <see cref="System.Diagnostics.EventLogEntryType"/> with the modern read-side
    /// <see cref="System.Diagnostics.Eventing.Reader.StandardEventLevel"/> and the
    /// <c>SuccessAudit</c>/<c>FailureAudit</c> keywords that neither enum expresses alone.
    /// <para>
    /// <c>SuccessAudit</c>/<c>FailureAudit</c> do not exist as an <c>EventRecord.Level</c>
    /// value on the read side - they are inferred from <c>EventRecord.Keywords</c> via
    /// <see cref="System.Diagnostics.Eventing.Reader.StandardEventKeywords.AuditSuccess"/>/
    /// <see cref="System.Diagnostics.Eventing.Reader.StandardEventKeywords.AuditFailure"/>.
    /// Filtering and reporting in this component account for that - do not assume
    /// <c>Level</c> alone distinguishes an audit entry when reading.
    /// </para>
    /// </summary>
    public enum EventLogLevel
    {
        /// <summary>No specific level (EventLogReader's LogAlways).</summary>
        LogAlways,
        /// <summary>A critical/fatal error.</summary>
        Critical,
        /// <summary>An error. Maps to <see cref="System.Diagnostics.EventLogEntryType.Error"/> on write.</summary>
        Error,
        /// <summary>A warning. Maps to <see cref="System.Diagnostics.EventLogEntryType.Warning"/> on write.</summary>
        Warning,
        /// <summary>An informational entry. Maps to <see cref="System.Diagnostics.EventLogEntryType.Information"/> on write.</summary>
        Informational,
        /// <summary>A verbose/diagnostic entry.</summary>
        Verbose,
        /// <summary>A successful audited access. Maps to <see cref="System.Diagnostics.EventLogEntryType.SuccessAudit"/> on write; inferred from Keywords on read.</summary>
        SuccessAudit,
        /// <summary>A failed audited access. Maps to <see cref="System.Diagnostics.EventLogEntryType.FailureAudit"/> on write; inferred from Keywords on read.</summary>
        FailureAudit
    }
}
