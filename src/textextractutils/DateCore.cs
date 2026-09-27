using System;
using System.Globalization;
using System.Text;

namespace TextExtractAutomation
{
    /// <summary>
    /// Calendar-date formats and parsing for Date fields (copied from ReconciliationUtils: components are standalone). Invariant culture only: a date is a plain
    /// day number, and no machine culture, time zone or business calendar is ever consulted.
    /// </summary>
    internal static class DateCore
    {
        /// <summary>The format used when a Date field gives none.</summary>
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
    }
}
