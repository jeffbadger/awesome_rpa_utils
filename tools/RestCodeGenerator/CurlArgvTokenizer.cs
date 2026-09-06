using System.Collections.Generic;
using System.Text;

namespace RestCodeGenerator;

/// <summary>Minimal shell-argv lexer for curl commands: handles single/double-quoted values,
/// backslash escapes, and backslash-newline line continuations (covers real "Copy as cURL"
/// exports from browser devtools/Postman/Insomnia) — not a full POSIX shell.</summary>
internal static class CurlArgvTokenizer
{
    internal static List<string> Tokenize(string text)
    {
        var joined = text.Replace("\\\r\n", " ").Replace("\\\n", " ");

        var tokens = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        var i = 0;
        while (i < joined.Length)
        {
            var c = joined[i];
            if (char.IsWhiteSpace(c))
            {
                if (inToken) { tokens.Add(current.ToString()); current.Clear(); inToken = false; }
                i++;
                continue;
            }
            inToken = true;
            if (c == '\'')
            {
                i++;
                while (i < joined.Length && joined[i] != '\'') { current.Append(joined[i]); i++; }
                i++;   // skip closing quote (best-effort: a missing one just stops here)
            }
            else if (c == '"')
            {
                i++;
                while (i < joined.Length && joined[i] != '"')
                {
                    if (joined[i] == '\\' && i + 1 < joined.Length &&
                        (joined[i + 1] is '"' or '\\' or '$' or '`'))
                    {
                        current.Append(joined[i + 1]);
                        i += 2;
                    }
                    else { current.Append(joined[i]); i++; }
                }
                i++;   // skip closing quote
            }
            else if (c == '\\' && i + 1 < joined.Length)
            {
                current.Append(joined[i + 1]);
                i += 2;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }
        if (inToken) tokens.Add(current.ToString());
        return tokens;
    }
}
