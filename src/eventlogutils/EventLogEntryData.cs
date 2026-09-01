using System.Text.Json;

namespace EventLogAutomation
{
    /// <summary>Shared serializer options for <see cref="EventLogEntryData"/>.</summary>
    internal static class EventLogJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions();
    }

    /// <summary>
    /// A single Windows Event Log record, flattened into Pega-mappable properties.
    /// Produced internally by the query methods and serialized to JSON by the
    /// <c>*Json</c> methods - not itself a public method return type, since Pega Robot
    /// Studio prefers scalars/JSON over complex objects.
    /// </summary>
    public class EventLogEntryData
    {
        /// <summary>When the event was recorded, as a round-trippable ISO-8601 UTC string.</summary>
        public string TimeCreatedIso8601 { get; internal set; }

        /// <summary>The log's per-record sequence number.</summary>
        public long RecordId { get; internal set; }

        /// <summary>The provider-defined event id.</summary>
        public int EventId { get; internal set; }

        /// <summary>The repository-owned severity level.</summary>
        public EventLogLevel Level { get; internal set; }

        /// <summary>The level's human-readable display name, e.g. "Error".</summary>
        public string LevelDisplayName { get; internal set; }

        /// <summary>The event source/provider name.</summary>
        public string ProviderName { get; internal set; }

        /// <summary>The log this record came from, e.g. "Application".</summary>
        public string LogName { get; internal set; }

        /// <summary>The machine that generated the record.</summary>
        public string MachineName { get; internal set; }

        /// <summary>The formatted event message, or null if it could not be resolved (e.g. a missing provider manifest).</summary>
        public string Message { get; internal set; }

        /// <summary>The task's human-readable display name, or null.</summary>
        public string TaskDisplayName { get; internal set; }
    }
}
