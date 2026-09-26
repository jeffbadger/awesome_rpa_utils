using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ReconciliationAutomation
{
    /// <summary>One problem found in a definition: where, a stable code, and an explanation.</summary>
    internal sealed class Finding
    {
        internal Finding(string path, string code, string message)
        {
            Path = path;
            Code = code;
            Message = message;
        }

        internal string Path { get; }
        internal string Code { get; }
        internal string Message { get; }

        /// <summary>The finding as one sentence, for a public failure message.</summary>
        internal string Format() => Path + ": " + Message + " (" + Code + ").";
    }

    internal enum RuleKind
    {
        Text,
        Decimal,
        /// <summary>Release 2: JSON true/false compared directly.</summary>
        Boolean,
        /// <summary>Release 2: an exact amount plus a required currency on each side; different currencies never compare amounts.</summary>
        Money,
        /// <summary>Release 2: a date with an explicit format per side, compared in whole days.</summary>
        CalendarDate,
        /// <summary>Release 2: an ISO instant with an explicit offset or Z, compared as a UTC instant in whole seconds.</summary>
        Instant
    }

    /// <summary>One part of the business key.</summary>
    internal sealed class KeyMappingDef
    {
        internal string Name;
        internal string LeftPointer;
        internal string RightPointer;
        internal string[] LeftSegments;
        internal string[] RightSegments;
        internal bool Trim;
        internal bool IgnoreCase;
    }

    /// <summary>One field comparison. Only the options applicable to <see cref="Kind"/> are meaningful.</summary>
    internal sealed class ComparisonDef
    {
        internal string Name;
        internal RuleKind Kind;
        internal string LeftPointer;
        internal string RightPointer;
        internal string[] LeftSegments;
        internal string[] RightSegments;
        internal string LeftCurrencyPointer;      // Money
        internal string RightCurrencyPointer;     // Money
        internal string[] LeftCurrencySegments;   // Money
        internal string[] RightCurrencySegments;  // Money
        internal string LeftFormat = DateCore.DefaultFormat;    // CalendarDate
        internal string RightFormat = DateCore.DefaultFormat;   // CalendarDate
        internal int DateTolerance;               // CalendarDate: days; Instant: seconds
        internal bool Trim;                       // Text
        internal bool IgnoreCase;                 // Text
        internal string AbsoluteTolerance = "0";  // Decimal and Money (invariant decimal text)

        private sealed class ParsedTolerance
        {
            internal ParsedTolerance(string text)
            {
                Text = text;
                ExactDecimal.TryParseText(text, out ExactDecimal parsed, out string error);
                Value = parsed;
                Error = error;
            }

            internal readonly string Text;
            internal readonly ExactDecimal Value;
            internal readonly string Error;
        }

        // volatile: the object is fully built (readonly fields, set in its constructor) before this reference is published, and a
        // reader that sees the reference is guaranteed to see those fields, on every processor architecture.
        private volatile ParsedTolerance parsedTolerance;

        /// <summary>
        /// The tolerance as an exact number, parsed once and remembered (a run compares every pair against it). Keyed on the text, so
        /// changing <see cref="AbsoluteTolerance"/> is honored. Safe for concurrent readers: the remembered result is one immutable object
        /// published through a volatile reference, so a reader sees either the previous result or the complete new one, never a partly
        /// written one. A tolerance is validated when a rule is added, so a failure here means the rule was built some other way; it is
        /// reported, never thrown.
        /// </summary>
        internal bool TryGetTolerance(out ExactDecimal value, out string error)
        {
            ParsedTolerance parsed = parsedTolerance;
            string text = AbsoluteTolerance;
            if (parsed == null || parsed.Text != text)
            {
                parsed = new ParsedTolerance(text);
                parsedTolerance = parsed;
            }
            value = parsed.Value;
            error = parsed.Error;
            return parsed.Error == null;
        }
        internal ComparisonNullPolicy NullPolicy = ComparisonNullPolicy.RequireValue;
    }

    /// <summary>The whole configuration of a reconciliation. Copied before every change, so a rejected change leaves the current definition untouched.</summary>
    internal sealed class ReconciliationDefinition
    {
        internal const int SchemaVersion = 1;
        internal const int MaxKeys = 16;
        internal const int MaxComparisons = 128;
        internal const int MaxNameLength = 128;
        internal const int MaxToleranceLength = 256;
        internal const int MaxDefinitionJsonCharacters = 256000;

        internal List<KeyMappingDef> Keys = new List<KeyMappingDef>();
        internal List<ComparisonDef> Comparisons = new List<ComparisonDef>();
        internal ReconciliationLimits Limits = new ReconciliationLimits();

        internal ReconciliationDefinition Clone() => new ReconciliationDefinition
        {
            Keys = new List<KeyMappingDef>(Keys),           // the items are never mutated after they are added
            Comparisons = new List<ComparisonDef>(Comparisons),
            Limits = Limits.Clone()
        };

        // ------------------------------------------------------------------ shared rules (builders and JSON loading)

        internal static Finding CheckName(string name, IEnumerable<string> taken, string path)
        {
            if (string.IsNullOrWhiteSpace(name)) return new Finding(path, "InvalidName", "a name is required");
            if (name.Length > MaxNameLength) return new Finding(path, "InvalidName", "the name is longer than " + MaxNameLength + " characters");
            if (TextCheck.HasUnpairedSurrogate(name)) return new Finding(path, "InvalidName", "the name contains text that is not valid (an unpaired surrogate character)");
            if (taken.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
                return new Finding(path, "DuplicateName", "the name '" + name + "' is already used; names are unique across keys and comparisons, ignoring case");
            return null;
        }

        internal static Finding CheckPointer(string pointer, string path, out string[] segments)
        {
            segments = null;
            // A pointer with an unpaired surrogate could never match a property and would make the canonical JSON unwritable.
            if (pointer != null && TextCheck.HasUnpairedSurrogate(pointer))
                return new Finding(path, "InvalidPointer", "the pointer contains text that is not valid (an unpaired surrogate character)");
            if (!JsonPointer.TryParse(pointer, out segments, out string error)) return new Finding(path, "InvalidPointer", error);
            return null;
        }

        /// <summary>A tolerance is invariant decimal text, non-negative: digits with an optional fraction (for example 0 or 0.01). No sign, no exponent, no separators.</summary>
        internal static Finding CheckTolerance(string text, string path)
        {
            if (string.IsNullOrEmpty(text))
                return new Finding(path, "InvalidTolerance", "a tolerance is required; use \"0\" for an exact comparison");
            if (text.Length > MaxToleranceLength)
                return new Finding(path, "InvalidTolerance", "the tolerance is longer than " + MaxToleranceLength + " characters");
            int i = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            bool ok = i > 0;
            if (ok && i < text.Length)
            {
                ok = text[i] == '.';
                int fractionStart = ++i;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
                ok = ok && i > fractionStart && i == text.Length;
            }
            if (!ok) return new Finding(path, "InvalidTolerance", "the tolerance must be a non-negative invariant decimal such as 0 or 0.01 (digits with an optional fraction; no sign, exponent or separators)");
            // it must also be exactly representable as a decimal, like every value it is compared against, so it can never be rounded
            if (!ExactDecimal.TryParseText(text, out _, out string reason)) return new Finding(path, "InvalidTolerance", "the tolerance cannot be used: " + reason);
            return null;
        }

        // ------------------------------------------------------------------ builders (each works on a copy; null = accepted)

        internal Finding TryAddKey(string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase)
        {
            if (Keys.Count >= MaxKeys) return new Finding("keys", "TooManyKeys", "a definition may have at most " + MaxKeys + " key mappings");
            Finding f = CheckName(name, AllNames(), "name");
            if (f != null) return f;
            f = CheckPointer(leftPointer, "leftPointer", out string[] left);
            if (f != null) return f;
            f = CheckPointer(rightPointer, "rightPointer", out string[] right);
            if (f != null) return f;
            Keys.Add(new KeyMappingDef { Name = name, LeftPointer = leftPointer, RightPointer = rightPointer, LeftSegments = left, RightSegments = right, Trim = trim, IgnoreCase = ignoreCase });
            return null;
        }

        internal Finding TryAddText(string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase, ComparisonNullPolicy nullPolicy)
        {
            Finding f = CheckComparisonCommon(name, leftPointer, rightPointer, nullPolicy, out string[] left, out string[] right);
            if (f != null) return f;
            Comparisons.Add(new ComparisonDef { Name = name, Kind = RuleKind.Text, LeftPointer = leftPointer, RightPointer = rightPointer, LeftSegments = left, RightSegments = right, Trim = trim, IgnoreCase = ignoreCase, NullPolicy = nullPolicy });
            return null;
        }

        internal Finding TryAddDecimal(string name, string leftPointer, string rightPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy)
        {
            Finding f = CheckComparisonCommon(name, leftPointer, rightPointer, nullPolicy, out string[] left, out string[] right)
                ?? CheckTolerance(absoluteTolerance, "absoluteTolerance");
            if (f != null) return f;
            Comparisons.Add(new ComparisonDef { Name = name, Kind = RuleKind.Decimal, LeftPointer = leftPointer, RightPointer = rightPointer, LeftSegments = left, RightSegments = right, AbsoluteTolerance = absoluteTolerance, NullPolicy = nullPolicy });
            return null;
        }

        internal Finding TryAddBoolean(string name, string leftPointer, string rightPointer, ComparisonNullPolicy nullPolicy)
        {
            Finding f = CheckComparisonCommon(name, leftPointer, rightPointer, nullPolicy, out string[] left, out string[] right);
            if (f != null) return f;
            Comparisons.Add(new ComparisonDef { Name = name, Kind = RuleKind.Boolean, LeftPointer = leftPointer, RightPointer = rightPointer, LeftSegments = left, RightSegments = right, NullPolicy = nullPolicy });
            return null;
        }

        internal Finding TryAddMoney(string name, string leftPointer, string rightPointer, string leftCurrencyPointer, string rightCurrencyPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy)
        {
            Finding f = CheckComparisonCommon(name, leftPointer, rightPointer, nullPolicy, out string[] left, out string[] right);
            if (f != null) return f;
            f = CheckPointer(leftCurrencyPointer, "leftCurrencyPointer", out string[] leftCurrency);
            if (f != null) return f;
            f = CheckPointer(rightCurrencyPointer, "rightCurrencyPointer", out string[] rightCurrency);
            if (f != null) return f;
            f = CheckTolerance(absoluteTolerance, "absoluteTolerance");
            if (f != null) return f;
            Comparisons.Add(new ComparisonDef
            {
                Name = name, Kind = RuleKind.Money, LeftPointer = leftPointer, RightPointer = rightPointer, LeftSegments = left, RightSegments = right,
                LeftCurrencyPointer = leftCurrencyPointer, RightCurrencyPointer = rightCurrencyPointer, LeftCurrencySegments = leftCurrency, RightCurrencySegments = rightCurrency,
                AbsoluteTolerance = absoluteTolerance, NullPolicy = nullPolicy
            });
            return null;
        }

        internal Finding TryAddCalendarDate(string name, string leftPointer, string rightPointer, string leftFormat, string rightFormat, int toleranceDays, ComparisonNullPolicy nullPolicy)
        {
            Finding f = CheckComparisonCommon(name, leftPointer, rightPointer, nullPolicy, out string[] left, out string[] right);
            if (f != null) return f;
            f = CheckFormat(leftFormat, "leftFormat") ?? CheckFormat(rightFormat, "rightFormat") ?? CheckDateTolerance(toleranceDays, "toleranceDays");
            if (f != null) return f;
            Comparisons.Add(new ComparisonDef
            {
                Name = name, Kind = RuleKind.CalendarDate, LeftPointer = leftPointer, RightPointer = rightPointer, LeftSegments = left, RightSegments = right,
                LeftFormat = leftFormat, RightFormat = rightFormat, DateTolerance = toleranceDays, NullPolicy = nullPolicy
            });
            return null;
        }

        internal Finding TryAddInstant(string name, string leftPointer, string rightPointer, int toleranceSeconds, ComparisonNullPolicy nullPolicy)
        {
            Finding f = CheckComparisonCommon(name, leftPointer, rightPointer, nullPolicy, out string[] left, out string[] right)
                ?? CheckDateTolerance(toleranceSeconds, "toleranceSeconds");
            if (f != null) return f;
            Comparisons.Add(new ComparisonDef
            {
                Name = name, Kind = RuleKind.Instant, LeftPointer = leftPointer, RightPointer = rightPointer, LeftSegments = left, RightSegments = right,
                DateTolerance = toleranceSeconds, NullPolicy = nullPolicy
            });
            return null;
        }

        internal static Finding CheckFormat(string format, string path)
        {
            string problem = DateCore.CheckFormat(format);
            return problem == null ? null : new Finding(path, "InvalidFormat", problem);
        }

        internal static Finding CheckDateTolerance(int tolerance, string path) =>
            tolerance < 0 ? new Finding(path, "InvalidTolerance", "the tolerance must be a whole number of at least 0") : null;

        private Finding CheckComparisonCommon(string name, string leftPointer, string rightPointer, ComparisonNullPolicy nullPolicy, out string[] left, out string[] right)
        {
            left = right = null;
            if (Comparisons.Count >= MaxComparisons) return new Finding("comparisons", "TooManyComparisons", "a definition may have at most " + MaxComparisons + " comparisons");
            Finding f = CheckName(name, AllNames(), "name");
            if (f != null) return f;
            f = CheckPointer(leftPointer, "leftPointer", out left);
            if (f != null) return f;
            f = CheckPointer(rightPointer, "rightPointer", out right);
            if (f != null) return f;
            if (!Enum.IsDefined(typeof(ComparisonNullPolicy), nullPolicy))
                return new Finding("nullPolicy", "UnknownEnumValue", "nullPolicy " + (int)nullPolicy + " is not a known ComparisonNullPolicy");
            return null;
        }

        internal IEnumerable<string> AllNames() => Keys.Select(k => k.Name).Concat(Comparisons.Select(c => c.Name));

        // ------------------------------------------------------------------ canonical JSON

        /// <summary>
        /// The definition as canonical JSON: fixed property order, every option spelled out (defaults included), compact.
        /// The same definition always produces the same text, whether it was built with methods or loaded from JSON.
        /// </summary>
        internal string ToCanonicalJson()
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartObject();
                    w.WriteNumber("schemaVersion", SchemaVersion);

                    w.WriteStartArray("keys");
                    foreach (KeyMappingDef k in Keys)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", k.Name);
                        w.WriteString("leftPointer", k.LeftPointer);
                        w.WriteString("rightPointer", k.RightPointer);
                        w.WriteBoolean("trim", k.Trim);
                        w.WriteBoolean("ignoreCase", k.IgnoreCase);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();

                    w.WriteStartArray("comparisons");
                    foreach (ComparisonDef c in Comparisons)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", c.Name);
                        w.WriteString("kind", c.Kind.ToString());
                        w.WriteString("leftPointer", c.LeftPointer);
                        w.WriteString("rightPointer", c.RightPointer);
                        if (c.Kind == RuleKind.Money)
                        {
                            w.WriteString("leftCurrencyPointer", c.LeftCurrencyPointer);
                            w.WriteString("rightCurrencyPointer", c.RightCurrencyPointer);
                        }
                        if (c.Kind == RuleKind.Text)
                        {
                            w.WriteBoolean("trim", c.Trim);
                            w.WriteBoolean("ignoreCase", c.IgnoreCase);
                        }
                        else if (c.Kind == RuleKind.Decimal || c.Kind == RuleKind.Money)
                        {
                            w.WriteString("absoluteTolerance", c.AbsoluteTolerance);
                        }
                        else if (c.Kind == RuleKind.CalendarDate)
                        {
                            w.WriteString("leftFormat", c.LeftFormat);
                            w.WriteString("rightFormat", c.RightFormat);
                            w.WriteNumber("toleranceDays", c.DateTolerance);
                        }
                        else if (c.Kind == RuleKind.Instant)
                        {
                            w.WriteNumber("toleranceSeconds", c.DateTolerance);
                        }
                        w.WriteString("nullPolicy", c.NullPolicy.ToString());
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();

                    w.WriteStartObject("limits");
                    w.WriteNumber("maximumRowsPerSide", Limits.MaximumRowsPerSide);
                    w.WriteNumber("maximumInputCharactersPerSide", Limits.MaximumInputCharactersPerSide);
                    w.WriteNumber("maximumResults", Limits.MaximumResults);
                    w.WriteNumber("maximumDifferenceDetails", Limits.MaximumDifferenceDetails);
                    w.WriteEndObject();

                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
