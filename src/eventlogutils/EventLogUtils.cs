using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;

namespace EventLogAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that reads, queries, waits for, writes, and
    /// exports/imports Windows Event Log entries on the local machine.
    /// <para>
    /// Like every component in this suite, all methods honor the never-throws contract:
    /// invalid input (a null/empty log or source name, a negative timeout, an unknown
    /// filter value) and runtime failures (a missing log, insufficient rights, a
    /// malformed exported file) return <c>false</c> with a descriptive message instead
    /// of throwing. Timeouts are likewise <c>false</c> returns.
    /// </para>
    /// <para>
    /// Distinct from <c>EventUtils</c> (window messages/WinEvent hooks/input events) -
    /// this component is exclusively about the Windows Event Log subsystem
    /// (<see cref="System.Diagnostics.EventLog"/>/<see cref="EventLogReader"/>).
    /// </para>
    /// <para>
    /// Scope: local machine only (no remote log support), and does not include
    /// <c>DeleteEventSource</c> - source/log deletion is intentionally out of scope for
    /// this component.
    /// </para>
    /// </summary>
    [Description("Reads, queries, waits for, writes, and exports/imports Windows Event Log entries. " +
                 "All methods return True/False with a failure message instead of throwing. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class EventLogUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public EventLogUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public EventLogUtils(IContainer container)
        {
            container?.Add(this);
        }

        /// <summary>Practical cap on a classic EventLog.WriteEntry message; longer text is truncated rather than left to throw.</summary>
        private const int MaxEventLogMessageLength = 31839;

        #region Discovery

        /// <summary>Gets the names of every event log on the local machine. Empty list on failure. Never throws.</summary>
        [Category("EventLog - Discovery")]
        [Description("Gets the names of every event log on the local machine. Empty list on failure. Never throws.")]
        public List<string> ListLogNames()
        {
            try
            {
                using var session = new EventLogSession();
                return session.GetLogNames().ToList();
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return new List<string>();
            }
        }

        /// <summary>Same as <see cref="ListLogNames"/>, joined into a single delimited string, for designers without a <c>List&lt;string&gt;</c> proxy. Never throws.</summary>
        [Category("EventLog - Discovery")]
        [Description("Same as ListLogNames, joined into a single delimited string. Never throws.")]
        public string ListLogNamesDelimited(string delimiter = ",")
        {
            return string.Join(delimiter ?? ",", ListLogNames());
        }

        /// <summary>Returns <c>true</c> if a log with the given name exists on the local machine. Never throws.</summary>
        /// <param name="logName">The log name to check, e.g. "Application".</param>
        /// <param name="message"><c>null</c> when the log exists; otherwise a human-readable reason it doesn't (or the check failed).</param>
        [Category("EventLog - Discovery")]
        [Description("Returns True if a log with the given name exists. Never throws.")]
        public bool DoesLogExistSimple(string logName, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }
                using var session = new EventLogSession();
                bool exists = session.GetLogNames().Contains(logName, StringComparer.OrdinalIgnoreCase);
                message = exists ? null : $"No event log named '{logName}' exists on this machine.";
                return exists;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DoesLogExistSimple", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="DoesLogExistSimple"/>, plus a <c>querySucceeded</c> output separating "the query completed" from "the log exists". Never throws.</summary>
        [Category("EventLog - Discovery")]
        [Description("Returns True if a log with the given name exists, plus whether the query itself succeeded. Never throws.")]
        public bool DoesLogExist(string logName, out bool querySucceeded, out string message)
        {
            bool exists = DoesLogExistSimple(logName, out message);
            querySucceeded = message == null;
            return exists;
        }

        /// <summary>Returns <c>true</c> if an event source with the given name is registered on the local machine. Never throws.</summary>
        [Category("EventLog - Discovery")]
        [Description("Returns True if an event source with the given name is registered. Never throws.")]
        public bool DoesSourceExistSimple(string sourceName, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    message = "A source name is required.";
                    return false;
                }
                bool exists = EventLog.SourceExists(sourceName);
                message = exists ? null : $"No event source named '{sourceName}' is registered.";
                return exists;
            }
            catch (SecurityException)
            {
                message = $"Access is denied checking whether source '{sourceName}' exists; run elevated or grant registry read access to the Event Log source key.";
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DoesSourceExistSimple", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="DoesSourceExistSimple"/>, plus a <c>querySucceeded</c> output. Never throws.</summary>
        [Category("EventLog - Discovery")]
        [Description("Returns True if an event source is registered, plus whether the query itself succeeded. Never throws.")]
        public bool DoesSourceExist(string sourceName, out bool querySucceeded, out string message)
        {
            bool exists = DoesSourceExistSimple(sourceName, out message);
            querySucceeded = message == null;
            return exists;
        }

        /// <summary>Finds which log a registered event source currently writes to. A source can only ever be registered to one log for its lifetime. Never throws.</summary>
        [Category("EventLog - Discovery")]
        [Description("Finds which log a registered event source currently writes to. Never throws.")]
        public bool TryGetLogNameForSource(string sourceName, out string logName, out string message)
        {
            logName = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    message = "A source name is required.";
                    return false;
                }
                if (!EventLog.SourceExists(sourceName))
                {
                    message = $"No event source named '{sourceName}' is registered.";
                    return false;
                }
                logName = EventLog.LogNameFromSourceName(sourceName, ".");
                if (string.IsNullOrEmpty(logName))
                {
                    message = $"Source '{sourceName}' is not associated with any log.";
                    return false;
                }
                return true;
            }
            catch (SecurityException)
            {
                message = $"Access is denied looking up the log for source '{sourceName}'; run elevated or grant registry read access.";
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("TryGetLogNameForSource", ex);
                return false;
            }
        }

        /// <summary>
        /// Registers a new event source, creating the log if it doesn't already exist.
        /// Idempotent when the source is already registered to the same log; a hard
        /// failure when it's already registered to a different log, since Windows does
        /// not allow reassigning a source once created. Requires administrator rights.
        /// Never throws.
        /// </summary>
        /// <param name="alreadyExisted"><c>true</c> if the source was already registered to <paramref name="logName"/> (a no-op success); <c>false</c> if it was newly created.</param>
        [Category("EventLog - Discovery")]
        [Description("Registers a new event source, creating the log if needed. Requires administrator rights. Never throws.")]
        public bool CreateEventSource(string sourceName, string logName, out bool alreadyExisted, out string message)
        {
            alreadyExisted = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    message = "A source name is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }

                if (EventLog.SourceExists(sourceName))
                {
                    string existingLog = EventLog.LogNameFromSourceName(sourceName, ".");
                    if (string.Equals(existingLog, logName, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyExisted = true;
                        return true;
                    }
                    message = $"Source '{sourceName}' is already registered to log '{existingLog}', not '{logName}'. Windows does not allow reassigning a source to a different log; delete and recreate the source instead.";
                    return false;
                }

                var sourceData = new EventSourceCreationData(sourceName, logName);
                EventLog.CreateEventSource(sourceData);
                return true;
            }
            catch (SecurityException)
            {
                message = $"Access is denied creating event source '{sourceName}'; this requires administrator rights.";
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CreateEventSource", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="CreateEventSource"/>, without the <c>alreadyExisted</c> output. Requires administrator rights. Never throws.</summary>
        [Category("EventLog - Discovery")]
        [Description("Same as CreateEventSource, without the alreadyExisted output. Requires administrator rights. Never throws.")]
        public bool CreateEventSourceSimple(string sourceName, string logName, out string message)
        {
            return CreateEventSource(sourceName, logName, out _, out message);
        }

        #endregion

        #region Query

        /// <summary>
        /// Finds the single most recent entry in a log matching the given filters.
        /// <paramref name="sourceFilter"/> and <paramref name="levelFilter"/> are optional
        /// comma-separated lists (OR'd); <paramref name="eventIdFilter"/> of 0 means "any
        /// event id"; <paramref name="sinceIso8601"/> empty/null means "unbounded".
        /// A <c>false</c> return with a <c>null</c> <paramref name="message"/> means no
        /// match was found (a normal outcome); a <c>false</c> with a non-null message means
        /// the query itself failed. Never throws.
        /// </summary>
        [Category("EventLog - Query")]
        [Description("Finds the single most recent entry in a log matching the given filters. Never throws.")]
        public bool TryGetMostRecentEntry(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601,
            out string timeCreatedIso8601, out int eventId, out string level, out string source, out string entryMessage, out string message)
        {
            timeCreatedIso8601 = default;
            eventId = default;
            level = default;
            source = default;
            entryMessage = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }
                if (!EventLogFilterCore.TryParseLevels(levelFilter, out var levels, out message)) return false;
                if (!EventLogFilterCore.TryParseSince(sinceIso8601, out var since, out message)) return false;
                if (!EventLogFilterCore.TryBuildXPath(sourceFilter, levels, eventIdFilter, since, out string xpath, out message)) return false;

                if (!TryQueryEntries(logName, PathType.LogName, xpath, 1, true, messageContains, out var entries, out message))
                    return false;

                if (entries.Count == 0)
                {
                    message = null;
                    return false;
                }

                var e = entries[0];
                timeCreatedIso8601 = e.TimeCreatedIso8601;
                eventId = e.EventId;
                level = e.LevelDisplayName;
                source = e.ProviderName;
                entryMessage = e.Message;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("TryGetMostRecentEntry", ex);
                return false;
            }
        }

        /// <summary>Counts entries in a log matching the given filters. Note: this materializes every match before counting - narrow the filter for very large logs. Never throws.</summary>
        [Category("EventLog - Query")]
        [Description("Counts entries in a log matching the given filters. Never throws.")]
        public bool CountMatchingEntries(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601, out int count, out string message)
        {
            count = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }
                if (!EventLogFilterCore.TryParseLevels(levelFilter, out var levels, out message)) return false;
                if (!EventLogFilterCore.TryParseSince(sinceIso8601, out var since, out message)) return false;
                if (!EventLogFilterCore.TryBuildXPath(sourceFilter, levels, eventIdFilter, since, out string xpath, out message)) return false;

                if (!TryQueryEntries(logName, PathType.LogName, xpath, 0, false, messageContains, out var entries, out message))
                    return false;

                count = entries.Count;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("CountMatchingEntries", ex);
                return false;
            }
        }

        /// <summary>
        /// Queries a live log with a raw <see cref="EventLogQuery"/> XPath expression, for
        /// filters the scalar helpers above can't express (e.g. an <c>EventData</c> payload
        /// field). Returns matches as a JSON array of entry objects, newest first. Never throws.
        /// </summary>
        [Category("EventLog - Query")]
        [Description("Queries a live log with a raw XPath expression. Returns matches as a JSON array, newest first. Never throws.")]
        public bool QueryByXPath(string logName, string xpath, int maxCount, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(xpath))
                {
                    message = "An XPath filter is required.";
                    return false;
                }
                if (maxCount <= 0)
                {
                    message = "maxCount must be greater than zero.";
                    return false;
                }

                if (!TryQueryEntries(logName, PathType.LogName, xpath, maxCount, true, null, out var entries, out message))
                    return false;

                json = JsonSerializer.Serialize(entries, EventLogJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("QueryByXPath", ex);
                return false;
            }
        }

        #endregion

        #region Write

        /// <summary>
        /// Writes an entry to the log the given source is already registered to. Does
        /// not create the source - call <see cref="CreateEventSource"/> first. A message
        /// longer than the practical write limit is truncated (reported via
        /// <paramref name="errorMessage"/> even though the write still succeeds). Never throws.
        /// </summary>
        /// <param name="errorMessage">Named to avoid colliding with the <paramref name="message"/> input parameter. <c>null</c> on a clean success; a truncation note on a successful-but-truncated write; a failure reason otherwise.</param>
        [Category("EventLog - Write")]
        [Description("Writes an entry to the log the given source is registered to. Requires the source to already exist. Never throws.")]
        public bool WriteEntry(string sourceName, string message, EventLogLevel level, int eventId, out string errorMessage)
        {
            errorMessage = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    errorMessage = "A source name is required.";
                    return false;
                }
                if (string.IsNullOrEmpty(message))
                {
                    errorMessage = "A message is required.";
                    return false;
                }
                if (!Enum.IsDefined(typeof(EventLogLevel), level))
                {
                    errorMessage = $"'{level}' is not a defined EventLogLevel value.";
                    return false;
                }
                if (!EventLog.SourceExists(sourceName))
                {
                    errorMessage = $"No event source named '{sourceName}' is registered. Call CreateEventSource first.";
                    return false;
                }

                bool truncated = message.Length > MaxEventLogMessageLength;
                string entryText = truncated ? message.Substring(0, MaxEventLogMessageLength) : message;

                EventLog.WriteEntry(sourceName, entryText, ToEventLogEntryType(level), eventId);
                errorMessage = truncated ? $"Message truncated to {MaxEventLogMessageLength} characters before writing." : null;
                return true;
            }
            catch (SecurityException)
            {
                errorMessage = $"Access is denied writing to the event log via source '{sourceName}'.";
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                errorMessage = NeverThrowsGuard.Failure("WriteEntry", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WriteEntry"/>, defaulting to <see cref="EventLogLevel.Informational"/> and event id 0. Never throws.</summary>
        [Category("EventLog - Write")]
        [Description("Same as WriteEntry, defaulting to Informational level and event id 0. Never throws.")]
        public bool WriteEntrySimple(string sourceName, string message, out string errorMessage)
        {
            return WriteEntry(sourceName, message, EventLogLevel.Informational, 0, out errorMessage);
        }

        private static EventLogEntryType ToEventLogEntryType(EventLogLevel level)
        {
            switch (level)
            {
                case EventLogLevel.Error:
                case EventLogLevel.Critical:
                    return EventLogEntryType.Error;
                case EventLogLevel.Warning:
                    return EventLogEntryType.Warning;
                case EventLogLevel.SuccessAudit:
                    return EventLogEntryType.SuccessAudit;
                case EventLogLevel.FailureAudit:
                    return EventLogEntryType.FailureAudit;
                default:
                    return EventLogEntryType.Information;
            }
        }

        #endregion

        #region Wait

        /// <summary>
        /// Polls a log until a new entry matching the given filters appears, or the
        /// timeout elapses. Only entries created after this call started are eligible -
        /// a pre-existing matching entry does not satisfy the wait. Never throws.
        /// </summary>
        [Category("EventLog - Wait")]
        [Description("Polls a log until a new entry matching the given filters appears, or the timeout elapses. Never throws.")]
        public bool WaitForEntry(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, int timeoutMs, int pollIntervalMs,
            out bool timedOut, out string timeCreatedIso8601, out int eventId, out string entryMessage, out string message)
        {
            timedOut = default;
            timeCreatedIso8601 = default;
            eventId = default;
            entryMessage = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must not be negative.";
                    return false;
                }
                if (pollIntervalMs < 1) pollIntervalMs = 1;
                if (!EventLogFilterCore.TryParseLevels(levelFilter, out var levels, out message)) return false;

                // Only entries created after the wait started are eligible.
                DateTimeOffset startedAt = DateTimeOffset.UtcNow;
                if (!EventLogFilterCore.TryBuildXPath(sourceFilter, levels, eventIdFilter, startedAt, out string xpath, out message)) return false;

                int start = Environment.TickCount;
                while (true)
                {
                    if (!TryQueryEntries(logName, PathType.LogName, xpath, 1, true, messageContains, out var entries, out message))
                        return false;

                    if (entries.Count > 0)
                    {
                        var e = entries[0];
                        timeCreatedIso8601 = e.TimeCreatedIso8601;
                        eventId = e.EventId;
                        entryMessage = e.Message;
                        message = null;
                        return true;
                    }

                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForEntry", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForEntry"/>, without the <c>timedOut</c>/entry outputs - just True/False plus a message. Never throws.</summary>
        [Category("EventLog - Wait")]
        [Description("Same as WaitForEntry, without the timedOut/entry outputs. Never throws.")]
        public bool WaitForEntrySimple(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForEntry(logName, sourceFilter, levelFilter, eventIdFilter, messageContains, timeoutMs, pollIntervalMs, out _, out _, out _, out _, out message);
        }

        #endregion

        #region Export

        /// <summary>Exports entries matching an XPath filter from a live log to a <c>.evtx</c> file. Never throws.</summary>
        [Category("EventLog - Export")]
        [Description("Exports entries matching an XPath filter from a live log to a .evtx file. Never throws.")]
        public bool ExportFilteredLog(string logName, string xpath, string exportFilePath, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(exportFilePath))
                {
                    message = "An export file path is required.";
                    return false;
                }
                string effectiveXPath = string.IsNullOrWhiteSpace(xpath) ? "*" : xpath;

                using var session = new EventLogSession();
                session.ExportLog(logName, PathType.LogName, effectiveXPath, exportFilePath);
                return true;
            }
            catch (EventLogNotFoundException)
            {
                message = $"No event log named '{logName}' exists on this machine.";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                message = $"Access is denied exporting log '{logName}'; run elevated or add the caller to the 'Event Log Readers' group.";
                return false;
            }
            catch (IOException ex)
            {
                message = NeverThrowsGuard.Failure("ExportFilteredLog", ex);
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ExportFilteredLog", ex);
                return false;
            }
        }

        /// <summary>Queries a previously exported <c>.evtx</c> file with a raw XPath filter. Returns matches as a JSON array. Never throws.</summary>
        [Category("EventLog - Export")]
        [Description("Queries a previously exported .evtx file with a raw XPath filter. Returns matches as a JSON array. Never throws.")]
        public bool QueryExportedLog(string evtxFilePath, string xpath, int maxCount, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(evtxFilePath))
                {
                    message = "An exported log file path is required.";
                    return false;
                }
                if (maxCount <= 0)
                {
                    message = "maxCount must be greater than zero.";
                    return false;
                }
                if (!File.Exists(evtxFilePath))
                {
                    message = $"No file found at '{evtxFilePath}'.";
                    return false;
                }
                string effectiveXPath = string.IsNullOrWhiteSpace(xpath) ? "*" : xpath;

                if (!TryQueryEntries(evtxFilePath, PathType.FilePath, effectiveXPath, maxCount, false, null, out var entries, out message))
                    return false;

                json = JsonSerializer.Serialize(entries, EventLogJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("QueryExportedLog", ex);
                return false;
            }
        }

        #endregion

        #region JSON summary convenience

        /// <summary>
        /// Queries a log with scalar filters and returns matches as a JSON array, newest
        /// first - the main bulk-read method most flows will actually use. Never throws.
        /// </summary>
        [Category("EventLog - Query")]
        [Description("Queries a log with scalar filters and returns matches as a JSON array, newest first. Never throws.")]
        public bool QueryRecentEntriesJson(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601, int maxCount, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }
                if (maxCount <= 0)
                {
                    message = "maxCount must be greater than zero.";
                    return false;
                }
                if (!EventLogFilterCore.TryParseLevels(levelFilter, out var levels, out message)) return false;
                if (!EventLogFilterCore.TryParseSince(sinceIso8601, out var since, out message)) return false;
                if (!EventLogFilterCore.TryBuildXPath(sourceFilter, levels, eventIdFilter, since, out string xpath, out message)) return false;

                if (!TryQueryEntries(logName, PathType.LogName, xpath, maxCount, true, messageContains, out var entries, out message))
                    return false;

                json = JsonSerializer.Serialize(entries, EventLogJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("QueryRecentEntriesJson", ex);
                return false;
            }
        }

        /// <summary>Gets the last N entries from a log, unfiltered, as a JSON array. Never throws.</summary>
        [Category("EventLog - Query")]
        [Description("Gets the last N entries from a log, unfiltered, as a JSON array. Never throws.")]
        public bool DumpRecentEntriesJson(string logName, int count, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(logName))
                {
                    message = "A log name is required.";
                    return false;
                }
                if (count <= 0)
                {
                    message = "count must be greater than zero.";
                    return false;
                }

                if (!TryQueryEntries(logName, PathType.LogName, "*", count, true, null, out var entries, out message))
                    return false;

                json = JsonSerializer.Serialize(entries, EventLogJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DumpRecentEntriesJson", ex);
                return false;
            }
        }

        #endregion

        #region Internal query plumbing

        /// <summary>
        /// Runs an <see cref="EventLogQuery"/>/<see cref="EventLogReader"/> pass and
        /// converts matching records to <see cref="EventLogEntryData"/>, applying the
        /// post-hoc <paramref name="messageContains"/> filter (not expressible via XPath)
        /// per record. <paramref name="maxCount"/> of 0 or less means no cap - used
        /// internally by <see cref="CountMatchingEntries"/>; every public method that
        /// exposes a <c>maxCount</c>/<c>count</c> parameter validates it as positive itself
        /// before calling this. Disposes the reader/query and each record it reads,
        /// including on an error partway through enumeration.
        /// </summary>
        private static bool TryQueryEntries(string logPath, PathType pathType, string xpath, int maxCount, bool reverseDirection, string messageContains, out List<EventLogEntryData> entries, out string message)
        {
            entries = null;
            message = default;
            EventLogReader reader = null;
            try
            {
                var query = new EventLogQuery(logPath, pathType, xpath) { ReverseDirection = reverseDirection };
                reader = new EventLogReader(query);

                var results = new List<EventLogEntryData>();
                EventRecord record;
                while ((maxCount <= 0 || results.Count < maxCount) && (record = reader.ReadEvent()) != null)
                {
                    using (record)
                    {
                        if (TryConvert(record, out EventLogEntryData data) && EventLogFilterCore.MatchesMessage(data.Message, messageContains))
                            results.Add(data);
                    }
                }

                entries = results;
                return true;
            }
            catch (EventLogNotFoundException)
            {
                message = pathType == PathType.LogName
                    ? $"No event log named '{logPath}' exists on this machine."
                    : $"No event log file found at '{logPath}'.";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                message = $"Access is denied reading '{logPath}'; run elevated or add the caller to the 'Event Log Readers' group.";
                return false;
            }
            catch (EventLogException ex)
            {
                message = NeverThrowsGuard.Failure("Query", ex);
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Query", ex);
                return false;
            }
            finally
            {
                reader?.Dispose();
            }
        }

        /// <summary>
        /// Converts a single <see cref="EventRecord"/> to <see cref="EventLogEntryData"/>.
        /// Non-essential fields (level display name, task display name, formatted message)
        /// are guarded individually - a manifest-less provider or a record from another
        /// machine can throw resolving these - so one bad field doesn't discard an
        /// otherwise-good record. Returns <c>false</c> only if a genuinely essential field
        /// (record id/event id/provider) can't be read at all.
        /// </summary>
        private static bool TryConvert(EventRecord record, out EventLogEntryData data)
        {
            data = null;
            try
            {
                var converted = new EventLogEntryData
                {
                    RecordId = record.RecordId ?? 0,
                    EventId = record.Id,
                    ProviderName = record.ProviderName,
                    LogName = record.LogName,
                    MachineName = record.MachineName,
                    TimeCreatedIso8601 = record.TimeCreated.HasValue ? record.TimeCreated.Value.ToUniversalTime().ToString("o") : null,
                    Level = ToEventLogLevel(record)
                };
                converted.LevelDisplayName = SafeGet(() => record.LevelDisplayName) ?? converted.Level.ToString();
                converted.TaskDisplayName = SafeGet(() => record.TaskDisplayName);
                converted.Message = SafeGet(() => record.FormatDescription());

                data = converted;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeGet(Func<string> accessor)
        {
            try { return accessor(); }
            catch { return null; }
        }

        private static EventLogLevel ToEventLogLevel(EventRecord record)
        {
            long keywords = record.Keywords ?? 0;
            if ((keywords & (long)StandardEventKeywords.AuditFailure) != 0)
                return EventLogLevel.FailureAudit;
            if ((keywords & (long)StandardEventKeywords.AuditSuccess) != 0)
                return EventLogLevel.SuccessAudit;

            byte level = record.Level ?? (byte)StandardEventLevel.LogAlways;
            if (level == (byte)StandardEventLevel.Critical) return EventLogLevel.Critical;
            if (level == (byte)StandardEventLevel.Error) return EventLogLevel.Error;
            if (level == (byte)StandardEventLevel.Warning) return EventLogLevel.Warning;
            if (level == (byte)StandardEventLevel.Informational) return EventLogLevel.Informational;
            if (level == (byte)StandardEventLevel.Verbose) return EventLogLevel.Verbose;
            return EventLogLevel.LogAlways;
        }

        #endregion
    }
}
