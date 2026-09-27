using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TextExtractAutomation
{
    /// <summary>The outcome for one field of a template. Immutable once published.</summary>
    internal sealed class FieldResult
    {
        internal string Name;
        internal bool Required;
        internal bool Found;
        internal string Value;          // normalized; null unless found
        internal string Raw;            // the text as it appeared (also for an invalid value); null when there was none
        internal string Reason;         // null when found; MissingLabel, MissingValue, InvalidValue, AmbiguousValue, PatternTimeout
        internal string Explanation;    // why, never quoting the text; null when found
        internal int LineNumber;        // the value's line, else the label's line, else 0
        internal string Label;          // the label alternative that matched (label fields), or null
        internal bool LabelSlipped;     // the label needed an OCR confusion to match
        internal string Currency;       // Amount fields: the symbol or code next to the amount, or null
        internal int Occurrences;       // how many times the label (or pattern) was found
    }

    /// <summary>The published outcome of one extraction: a result per field in template order, and the counts.</summary>
    internal sealed class ExtractionSnapshot
    {
        internal ExtractionSnapshot(IList<FieldResult> fields)
        {
            Fields = new ReadOnlyCollection<FieldResult>(fields);
            FoundCount = fields.Count(f => f.Found);
            MissingRequiredCount = fields.Count(f => f.Required && !f.Found);
        }

        internal IReadOnlyList<FieldResult> Fields { get; }
        internal int FoundCount { get; }
        internal int MissingRequiredCount { get; }

        internal FieldResult Find(string name) => Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs a template over a text: finds every label or pattern occurrence, reads each one's typed value, then applies the field's occurrence policy.</summary>
    internal static class Extraction
    {
        /// <summary>One occurrence of a field (a label or pattern match) with its converted value.</summary>
        private sealed class Hit
        {
            internal Converted Value;
            internal string Raw;             // the captured part as it appeared, or the whole span when nothing valid was captured
            internal int LineNumber;
            internal int LabelLine;
            internal string Label;
            internal bool Slipped;
        }

        internal static ExtractionSnapshot Run(Template template, string text)
        {
            List<TextLine> lines = TextLines.Split(text);
            List<LabelCandidate> candidates = LabelLocator.Locate(template, text, lines);
            var results = new List<FieldResult>();
            for (int f = 0; f < template.Fields.Count; f++)
            {
                FieldDef field = template.Fields[f];
                bool timedOut = false;
                List<Hit> occurrences = field.Kind == FieldKind.Label
                    ? candidates.Where(c => c.FieldIndex == f).Select(c => FromLabel(field, text, c)).ToList()
                    : FromPattern(field, text, lines, out timedOut);
                results.Add(Decide(field, occurrences, timedOut));
            }
            return new ExtractionSnapshot(results);
        }

        private static Hit FromLabel(FieldDef field, string original, LabelCandidate c)
        {
            Converted value = ValueConverter.Convert(field, c.Value);
            string raw = c.Raw.Length == 0 ? null : c.Raw;
            if (value.Ok && c.SpanLine != null) raw = c.SpanLine.Raw(original, c.SpanStart + value.Start, c.SpanStart + value.End);
            return new Hit { Value = value, Raw = raw, LineNumber = c.ValueLine != 0 ? c.ValueLine : c.LabelLine, LabelLine = c.LabelLine, Label = c.Label, Slipped = c.Slipped };
        }

        /// <summary>
        /// Every match of the field's pattern in the original text. The value group is normalized like the rest of the text (so the types read it the
        /// same way) and reported as it appeared. A pattern that runs past its time limit stops the search and is reported as PatternTimeout.
        /// </summary>
        private static List<Hit> FromPattern(FieldDef field, string text, List<TextLine> lines, out bool timedOut)
        {
            timedOut = false;
            var list = new List<Hit>();
            try
            {
                foreach (Match m in field.CompiledPattern.Matches(text))
                {
                    Group g = m.Groups["value"];
                    if (!g.Success) continue;
                    string normalized = string.Join(" ", TextLines.Split(g.Value).Select(l => l.Text));
                    Converted value = ValueConverter.Convert(field, normalized);
                    list.Add(new Hit { Value = value, Raw = g.Length == 0 ? null : g.Value, LineNumber = LineOf(text, g.Index), LabelLine = LineOf(text, g.Index) });
                }
            }
            catch (RegexMatchTimeoutException)
            {
                timedOut = true;
            }
            return list;
        }

        /// <summary>The 1-based line of an offset, counting line breaks the same way <see cref="TextLines"/> does (CRLF once).</summary>
        internal static int LineOf(string text, int offset)
        {
            int line = 1;
            for (int i = 0; i < offset && i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n' && i + 1 < offset) i++; line++; }
                else if (c == '\n' || c == '\u0085' || c == '\u2028' || c == '\u2029' || c == '\u000B' || c == '\u000C') line++;
            }
            return line;
        }

        /// <summary>
        /// Applies the occurrence policy. First and Last take that occurrence as it is. RequireUnique accepts a label found more than once only when every
        /// valid occurrence has the same value (a total repeated in a header and a footer); two different values are AmbiguousValue, never a guess.
        /// </summary>
        private static FieldResult Decide(FieldDef field, List<Hit> occurrences, bool timedOut)
        {
            var result = new FieldResult { Name = field.Name, Required = field.Required, Occurrences = occurrences.Count };
            if (timedOut)
            {
                result.Reason = "PatternTimeout";
                result.Explanation = "the pattern took longer than " + TemplateLimits.PatternTimeoutMilliseconds + " ms and was stopped; simplify it (nested repeats such as (a+)+ can take very long)";
                return result;
            }
            if (occurrences.Count == 0)
            {
                result.Reason = "MissingLabel";
                result.Explanation = field.Kind == FieldKind.Label ? "none of the field's labels was found" : "the pattern did not match";
                return result;
            }

            Hit chosen;
            if (field.Occurrence == TextExtractAutomation.Occurrence.First) chosen = occurrences[0];
            else if (field.Occurrence == TextExtractAutomation.Occurrence.Last) chosen = occurrences[occurrences.Count - 1];
            else
            {
                var valid = occurrences.Where(o => o.Value.Ok).ToList();
                int distinct = valid.Select(o => o.Value.Value).Distinct(StringComparer.Ordinal).Count();
                if (distinct > 1)
                {
                    result.Reason = "AmbiguousValue";
                    result.Explanation = "the field was found " + occurrences.Count + " times with " + distinct + " different values; set its occurrence to First or Last, or use a more specific label";
                    result.LineNumber = valid[0].LineNumber;
                    result.Label = valid[0].Label;
                    return result;
                }
                chosen = valid.Count > 0 ? valid[0] : occurrences[0];
            }

            result.LineNumber = chosen.LineNumber;
            result.Label = chosen.Label;
            result.LabelSlipped = chosen.Slipped;
            result.Raw = chosen.Raw;
            if (chosen.Value.Ok)
            {
                result.Found = true;
                result.Value = chosen.Value.Value;
                result.Currency = chosen.Value.Currency;
            }
            else
            {
                result.Reason = chosen.Value.Reason;
                result.Explanation = chosen.Value.Reason == "MissingValue"
                    ? (field.Kind == FieldKind.Label ? "the label was found but there is no text where its value should be (" + field.Position + ")" : "the pattern matched an empty value")
                    : "the value is not a valid " + field.Type + ": " + chosen.Value.Detail;
                if (chosen.Value.Reason == "MissingValue") result.LineNumber = chosen.LabelLine;
            }
            return result;
        }

        internal static string ToJson(ExtractionSnapshot snapshot)
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) }))
                {
                    w.WriteStartObject();
                    w.WriteNumber("foundCount", snapshot.FoundCount);
                    w.WriteNumber("missingRequiredCount", snapshot.MissingRequiredCount);
                    w.WriteStartArray("fields");
                    foreach (FieldResult r in snapshot.Fields)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", r.Name);
                        w.WriteBoolean("found", r.Found);
                        w.WriteBoolean("required", r.Required);
                        w.WriteString("value", r.Value);
                        w.WriteString("raw", r.Raw);
                        w.WriteString("reason", r.Reason);
                        w.WriteString("explanation", r.Explanation);
                        w.WriteNumber("lineNumber", r.LineNumber);
                        w.WriteString("label", r.Label);
                        w.WriteBoolean("labelSlipped", r.LabelSlipped);
                        w.WriteString("currency", r.Currency);
                        w.WriteNumber("occurrences", r.Occurrences);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
