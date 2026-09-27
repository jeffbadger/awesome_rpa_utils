namespace TextExtractAutomation
{
    /// <summary>Where a field's value sits relative to its label.</summary>
    public enum ValuePosition
    {
        /// <summary>After the label on the same line, up to the next known label or the end of the line.</summary>
        SameLine,
        /// <summary>On the next line that is not empty.</summary>
        NextLine,
        /// <summary>On the next line, under the label's column (aligned forms and screens).</summary>
        Below
    }

    /// <summary>What a value must look like; the type decides what is captured, whether it is valid and how it is normalized.</summary>
    public enum FieldType
    {
        /// <summary>Any text, trimmed.</summary>
        Text,
        /// <summary>One token of letters, digits and - / . _ (an invoice or policy number).</summary>
        Code,
        /// <summary>A whole number.</summary>
        Integer,
        /// <summary>A decimal number (format: DotDecimal or CommaDecimal).</summary>
        Decimal,
        /// <summary>A money amount with optional currency symbol or code, grouping, parentheses or trailing minus (format: DotDecimal or CommaDecimal).</summary>
        Amount,
        /// <summary>A calendar date (format: one or more date formats separated by |, default yyyy-MM-dd).</summary>
        Date,
        /// <summary>An email address.</summary>
        Email,
        /// <summary>An IBAN, checksum-validated.</summary>
        Iban,
        /// <summary>A percentage such as 12.5% (format: DotDecimal or CommaDecimal).</summary>
        Percentage
    }

    /// <summary>What to do when a field's label is found more than once.</summary>
    public enum Occurrence
    {
        /// <summary>The label must appear once; more than once is reported as AmbiguousValue.</summary>
        RequireUnique,
        /// <summary>Use the first occurrence.</summary>
        First,
        /// <summary>Use the last occurrence.</summary>
        Last
    }
}
