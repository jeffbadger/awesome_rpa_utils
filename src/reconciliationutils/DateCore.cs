using System;
using System.Globalization;
using System.Text;

namespace ReconciliationAutomation
{
    /// <summary>
    /// Calendar-date and instant parsing for the date rules. Everything is invariant-culture and zone-independent: a calendar date is a plain
    /// day number, an instant is a UTC tick count, and no machine culture, local zone, daylight-saving rule or business calendar is ever consulted.
    /// </summary>
    internal static class DateCore
    {
        /// <summary>The format used by the Simple forms.</summary>
        internal const string DefaultFormat = "yyyy-MM-dd";

        internal const int MaxFormatLength = 64;
        internal const int MaxTextLength = 64;

        /// <summary>The literal separator characters allowed between the date parts, outside quotes.</summary>
        private const string Separators = "-/. ,";

        /// <summary>
        /// Checks a calendar-date format. It must be built only from <c>yyyy</c>, <c>MM</c> and <c>dd</c> (each exactly once, so a date part is never
        /// missing), separators from <c>-/. ,</c>, and quoted literals such as <c>'T'</c>. Time, offset and era tokens, other letters, single-letter
        /// or longer runs (<c>d</c>, <c>MMM</c>) and escape characters are all refused. Returns a problem, or null when the format is acceptable.
        /// </summary>
        internal static string CheckFormat(string format)
        {
            if (string.IsNullOrEmpty(format)) return "a format is required, for example " + DefaultFormat;
            if (format.Length > MaxFormatLength) return "the format is longer than " + MaxFormatLength + " characters";
            if (TextCheck.HasUnpairedSurrogate(format)) return "the format contains text that is not valid (an unpaired surrogate character)";

            bool year = false, month = false, day = false;
            int i = 0;
            while (i < format.Length)
            {
                char c = format[i];
                if (c == 'y' || c == 'M' || c == 'd')
                {
                    int run = 1;
                    while (i + run < format.Length && format[i + run] == c) run++;
                    string token = new string(c, run);
                    if (c == 'y' && run == 4 && !year) year = true;
                    else if (c == 'M' && run == 2 && !month) month = true;
                    else if (c == 'd' && run == 2 && !day) day = true;
                    else return "'" + token + "' is not allowed here: use yyyy, MM and dd, each once";
                    i += run;
                }
                else if (c == '\'')
                {
                    int end = format.IndexOf('\'', i + 1);
                    if (end < 0) return "a quoted literal is not closed";
                    if (end == i + 1) return "a quoted literal is empty";
                    for (int k = i + 1; k < end; k++)
                        if (format[k] == '\\' || char.IsControl(format[k])) return "a quoted literal may not contain a backslash or a control character";
                    i = end + 1;
                }
                else if (Separators.IndexOf(c) >= 0) i++;
                else return "the character '" + c + "' is not allowed in a date format (only yyyy, MM, dd, the separators " + Separators + " and quoted literals; time and offset tokens are not supported)";
            }
            if (!year || !month || !day) return "the format must contain yyyy, MM and dd";
            return null;
        }

        /// <summary>Parses a calendar date. The result is its day number (days since 0001-01-01) and its normalized form <c>yyyy-MM-dd</c>. Surrounding white space is not accepted.</summary>
        internal static bool TryParseDate(string text, string format, out long dayNumber, out string normalized)
        {
            dayNumber = 0;
            normalized = null;
            if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength) return false;
            if (!DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)) return false;
            dayNumber = date.DayNumber;
            normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        private static readonly string[] InstantFormats =
        {
            "yyyy-MM-dd'T'HH:mm:sszzz",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"
        };

        /// <summary>
        /// Parses an instant. Only the four documented shapes are accepted: <c>yyyy-MM-ddTHH:mm:ss</c> with an optional fraction of one to seven digits,
        /// then either an explicit offset <c>+hh:mm</c>/<c>-hh:mm</c> or a literal <c>Z</c> (read explicitly as UTC). A value with no offset is refused,
        /// never read in the machine's zone. The result is the UTC tick count and its normalized form.
        /// </summary>
        internal static bool TryParseInstant(string text, out long utcTicks, out string normalized)
        {
            utcTicks = 0;
            normalized = null;
            if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength) return false;

            // Shape first, strictly (ASCII digits only, upper-case T and Z), then the calendar/clock/offset ranges by exact parsing.
            int shape = ShapeOf(text);
            if (shape < 0) return false;
            if (!DateTimeOffset.TryParseExact(text, InstantFormats[shape], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset value)) return false;
            utcTicks = value.UtcTicks;
            normalized = value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>0 = offset, no fraction; 1 = offset with fraction; 2 = Z, no fraction; 3 = Z with fraction; -1 = not one of the shapes.</summary>
        private static int ShapeOf(string s)
        {
            // yyyy-MM-ddTHH:mm:ss = 19 characters
            if (s.Length < 20) return -1;
            for (int i = 0; i < 19; i++)
            {
                char c = s[i];
                bool ok = i == 4 || i == 7 ? c == '-' : i == 10 ? c == 'T' : i == 13 || i == 16 ? c == ':' : IsDigit(c);
                if (!ok) return -1;
            }
            int pos = 19;
            bool fraction = false;
            if (s[pos] == '.')
            {
                int start = ++pos;
                while (pos < s.Length && IsDigit(s[pos])) pos++;
                int digits = pos - start;
                if (digits < 1 || digits > 7) return -1;
                fraction = true;
            }
            string rest = s.Substring(pos);
            if (rest == "Z") return fraction ? 3 : 2;
            if (rest.Length == 6 && (rest[0] == '+' || rest[0] == '-') && IsDigit(rest[1]) && IsDigit(rest[2]) && rest[3] == ':' && IsDigit(rest[4]) && IsDigit(rest[5]))
                return fraction ? 1 : 0;
            return -1;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        /// <summary>A tick difference as exact seconds text, for example <c>90</c>, <c>-0.5</c> or <c>0.0000001</c> (no trailing zeros).</summary>
        internal static string SecondsText(long ticks)
        {
            if (ticks == 0) return "0";
            bool negative = ticks < 0;
            ulong magnitude = negative ? (ulong)(-(ticks + 1)) + 1UL : (ulong)ticks;      // safe for long.MinValue
            ulong whole = magnitude / (ulong)TimeSpan.TicksPerSecond;
            ulong fraction = magnitude % (ulong)TimeSpan.TicksPerSecond;
            var b = new StringBuilder();
            if (negative) b.Append('-');
            b.Append(whole.ToString(CultureInfo.InvariantCulture));
            if (fraction != 0) b.Append('.').Append(fraction.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0'));
            return b.ToString();
        }
    }
}
