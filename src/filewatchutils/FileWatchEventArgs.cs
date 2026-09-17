using System;

namespace FileWatchAutomation
{
    /// <summary>
    /// Data for <see cref="FileWatchUtils.Created"/>/<see cref="FileWatchUtils.Changed"/>/
    /// <see cref="FileWatchUtils.Deleted"/> - a plain scalar-property event-args type
    /// instead of the BCL's <see cref="System.IO.FileSystemEventArgs"/>, matching this
    /// suite's preference for repository-owned data on the Pega-facing boundary.
    /// </summary>
    public sealed class FileWatchChangeEventArgs : EventArgs
    {
        /// <summary>The full path of the file or directory the event was reported for.</summary>
        public string FullPath { get; }

        /// <summary>Which kind of change this is. Redundant with which event fired, kept for convenience.</summary>
        public FileChangeKind Kind { get; }

        internal FileWatchChangeEventArgs(string fullPath, FileChangeKind kind)
        {
            FullPath = fullPath;
            Kind = kind;
        }
    }

    /// <summary>
    /// Data for <see cref="FileWatchUtils.Renamed"/> - carries both the new and old path,
    /// since a rename is the one change kind <see cref="FileWatchChangeEventArgs"/> cannot
    /// describe with a single path.
    /// </summary>
    public sealed class FileWatchRenamedEventArgs : EventArgs
    {
        /// <summary>The file or directory's new full path.</summary>
        public string FullPath { get; }

        /// <summary>The file or directory's full path before the rename.</summary>
        public string OldFullPath { get; }

        internal FileWatchRenamedEventArgs(string fullPath, string oldFullPath)
        {
            FullPath = fullPath;
            OldFullPath = oldFullPath;
        }
    }

    /// <summary>
    /// Data for <see cref="FileWatchUtils.WatchError"/> - raised when the underlying watcher
    /// itself fails (e.g. an internal notification-buffer overflow), not when a subscriber's
    /// own handler throws. The watch has already stopped by the time this fires.
    /// </summary>
    public sealed class FileWatchErrorEventArgs : EventArgs
    {
        /// <summary>A human-readable description of what went wrong.</summary>
        public string Message { get; }

        internal FileWatchErrorEventArgs(string message)
        {
            Message = message;
        }
    }
}
