using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace TextExtractAutomation
{
    /// <summary>The outcome of reading a value of a type out of a span of text.</summary>
    internal sealed class Converted
    {
        internal bool Ok;
        internal string Value;       // the normalized value (invariant, culture-independent); null when not Ok
        internal string Reason;      // null when Ok; otherwise MissingValue or InvalidValue
        internal string Detail;      // why a value is invalid, without quoting it
        internal string Currency;    // Amount only: the symbol or code found next to the amount, or null
        internal int Start, End;     // the captured part of the span [Start, End); both 0 when not Ok

        internal static Converted Missing() => new Converted { Reason = "MissingValue", Detail = "there is no text where the value should be" };
        internal static Converted Invalid(string detail) => new Converted { Reason = "InvalidValue", Detail = detail };
        internal static Converted Found(string value, int start, int end, string currency = null) => new Converted { Ok = true, Value = value, Start = start, End = end, Currency = currency };
    }

    /// <summary>
    /// Reads a typed value out of the span of text a label points at. The type decides both what is captured (the first part of the span that
    /// looks like a value of that type) and whether it is valid, and gives a normalized form that never depends on the machine culture:
    /// <list type="bullet">
    /// <item>Text: the whole span. Code: the first token of letters, digits and - / . _ (a trailing full stop dropped).</item>
    /// <item>Integer, Decimal, Amount, Percentage: numbers written in the field's DecimalStyle only, with valid thousands grouping (groups of three),
    /// normalized to digits with a . decimal point and a leading - when negative, keeping the decimals as written; never rounded. Amounts may carry a
    /// currency symbol or three-letter code on either side and be negative by a sign, a trailing minus or parentheses. OCR slips inside a number
    /// (O for 0, I, l or | for 1) are repaired only next to a real digit.</item>
    /// <item>Date: the first part of the span that reads as a date in one of the field's formats, normalized to yyyy-MM-dd.</item>
    /// <item>Email: the first address; the domain is lower-cased. Iban: country code, check digits and account, checked with mod 97, normalized to upper case without spaces.</item>
    /// </list>
    /// </summary>
    internal static class ValueConverter
    {
        internal static Converted Convert(FieldDef field, string span)
        {
            if (string.IsNullOrWhiteSpace(span)) return Converted.Missing();
            switch (field.Type)
            {
                case FieldType.Text: return Text(span);
                case FieldType.Code: return Code(span);
                case FieldType.Integer: return Number(span, field.DecimalStyle, integerOnly: true, percentage: false);
                case FieldType.Decimal: return Number(span, field.DecimalStyle, integerOnly: false, percentage: false);
                case FieldType.Percentage: return Number(span, field.DecimalStyle, integerOnly: false, percentage: true);
                case FieldType.Amount: return Amount(span, field.DecimalStyle);
                case FieldType.Date: return Date(span, field.DateFormats);
                case FieldType.Email: return Email(span);
                case FieldType.Iban: return Iban(span);
                default: return Converted.Invalid("the field type is not known");
            }
        }

        // ------------------------------------------------------------------ text and codes

        private static Converted Text(string span)
        {
            int s = 0, e = span.Length;
            while (s < e && span[s] == ' ') s++;
            while (e > s && span[e - 1] == ' ') e--;
            return Converted.Found(span.Substring(s, e - s), s, e);
        }

        private static bool IsCodeChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '/' || c == '.' || c == '_';

        private static Converted Code(string span)
        {
            int i = 0;
            while (i < span.Length && (span[i] == ' ' || span[i] == '"' || span[i] == '\'' || span[i] == '(' || span[i] == '[')) i++;
            int s = i;
            while (i < span.Length && IsCodeChar(span[i])) i++;
            int e = i;
            while (e > s && span[e - 1] == '.') e--;   // the full stop ending a sentence is not part of the code; - / _ are, even at the end
            if (e <= s || !span.Skip(s).Take(e - s).Any(char.IsLetterOrDigit)) return Converted.Invalid("the text does not start with a code (letters, digits and - / . _)");
            return Converted.Found(span.Substring(s, e - s), s, e);
        }

        // ------------------------------------------------------------------ numbers

        /// <summary>A digit, or an OCR look-alike of one (O, I, l, |) that is repaired only when it touches a real digit.</summary>
        private static int DigitValue(string text, int i, int runStart, int runEnd)
        {
            char c = text[i];
            if (c >= '0' && c <= '9') return c - '0';
            int repaired = c == 'O' || c == 'o' ? 0 : c == 'I' || c == 'l' || c == '|' ? 1 : -1;
            if (repaired < 0) return -1;
            bool before = i > runStart && text[i - 1] >= '0' && text[i - 1] <= '9';
            bool after = i + 1 < runEnd && text[i + 1] >= '0' && text[i + 1] <= '9';
            return before || after ? repaired : -1;
        }

        private sealed class NumberToken
        {
            internal int Start, End;         // the token in the span
            internal string Normalized;      // digits, optional '.', leading '-'
            internal bool HasFraction;
        }

        /// <summary>
        /// Reads a number starting at <paramref name="start"/>: an optional sign, digits with optional grouping (groups of three separated by the style's
        /// grouping character, a space or an apostrophe), and an optional fraction after the style's decimal separator. Returns null when there is no
        /// number there or when the grouping is not valid (1,23 under DotDecimal is refused rather than guessed).
        /// </summary>
        private static NumberToken ReadNumber(string text, int start, DecimalStyle style, out bool badGrouping)
        {
            badGrouping = false;
            char point = style == DecimalStyle.DotDecimal ? '.' : ',';
            char group = style == DecimalStyle.DotDecimal ? ',' : '.';
            int i = start;
            bool negative = false;
            if (i < text.Length && (text[i] == '-' || text[i] == '+')) { negative = text[i] == '-'; i++; }
            int runEnd = text.Length;
            var digits = new StringBuilder();
            var groups = new System.Collections.Generic.List<int>();
            int current = 0;
            while (i < text.Length)
            {
                int d = DigitValue(text, i, start, runEnd);
                if (d >= 0) { digits.Append((char)('0' + d)); current++; i++; continue; }
                char c = text[i];
                bool separator = c == group || c == '\'' || (c == ' ' && current > 0);
                if (separator && current > 0 && i + 1 < text.Length && DigitValue(text, i + 1, start, runEnd) >= 0)
                {
                    // A grouping separator must be followed by exactly three digits. The style's grouping character and the apostrophe are only ever
                    // grouping, so any other count is refused (1'2345, 1,23); a space may also simply separate two numbers ("5 12/03"), so a space
                    // followed by anything but three digits ends the number instead.
                    int k = i + 1, n = 0;
                    while (k < text.Length && DigitValue(text, k, start, runEnd) >= 0) { k++; n++; }
                    if (n == 3 || c == group)
                    {
                        groups.Add(current);
                        current = 0;
                        i++;
                        continue;
                    }
                    if (c == '\'') { badGrouping = true; return null; }
                    break;
                }
                break;
            }
            groups.Add(current);
            bool leadingPoint = digits.Length == 0 && i < text.Length && text[i] == point && i + 1 < text.Length && DigitValue(text, i + 1, start, runEnd) >= 0;   // .50, or .O5 read by OCR
            if (digits.Length == 0 && !leadingPoint) return null;
            if (groups.Count > 1)
            {
                // the first group has 1 to 3 digits and every later one exactly 3
                if (groups[0] < 1 || groups[0] > 3 || groups.Skip(1).Any(g => g != 3)) { badGrouping = true; return null; }
            }
            var result = new StringBuilder();
            if (negative) result.Append('-');
            result.Append(digits.Length == 0 ? "0" : digits.ToString());
            bool fraction = false;
            if (i < text.Length && text[i] == point && i + 1 < text.Length && DigitValue(text, i + 1, start, runEnd) >= 0)
            {
                i++;
                result.Append('.');
                while (i < text.Length && DigitValue(text, i, start, runEnd) >= 0) { result.Append((char)('0' + DigitValue(text, i, start, runEnd))); i++; }
                fraction = true;
            }
            // another separator followed by a digit right after the fraction (1.234,56 read in the dot style) means the number is written in the other
            // style: refused rather than read as 1.234
            if (fraction && i + 1 < text.Length && (text[i] == point || text[i] == group) && text[i + 1] >= '0' && text[i + 1] <= '9') { badGrouping = true; return null; }
            // a decimal or grouping separator left dangling after the digits (1,234. or 12,) belongs to the sentence, not the number
            if (i < text.Length && char.IsLetterOrDigit(text[i])) return null;          // 12abc is not a number
            return new NumberToken { Start = start, End = i, Normalized = result.ToString(), HasFraction = fraction };
        }

        /// <summary>The first number in the span, at a token boundary. Returns null (with the reason) when there is none.</summary>
        private static NumberToken FirstNumber(string span, DecimalStyle style, out bool sawBadGrouping)
        {
            sawBadGrouping = false;
            for (int i = 0; i < span.Length; i++)
            {
                if (i > 0 && char.IsLetterOrDigit(span[i - 1])) continue;
                char c = span[i];
                bool couldStart = (c >= '0' && c <= '9') || c == '-' || c == '+' || c == (style == DecimalStyle.DotDecimal ? '.' : ',') || c == 'O' || c == 'o' || c == 'I' || c == 'l' || c == '|';
                // a value that starts with the other style's decimal separator (,5 under DotDecimal) is written in the other style: refuse it rather
                // than skip the separator and read 5
                char otherPoint = style == DecimalStyle.DotDecimal ? ',' : '.';
                if (c == otherPoint && i + 1 < span.Length && DigitValue(span, i + 1, i, span.Length) >= 0) { sawBadGrouping = true; return null; }
                if (!couldStart) continue;
                NumberToken token = ReadNumber(span, i, style, out bool badGrouping);
                if (badGrouping) { sawBadGrouping = true; return null; }     // the first number is written in the other style: refuse, never read a later part of it
                if (token != null) return token;
            }
            return null;
        }

        /// <summary>
        /// A number fits the component's exact range when .NET's decimal holds its value exactly: leading zeros of the whole part and trailing zeros of
        /// the fraction carry no value, so they do not count (0000.5000 is fine however many there are), but any significant digit that decimal would
        /// round away (more than 28 decimals, about 29 significant digits, or beyond about 7.9e28) makes the number unusable. The value is still reported
        /// with its digits as written.
        /// </summary>
        internal static bool Representable(string normalized)
        {
            string unsigned = normalized.StartsWith("-", StringComparison.Ordinal) ? normalized.Substring(1) : normalized;
            int point = unsigned.IndexOf('.');
            string whole = (point < 0 ? unsigned : unsigned.Substring(0, point)).TrimStart('0');
            string fraction = point < 0 ? string.Empty : unsigned.Substring(point + 1).TrimEnd('0');
            string significant = (whole.Length == 0 ? "0" : whole) + (fraction.Length == 0 ? string.Empty : "." + fraction);
            if (!decimal.TryParse(significant, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)) return false;
            string back = value.ToString(CultureInfo.InvariantCulture);
            if (back.Contains('.')) back = back.TrimEnd('0').TrimEnd('.');
            return back == significant;                  // decimal kept every significant digit: nothing was rounded
        }

        private static Converted Number(string span, DecimalStyle style, bool integerOnly, bool percentage)
        {
            NumberToken token = FirstNumber(span, style, out bool badGrouping);
            if (token == null)
                return Converted.Invalid(badGrouping ? "the number's grouping does not match the " + style + " style (thousands must be groups of three)" : "there is no number");
            int end = token.End;
            if (percentage)
            {
                int p = end;
                while (p < span.Length && span[p] == ' ') p++;
                if (p >= span.Length || span[p] != '%') return Converted.Invalid("a percentage needs a % after the number");
                end = p + 1;
            }
            if (integerOnly && token.HasFraction) return Converted.Invalid("the number has a fraction, but the field is an Integer");
            if (!Representable(token.Normalized)) return Converted.Invalid("the number is too large or has too many digits to be held exactly");
            return Converted.Found(token.Normalized, token.Start, end);
        }

        // ------------------------------------------------------------------ amounts

        private static bool IsCurrencySymbol(char c) => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.CurrencySymbol;

        private static bool IsCurrencyCodeAt(string span, int i) =>
            i + 3 <= span.Length && span.Substring(i, 3).All(ch => ch >= 'A' && ch <= 'Z')
            && (i == 0 || !char.IsLetterOrDigit(span[i - 1])) && (i + 3 == span.Length || !char.IsLetterOrDigit(span[i + 3]));

        private static Converted Amount(string span, DecimalStyle style)
        {
            // Find the first number, then look for a currency, a sign or parentheses around it (at most a space away).
            NumberToken token = FirstNumber(span, style, out bool badGrouping);
            if (token == null)
                return Converted.Invalid(badGrouping ? "the amount's grouping does not match the " + style + " style (thousands must be groups of three)" : "there is no amount");
            int start = token.Start, end = token.End;
            string value = token.Normalized;
            bool negative = value.StartsWith("-", StringComparison.Ordinal);
            string digits = negative ? value.Substring(1) : value;
            string currency = null;

            // before the number: "-$ 12", "$-12", "(EUR 12)", "USD 12"
            int b = start;
            int Back(int k) { while (k > 0 && span[k - 1] == ' ') k--; return k; }
            int k0 = Back(b);
            if (k0 > 0 && IsCurrencySymbol(span[k0 - 1])) { currency = span[k0 - 1].ToString(); b = k0 - 1; }
            else if (k0 >= 3 && IsCurrencyCodeAt(span, k0 - 3)) { currency = span.Substring(k0 - 3, 3); b = k0 - 3; }
            int k1 = Back(b);
            if (k1 > 0 && span[k1 - 1] == '-' && !negative) { negative = true; b = k1 - 1; }
            bool openParen = false;
            int k2 = Back(b);
            if (k2 > 0 && span[k2 - 1] == '(') { openParen = true; b = k2 - 1; }

            // after the number: "12 EUR", "12 €", "12-", "12)"
            int a = end;
            int Forward(int k) { while (k < span.Length && span[k] == ' ') k++; return k; }
            if (currency == null)
            {
                int f = Forward(a);
                if (f < span.Length && IsCurrencySymbol(span[f])) { currency = span[f].ToString(); a = f + 1; }
                else if (IsCurrencyCodeAt(span, f)) { currency = span.Substring(f, 3); a = f + 3; }
            }
            if (a < span.Length && span[a] == '-' && !negative && (a + 1 == span.Length || !char.IsDigit(span[a + 1])))
            {
                negative = true;
                a++;
                if (currency == null)                                        // "12.00- EUR": the currency may follow the trailing minus
                {
                    int f = Forward(a);
                    if (f < span.Length && IsCurrencySymbol(span[f])) { currency = span[f].ToString(); a = f + 1; }
                    else if (IsCurrencyCodeAt(span, f)) { currency = span.Substring(f, 3); a = f + 3; }
                }
            }
            if (openParen)
            {
                int f = Forward(a);
                if (f < span.Length && span[f] == ')') { a = f + 1; negative = true; }
                else { b = start; openParen = false; }                       // an unmatched ( is not an accounting negative
            }
            if (!Representable(digits)) return Converted.Invalid("the amount is too large or has too many digits to be held exactly");
            string normalized = (negative && digits.Trim('0', '.').Length > 0 ? "-" : string.Empty) + digits;
            return Converted.Found(normalized, Math.Min(b, start), Math.Max(a, end), currency);
        }

        // ------------------------------------------------------------------ dates

        private static Converted Date(string span, string[] formats)
        {
            // Every allowed format has a fixed length (yyyy, MM, dd and literals), so each is tried on the windows of that length that start and end at
            // a token boundary, left to right; the first date found wins. O and l/I inside a window are read as 0 and 1 only when the window then parses.
            for (int s = 0; s < span.Length; s++)
            {
                if (s > 0 && char.IsLetterOrDigit(span[s - 1])) continue;
                foreach (string format in formats)
                {
                    int length = FormatLength(format);
                    if (s + length > span.Length) continue;
                    if (s + length < span.Length && char.IsLetterOrDigit(span[s + length])) continue;
                    string window = span.Substring(s, length);
                    if (DateCore.TryParseDate(window, format, out _, out string normalized) || DateCore.TryParseDate(RepairDigits(window), format, out _, out normalized))
                        return Converted.Found(normalized, s, s + length);
                }
            }
            return Converted.Invalid("there is no date in the field's date format" + (formats.Length > 1 ? "s" : string.Empty));
        }

        /// <summary>The number of characters a date format produces: yyyy is 4, MM and dd are 2, a quoted literal is its content, anything else is 1.</summary>
        internal static int FormatLength(string format)
        {
            int n = 0;
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c == '\'') { int e = format.IndexOf('\'', i + 1); n += e - i - 1; i = e; }
                else if (c == 'y' || c == 'M' || c == 'd') { int r = 1; while (i + 1 < format.Length && format[i + 1] == c) { r++; i++; } n += r; }
                else n++;
            }
            return n;
        }

        private static string RepairDigits(string window)
        {
            var chars = window.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                int d = DigitValue(window, i, 0, window.Length);
                if (d >= 0 && !(chars[i] >= '0' && chars[i] <= '9')) chars[i] = (char)('0' + d);
            }
            return new string(chars);
        }

        // ------------------------------------------------------------------ email and IBAN

        private static readonly Regex EmailPattern = new Regex(
            @"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}(?![A-Za-z0-9-])",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(TemplateLimits.PatternTimeoutMilliseconds));

        private static Converted Email(string span)
        {
            Match m = EmailPattern.Match(span);
            if (!m.Success) return Converted.Invalid("there is no email address");
            string address = m.Value;
            int at = address.IndexOf('@');
            string local = address.Substring(0, at);
            if (local.StartsWith(".", StringComparison.Ordinal) || local.EndsWith(".", StringComparison.Ordinal) || local.Contains("..")) return Converted.Invalid("the email address is not valid");
            return Converted.Found(local + "@" + address.Substring(at + 1).ToLowerInvariant(), m.Index, m.Index + m.Length);
        }

        private static Converted Iban(string span)
        {
            bool sawCandidate = false;
            for (int s = 0; s + 4 <= span.Length; s++)
            {
                if (s > 0 && char.IsLetterOrDigit(span[s - 1])) continue;
                if (!IsAsciiLetter(span[s]) || !IsAsciiLetter(span[s + 1]) || !char.IsDigit(span[s + 2]) || !char.IsDigit(span[s + 3])) continue;
                // collect letters and digits, allowing single spaces between groups, up to the longest IBAN (34)
                var chars = new StringBuilder();
                var ends = new System.Collections.Generic.List<int>();
                int i = s;
                while (i < span.Length && chars.Length < 34)
                {
                    char c = span[i];
                    if (IsAsciiLetter(c) || (c >= '0' && c <= '9')) { chars.Append(char.ToUpperInvariant(c)); i++; ends.Add(i); continue; }
                    if (c == ' ' && i + 1 < span.Length && span[i + 1] != ' ' && chars.Length > 0) { i++; continue; }
                    break;
                }
                // the longest prefix (15 to 34 characters) that ends at a token boundary and passes mod 97 is the IBAN
                for (int length = chars.Length; length >= 15; length--)
                {
                    int end = ends[length - 1];
                    if (end < span.Length && char.IsLetterOrDigit(span[end])) continue;
                    string candidate = chars.ToString(0, length);
                    if (Mod97(candidate) == 1) return Converted.Found(candidate, s, end);
                }
                sawCandidate = true;                                         // something IBAN-shaped that does not check; a later token may still be the IBAN
            }
            return Converted.Invalid(sawCandidate ? "the IBAN's check digits do not match (mod 97)" : "there is no IBAN (two letters, two digits, then the account)");
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        /// <summary>ISO 13616: move the first four characters to the end, letters become 10..35, and the number mod 97 must be 1.</summary>
        internal static int Mod97(string iban)
        {
            string rearranged = iban.Substring(4) + iban.Substring(0, 4);
            int remainder = 0;
            foreach (char c in rearranged)
            {
                int v = c >= 'A' && c <= 'Z' ? c - 'A' + 10 : c - '0';
                remainder = v >= 10 ? (remainder * 100 + v) % 97 : (remainder * 10 + v) % 97;
            }
            return remainder;
        }
    }
}
