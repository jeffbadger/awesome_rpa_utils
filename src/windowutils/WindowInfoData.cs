using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowAutomation
{
    /// <summary>Shared serializer options for <see cref="WindowInfoData"/>.</summary>
    internal static class WindowJson
    {
        // Enums serialize as their names ("Normal"/"Minimized"/"Maximized") rather than
        // opaque integers, so the JSON is readable straight out of a log or a JSON step.
        public static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        public static string Serialize(IEnumerable<WindowInfoData> windows)
        {
            return JsonSerializer.Serialize(windows, Options);
        }
    }

    /// <summary>
    /// A single top-level window, flattened into Pega-mappable properties. Produced
    /// internally by <see cref="WindowUtils.EnumerateWindowsJson"/> - not itself a public
    /// method return type, since Pega Robot Studio prefers scalars/JSON over complex objects.
    /// </summary>
    public class WindowInfoData
    {
        /// <summary>
        /// The window handle as a number. Windows keeps handles within 32 significant bits
        /// even in 64-bit processes, so this round-trips through a 32-bit integer.
        /// </summary>
        public long Handle { get; internal set; }

        /// <summary>The window's title text (empty if it has none).</summary>
        public string Title { get; internal set; }

        /// <summary>The window's window-class name.</summary>
        public string ClassName { get; internal set; }

        /// <summary>The ID of the process that owns the window.</summary>
        public int ProcessId { get; internal set; }

        /// <summary>Whether the window is visible (see <see cref="WindowUtils.IsWindowVisible"/>).</summary>
        public bool IsVisible { get; internal set; }

        /// <summary>Whether the window accepts input (see <see cref="WindowUtils.IsWindowEnabled"/>).</summary>
        public bool IsEnabled { get; internal set; }

        /// <summary>Normal, Minimized, or Maximized.</summary>
        public WindowDisplayState State { get; internal set; }

        /// <summary>Left edge in screen pixels (about -32000 for a minimized window).</summary>
        public int Left { get; internal set; }

        /// <summary>Top edge in screen pixels (about -32000 for a minimized window).</summary>
        public int Top { get; internal set; }

        /// <summary>Width in pixels.</summary>
        public int Width { get; internal set; }

        /// <summary>Height in pixels.</summary>
        public int Height { get; internal set; }
    }
}
