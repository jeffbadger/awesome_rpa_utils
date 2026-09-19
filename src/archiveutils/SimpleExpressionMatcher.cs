using System;
#if !NETFRAMEWORK
using System.IO.Enumeration;
#endif

namespace ArchiveAutomation
{
    // Simple-expression (glob) matching for archive entry names. On net8/net10
    // this delegates straight to the BCL; net48 lacks System.IO.Enumeration, so
    // this ships a hand-rolled matcher with the same core semantics: '*' matches
    // any run of characters, '?' matches exactly one, and '[' ... ']' is a
    // character class (ranges via '-', negation via a leading '^' or '!'). All
    // other characters are matched ordinally and case-sensitively (the BCL
    // default); unmatched '[' is treated as a literal, like the BCL does.
    internal static class SimpleExpressionMatcher
    {
        internal static bool MatchesSimpleExpression(string expression, string value)
        {
#if NETFRAMEWORK
            if (expression == null || value == null)
                return false;
            return MatchSimple(expression, 0, value, 0);
#else
            return FileSystemName.MatchesSimpleExpression(expression, value);
#endif
        }

#if NETFRAMEWORK
        private static bool MatchSimple(string expression, int ei, string value, int vi)
        {
            while (ei < expression.Length)
            {
                char c = expression[ei];
                if (c == '*')
                {
                    // Collapse consecutive stars, then try every possible match start.
                    while (ei + 1 < expression.Length && expression[ei + 1] == '*')
                        ei++;
                    for (int k = vi; k <= value.Length; k++)
                        if (MatchSimple(expression, ei + 1, value, k))
                            return true;
                    return false;
                }
                if (vi >= value.Length)
                    return false;
                if (c == '?')
                {
                    ei++;
                    vi++;
                    continue;
                }
                if (c == '[')
                {
                    int close = expression.IndexOf(']', ei + 1);
                    if (close < 0)
                    {
                        // Unmatched bracket: the BCL treats '[' literally.
                        if (value[vi] != '[')
                            return false;
                        ei++;
                        vi++;
                        continue;
                    }
                    if (!MatchCharClass(expression, ei + 1, close, value[vi]))
                        return false;
                    ei = close + 1;
                    vi++;
                    continue;
                }
                if (c != value[vi])
                    return false;
                ei++;
                vi++;
            }
            return vi == value.Length;
        }

        private static bool MatchCharClass(string expression, int start, int end, char ch)
        {
            bool negate = false;
            int i = start;
            if (i < end && (expression[i] == '^' || expression[i] == '!'))
            {
                negate = true;
                i++;
            }
            bool matched = false;
            while (i < end)
            {
                if (i + 2 < end && expression[i + 1] == '-')
                {
                    if (ch >= expression[i] && ch <= expression[i + 2])
                        matched = true;
                    i += 3;
                }
                else
                {
                    if (expression[i] == ch)
                        matched = true;
                    i++;
                }
            }
            return negate ? !matched : matched;
        }
#endif
    }
}