using System;
using System.Text.Json;

namespace EventAutomation
{
    /// <summary>Shared serializer options: EventData uses public fields, which STJ omits by default.</summary>
    internal static class EventJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
    }

    /// <summary>
    /// A single WinEvent, flattened into Pega-mappable fields. Produced by the
    /// event engine and delivered to subscriptions and waiters — each consumer
    /// receives its own copy, but treat the object as read-only. All fields are
    /// plain strings/numbers so the object maps directly onto Pega properties;
    /// use <see cref="ToJson"/> for anything structured.
    /// </summary>
    public class EventData
    {
        /// <summary>Unique id for this event (GUID, no dashes).</summary>
        public string EventId;

        /// <summary>Human-readable event name, e.g. "WindowCreated", "DialogAppeared".</summary>
        public string Category;

        /// <summary><see cref="DateTime.UtcNow"/> ticks at capture time.</summary>
        public long Timestamp;

        /// <summary>Window handle as a 32-bit value (0 if none).</summary>
        public uint Hwnd;

        /// <summary>Process image name (e.g. "notepad"), or null if unknown.</summary>
        public string ProcessName;

        /// <summary>Owning process id.</summary>
        public uint ProcessId;

        /// <summary>Window class name (e.g. "#32770" for a dialog), or null.</summary>
        public string ClassName;

        /// <summary>Window title at capture time, or null.</summary>
        public string Title;

        /// <summary>Normalized state for state-change events, e.g. "Visible", "Minimized".</summary>
        public string State;

        /// <summary>Returns a shallow copy with the same field values.</summary>
        public EventData Clone()
        {
            return new EventData
            {
                EventId = EventId,
                Category = Category,
                Timestamp = Timestamp,
                Hwnd = Hwnd,
                ProcessName = ProcessName,
                ProcessId = ProcessId,
                ClassName = ClassName,
                Title = Title,
                State = State
            };
        }

        /// <summary>Serializes this event to a compact JSON object.</summary>
        public string ToJson()
        {
            try
            {
                return JsonSerializer.Serialize(this, EventJson.Options);
            }
            catch
            {
                return "{}";
            }
        }
    }
}
