using System.Text.Json;

namespace ReconciliationAutomation
{
    /// <summary>Reads fields from one row of a parsed JSON array.</summary>
    internal sealed class JsonRowReader : IRowReader
    {
        private readonly JsonElement row;

        internal JsonRowReader(JsonElement row) { this.row = row; }

        public bool IsObject => row.ValueKind == JsonValueKind.Object;

        public FieldValue Read(string[] pointerSegments)
        {
            if (!IsObject) return FieldValue.Missing;

            JsonElement current = row;
            for (int i = 0; i < pointerSegments.Length; i++)
            {
                // Only an object can be walked into. An array met on the way is unsupported data; a null or a scalar means the path is missing.
                if (current.ValueKind == JsonValueKind.Array) return FieldValue.UnsupportedBecause("an array on the path");
                if (current.ValueKind != JsonValueKind.Object) return FieldValue.Missing;
                // TryGetProperty is ordinal and case-sensitive, and the input scan has already rejected duplicate names.
                if (!current.TryGetProperty(pointerSegments[i], out current)) return FieldValue.Missing;
            }
            return Classify(current);
        }

        private static FieldValue Classify(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    try { return new FieldValue(FieldKind.String, value.GetString()); }
                    catch (System.InvalidOperationException) { return FieldValue.UnsupportedBecause("text that is not valid"); } // an escaped lone surrogate: legal JSON, not decodable text
                case JsonValueKind.Number:
                    string token = value.GetRawText();
                    return new FieldValue(IsIntegerToken(token) ? FieldKind.Integer : FieldKind.Number, token);
                case JsonValueKind.True:
                    return new FieldValue(FieldKind.Boolean, "true");
                case JsonValueKind.False:
                    return new FieldValue(FieldKind.Boolean, "false");
                case JsonValueKind.Null:
                    return FieldValue.Null;
                default:
                    return FieldValue.UnsupportedBecause(value.ValueKind == JsonValueKind.Array ? "an array" : "an object"); // at the leaf
            }
        }

        /// <summary>True for <c>-?(0|[1-9][0-9]*)</c>. JSON's own grammar already rules out leading zeros and a leading '+'.</summary>
        internal static bool IsIntegerToken(string token)
        {
            int i = token.Length > 0 && token[0] == '-' ? 1 : 0;
            if (i >= token.Length) return false;
            for (; i < token.Length; i++)
                if (token[i] < '0' || token[i] > '9') return false;
            return true;
        }
    }
}
