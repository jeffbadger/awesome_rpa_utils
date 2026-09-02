using System;
using System.IO;

namespace FileWatchAutomation
{
    /// <summary>
    /// Shared filter-parsing and mapping logic behind <see cref="FileWatchUtils.WatchForChange"/>/
    /// <c>...Simple</c>, so their change-kind semantics cannot drift between the two overloads.
    /// Internal - not part of the Pega-facing surface.
    /// </summary>
    internal static class FileWatchFilterCore
    {
        /// <summary>
        /// Parses a comma-separated list of <see cref="FileChangeKind"/> names into the
        /// combinable <see cref="WatcherChangeTypes"/> flags value <see cref="FileSystemWatcher.WaitForChanged(WatcherChangeTypes, int)"/>
        /// expects. Null/empty/whitespace-only input means "any kind" (<paramref name="types"/>
        /// is set to <see cref="WatcherChangeTypes.All"/>). Returns <c>false</c> with an error
        /// for any unrecognized token.
        /// </summary>
        internal static bool TryParseChangeKinds(string changeKindsCsv, out WatcherChangeTypes types, out string error)
        {
            types = WatcherChangeTypes.All;
            error = null;
            if (string.IsNullOrWhiteSpace(changeKindsCsv))
                return true;

            WatcherChangeTypes parsed = default;
            bool any = false;
            foreach (string token in changeKindsCsv.Split(','))
            {
                string trimmed = token.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (!Enum.TryParse(trimmed, true, out FileChangeKind kind))
                {
                    error = $"Unknown file change kind '{trimmed}'. Valid values: {string.Join(", ", Enum.GetNames(typeof(FileChangeKind)))}.";
                    return false;
                }
                parsed |= ToWatcherChangeTypes(kind);
                any = true;
            }

            types = any ? parsed : WatcherChangeTypes.All;
            return true;
        }

        /// <summary>Maps a single <see cref="WatcherChangeTypes"/> value (as returned by <see cref="WaitForChangedResult.ChangeType"/>) to the repository-owned <see cref="FileChangeKind"/>.</summary>
        internal static bool TryToFileChangeKind(WatcherChangeTypes changeType, out FileChangeKind kind, out string error)
        {
            error = null;
            switch (changeType)
            {
                case WatcherChangeTypes.Created:
                    kind = FileChangeKind.Created;
                    return true;
                case WatcherChangeTypes.Renamed:
                    kind = FileChangeKind.Renamed;
                    return true;
                case WatcherChangeTypes.Changed:
                    kind = FileChangeKind.Changed;
                    return true;
                case WatcherChangeTypes.Deleted:
                    kind = FileChangeKind.Deleted;
                    return true;
                default:
                    kind = default;
                    error = $"Unrecognized native change type '{changeType}'.";
                    return false;
            }
        }

        private static WatcherChangeTypes ToWatcherChangeTypes(FileChangeKind kind)
        {
            switch (kind)
            {
                case FileChangeKind.Created: return WatcherChangeTypes.Created;
                case FileChangeKind.Renamed: return WatcherChangeTypes.Renamed;
                case FileChangeKind.Changed: return WatcherChangeTypes.Changed;
                case FileChangeKind.Deleted: return WatcherChangeTypes.Deleted;
                default: return WatcherChangeTypes.All;
            }
        }
    }
}
