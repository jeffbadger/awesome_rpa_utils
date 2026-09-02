namespace FileWatchAutomation
{
    /// <summary>
    /// The kind of file-system change <see cref="FileWatchUtils.WatchForChange"/> detected.
    /// A single-value output counterpart to the combinable <c>changeKindsFilter</c> input
    /// CSV those methods accept (parsed internally to <see cref="System.IO.WatcherChangeTypes"/>
    /// by <see cref="FileWatchFilterCore.TryParseChangeKinds"/>).
    /// </summary>
    public enum FileChangeKind
    {
        /// <summary>A file or directory was created.</summary>
        Created,
        /// <summary>A file or directory was renamed.</summary>
        Renamed,
        /// <summary>A file's contents, attributes, or timestamps changed.</summary>
        Changed,
        /// <summary>A file or directory was deleted.</summary>
        Deleted
    }
}
