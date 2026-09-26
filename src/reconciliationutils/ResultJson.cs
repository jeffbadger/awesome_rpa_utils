using System.IO;
using System.Text;
using System.Text.Json;

namespace ReconciliationAutomation
{
    /// <summary>Writes the JSON forms of a summary and of a single result. Value fragments are already valid JSON, so they are written as they are.</summary>
    internal static class ResultJson
    {
        /// <summary>Thrown by the bounded writers when the output would pass the cap. Internal control flow: it never leaves the export call.</summary>
        internal sealed class OutputLimitExceededException : System.Exception { }

        private static void Check(Utf8JsonWriter w, long byteCap)
        {
            if (w.BytesCommitted + w.BytesPending > byteCap) throw new OutputLimitExceededException();
        }

        internal static string Summary(ReconciliationSummary s)
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream)) WriteSummary(w, s);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static string Result(ReconciliationResult r)
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream)) WriteResult(w, r, long.MaxValue);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static void WriteSummary(Utf8JsonWriter w, ReconciliationSummary s)
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

        /// <summary>Writes one result as an object. Throws <see cref="OutputLimitExceededException"/> as soon as the writer holds more than <paramref name="byteCap"/> bytes.</summary>
        internal static void WriteResult(Utf8JsonWriter w, ReconciliationResult r, long byteCap)
        {
        w.WriteStartObject();
        Str(w, byteCap, "id", r.Id);
        Str(w, byteCap, "kind", r.Kind.ToString());
        if (r.NormalizedKey == null) w.WriteNull("key"); else { string keyJson = ReconciliationKey.ToDisplayJson(r.NormalizedKey); Room(w, byteCap, keyJson); w.WritePropertyName("key"); w.WriteRawValue(keyJson, skipInputValidation: false); }
        Str(w, byteCap, "reasonCode", r.ReasonCode);
        Str(w, byteCap, "explanation", r.Explanation);
        if (r.MappingName != null) Str(w, byteCap, "mappingName", r.MappingName);
        if (r.Pointer != null) Str(w, byteCap, "pointer", r.Pointer);

        w.WriteStartArray("members");
        foreach (ResultMember m in r.Members)
        {
            w.WriteStartObject();
            Str(w, byteCap, "side", m.Side);
            w.WriteNumber("row", m.Row);
            if (m.OriginalKey == null) w.WriteNull("originalKey");
            else
            {
                w.WriteStartArray("originalKey");
                foreach (string part in m.OriginalKey) { Room(w, byteCap, part); w.WriteStringValue(part); }
                w.WriteEndArray();
            }
            w.WriteEndObject();
            Check(w, byteCap);
        }
        w.WriteEndArray();

        w.WriteStartArray("differences");
        foreach (ComparisonOutcome d in r.Differences)
        {
            w.WriteStartObject();
            Str(w, byteCap, "rule", d.RuleName);
            Str(w, byteCap, "ruleKind", d.Kind.ToString());
            Str(w, byteCap, "state", d.State.ToString());
            Str(w, byteCap, "reasonCode", d.ReasonCode);
            Str(w, byteCap, "explanation", d.Explanation);
            w.WriteBoolean("leftPresent", d.LeftPresent);
            w.WriteBoolean("rightPresent", d.RightPresent);
            Fragment(w, byteCap, "leftValue", d.LeftValueJson);
            Fragment(w, byteCap, "rightValue", d.RightValueJson);
            if (d.Kind == RuleKind.Money)
            {
                Fragment(w, byteCap, "leftCurrency", d.LeftCurrencyJson);
                Fragment(w, byteCap, "rightCurrency", d.RightCurrencyJson);
            }
            Str(w, byteCap, "leftInterpreted", d.LeftInterpreted);
            Str(w, byteCap, "rightInterpreted", d.RightInterpreted);
            Str(w, byteCap, "delta", d.Delta);
            w.WriteEndObject();
            Check(w, byteCap);
        }
        w.WriteEndArray();
        w.WriteEndObject();
        }

        /// <summary>
        /// Refuses, before it is written, a string or fragment that cannot fit: it needs at least one byte per character, so if even that passes the cap the
        /// output is already over and nothing more is built. Larger overshoots than one value's encoded size are therefore impossible.
        /// </summary>
        internal static void Room(Utf8JsonWriter w, long byteCap, string text)
        {
            if (text != null && byteCap != long.MaxValue && w.BytesCommitted + w.BytesPending + text.Length > byteCap) throw new OutputLimitExceededException();
        }

        private static void Str(Utf8JsonWriter w, long byteCap, string name, string value)
        {
            Room(w, byteCap, value);
            w.WriteString(name, value);
        }

        private static void Fragment(Utf8JsonWriter w, long byteCap, string name, string json)
        {
            if (json == null) { w.WriteNull(name); return; }             // a missing field
            Room(w, byteCap, json);
            w.WritePropertyName(name);
            w.WriteRawValue(json, skipInputValidation: false);
        }
    }
}
