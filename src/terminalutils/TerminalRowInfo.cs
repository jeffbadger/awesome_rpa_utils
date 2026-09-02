using System.Text.Json;

namespace TerminalAutomation
{
    /// <summary>Shared serializer options for <see cref="TerminalRowInfo"/>.</summary>
    internal static class TerminalJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions();
    }

    /// <summary>
    /// A single row of a captured console screen, flattened into Pega-mappable properties.
    /// Produced internally by <see cref="TerminalUtils.ReadScreenRowsJson"/> - not itself a
    /// public method return type, since Pega Robot Studio prefers scalars/JSON over complex
    /// objects.
    /// </summary>
    public class TerminalRowInfo
    {
        /// <summary>The row's 0-based index within the visible viewport.</summary>
        public int RowIndex { get; internal set; }

        /// <summary>The row's full text, right-trimmed of the buffer's trailing padding spaces.</summary>
        public string Text { get; internal set; }

        /// <summary>
        /// A heuristic column split of <see cref="Text"/> on runs of 2 or more whitespace
        /// characters - not true attribute-based field parsing. See
        /// <see cref="FieldSplitting"/> for the exact rule.
        /// </summary>
        public string[] Fields { get; internal set; }
    }
}
