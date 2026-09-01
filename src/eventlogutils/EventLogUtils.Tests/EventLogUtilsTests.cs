using System;
using System.Collections.Generic;
using System.Text.Json;
using EventLogAutomation;
using Xunit;

namespace EventLogAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for EventLogUtils' input guards and the pure
    /// filter-parsing/XPath-building logic in <see cref="EventLogFilterCore"/> - the paths
    /// that return before any live Windows Event Log call. These run anywhere (including
    /// non-Windows CI shells); live discovery/query/write/wait/export coverage against a
    /// real log is in the repo's TESTING.md.
    /// </summary>
    public class EventLogUtilsTests
    {
        private readonly EventLogUtils _log = new EventLogUtils();

        // --- Discovery guards ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void DoesLogExistSimple_NullOrEmptyName_ReturnsFalseWithMessage(string logName)
        {
            bool exists = _log.DoesLogExistSimple(logName, out string message);

            Assert.False(exists);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DoesLogExist_NullName_ReturnsFalseWithQueryFailed()
        {
            bool exists = _log.DoesLogExist(null, out bool querySucceeded, out string message);

            Assert.False(exists);
            Assert.False(querySucceeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void DoesSourceExistSimple_NullOrEmptyName_ReturnsFalseWithMessage(string sourceName)
        {
            bool exists = _log.DoesSourceExistSimple(sourceName, out string message);

            Assert.False(exists);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetLogNameForSource_NullName_ReturnsFalseWithMessage()
        {
            bool found = _log.TryGetLogNameForSource(null, out string logName, out string message);

            Assert.False(found);
            Assert.Null(logName);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null, "Application")]
        [InlineData("MySource", null)]
        public void CreateEventSourceSimple_NullArgs_ReturnsFalseWithMessage(string sourceName, string logName)
        {
            bool created = _log.CreateEventSourceSimple(sourceName, logName, out string message);

            Assert.False(created);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void CreateEventSource_NullSourceName_ReturnsFalseWithMessage()
        {
            bool created = _log.CreateEventSource(null, "Application", out bool alreadyExisted, out string message);

            Assert.False(created);
            Assert.False(alreadyExisted);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Write guards ---

        [Fact]
        public void WriteEntrySimple_NullSourceName_ReturnsFalseWithMessage()
        {
            bool wrote = _log.WriteEntrySimple(null, "hello", out string message);

            Assert.False(wrote);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WriteEntrySimple_EmptyMessage_ReturnsFalseWithMessage()
        {
            bool wrote = _log.WriteEntrySimple("MySource", "", out string message);

            Assert.False(wrote);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WriteEntry_UndefinedLevel_ReturnsFalseWithMessage()
        {
            bool wrote = _log.WriteEntry("MySource", "hello", (EventLogLevel)999, 0, out string message);

            Assert.False(wrote);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Query guards ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryGetMostRecentEntry_NullOrEmptyLogName_ReturnsFalseWithMessage(string logName)
        {
            bool found = _log.TryGetMostRecentEntry(logName, null, null, 0, null, null,
                out _, out _, out _, out _, out _, out string message);

            Assert.False(found);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetMostRecentEntry_UnknownLevelFilter_ReturnsFalseWithMessage()
        {
            bool found = _log.TryGetMostRecentEntry("Application", null, "NotALevel", 0, null, null,
                out _, out _, out _, out _, out _, out string message);

            Assert.False(found);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetMostRecentEntry_MalformedSince_ReturnsFalseWithMessage()
        {
            bool found = _log.TryGetMostRecentEntry("Application", null, null, 0, null, "not-a-date",
                out _, out _, out _, out _, out _, out string message);

            Assert.False(found);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void CountMatchingEntries_NullOrEmptyLogName_ReturnsFalseWithMessage(string logName)
        {
            bool ok = _log.CountMatchingEntries(logName, null, null, 0, null, null, out _, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void QueryByXPath_NullXPath_ReturnsFalseWithMessage()
        {
            bool ok = _log.QueryByXPath("Application", null, 10, out _, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void QueryByXPath_NegativeMaxCount_ReturnsFalseWithMessage()
        {
            bool ok = _log.QueryByXPath("Application", "*", -1, out _, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void QueryRecentEntriesJson_ZeroMaxCount_ReturnsFalseWithMessage()
        {
            bool ok = _log.QueryRecentEntriesJson("Application", null, null, 0, null, null, 0, out _, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void DumpRecentEntriesJson_ZeroCount_ReturnsFalseWithMessage()
        {
            bool ok = _log.DumpRecentEntriesJson("Application", 0, out _, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Wait guards ---

        [Fact]
        public void WaitForEntrySimple_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool ok = _log.WaitForEntrySimple("Application", null, null, 0, null, -1, 50, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForEntry_NullOrEmptyLogName_ReturnsFalseWithMessage(string logName)
        {
            bool ok = _log.WaitForEntry(logName, null, null, 0, null, 100, 50,
                out bool timedOut, out _, out _, out _, out string message);

            Assert.False(ok);
            Assert.False(timedOut);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Export/import guards ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ExportFilteredLog_NullOrEmptyLogName_ReturnsFalseWithMessage(string logName)
        {
            bool ok = _log.ExportFilteredLog(logName, "*", "out.evtx", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ExportFilteredLog_NullExportPath_ReturnsFalseWithMessage()
        {
            bool ok = _log.ExportFilteredLog("Application", "*", null, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void QueryExportedLog_MissingFile_ReturnsFalseWithMessage()
        {
            bool ok = _log.QueryExportedLog("/no/such/file.evtx", "*", 10, out _, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void QueryExportedLog_NegativeMaxCount_ReturnsFalseWithMessage()
        {
            bool ok = _log.QueryExportedLog(null, "*", -1, out _, out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- EventLogFilterCore pure-logic tests ---

        [Fact]
        public void TryParseLevels_NullOrEmpty_ReturnsNoFilter()
        {
            bool ok = EventLogFilterCore.TryParseLevels(null, out var levels, out string error);

            Assert.True(ok);
            Assert.Null(levels);
            Assert.Null(error);
        }

        [Fact]
        public void TryParseLevels_ValidCsv_ReturnsParsedSet()
        {
            bool ok = EventLogFilterCore.TryParseLevels("error, Warning", out var levels, out string error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Contains(EventLogLevel.Error, levels);
            Assert.Contains(EventLogLevel.Warning, levels);
            Assert.Equal(2, levels.Count);
        }

        [Fact]
        public void TryParseLevels_UnknownValue_ReturnsFalseWithError()
        {
            bool ok = EventLogFilterCore.TryParseLevels("Error,Bogus", out var levels, out string error);

            Assert.False(ok);
            Assert.Null(levels);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void TryParseSince_NullOrEmpty_ReturnsUnbounded()
        {
            bool ok = EventLogFilterCore.TryParseSince("", out var since, out string error);

            Assert.True(ok);
            Assert.Null(since);
            Assert.Null(error);
        }

        [Fact]
        public void TryParseSince_ValidIso8601_ParsesToExpectedInstant()
        {
            bool ok = EventLogFilterCore.TryParseSince("2026-01-01T00:00:00Z", out var since, out string error);

            Assert.True(ok);
            Assert.NotNull(since);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), since.Value);
            Assert.Null(error);
        }

        [Fact]
        public void TryParseSince_Malformed_ReturnsFalseWithError()
        {
            bool ok = EventLogFilterCore.TryParseSince("not-a-date", out var since, out string error);

            Assert.False(ok);
            Assert.Null(since);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void TryBuildXPath_NoFilters_ReturnsWildcard()
        {
            bool ok = EventLogFilterCore.TryBuildXPath(null, null, 0, null, out string xpath, out string error);

            Assert.True(ok);
            Assert.Equal("*", xpath);
            Assert.Null(error);
        }

        [Fact]
        public void TryBuildXPath_EventIdFilter_IncludesEventIdCondition()
        {
            bool ok = EventLogFilterCore.TryBuildXPath(null, null, 1000, null, out string xpath, out string error);

            Assert.True(ok);
            Assert.Contains("EventID=1000", xpath);
        }

        [Fact]
        public void TryBuildXPath_SourceFilter_IncludesProviderCondition()
        {
            bool ok = EventLogFilterCore.TryBuildXPath("MySource", null, 0, null, out string xpath, out string error);

            Assert.True(ok);
            Assert.Contains("Provider", xpath);
            Assert.Contains("MySource", xpath);
        }

        [Fact]
        public void TryBuildXPath_SourceContainingQuote_ReturnsFalseWithError()
        {
            bool ok = EventLogFilterCore.TryBuildXPath("My'Source", null, 0, null, out string xpath, out string error);

            Assert.False(ok);
            Assert.Null(xpath);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void TryBuildXPath_SinceFilter_IncludesTimeCreatedCondition()
        {
            var since = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            bool ok = EventLogFilterCore.TryBuildXPath(null, null, 0, since, out string xpath, out string error);

            Assert.True(ok);
            Assert.Contains("TimeCreated", xpath);
        }

        [Fact]
        public void TryBuildXPath_SuccessAuditLevel_UsesKeywordsBand()
        {
            var levels = new HashSet<EventLogLevel> { EventLogLevel.SuccessAudit };
            bool ok = EventLogFilterCore.TryBuildXPath(null, levels, 0, null, out string xpath, out string error);

            Assert.True(ok);
            Assert.Contains("band(Keywords,", xpath);
        }

        [Fact]
        public void MatchesMessage_EmptyFilter_AlwaysMatches()
        {
            Assert.True(EventLogFilterCore.MatchesMessage(null, null));
            Assert.True(EventLogFilterCore.MatchesMessage("anything", ""));
        }

        [Fact]
        public void MatchesMessage_SubstringCaseInsensitive()
        {
            Assert.True(EventLogFilterCore.MatchesMessage("Something Failed", "failed"));
            Assert.False(EventLogFilterCore.MatchesMessage("Something Failed", "succeeded"));
            Assert.False(EventLogFilterCore.MatchesMessage(null, "failed"));
        }

        // --- EventLogEntryData JSON round-trip ---

        [Fact]
        public void EventLogEntryData_SerializesExpectedShape()
        {
            var entry = new EventLogEntryData
            {
                TimeCreatedIso8601 = "2026-01-01T00:00:00.000Z",
                RecordId = 42,
                EventId = 1000,
                Level = EventLogLevel.Error,
                LevelDisplayName = "Error",
                ProviderName = "MySource",
                LogName = "Application",
                MachineName = "localhost",
                Message = "Something failed",
                TaskDisplayName = null
            };

            string json = JsonSerializer.Serialize(entry, EventLogJson.Options);
            using var doc = JsonDocument.Parse(json);

            Assert.Equal(1000, doc.RootElement.GetProperty("EventId").GetInt32());
            Assert.Equal("Something failed", doc.RootElement.GetProperty("Message").GetString());
            Assert.Equal("MySource", doc.RootElement.GetProperty("ProviderName").GetString());
        }
    }
}
