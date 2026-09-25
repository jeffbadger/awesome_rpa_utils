namespace ReconciliationAutomation
{
    /// <summary>The Release 1 resource limits. Every configured value must be positive and at most its allowed maximum.</summary>
    internal sealed class ReconciliationLimits
    {
        internal const int DefaultRowsPerSide = 50000;
        internal const int MaxRowsPerSide = 250000;
        internal const int DefaultInputCharactersPerSide = 8000000;
        internal const int MaxInputCharactersPerSide = 32000000;
        internal const int DefaultResults = 100000;
        internal const int MaxResults = 500000;
        internal const int DefaultDifferenceDetails = 100000;
        internal const int MaxDifferenceDetails = 500000;

        internal int MaximumRowsPerSide = DefaultRowsPerSide;
        internal int MaximumInputCharactersPerSide = DefaultInputCharactersPerSide;
        internal int MaximumResults = DefaultResults;
        internal int MaximumDifferenceDetails = DefaultDifferenceDetails;

        internal ReconciliationLimits Clone() => (ReconciliationLimits)MemberwiseClone();

        /// <summary>Checks one limit value; returns an explanation or null when it is acceptable.</summary>
        internal static string Check(string name, int value, int maximum)
        {
            if (value < 1) return name + " must be at least 1";
            if (value > maximum) return name + " must not exceed " + maximum;
            return null;
        }

        /// <summary>The first problem with these limits, or null when all four are acceptable.</summary>
        internal string FirstProblem() =>
            Check("maximumRowsPerSide", MaximumRowsPerSide, MaxRowsPerSide)
            ?? Check("maximumInputCharactersPerSide", MaximumInputCharactersPerSide, MaxInputCharactersPerSide)
            ?? Check("maximumResults", MaximumResults, MaxResults)
            ?? Check("maximumDifferenceDetails", MaximumDifferenceDetails, MaxDifferenceDetails);
    }
}
