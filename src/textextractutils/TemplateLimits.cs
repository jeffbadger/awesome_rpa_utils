namespace TextExtractAutomation
{
    /// <summary>The template's resource limits. Only the text size is configurable; the rest are fixed bounds of the template itself.</summary>
    internal sealed class TemplateLimits
    {
        internal const int DefaultTextCharacters = 1000000;
        internal const int MaxTextCharacters = 10000000;

        internal const int MaxFields = 200;
        internal const int MaxLabelsPerField = 20;
        internal const int MaxLabelLength = 128;
        internal const int MaxNameLength = 128;
        internal const int MaxPatternLength = 1024;
        internal const int MaxDateFormats = 10;
        internal const int MaxTemplateJsonCharacters = 256000;
        internal const int PatternTimeoutMilliseconds = 100;          // one match attempt
        internal const int PatternBudgetMilliseconds = 1000;          // all of one pattern field's matches in one extraction
        internal const int MaxPatternMatches = 10000;                 // matches of one pattern field in one extraction

        internal int MaximumTextCharacters = DefaultTextCharacters;

        internal TemplateLimits Clone() => (TemplateLimits)MemberwiseClone();

        /// <summary>Checks one limit value; returns an explanation or null when it is acceptable.</summary>
        internal static string Check(string name, int value, int maximum)
        {
            if (value < 1) return name + " must be at least 1";
            if (value > maximum) return name + " must not exceed " + maximum;
            return null;
        }
    }
}
