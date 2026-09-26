namespace ReconciliationAutomation
{
    internal static class TextCheck
    {
        /// <summary>
        /// True when the string contains a UTF-16 surrogate that has no partner (a lone high or low half). Such text cannot be encoded as
        /// UTF-8: the JSON parser would throw or, worse, silently substitute U+FFFD, so two different bad strings could end up as the same
        /// key. The component refuses it up front with a clear finding.
        /// </summary>
        internal static bool HasUnpairedSurrogate(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                    else return true;
                }
                else if (char.IsLowSurrogate(c)) return true;
            }
            return false;
        }
    }
}
