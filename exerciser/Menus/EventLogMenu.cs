using System.Collections.Generic;
using EventLogAutomation;

namespace Exerciser.Menus
{
    /// <summary>
    /// EventLogUtils' menu. Point every entry at the disposable "ZZTestEventLogUtils"
    /// source / "ZZTestLog" log created by the Setup/Cleanup menu's EventLogUtils
    /// entry - never at Application/System/Security directly (TESTING.md's explicit
    /// warning). CreateEventSource and reading the Security log both need an
    /// elevated session; that requirement isn't something this menu can work
    /// around. eventIdFilter of 0 means "no filter" here, matching the
    /// component's own convention; blank string filters (sourceFilter,
    /// levelFilter, messageContains, sinceIso8601) mean the same. Simple-suffixed
    /// wrapper overloads are omitted in favor of their fuller siblings.
    /// </summary>
    internal static class EventLogMenu
    {
        internal static MenuItem[] Build(EventLogUtils eventLog)
        {
            return new[]
            {
                new MenuItem("ListLogNames", "No input. Assert the test log appears.", () =>
                {
                    List<string> names = eventLog.ListLogNames();
                    Report.Result(true, null, ("names (count)", names.Count), ("names", names));
                }),
                new MenuItem("DoesLogExist", "A made-up name returns false + message, never an exception.", () =>
                {
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    bool ok = eventLog.DoesLogExist(logName, out bool querySucceeded, out string message);
                    Report.Result(ok, message, ("querySucceeded", querySucceeded));
                }),
                new MenuItem("DoesSourceExist", "A made-up name returns false + message.", () =>
                {
                    string sourceName = Prompt.String("Source name", "ZZTestEventLogUtils");
                    bool ok = eventLog.DoesSourceExist(sourceName, out bool querySucceeded, out string message);
                    Report.Result(ok, message, ("querySucceeded", querySucceeded));
                }),
                new MenuItem("TryGetLogNameForSource", "No input beyond the source name.", () =>
                {
                    string sourceName = Prompt.String("Source name", "ZZTestEventLogUtils");
                    bool ok = eventLog.TryGetLogNameForSource(sourceName, out string logName, out string message);
                    Report.Result(ok, message, ("logName", logName));
                }),
                new MenuItem("CreateEventSource", "Needs an elevated session. Idempotent case: run again with the same source+log, expect alreadyExisted=true. A different log name for the same source fails - Windows doesn't allow reassignment.", () =>
                {
                    string sourceName = Prompt.String("Source name", "ZZTestEventLogUtils");
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    bool ok = eventLog.CreateEventSource(sourceName, logName, out bool alreadyExisted, out string message);
                    Report.Result(ok, message, ("alreadyExisted", alreadyExisted));
                }),
                new MenuItem("WriteEntry", "Write a known message/level/event id, then verify it with TryGetMostRecentEntry/QueryRecentEntriesJson below.", () =>
                {
                    string sourceName = Prompt.String("Source name", "ZZTestEventLogUtils");
                    string message = Prompt.String("Message", "Exerciser test entry");
                    EventLogLevel level = Prompt.Enum<EventLogLevel>("Level");
                    int eventId = Prompt.Int("Event ID", 1);
                    bool ok = eventLog.WriteEntry(sourceName, message, level, eventId, out string errorMessage);
                    Report.Result(ok, errorMessage);
                }),
                new MenuItem("TryGetMostRecentEntry", "Write an entry first with WriteEntry above, then assert each filter dimension in isolation and combined.", () =>
                {
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    string sourceFilter = Prompt.String("Source filter (blank for none)", "");
                    string levelFilter = Prompt.String("Level filter (blank for none)", "");
                    int eventIdFilter = Prompt.Int("Event ID filter (0 for none)", 0);
                    string messageContains = Prompt.String("Message contains (blank for none)", "");
                    string sinceIso8601 = Prompt.String("Since (ISO 8601, blank for none)", "");
                    bool ok = eventLog.TryGetMostRecentEntry(logName, sourceFilter, levelFilter, eventIdFilter, messageContains, sinceIso8601,
                        out string timeCreatedIso8601, out int eventId, out string level, out string source, out string entryMessage, out string message);
                    Report.Result(ok, message, ("timeCreatedIso8601", timeCreatedIso8601), ("eventId", eventId), ("level", level), ("source", source), ("entryMessage", entryMessage));
                }),
                new MenuItem("CountMatchingEntries", "Same filter dimensions as TryGetMostRecentEntry.", () =>
                {
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    string sourceFilter = Prompt.String("Source filter (blank for none)", "");
                    string levelFilter = Prompt.String("Level filter (blank for none)", "");
                    int eventIdFilter = Prompt.Int("Event ID filter (0 for none)", 0);
                    string messageContains = Prompt.String("Message contains (blank for none)", "");
                    string sinceIso8601 = Prompt.String("Since (ISO 8601, blank for none)", "");
                    bool ok = eventLog.CountMatchingEntries(logName, sourceFilter, levelFilter, eventIdFilter, messageContains, sinceIso8601, out int count, out string message);
                    Report.Result(ok, message, ("count", count));
                }),
                new MenuItem("QueryByXPath", "Raw XPath query against the log.", () =>
                {
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    string xpath = Prompt.String("XPath", "*");
                    int maxCount = Prompt.Int("Max count", 10);
                    bool ok = eventLog.QueryByXPath(logName, xpath, maxCount, out string json, out string message);
                    Report.Result(ok, message, ("json", json));
                }),
                new MenuItem("QueryRecentEntriesJson", "Same filter dimensions as TryGetMostRecentEntry, returned as a JSON array.", () =>
                {
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    string sourceFilter = Prompt.String("Source filter (blank for none)", "");
                    string levelFilter = Prompt.String("Level filter (blank for none)", "");
                    int eventIdFilter = Prompt.Int("Event ID filter (0 for none)", 0);
                    string messageContains = Prompt.String("Message contains (blank for none)", "");
                    string sinceIso8601 = Prompt.String("Since (ISO 8601, blank for none)", "");
                    int maxCount = Prompt.Int("Max count", 10);
                    bool ok = eventLog.QueryRecentEntriesJson(logName, sourceFilter, levelFilter, eventIdFilter, messageContains, sinceIso8601, maxCount, out string json, out string message);
                    Report.Result(ok, message, ("json", json));
                }),
                new MenuItem("DumpRecentEntriesJson", "No filters - the last N entries as JSON.", () =>
                {
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    int count = Prompt.Int("Count", 10);
                    bool ok = eventLog.DumpRecentEntriesJson(logName, count, out string json, out string message);
                    Report.Result(ok, message, ("json", json));
                }),
                new MenuItem("WaitForEntry", "Found-in-time case: write a matching entry (WriteEntry above) from a delayed action after the wait starts; a pre-existing matching entry must NOT satisfy it.", () =>
                {
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    string sourceFilter = Prompt.String("Source filter (blank for none)", "");
                    string levelFilter = Prompt.String("Level filter (blank for none)", "");
                    int eventIdFilter = Prompt.Int("Event ID filter (0 for none)", 0);
                    string messageContains = Prompt.String("Message contains (blank for none)", "");
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    int pollIntervalMs = Prompt.Int("Poll interval ms", 500);
                    bool ok = eventLog.WaitForEntry(logName, sourceFilter, levelFilter, eventIdFilter, messageContains, timeoutMs, pollIntervalMs,
                        out bool timedOut, out string timeCreatedIso8601, out int eventId, out string entryMessage, out string message);
                    Report.Result(ok, message, ("timedOut", timedOut), ("timeCreatedIso8601", timeCreatedIso8601), ("eventId", eventId), ("entryMessage", entryMessage));
                }),
                new MenuItem("ExportFilteredLog", "Exports matching entries to a .evtx file - read it back with QueryExportedLog below.", () =>
                {
                    string logName = Prompt.String("Log name", "ZZTestLog");
                    string xpath = Prompt.String("XPath", "*");
                    string exportFilePath = Prompt.String("Export file path", "ZZTestLog-export.evtx");
                    bool ok = eventLog.ExportFilteredLog(logName, xpath, exportFilePath, out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("QueryExportedLog", "Query a .evtx file produced by ExportFilteredLog above. A missing/corrupt path returns false + non-null message, distinct from the live-log not-found case.", () =>
                {
                    string evtxFilePath = Prompt.String("Exported .evtx file path", "ZZTestLog-export.evtx");
                    string xpath = Prompt.String("XPath", "*");
                    int maxCount = Prompt.Int("Max count", 10);
                    bool ok = eventLog.QueryExportedLog(evtxFilePath, xpath, maxCount, out string json, out string message);
                    Report.Result(ok, message, ("json", json));
                })
            };
        }
    }
}
