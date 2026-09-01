using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;

namespace EventLogAutomation
{
    /// <summary>
    /// Shared filter-parsing and XPath-building logic behind every scalar-filtered query
    /// method (<c>TryGetMostRecentEntry</c>, <c>CountMatchingEntries</c>, <c>WaitForEntry</c>,
    /// <c>QueryRecentEntriesJson</c>), so their filter semantics (level CSV parsing,
    /// ISO-8601 parsing, XPath construction) cannot drift between methods. Internal -
    /// not part of the Pega-facing surface.
    /// </summary>
    internal static class EventLogFilterCore
    {
        /// <summary>
        /// Parses a comma-separated list of <see cref="EventLogLevel"/> names. Null/empty/
        /// whitespace-only input means "no filter" (<paramref name="levels"/> is set to
        /// <c>null</c>, not an empty set). Returns <c>false</c> with an error for any
        /// unrecognized token.
        /// </summary>
        internal static bool TryParseLevels(string levelFilterCsv, out HashSet<EventLogLevel> levels, out string error)
        {
            levels = null;
            error = null;
            if (string.IsNullOrWhiteSpace(levelFilterCsv))
                return true;

            var parsed = new HashSet<EventLogLevel>();
            foreach (string token in levelFilterCsv.Split(','))
            {
                string trimmed = token.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (!Enum.TryParse(trimmed, true, out EventLogLevel level))
                {
                    error = $"Unknown event log level '{trimmed}'. Valid values: {string.Join(", ", Enum.GetNames(typeof(EventLogLevel)))}.";
                    return false;
                }
                parsed.Add(level);
            }

            levels = parsed.Count == 0 ? null : parsed;
            return true;
        }

        /// <summary>
        /// Parses an ISO-8601 date/time string. Null/empty/whitespace-only input means
        /// "unbounded" (<paramref name="since"/> is <c>null</c>). Returns <c>false</c> with
        /// an error for a malformed value.
        /// </summary>
        internal static bool TryParseSince(string sinceIso8601, out DateTimeOffset? since, out string error)
        {
            since = null;
            error = null;
            if (string.IsNullOrWhiteSpace(sinceIso8601))
                return true;

            if (!DateTimeOffset.TryParse(sinceIso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            {
                error = $"'{sinceIso8601}' is not a valid ISO-8601 date/time.";
                return false;
            }

            since = parsed;
            return true;
        }

        /// <summary>
        /// Builds an <see cref="EventLogQuery"/>-compatible XPath expression from scalar
        /// filter inputs. A <paramref name="sourceFilter"/> is a comma-separated list of
        /// provider/source names (OR'd together); <paramref name="eventIdFilter"/> of 0
        /// means "any event id"; <paramref name="levels"/> of <c>null</c>/empty means "any
        /// level". <c>messageContains</c> is deliberately not handled here - the rendered
        /// message is not queryable via XPath, so callers post-filter it themselves via
        /// <see cref="MatchesMessage"/> after formatting each candidate record.
        /// </summary>
        internal static bool TryBuildXPath(string sourceFilter, HashSet<EventLogLevel> levels, int eventIdFilter, DateTimeOffset? since, out string xpath, out string error)
        {
            xpath = null;
            error = null;
            var conditions = new List<string>();

            if (eventIdFilter != 0)
                conditions.Add($"(EventID={eventIdFilter})");

            if (!string.IsNullOrWhiteSpace(sourceFilter))
            {
                string[] sources = sourceFilter.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();
                if (sources.Any(s => s.Contains('\'')))
                {
                    error = "Source names in sourceFilter must not contain a single quote character.";
                    return false;
                }
                if (sources.Length > 0)
                    conditions.Add("(" + string.Join(" or ", sources.Select(s => $"Provider[@Name='{s}']")) + ")");
            }

            if (since.HasValue)
                conditions.Add($"TimeCreated[@SystemTime>='{since.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}']");

            if (levels != null && levels.Count > 0)
                conditions.Add("(" + string.Join(" or ", levels.Select(LevelCondition)) + ")");

            xpath = conditions.Count == 0 ? "*" : "*[System[" + string.Join(" and ", conditions) + "]]";
            return true;
        }

        /// <summary>Case-insensitive substring check against a formatted event message. Null/empty <paramref name="messageContains"/> always matches.</summary>
        internal static bool MatchesMessage(string message, string messageContains)
        {
            if (string.IsNullOrEmpty(messageContains))
                return true;
            return message != null && message.IndexOf(messageContains, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string LevelCondition(EventLogLevel level)
        {
            switch (level)
            {
                case EventLogLevel.SuccessAudit:
                    return $"band(Keywords,{(long)StandardEventKeywords.AuditSuccess})";
                case EventLogLevel.FailureAudit:
                    return $"band(Keywords,{(long)StandardEventKeywords.AuditFailure})";
                default:
                    return $"Level={(int)ToStandardEventLevel(level)}";
            }
        }

        private static StandardEventLevel ToStandardEventLevel(EventLogLevel level)
        {
            switch (level)
            {
                case EventLogLevel.Critical: return StandardEventLevel.Critical;
                case EventLogLevel.Error: return StandardEventLevel.Error;
                case EventLogLevel.Warning: return StandardEventLevel.Warning;
                case EventLogLevel.Informational: return StandardEventLevel.Informational;
                case EventLogLevel.Verbose: return StandardEventLevel.Verbose;
                default: return StandardEventLevel.LogAlways;
            }
        }
    }
}
