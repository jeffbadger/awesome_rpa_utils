namespace JsonAutomation
{
    /// <summary>
    /// A comparison operator for filtering a JSON array by a field's value, kept as a
    /// repo-owned enum so this component's public contract doesn't depend on any
    /// particular .NET comparison-operator type.
    /// </summary>
    public enum JsonComparisonOperator
    {
        /// <summary>The field's value equals the comparison value.</summary>
        Equals,
        /// <summary>The field's value does not equal the comparison value.</summary>
        NotEquals,
        /// <summary>The field's value is greater than the comparison value.</summary>
        GreaterThan,
        /// <summary>The field's value is greater than or equal to the comparison value.</summary>
        GreaterThanOrEqual,
        /// <summary>The field's value is less than the comparison value.</summary>
        LessThan,
        /// <summary>The field's value is less than or equal to the comparison value.</summary>
        LessThanOrEqual,
        /// <summary>The field's value, as a string, contains the comparison value as a substring.</summary>
        Contains
    }
}
