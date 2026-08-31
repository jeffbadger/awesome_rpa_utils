using System;
using System.Text.Json;

namespace EventAutomation
{
    /// <summary>Shared serializer options for <see cref="EventData"/>.</summary>
    internal static class EventJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions();
    }

    /// <summary>
    /// A single WinEvent, flattened into Pega-mappable properties. Produced by the
    /// event engine and delivered to subscriptions and waiters — each consumer
    /// receives its own copy, but treat the object as read-only. All properties are
    /// plain strings/numbers so the object maps directly onto Pega properties;
    /// use <see cref="ToJson"/> for anything structured.
    /// </summary>
    public class EventData
    {
        /// <summary>Unique id for this event (GUID, no dashes).</summary>
        public string EventId { get; internal set; }

        /// <summary>Human-readable event name, e.g. "WindowCreated", "DialogAppeared".</summary>
        public string Category { get; internal set; }

        /// <summary><see cref="DateTime.UtcNow"/> ticks at capture time.</summary>
        public long Timestamp { get; internal set; }

        /// <summary>
        /// Window handle as a signed 64-bit value (0 if none), safe for both 32- and
        /// 64-bit handles. Reconstruct an <see cref="IntPtr"/> for WindowUtils/
        /// UIAutomationUtils methods with <c>new IntPtr(Hwnd)</c>.
        /// </summary>
        public long Hwnd { get; internal set; }

        /// <summary>Process image name (e.g. "notepad"), or null if unknown.</summary>
        public string ProcessName { get; internal set; }

        /// <summary>Owning process id.</summary>
        public uint ProcessId { get; internal set; }

        /// <summary>Window class name (e.g. "#32770" for a dialog), or null.</summary>
        public string ClassName { get; internal set; }

        /// <summary>Window title at capture time, or null.</summary>
        public string Title { get; internal set; }

        /// <summary>Normalized state for state-change events, e.g. "Visible", "Minimized".</summary>
        public string State { get; internal set; }

        /// <summary>Returns a shallow copy with the same property values.</summary>
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
