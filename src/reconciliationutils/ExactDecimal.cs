using System;
using System.Numerics;
using System.Text;

namespace ReconciliationAutomation
{
    /// <summary>
    /// An exact decimal number: <c>Unscaled / 10^Scale</c> with the smallest possible scale (no trailing fractional zeros), held in a
    /// <see cref="BigInteger"/> so that adding, subtracting and comparing can never overflow or round. Parsing accepts only values that a
    /// .NET <see cref="decimal"/> can hold exactly (at most 28 fractional digits, magnitude within 96 bits), so precision is never lost
    /// silently and a value is never rounded. Everything is culture-independent.
    /// </summary>
    internal readonly struct ExactDecimal : IComparable<ExactDecimal>
    {
        /// <summary>The scale a .NET decimal can carry.</summary>
        internal const int MaxScale = 28;

        private static readonly BigInteger MaxMantissa = (BigInteger.One << 96) - 1;

        private ExactDecimal(BigInteger unscaled, int scale)
        {
            Unscaled = unscaled;
            Scale = scale;
        }

        internal BigInteger Unscaled { get; }
        internal int Scale { get; }

        internal bool IsZero => Unscaled.IsZero;

        // ------------------------------------------------------------------ parsing

        /// <summary>
        /// Parses invariant numeric text: an optional sign, digits, and an optional fraction (<c>[+-]?[0-9]+(\.[0-9]+)?</c>). No exponent,
        /// grouping separators, currency symbols or surrounding white space.
        /// </summary>
        internal static bool TryParseText(string text, out ExactDecimal value, out string error)
        {
            value = default;
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "the text is empty"; return false; }
            if (text.Length > JsonInput.MaxNumberTokenLength) { error = "the text is longer than " + JsonInput.MaxNumberTokenLength + " characters"; return false; }

            int i = 0;
            bool negative = false;
            if (text[0] == '+' || text[0] == '-') { negative = text[0] == '-'; i = 1; }

            int integerStart = i;
            while (i < text.Length && IsDigit(text[i])) i++;
            int integerDigits = i - integerStart;
            if (integerDigits == 0) { error = "it is not a number (expected digits, an optional fraction, and an optional sign)"; return false; }

            int fractionStart = i;
            int fractionDigits = 0;
            if (i < text.Length && text[i] == '.')
            {
                fractionStart = ++i;
                while (i < text.Length && IsDigit(text[i])) i++;
                fractionDigits = i - fractionStart;
                if (fractionDigits == 0) { error = "it is not a number (a decimal point must be followed by digits)"; return false; }
            }
            if (i != text.Length) { error = "it is not a plain decimal number (no exponent, separators, symbols or spaces are accepted in text)"; return false; }

            return Build(negative, text, integerStart, integerDigits, fractionStart, fractionDigits, BigInteger.Zero, out value, out error);
        }

        /// <summary>Parses a JSON number token: <c>-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?</c>. Exponents are honored only when the exact result fits.</summary>
        internal static bool TryParseJsonNumber(string token, out ExactDecimal value, out string error)
        {
            value = default;
            error = null;
            if (string.IsNullOrEmpty(token)) { error = "the number is empty"; return false; }
            if (token.Length > JsonInput.MaxNumberTokenLength) { error = "the number is longer than " + JsonInput.MaxNumberTokenLength + " characters"; return false; }

            int i = 0;
            bool negative = false;
            if (token[0] == '-') { negative = true; i = 1; }

            int integerStart = i;
            while (i < token.Length && IsDigit(token[i])) i++;
            int integerDigits = i - integerStart;
            if (integerDigits == 0) { error = "it is not a number"; return false; }
            if (integerDigits > 1 && token[integerStart] == '0') { error = "it is not a number"; return false; }

            int fractionStart = i;
            int fractionDigits = 0;
            if (i < token.Length && token[i] == '.')
            {
                fractionStart = ++i;
                while (i < token.Length && IsDigit(token[i])) i++;
                fractionDigits = i - fractionStart;
                if (fractionDigits == 0) { error = "it is not a number"; return false; }
            }

            BigInteger exponent = BigInteger.Zero;
            if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
            {
                i++;
                bool expNegative = false;
                if (i < token.Length && (token[i] == '+' || token[i] == '-')) { expNegative = token[i] == '-'; i++; }
                int expStart = i;
                while (i < token.Length && IsDigit(token[i])) i++;
                if (i == expStart) { error = "it is not a number"; return false; }
                exponent = BigInteger.Parse(token.Substring(expStart, i - expStart), System.Globalization.CultureInfo.InvariantCulture);
                if (expNegative) exponent = -exponent;
            }
            if (i != token.Length) { error = "it is not a number"; return false; }

            return Build(negative, token, integerStart, integerDigits, fractionStart, fractionDigits, exponent, out value, out error);
        }

        private static bool Build(bool negative, string text, int integerStart, int integerDigits, int fractionStart, int fractionDigits, BigInteger exponent, out ExactDecimal value, out string error)
        {
            value = default;
            error = null;

            // digits = integer part followed by fraction part; the value is digits * 10^(exponent - fractionDigits)
            string digits = text.Substring(integerStart, integerDigits) + (fractionDigits > 0 ? text.Substring(fractionStart, fractionDigits) : string.Empty);
            digits = digits.TrimStart('0');
            if (digits.Length == 0) { value = new ExactDecimal(BigInteger.Zero, 0); return true; }   // zero, however it is written (0, -0, 0.000, 0e999)

            // strip trailing zeros so the scale is minimal; each one removed raises the exponent by one
            int trailingZeros = digits.Length - digits.TrimEnd('0').Length;
            digits = digits.Substring(0, digits.Length - trailingZeros);
            BigInteger scale = fractionDigits - (exponent + trailingZeros);      // digits * 10^-scale

            if (scale > MaxScale)
            {
                error = "it has more than " + MaxScale + " decimal places, so it cannot be held exactly (it would have to be rounded)";
                return false;
            }
            BigInteger unscaled = BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
            int finalScale = 0;
            if (scale < 0)
            {
                // a positive power of ten: an integer with trailing zeros
                if (-scale > 29) { error = "it is too large to be held exactly"; return false; }
                unscaled *= BigInteger.Pow(10, (int)-scale);
            }
            else finalScale = (int)scale;

            if (unscaled > MaxMantissa) { error = "it has too many significant digits or is too large to be held exactly"; return false; }
            value = new ExactDecimal(negative ? -unscaled : unscaled, finalScale);
            return true;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        // ------------------------------------------------------------------ arithmetic (exact, never overflows)

        internal static ExactDecimal Subtract(ExactDecimal left, ExactDecimal right)
        {
            int scale = Math.Max(left.Scale, right.Scale);
            BigInteger difference = left.AtScale(scale) - right.AtScale(scale);
            return Normalize(difference, scale);
        }

        internal ExactDecimal Abs() => new ExactDecimal(BigInteger.Abs(Unscaled), Scale);

        public int CompareTo(ExactDecimal other)
        {
            int scale = Math.Max(Scale, other.Scale);
            return AtScale(scale).CompareTo(other.AtScale(scale));
        }

        private BigInteger AtScale(int scale) => scale == Scale ? Unscaled : Unscaled * BigInteger.Pow(10, scale - Scale);

        private static ExactDecimal Normalize(BigInteger unscaled, int scale)
        {
            if (unscaled.IsZero) return new ExactDecimal(BigInteger.Zero, 0);
            while (scale > 0 && (unscaled % 10).IsZero)
            {
                unscaled /= 10;
                scale--;
            }
            return new ExactDecimal(unscaled, scale);
        }

        // ------------------------------------------------------------------ formatting

        /// <summary>Invariant plain text: no exponent, no trailing fractional zeros, no plus sign, <c>0</c> for zero (never <c>-0</c>).</summary>
        public override string ToString()
        {
            if (Unscaled.IsZero) return "0";
            string digits = BigInteger.Abs(Unscaled).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var text = new StringBuilder();
            if (Unscaled.Sign < 0) text.Append('-');
            if (Scale == 0) return text.Append(digits).ToString();
            if (digits.Length <= Scale) return text.Append("0.").Append('0', Scale - digits.Length).Append(digits).ToString();
            return text.Append(digits, 0, digits.Length - Scale).Append('.').Append(digits, digits.Length - Scale, Scale).ToString();
        }
    }
}
