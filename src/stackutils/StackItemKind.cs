namespace StackAutomation
{
    /// <summary>Identifies the kind of value stored in a stack item.</summary>
    public enum StackItemKind
    {
        /// <summary>Plain text, including an empty string.</summary>
        Text = 0,
        /// <summary>A syntactically valid raw JSON value.</summary>
        Json = 1,
        /// <summary>A normalized absolute path to a file that existed when pushed.</summary>
        FileReference = 2
    }
}
