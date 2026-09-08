namespace JsonAutomation
{
    /// <summary>
    /// The kind of value found at a JSON path, reported without leaking Newtonsoft's own
    /// <c>JTokenType</c> (which has more granularity than a Pega automation needs, e.g.
    /// separate Integer/Float members) into this component's public contract.
    /// </summary>
    public enum JsonValueKind
    {
        /// <summary>The path did not resolve to any value.</summary>
        NotFound,
        /// <summary>The value at the path is a JSON null literal.</summary>
        Null,
        /// <summary>The value at the path is a string.</summary>
        String,
        /// <summary>The value at the path is a number (integer or floating-point).</summary>
        Number,
        /// <summary>The value at the path is a boolean.</summary>
        Boolean,
        /// <summary>The value at the path is a JSON array.</summary>
        Array,
        /// <summary>The value at the path is a JSON object.</summary>
        Object
    }
}
