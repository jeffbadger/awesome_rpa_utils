using System;
using System.Collections.Generic;
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
        internal ComparisonOutcome Copy() => (ComparisonOutcome)MemberwiseClone();

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

        /// <summary>Money rules only: each side's currency field as a JSON fragment (null when missing).</summary>
        internal string LeftCurrencyJson { get; set; }
        internal string RightCurrencyJson { get; set; }

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
            rule.Kind == RuleKind.Money
                ? EvaluateMoney(rule, left.Read(rule.LeftSegments), right.Read(rule.RightSegments), left.Read(rule.LeftCurrencySegments), right.Read(rule.RightCurrencySegments))
                : Evaluate(rule, left.Read(rule.LeftSegments), right.Read(rule.RightSegments));

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

            // A money rule also needs its currency fields, which this overload does not have: use EvaluateMoney (the row-reader overload does).
            if (rule.Kind == RuleKind.Money)
                return Invalid(outcome, "InvalidType", "A money rule needs its currency fields to be evaluated.");

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

            string mismatch = rule.Kind == RuleKind.Text ? "TextMismatch" : rule.Kind == RuleKind.Boolean ? "BooleanMismatch" : "DecimalMismatch";

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

            if (rule.Kind == RuleKind.Boolean)
                return l.Interpreted == r.Interpreted
                    ? outcome.With(ComparisonState.Equal, null, null)
                    : outcome.With(ComparisonState.Different, mismatch, "The Boolean values differ.");

            return CompareAmounts(outcome, l, r, tolerance, mismatch);
        }

        private static ComparisonOutcome CompareAmounts(ComparisonOutcome outcome, Side l, Side r, ExactDecimal tolerance, string mismatch)
        {
            ExactDecimal delta = ExactDecimal.Subtract(r.Number, l.Number);          // right minus left, exact even beyond the range of a decimal
            outcome.Delta = delta.ToString();
            return delta.Abs().CompareTo(tolerance) <= 0
                ? outcome.With(ComparisonState.Equal, null, null)
                : outcome.With(ComparisonState.Different, mismatch, "The values differ by " + delta + " (right minus left), which is more than the tolerance " + tolerance + ".");
        }

        /// <summary>
        /// A money comparison: an exact amount (Decimal semantics) gated on a currency on each side. Missing fields come first, then an unusable
        /// currency (<c>InvalidCurrency</c>), then an unusable amount. Currencies that differ are a mismatch (<c>CurrencyMismatch</c>) and the amounts are
        /// never compared, so no misleading delta exists; the null policy applies to the amounts only once the currencies are valid and agree.
        /// </summary>
        internal static ComparisonOutcome EvaluateMoney(ComparisonDef rule, FieldValue left, FieldValue right, FieldValue leftCurrency, FieldValue rightCurrency)
        {
            var outcome = new ComparisonOutcome
            {
                RuleName = rule.Name,
                Kind = rule.Kind,
                LeftPresent = left.Kind != FieldKind.Missing,
                RightPresent = right.Kind != FieldKind.Missing,
                LeftValueJson = ValueJson(left),
                RightValueJson = ValueJson(right),
                LeftCurrencyJson = ValueJson(leftCurrency),
                RightCurrencyJson = ValueJson(rightCurrency)
            };

            if (!rule.TryGetTolerance(out ExactDecimal tolerance, out string toleranceError))
                return Invalid(outcome, "InvalidDecimal", "The rule's tolerance is not a usable decimal (" + toleranceError + ").");

            Side l = Interpret(rule, left);
            Side r = Interpret(rule, right);
            string lc = CurrencyOf(leftCurrency, out bool lcMissing, out string lcProblem, "left");
            string rc = CurrencyOf(rightCurrency, out bool rcMissing, out string rcProblem, "right");

            string leftAmountProblem = ProblemOf(rule, l, "left amount");
            string rightAmountProblem = ProblemOf(rule, r, "right amount");
            if (leftAmountProblem != null || rightAmountProblem != null || lcProblem != null || rcProblem != null)
            {
                var explanation = new List<string>();
                if (leftAmountProblem != null) explanation.Add(leftAmountProblem);
                if (rightAmountProblem != null) explanation.Add(rightAmountProblem);
                if (lcProblem != null) explanation.Add(lcProblem);
                if (rcProblem != null) explanation.Add(rcProblem);
                string code = l.IsMissing || r.IsMissing || lcMissing || rcMissing ? "MissingField"
                    : lcProblem != null || rcProblem != null ? "InvalidCurrency"
                    : l.BadCode ?? r.BadCode ?? "NullNotAllowed";
                return Invalid(outcome, code, string.Join(" ", explanation));
            }

            outcome.LeftInterpreted = l.IsNull ? null : l.Number + " " + lc;
            outcome.RightInterpreted = r.IsNull ? null : r.Number + " " + rc;

            if (lc != rc)
                return outcome.With(ComparisonState.Different, "CurrencyMismatch", "The currencies differ, so the amounts are not compared.");

            if (l.IsNull && r.IsNull) return outcome.With(ComparisonState.Equal, null, null);
            if (l.IsNull || r.IsNull) return outcome.With(ComparisonState.Different, "DecimalMismatch", "One side is null and the other has a value.");
            return CompareAmounts(outcome, l, r, tolerance, "DecimalMismatch");
        }

        /// <summary>A currency code: a string of exactly three ASCII letters once trimmed, normalized to upper case. No registry lookup is claimed.</summary>
        private static string CurrencyOf(FieldValue value, out bool missing, out string problem, string name)
        {
            missing = value.Kind == FieldKind.Missing;
            problem = null;
            if (missing) { problem = "The " + name + " currency field is missing."; return null; }
            if (value.Kind != FieldKind.String || value.Text == null) { problem = "The " + name + " currency must be a string of three letters but found " + Describe(value) + "."; return null; }
            string trimmed = value.Text.Trim();
            if (trimmed.Length != 3 || !IsAsciiLetter(trimmed[0]) || !IsAsciiLetter(trimmed[1]) || !IsAsciiLetter(trimmed[2]))
            {
                problem = "The " + name + " currency is not a three-letter code.";
                return null;
            }
            return trimmed.ToUpperInvariant();
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

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

            // A string, number or Boolean always carries its text. A row source that breaks that contract (a DataTable reader with a bad
            // cell) must produce a comparison problem, not a null reference or, worse, a null treated as an ordinary empty value.
            if (value.Text == null && (value.Kind == FieldKind.String || value.Kind == FieldKind.Integer || value.Kind == FieldKind.Number || value.Kind == FieldKind.Boolean))
            {
                side.BadCode = "InvalidType";
                side.BadReason = "the value has no text (a malformed value from the row source)";
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

            if (rule.Kind == RuleKind.Boolean)
            {
                if (value.Kind != FieldKind.Boolean)
                {
                    side.BadCode = "InvalidType";
                    side.BadReason = "a Boolean rule needs true or false (not text, and not the numbers 0 or 1) but found " + Describe(value);
                    return side;
                }
                side.Interpreted = value.Text;
                return side;
            }

            // Decimal and Money: a JSON number, or a string of invariant numeric text. Nothing else converts implicitly.
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

        /// <summary>True for a token matching JSON's number grammar: <c>-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?</c>.</summary>
        internal static bool IsJsonNumber(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            int i = token[0] == '-' ? 1 : 0;
            if (i >= token.Length || !IsDigit(token[i])) return false;
            if (token[i] == '0') i++;
            else while (i < token.Length && IsDigit(token[i])) i++;
            if (i < token.Length && token[i] == '.')
            {
                int start = ++i;
                while (i < token.Length && IsDigit(token[i])) i++;
                if (i == start) return false;
            }
            if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
            {
                i++;
                if (i < token.Length && (token[i] == '+' || token[i] == '-')) i++;
                int start = i;
                while (i < token.Length && IsDigit(token[i])) i++;
                if (i == start) return false;
            }
            return i == token.Length;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

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
                    // A number is written as its original token only if that really is a JSON number. A row source that does not
                    // guarantee well-formed tokens (a DataTable) could hand over 01 or 1. or text: those are kept as a quoted string,
                    // so the fragment is valid JSON and the original text is not lost.
                    return IsJsonNumber(value.Text) ? value.Text : JsonSerializer.Serialize(value.Text ?? string.Empty, ValueJsonOptions);
                case FieldKind.Boolean:
                    return value.Text == "true" || value.Text == "false" ? value.Text : JsonSerializer.Serialize(value.Text ?? string.Empty, ValueJsonOptions);
                default: return JsonSerializer.Serialize("(" + (value.Text ?? "unsupported value") + ")", ValueJsonOptions);
            }
        }
    }
}
