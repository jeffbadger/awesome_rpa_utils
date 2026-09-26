using System;
using System.Text.Json;

namespace ReconciliationAutomation
{
    internal enum ComparisonState
    {
        /// <summary>The two sides agree under the rule.</summary>
        Equal,
        /// <summary>Both sides are usable and they disagree.</summary>
        Different,
        /// <summary>At least one side cannot be compared (missing, wrong type, not a valid number, a null where a value is required).</summary>
        Invalid
    }

    /// <summary>What one comparison rule found for one pair of records.</summary>
    internal sealed class ComparisonOutcome
    {
        internal string RuleName { get; set; }
        internal RuleKind Kind { get; set; }
        internal ComparisonState State { get; set; }

        /// <summary>A stable code such as <c>MissingField</c>, <c>TextMismatch</c> or <c>DecimalMismatch</c>; null when equal.</summary>
        internal string ReasonCode { get; set; }

        /// <summary>A human-readable explanation; it never quotes a text value.</summary>
        internal string Explanation { get; set; }

        internal bool LeftPresent { get; set; }
        internal bool RightPresent { get; set; }

        /// <summary>The value exactly as found, as a JSON fragment: null when the field is missing, <c>null</c> for a JSON null, a quoted string, a number token, true/false.</summary>
        internal string LeftValueJson { get; set; }
        internal string RightValueJson { get; set; }

        /// <summary>The value after the rule's interpretation (trimmed text, normalized number); null when there is none.</summary>
        internal string LeftInterpreted { get; set; }
        internal string RightInterpreted { get; set; }

        /// <summary>Decimal rules: <c>right - left</c> as exact invariant text (it may exceed the range of a decimal), when both values were usable.</summary>
        internal string Delta { get; set; }
    }

    /// <summary>
    /// The rule semantics for Text and Decimal comparisons. Everything is exact and culture-independent: ordinal text comparison, invariant
    /// number parsing, no implicit type conversion, no rounding, and no equivalence between missing, null, empty and zero.
    /// </summary>
    internal static class ComparisonCore
    {
        internal static ComparisonOutcome Evaluate(ComparisonDef rule, IRowReader left, IRowReader right) =>
            Evaluate(rule, left.Read(rule.LeftSegments), right.Read(rule.RightSegments));

        internal static ComparisonOutcome Evaluate(ComparisonDef rule, FieldValue left, FieldValue right)
        {
            var outcome = new ComparisonOutcome
            {
                RuleName = rule.Name,
                Kind = rule.Kind,
                LeftPresent = left.Kind != FieldKind.Missing,
                RightPresent = right.Kind != FieldKind.Missing,
                LeftValueJson = ValueJson(left),
                RightValueJson = ValueJson(right)
            };

            ExactDecimal tolerance = default;
            if (rule.Kind == RuleKind.Decimal && !rule.TryGetTolerance(out tolerance, out string toleranceError))
                return Invalid(outcome, "InvalidDecimal", "The rule's tolerance is not a usable decimal (" + toleranceError + ").");

            Side l = Interpret(rule, left);
            Side r = Interpret(rule, right);
            outcome.LeftInterpreted = l.Interpreted;
            outcome.RightInterpreted = r.Interpreted;

            // Problems on either side make the comparison invalid; the first one (left before right) names the reason.
            string leftProblem = ProblemOf(rule, l, "left");
            string rightProblem = ProblemOf(rule, r, "right");
            if (leftProblem != null || rightProblem != null)
            {
                // A missing field outranks an uninterpretable value, which outranks a null where a value is required; within one tier the left side wins.
                string code = l.IsMissing || r.IsMissing ? "MissingField"
                    : l.BadCode != null ? l.BadCode
                    : r.BadCode != null ? r.BadCode
                    : "NullNotAllowed";
                string explanation = leftProblem != null && rightProblem != null ? leftProblem + " " + rightProblem : leftProblem ?? rightProblem;
                return Invalid(outcome, code, explanation);
            }

            string mismatch = rule.Kind == RuleKind.Text ? "TextMismatch" : "DecimalMismatch";

            // With AllowBothNull the nulls are legitimate: two nulls agree, a null against a value is a difference.
            if (l.IsNull && r.IsNull) return outcome.With(ComparisonState.Equal, null, null);
            if (l.IsNull || r.IsNull)
                return outcome.With(ComparisonState.Different, mismatch, "One side is null and the other has a value.");

            if (rule.Kind == RuleKind.Text)
            {
                StringComparison comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(l.Interpreted, r.Interpreted, comparison)
                    ? outcome.With(ComparisonState.Equal, null, null)
                    : outcome.With(ComparisonState.Different, mismatch, "The text values differ.");
            }

            ExactDecimal delta = ExactDecimal.Subtract(r.Number, l.Number);          // right minus left, exact even beyond the range of a decimal
            outcome.Delta = delta.ToString();
            return delta.Abs().CompareTo(tolerance) <= 0
                ? outcome.With(ComparisonState.Equal, null, null)
                : outcome.With(ComparisonState.Different, mismatch, "The values differ by " + delta + " (right minus left), which is more than the tolerance " + tolerance + ".");
        }

        // ------------------------------------------------------------------ one side

        private sealed class Side
        {
            internal bool IsMissing, IsNull;
            internal string BadCode, BadReason;
            internal string Interpreted;       // text after trimming, or the normalized number
            internal ExactDecimal Number;

        }

        private static Side Interpret(ComparisonDef rule, FieldValue value)
        {
            var side = new Side();
            switch (value.Kind)
            {
                case FieldKind.Missing:
                    side.IsMissing = true;
                    return side;
                case FieldKind.Null:
                    side.IsNull = true;
                    return side;
            }

            if (rule.Kind == RuleKind.Text)
            {
                if (value.Kind != FieldKind.String)
                {
                    side.BadCode = "InvalidType";
                    side.BadReason = "a text rule needs a string but found " + Describe(value);
                    return side;
                }
                side.Interpreted = rule.Trim ? value.Text.Trim() : value.Text;
                return side;
            }

            // Decimal: a JSON number, or a string of invariant numeric text. Nothing else converts implicitly.
            string error;
            bool ok;
            if (value.Kind == FieldKind.Integer || value.Kind == FieldKind.Number) ok = ExactDecimal.TryParseJsonNumber(value.Text, out side.Number, out error);
            else if (value.Kind == FieldKind.String) ok = ExactDecimal.TryParseText(value.Text, out side.Number, out error);
            else
            {
                side.BadCode = "InvalidType";
                side.BadReason = "a decimal rule needs a number or numeric text but found " + Describe(value);
                return side;
            }
            if (!ok)
            {
                side.BadCode = "InvalidDecimal";
                side.BadReason = "the value is not a usable decimal: " + error;
                return side;
            }
            side.Interpreted = side.Number.ToString();
            return side;
        }

        /// <summary>The explanation of why this side blocks a comparison, or null when it does not.</summary>
        private static string ProblemOf(ComparisonDef rule, Side side, string name)
        {
            if (side.IsMissing) return "The " + name + " field is missing.";
            if (side.IsNull && rule.NullPolicy == ComparisonNullPolicy.RequireValue) return "The " + name + " value is null and this rule requires a value.";
            if (side.BadCode != null) return "The " + name + " value cannot be compared: " + side.BadReason + ".";
            return null;
        }

        private static string Describe(FieldValue value)
        {
            switch (value.Kind)
            {
                case FieldKind.Integer:
                case FieldKind.Number: return "a number";
                case FieldKind.Boolean: return "a Boolean";
                case FieldKind.String: return "a string";
                default: return value.Text ?? "an unsupported value";
            }
        }

        // ------------------------------------------------------------------ helpers

        private static readonly JsonSerializerOptions ValueJsonOptions = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static ComparisonOutcome Invalid(ComparisonOutcome outcome, string code, string explanation) => outcome.With(ComparisonState.Invalid, code, explanation);

        private static ComparisonOutcome With(this ComparisonOutcome outcome, ComparisonState state, string code, string explanation)
        {
            outcome.State = state;
            outcome.ReasonCode = code;
            outcome.Explanation = explanation;
            return outcome;
        }

        /// <summary>The original value as a JSON fragment, so missing, null, empty and zero stay distinguishable. Missing is a null string.</summary>
        internal static string ValueJson(FieldValue value)
        {
            switch (value.Kind)
            {
                case FieldKind.Missing: return null;
                case FieldKind.Null: return "null";
                case FieldKind.String: return JsonSerializer.Serialize(value.Text, ValueJsonOptions);
                case FieldKind.Integer:
                case FieldKind.Number:
                case FieldKind.Boolean: return value.Text;
                default: return JsonSerializer.Serialize("(" + (value.Text ?? "unsupported value") + ")", ValueJsonOptions);
            }
        }
    }
}
