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
        /// <summary>A decimal number, read with the field's DecimalStyle.</summary>
        Decimal,
        /// <summary>A money amount with optional currency symbol or code, grouping, parentheses or trailing minus read with the field's DecimalStyle.</summary>
        Amount,
        /// <summary>A calendar date, read with the field's date formats (default yyyy-MM-dd).</summary>
        Date,
        /// <summary>An email address.</summary>
        Email,
        /// <summary>An IBAN, checksum-validated.</summary>
        Iban,
        /// <summary>A percentage such as 12.5%, read with the field's DecimalStyle.</summary>
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

    /// <summary>How numbers are written in the text. One style per field: 1,234 means a thousand and more in one style and just over one in the other, so it is never guessed.</summary>
    public enum DecimalStyle
    {
        /// <summary>A dot before the decimals and commas (or spaces) between thousands: 1,234.56.</summary>
        DotDecimal,
        /// <summary>A comma before the decimals and dots (or spaces) between thousands: 1.234,56.</summary>
        CommaDecimal
    }
}
