using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinEventAutomation
{
    /// <summary>Shared serializer options for <see cref="WinEventData"/>.</summary>
    internal static class WinEventJson
    {
        public static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new IntPtrJsonConverter());
            return options;
        }
    }

    internal sealed class IntPtrJsonConverter : JsonConverter<IntPtr>
    {
        public override IntPtr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new IntPtr(reader.GetInt64());
        }

        public override void Write(Utf8JsonWriter writer, IntPtr value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.ToInt64());
        }
    }

    /// <summary>
    /// A single WinEvent, flattened into Pega-mappable properties. Produced by the
    /// event engine and delivered to subscriptions and waiters — each consumer
    /// receives its own copy, but treat the object as read-only. All properties are
    /// plain strings/numbers/handles so the object maps directly onto Pega properties;
    /// use <see cref="ToJson"/> for anything structured.
    /// </summary>
    public class WinEventData
    {
        /// <summary>Unique id for this event (GUID, no dashes).</summary>
        public string EventId { get; internal set; }

        /// <summary>Human-readable event name, e.g. "WindowCreated", "DialogAppeared".</summary>
        public string Category { get; internal set; }

        /// <summary><see cref="DateTime.UtcNow"/> ticks at capture time.</summary>
        public long Timestamp { get; internal set; }

        /// <summary>
        /// Window handle (zero if none), represented as an <see cref="IntPtr"/> so
        /// it can be passed directly to WindowUtils/UIAutomationUtils methods.
        /// </summary>
        public IntPtr Hwnd { get; internal set; }

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
        public WinEventData Clone()
        {
            return new WinEventData
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
                return JsonSerializer.Serialize(this, WinEventJson.Options);
            }
            catch
            {
                return "{}";
            }
        }
    }
}
