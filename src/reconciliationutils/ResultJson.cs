using System.IO;
using System.Text;
using System.Text.Json;

namespace ReconciliationAutomation
{
    /// <summary>Writes the JSON forms of a summary and of a single result. Value fragments are already valid JSON, so they are written as they are.</summary>
    internal static class ResultJson
    {
        internal static string Summary(ReconciliationSummary s)
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartObject();
                    w.WriteNumber("leftRowCount", s.LeftRowCount);
                    w.WriteNumber("rightRowCount", s.RightRowCount);
                    w.WriteNumber("matchedPairCount", s.MatchedPairCount);
                    w.WriteNumber("differentPairCount", s.DifferentPairCount);
                    w.WriteNumber("invalidPairCount", s.InvalidPairCount);
                    w.WriteNumber("onlyLeftCount", s.OnlyLeftCount);
                    w.WriteNumber("onlyRightCount", s.OnlyRightCount);
                    w.WriteNumber("invalidLeftRowCount", s.InvalidLeftRowCount);
                    w.WriteNumber("invalidRightRowCount", s.InvalidRightRowCount);
                    w.WriteNumber("ambiguousKeyCount", s.AmbiguousKeyCount);
                    w.WriteNumber("ambiguousLeftRowCount", s.AmbiguousLeftRowCount);
                    w.WriteNumber("ambiguousRightRowCount", s.AmbiguousRightRowCount);
                    w.WriteNumber("resultCount", s.ResultCount);
                    w.WriteNumber("exceptionCount", s.ExceptionCount);
                    w.WriteNumber("differenceCount", s.DifferenceCount);
                    w.WriteBoolean("allMatched", s.AllMatched);
                    w.WriteBoolean("bothInputsEmpty", s.BothInputsEmpty);
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static string Result(ReconciliationResult r)
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartObject();
                    w.WriteString("id", r.Id);
                    w.WriteString("kind", r.Kind.ToString());
                    if (r.NormalizedKey == null) w.WriteNull("key"); else { w.WritePropertyName("key"); w.WriteRawValue(ReconciliationKey.ToDisplayJson(r.NormalizedKey), skipInputValidation: false); }
                    w.WriteString("reasonCode", r.ReasonCode);
                    w.WriteString("explanation", r.Explanation);
                    if (r.MappingName != null) w.WriteString("mappingName", r.MappingName);
                    if (r.Pointer != null) w.WriteString("pointer", r.Pointer);

                    w.WriteStartArray("members");
                    foreach (ResultMember m in r.Members)
                    {
                        w.WriteStartObject();
                        w.WriteString("side", m.Side);
                        w.WriteNumber("row", m.Row);
                        if (m.OriginalKey == null) w.WriteNull("originalKey");
                        else
                        {
                            w.WriteStartArray("originalKey");
                            foreach (string part in m.OriginalKey) w.WriteStringValue(part);
                            w.WriteEndArray();
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();

                    w.WriteStartArray("differences");
                    foreach (ComparisonOutcome d in r.Differences)
                    {
                        w.WriteStartObject();
                        w.WriteString("rule", d.RuleName);
                        w.WriteString("ruleKind", d.Kind.ToString());
                        w.WriteString("state", d.State.ToString());
                        w.WriteString("reasonCode", d.ReasonCode);
                        w.WriteString("explanation", d.Explanation);
                        w.WriteBoolean("leftPresent", d.LeftPresent);
                        w.WriteBoolean("rightPresent", d.RightPresent);
                        Fragment(w, "leftValue", d.LeftValueJson);
                        Fragment(w, "rightValue", d.RightValueJson);
                        w.WriteString("leftInterpreted", d.LeftInterpreted);
                        w.WriteString("rightInterpreted", d.RightInterpreted);
                        w.WriteString("delta", d.Delta);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void Fragment(Utf8JsonWriter w, string name, string json)
        {
            if (json == null) w.WriteNull(name);               // a missing field
            else { w.WritePropertyName(name); w.WriteRawValue(json, skipInputValidation: false); }
        }
    }
}
