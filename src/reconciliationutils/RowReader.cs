namespace ReconciliationAutomation
{
    /// <summary>What a field lookup found in a row.</summary>
    internal enum FieldKind
    {
        /// <summary>The path does not exist (also: an intermediate value on the path was null or a scalar).</summary>
        Missing,
        /// <summary>The path exists and holds an explicit null.</summary>
        Null,
        /// <summary>A string.</summary>
        String,
        /// <summary>A JSON number written as an integer token (<c>-?(0|[1-9][0-9]*)</c>: no fraction, no exponent).</summary>
        Integer,
        /// <summary>Any other JSON number; <see cref="FieldValue.Text"/> is the original token.</summary>
        Number,
        /// <summary>A JSON true/false; <see cref="FieldValue.Text"/> is <c>true</c> or <c>false</c>.</summary>
        Boolean,
        /// <summary>An object or array at the leaf, or an array met while walking the path.</summary>
        Unsupported
    }

    /// <summary>The result of reading one field: its kind and, where it has one, its exact text.</summary>
    internal readonly struct FieldValue
    {
        internal FieldValue(FieldKind kind, string text = null)
        {
            Kind = kind;
            Text = text;
        }

        internal FieldKind Kind { get; }

        /// <summary>The string value, the original number token, or <c>true</c>/<c>false</c>; for <see cref="FieldKind.Unsupported"/> a short description of what was found (never its content); null for the other kinds.</summary>
        internal string Text { get; }

        internal static readonly FieldValue Missing = new FieldValue(FieldKind.Missing);
        internal static readonly FieldValue Null = new FieldValue(FieldKind.Null);
        internal static readonly FieldValue Unsupported = new FieldValue(FieldKind.Unsupported, "unsupported value");

        internal static FieldValue UnsupportedBecause(string description) => new FieldValue(FieldKind.Unsupported, description);
    }

    /// <summary>
    /// The seam between where rows come from and the matching logic. Matching and comparison read fields only through this,
    /// so JSON input (Release 1) and DataTable input (Release 2) share every rule downstream.
    /// </summary>
    internal interface IRowReader
    {
        /// <summary>False for a row that is not an object (for example a number in the input array).</summary>
        bool IsObject { get; }

        /// <summary>Reads the field at a parsed restricted JSON pointer (see <see cref="JsonPointer"/>).</summary>
        FieldValue Read(string[] pointerSegments);
    }
}
