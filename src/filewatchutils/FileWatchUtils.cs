using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace FileWatchAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component for coordinating with files produced by other
    /// applications: waiting for a file to appear/disappear/change, waiting until a file
    /// is stable (size and last-write time unchanged for a continuous interval) or no
    /// longer locked, waiting for a directory to contain a matching file, watching for
    /// created/renamed/changed/deleted events, atomically moving/replacing a completed
    /// file, claiming a work file for exclusive processing, hashing files to detect
    /// duplicate or unchanged inputs, and reading file metadata as scalars or JSON.
    /// <para>
    /// Like every component in this suite, all methods honor the never-throws contract:
    /// invalid input (a null/empty path, a negative timeout) and runtime failures (a
    /// missing file/directory, an access-denied condition, a malformed argument) return
    /// <c>false</c> with a descriptive message instead of throwing. Timeouts are likewise
    /// <c>false</c> returns - a slow-arriving or slow-writing file is a normal, checkable
    /// outcome.
    /// </para>
    /// <para>
    /// Unlike every other component in this suite, every operation here is plain
    /// cross-platform BCL (<see cref="System.IO"/>, <see cref="System.Security.Cryptography"/>,
    /// <see cref="FileSystemWatcher"/>) with zero P/Invoke - the <c>-windows</c> target
    /// framework is kept only for consistency with the rest of the suite, not because any
    /// method here needs a Windows-only API.
    /// </para>
    /// </summary>
    [Description("Coordinates with files produced by other applications: wait for existence/deletion/change/stability/unlock, " +
                 "watch for filesystem events, atomically move/replace/claim files, hash files, and read file metadata. " +
                 "All methods return True/False with a failure message instead of throwing. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class FileWatchUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public FileWatchUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public FileWatchUtils(IContainer container)
        {
            container?.Add(this);
        }

        private readonly object _watchLock = new object();
        private FileSystemWatcher _watcher;
        private bool _disposed;

        /// <summary>
        /// Stops and disposes the background watcher, if one is running. Safe to call
        /// multiple times. This is the only resource this component ever holds - every
        /// other method here is self-contained per call.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Recorded before tearing down the watcher so a StartWatching call that
                // arrives concurrently with (or immediately after) disposal cannot create
                // a live watcher this now-disposed instance would never stop again.
                lock (_watchLock)
                {
                    _disposed = true;
                }
                StopWatchingCore();
            }

            base.Dispose(disposing);
        }

        #region Wait - Existence & Change

        /// <summary>Polls until a file appears at the given path, or the timeout elapses. Never throws.</summary>
        /// <param name="path">The file path to watch.</param>
        /// <param name="timeoutMs">The maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">The delay between checks, in milliseconds. Values below 1 are treated as 1.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed before the file appeared.</param>
        /// <param name="message"><c>null</c> on success (including a timeout); a failure reason otherwise.</param>
        [Category("FileWatch - Wait")]
        [Description("Polls until a file appears at the given path, or the timeout elapses. Never throws.")]
        public bool WaitForFileToExist(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A file path is required.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must not be negative.";
                    return false;
                }
                if (pollIntervalMs < 1) pollIntervalMs = 1;

                int start = Environment.TickCount;
                while (true)
                {
                    if (File.Exists(path))
                    {
                        message = null;
                        return true;
                    }
                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForFileToExist", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForFileToExist"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("FileWatch - Wait")]
        [Description("Same as WaitForFileToExist, without the timedOut output. Never throws.")]
        public bool WaitForFileToExistSimple(string path, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForFileToExist(path, timeoutMs, pollIntervalMs, out _, out message);
        }

        /// <summary>Polls until the file at the given path no longer exists, or the timeout elapses. Never throws.</summary>
        [Category("FileWatch - Wait")]
        [Description("Polls until a file at the given path is deleted, or the timeout elapses. Never throws.")]
        public bool WaitForFileToBeDeleted(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A file path is required.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must not be negative.";
                    return false;
                }
                if (pollIntervalMs < 1) pollIntervalMs = 1;

                int start = Environment.TickCount;
                while (true)
                {
                    if (!File.Exists(path))
                    {
                        message = null;
                        return true;
                    }
                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForFileToBeDeleted", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForFileToBeDeleted"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("FileWatch - Wait")]
        [Description("Same as WaitForFileToBeDeleted, without the timedOut output. Never throws.")]
        public bool WaitForFileToBeDeletedSimple(string path, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForFileToBeDeleted(path, timeoutMs, pollIntervalMs, out _, out message);
        }

        /// <summary>
        /// Snapshots a file's size and last-write time, then polls until either differs
        /// (or the file is deleted, itself a change from "existing" to "gone"), or the
        /// timeout elapses. The file must already exist when this is called - see
        /// <see cref="WaitForFileToExist"/> first if it may not have been created yet.
        /// Never throws.
        /// </summary>
        [Category("FileWatch - Wait")]
        [Description("Polls until a file's size or last-write time changes from what it was at call time, or the timeout elapses. Never throws.")]
        public bool WaitForFileToChange(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A file path is required.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must not be negative.";
                    return false;
                }
                if (pollIntervalMs < 1) pollIntervalMs = 1;

                var initial = new FileInfo(path);
                if (!initial.Exists)
                {
                    message = $"File '{path}' does not exist. Call WaitForFileToExist first if it may not have been created yet.";
                    return false;
                }
                long initialSize = initial.Length;
                DateTime initialWriteUtc = initial.LastWriteTimeUtc;

                int start = Environment.TickCount;
                while (true)
                {
                    var current = new FileInfo(path);
                    if (!current.Exists || current.Length != initialSize || current.LastWriteTimeUtc != initialWriteUtc)
                    {
                        message = null;
                        return true;
                    }
                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForFileToChange", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForFileToChange"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("FileWatch - Wait")]
        [Description("Same as WaitForFileToChange, without the timedOut output. Never throws.")]
        public bool WaitForFileToChangeSimple(string path, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForFileToChange(path, timeoutMs, pollIntervalMs, out _, out message);
        }

        #endregion

        #region Wait - Stability

        /// <summary>
        /// The flagship method of this component: polls until a file's size and last-write
        /// time have been unchanged for a continuous <paramref name="stableDurationMs"/>
        /// window (not just two samples that far apart), or the overall timeout elapses.
        /// Prevents "the robot opened the export before the application finished writing
        /// it" failures. The file must already exist when this is called - a nonexistent
        /// file is a hard, specific failure (see <see cref="WaitForFileToExist"/>), not a
        /// silent wait for it to appear. Never throws.
        /// </summary>
        /// <param name="path">The file path to watch.</param>
        /// <param name="stableDurationMs">How long the file's size and last-write time must remain unchanged before it is considered stable, in milliseconds.</param>
        /// <param name="timeoutMs">The overall maximum time to wait, in milliseconds. If shorter than <paramref name="stableDurationMs"/>, a still-changing file simply times out rather than ever reporting stable.</param>
        /// <param name="pollIntervalMs">The delay between checks, in milliseconds. Values below 1 are treated as 1.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed before the file became stable.</param>
        /// <param name="message"><c>null</c> on success (including a timeout); a failure reason otherwise (including the file not existing, or being deleted while waiting).</param>
        [Category("FileWatch - Wait")]
        [Description("Polls until a file's size and last-write time are unchanged for a continuous interval, or the timeout elapses. Never throws.")]
        public bool WaitForFileStable(string path, int stableDurationMs, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A file path is required.";
                    return false;
                }
                if (stableDurationMs < 0)
                {
                    message = "stableDurationMs must not be negative.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must not be negative.";
                    return false;
                }
                if (pollIntervalMs < 1) pollIntervalMs = 1;

                var initial = new FileInfo(path);
                if (!initial.Exists)
                {
                    message = $"File '{path}' does not exist. Call WaitForFileToExist first if it may not have been created yet.";
                    return false;
                }

                long lastSize = initial.Length;
                DateTime lastWriteUtc = initial.LastWriteTimeUtc;
                int start = Environment.TickCount;
                int lastChangedTick = start;

                while (true)
                {
                    var current = new FileInfo(path);
                    if (!current.Exists)
                    {
                        message = $"File '{path}' was deleted while waiting for it to become stable.";
                        return false;
                    }

                    int now = Environment.TickCount;
                    if (current.Length != lastSize || current.LastWriteTimeUtc != lastWriteUtc)
                    {
                        lastSize = current.Length;
                        lastWriteUtc = current.LastWriteTimeUtc;
                        lastChangedTick = now;
                    }
                    else if (unchecked(now - lastChangedTick) >= stableDurationMs)
                    {
                        message = null;
                        return true;
                    }

                    if (unchecked(now - start) >= timeoutMs)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForFileStable", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForFileStable"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("FileWatch - Wait")]
        [Description("Same as WaitForFileStable, without the timedOut output. Never throws.")]
        public bool WaitForFileStableSimple(string path, int stableDurationMs, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForFileStable(path, stableDurationMs, timeoutMs, pollIntervalMs, out _, out message);
        }

        #endregion

        #region Wait - Lock

        /// <summary>Returns <c>true</c> if the file is currently locked (cannot be opened for exclusive read access). Never throws.</summary>
        /// <param name="path">The file path to check.</param>
        /// <param name="message"><c>null</c> when the file is locked; otherwise a human-readable reason it isn't (or the check failed).</param>
        [Category("FileWatch - Wait")]
        [Description("Returns True if the file is currently locked (cannot be opened for exclusive read access). Never throws.")]
        public bool IsFileLockedSimple(string path, out string message)
        {
            return TryIsFileLocked(path, out _, out message);
        }

        /// <summary>
        /// Same as <see cref="IsFileLockedSimple"/>, plus a <paramref name="querySucceeded"/>
        /// output so the automation doesn't need to interpret <paramref name="message"/> to
        /// tell "genuinely unlocked" apart from "the check itself failed" (e.g. the file
        /// doesn't exist). Never throws.
        /// </summary>
        [Category("FileWatch - Wait")]
        [Description("Same as IsFileLockedSimple, plus a querySucceeded output. Never throws.")]
        public bool IsFileLocked(string path, out bool querySucceeded, out string message)
        {
            return TryIsFileLocked(path, out querySucceeded, out message);
        }

        private static bool TryIsFileLocked(string path, out bool querySucceeded, out string message)
        {
            querySucceeded = false;
            message = default;
            if (string.IsNullOrWhiteSpace(path))
            {
                message = "A file path is required.";
                return false;
            }
            if (!File.Exists(path))
            {
                message = $"File '{path}' does not exist.";
                return false;
            }

            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    // Opened exclusively without issue - not locked.
                }
                querySucceeded = true;
                message = "The file is not locked.";
                return false;
            }
            catch (IOException)
            {
                // A sharing violation - another handle is open somewhere with an incompatible share mode.
                querySucceeded = true;
                message = null;
                return true;
            }
        }

        /// <summary>
        /// Polls <see cref="IsFileLocked"/> until it reports the file unlocked, or the
        /// timeout elapses. Note: a writer that keeps <see cref="FileShare.ReadWrite"/> open
        /// the entire time it writes will report "unlocked" throughout, even mid-write -
        /// use <see cref="WaitForFileStable"/> to detect "the writer is actually done",
        /// not this method alone. Never throws.
        /// </summary>
        [Category("FileWatch - Wait")]
        [Description("Polls until a file is no longer locked, or the timeout elapses. Never throws.")]
        public bool WaitForFileUnlocked(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)
        {
            timedOut = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A file path is required.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must not be negative.";
                    return false;
                }
                if (pollIntervalMs < 1) pollIntervalMs = 1;

                int start = Environment.TickCount;
                while (true)
                {
                    bool locked = TryIsFileLocked(path, out bool querySucceeded, out string lockMessage);
                    if (querySucceeded && !locked)
                    {
                        message = null;
                        return true;
                    }
                    if (!querySucceeded)
                    {
                        // A genuine failure (bad path, missing file) rather than "still locked" -
                        // surface it immediately rather than spinning until timeout.
                        message = lockMessage;
                        return false;
                    }

                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForFileUnlocked", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForFileUnlocked"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("FileWatch - Wait")]
        [Description("Same as WaitForFileUnlocked, without the timedOut output. Never throws.")]
        public bool WaitForFileUnlockedSimple(string path, int timeoutMs, int pollIntervalMs, out string message)
        {
            return WaitForFileUnlocked(path, timeoutMs, pollIntervalMs, out _, out message);
        }

        #endregion

        #region Wait - Directory Pattern

        /// <summary>Polls a directory until it contains at least one file matching a search pattern, or the timeout elapses. Never throws.</summary>
        /// <param name="directoryPath">The directory to watch.</param>
        /// <param name="searchPattern">A <see cref="Directory.GetFiles(string, string)"/>-style pattern, e.g. <c>"*.csv"</c>.</param>
        /// <param name="includeSubdirectories">Whether to also search subdirectories.</param>
        /// <param name="timeoutMs">The maximum time to wait, in milliseconds.</param>
        /// <param name="pollIntervalMs">The delay between checks, in milliseconds. Values below 1 are treated as 1.</param>
        /// <param name="matchedFilePath">The first matching file's full path on success; unset otherwise.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed before a match appeared.</param>
        /// <param name="message"><c>null</c> on success (including a timeout); a failure reason otherwise.</param>
        [Category("FileWatch - Wait")]
        [Description("Polls a directory until it contains a file matching a pattern, or the timeout elapses. Never throws.")]
        public bool WaitForFileMatchingPattern(string directoryPath, string searchPattern, bool includeSubdirectories, int timeoutMs, int pollIntervalMs, out string matchedFilePath, out bool timedOut, out string message)
        {
            matchedFilePath = default;
            timedOut = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    message = "A directory path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(searchPattern))
                {
                    message = "A search pattern is required.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must not be negative.";
                    return false;
                }
                if (pollIntervalMs < 1) pollIntervalMs = 1;
                if (!Directory.Exists(directoryPath))
                {
                    message = $"Directory '{directoryPath}' does not exist.";
                    return false;
                }

                SearchOption option = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                int start = Environment.TickCount;
                while (true)
                {
                    string[] matches = Directory.GetFiles(directoryPath, searchPattern, option);
                    if (matches.Length > 0)
                    {
                        matchedFilePath = matches[0];
                        message = null;
                        return true;
                    }
                    if (unchecked(Environment.TickCount - start) >= timeoutMs)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WaitForFileMatchingPattern", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WaitForFileMatchingPattern"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("FileWatch - Wait")]
        [Description("Same as WaitForFileMatchingPattern, without the timedOut output. Never throws.")]
        public bool WaitForFileMatchingPatternSimple(string directoryPath, string searchPattern, bool includeSubdirectories, int timeoutMs, int pollIntervalMs, out string matchedFilePath, out string message)
        {
            return WaitForFileMatchingPattern(directoryPath, searchPattern, includeSubdirectories, timeoutMs, pollIntervalMs, out matchedFilePath, out _, out message);
        }

        #endregion

        #region Watch

        /// <summary>
        /// Blocks until a filesystem event matching <paramref name="changeKindsFilter"/>
        /// occurs in <paramref name="directoryPath"/>, or the timeout elapses, via
        /// <see cref="FileSystemWatcher.WaitForChanged(WatcherChangeTypes, int)"/> - this
        /// suite's usual synchronous <c>WaitForX</c> shape, backed here by the OS's native
        /// change-notification API instead of polling. The directory must already exist.
        /// Never throws.
        /// </summary>
        /// <param name="directoryPath">The directory to watch. Must already exist.</param>
        /// <param name="filter">A <see cref="FileSystemWatcher.Filter"/>-style pattern (e.g. <c>"*.csv"</c>), or null/empty for all files.</param>
        /// <param name="changeKindsFilter">A comma-separated list of <see cref="FileChangeKind"/> names to watch for, or null/empty for all kinds.</param>
        /// <param name="includeSubdirectories">Whether to also watch subdirectories.</param>
        /// <param name="timeoutMs">The maximum time to wait, in milliseconds.</param>
        /// <param name="changedPath">The full path of the file/directory the event was reported for, on success.</param>
        /// <param name="detectedKind">The kind of change detected, on success.</param>
        /// <param name="timedOut"><c>true</c> if the timeout elapsed before a matching event occurred.</param>
        /// <param name="message"><c>null</c> on success (including a timeout); a failure reason otherwise.</param>
        [Category("FileWatch - Watch")]
        [Description("Blocks until a matching created/renamed/changed/deleted event occurs in a directory, or the timeout elapses. Never throws.")]
        public bool WatchForChange(string directoryPath, string filter, string changeKindsFilter, bool includeSubdirectories, int timeoutMs, out string changedPath, out FileChangeKind detectedKind, out bool timedOut, out string message)
        {
            changedPath = default;
            detectedKind = default;
            timedOut = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    message = "A directory path is required.";
                    return false;
                }
                if (timeoutMs < 0)
                {
                    message = "timeoutMs must not be negative.";
                    return false;
                }
                if (!Directory.Exists(directoryPath))
                {
                    message = $"Directory '{directoryPath}' does not exist. WatchForChange requires the directory to already exist.";
                    return false;
                }
                if (!FileWatchFilterCore.TryParseChangeKinds(changeKindsFilter, out WatcherChangeTypes types, out message))
                    return false;

                using (var watcher = new FileSystemWatcher(directoryPath))
                {
                    watcher.IncludeSubdirectories = includeSubdirectories;
                    if (!string.IsNullOrEmpty(filter))
                        watcher.Filter = filter;

                    WaitForChangedResult result = watcher.WaitForChanged(types, timeoutMs);
                    if (result.TimedOut)
                    {
                        timedOut = true;
                        message = null;
                        return false;
                    }

                    if (!FileWatchFilterCore.TryToFileChangeKind(result.ChangeType, out detectedKind, out message))
                        return false;

                    changedPath = Path.Combine(directoryPath, result.Name ?? string.Empty);
                    message = null;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("WatchForChange", ex);
                return false;
            }
        }

        /// <summary>Same as <see cref="WatchForChange"/>, without the <c>timedOut</c> output. Never throws.</summary>
        [Category("FileWatch - Watch")]
        [Description("Same as WatchForChange, without the timedOut output. Never throws.")]
        public bool WatchForChangeSimple(string directoryPath, string filter, string changeKindsFilter, bool includeSubdirectories, int timeoutMs, out string changedPath, out FileChangeKind detectedKind, out string message)
        {
            return WatchForChange(directoryPath, filter, changeKindsFilter, includeSubdirectories, timeoutMs, out changedPath, out detectedKind, out _, out message);
        }

        #endregion

        #region Watch - Background

        /// <summary>
        /// Starts watching a directory for filesystem changes in the background, without
        /// blocking. Subscribe to <see cref="Created"/>/<see cref="Changed"/>/<see cref="Deleted"/>/
        /// <see cref="Renamed"/> for whichever kinds of change the automation cares about -
        /// an event with no subscriber simply never fires, so there is no separate "which
        /// kinds" filter to configure here (contrast <see cref="WatchForChange"/>'s
        /// <c>changeKindsFilter</c>). The directory must already exist. Never throws.
        /// </summary>
        /// <param name="directoryPath">The directory to watch. Must already exist.</param>
        /// <param name="filter">A <see cref="FileSystemWatcher.Filter"/>-style pattern (e.g. <c>"*.csv"</c>), or null/empty for all files.</param>
        /// <param name="includeSubdirectories">Whether to also watch subdirectories.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the watch could not start.</param>
        /// <returns><c>true</c> on success; <c>false</c> if a watch is already running (call <see cref="StopWatching"/> first), or the directory is invalid. Never throws.</returns>
        /// <remarks>
        /// <see cref="Created"/>/<see cref="Changed"/>/<see cref="Deleted"/>/<see cref="Renamed"/>/
        /// <see cref="WatchError"/> all fire on a background thread-pool thread, not the
        /// thread that called this method - a handler must not assume it runs
        /// synchronously with the rest of the automation. A handler that throws is caught
        /// and logged rather than allowed to crash the process, but its exception cannot
        /// be reported back through this method's <paramref name="message"/>, which has
        /// already returned by the time any event fires.
        /// </remarks>
        [Category("FileWatch - Watch")]
        [Description("Starts watching a directory for filesystem changes in the background, without blocking. Subscribe to Created/Changed/Deleted/Renamed for the kinds you care about. Returns True on success; never throws.")]
        public bool StartWatching(string directoryPath, string filter, bool includeSubdirectories, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    message = "A directory path is required.";
                    return false;
                }
                if (!Directory.Exists(directoryPath))
                {
                    message = $"Directory '{directoryPath}' does not exist. StartWatching requires the directory to already exist.";
                    return false;
                }

                lock (_watchLock)
                {
                    if (_disposed)
                    {
                        message = "This FileWatchUtils instance has been disposed and cannot start a new watch.";
                        return false;
                    }
                    if (_watcher != null)
                    {
                        message = "Already watching. Call StopWatching first.";
                        return false;
                    }

                    var watcher = new FileSystemWatcher(directoryPath)
                    {
                        IncludeSubdirectories = includeSubdirectories,
                        // Explicit rather than FileSystemWatcher's default (LastWrite |
                        // FileName | DirectoryName): Changed is documented as covering
                        // content/attribute/timestamp changes, and the default alone
                        // misses pure attribute or creation-time changes.
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                            | NotifyFilters.DirectoryName | NotifyFilters.Attributes
                            | NotifyFilters.Size | NotifyFilters.CreationTime
                    };
                    if (!string.IsNullOrEmpty(filter))
                        watcher.Filter = filter;

                    watcher.Created += OnNativeCreated;
                    watcher.Changed += OnNativeChanged;
                    watcher.Deleted += OnNativeDeleted;
                    watcher.Renamed += OnNativeRenamed;
                    watcher.Error += OnNativeError;

                    try
                    {
                        watcher.EnableRaisingEvents = true;
                    }
                    catch
                    {
                        watcher.Created -= OnNativeCreated;
                        watcher.Changed -= OnNativeChanged;
                        watcher.Deleted -= OnNativeDeleted;
                        watcher.Renamed -= OnNativeRenamed;
                        watcher.Error -= OnNativeError;
                        watcher.Dispose();
                        throw;
                    }

                    _watcher = watcher;
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("StartWatching", ex);
                return false;
            }
        }

        /// <summary>
        /// Stops and disposes the background watcher started by <see cref="StartWatching"/>.
        /// Never throws.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason (including "not currently watching").</param>
        /// <returns><c>true</c> on success; <c>false</c> if no watch is currently running. Never throws.</returns>
        [Category("FileWatch - Watch")]
        [Description("Stops and disposes the background watcher started by StartWatching. Returns True on success; never throws.")]
        public bool StopWatching(out string message)
        {
            message = default;
            try
            {
                if (!StopWatchingCore())
                {
                    message = "Not currently watching. Call StartWatching first.";
                    return false;
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("StopWatching", ex);
                return false;
            }
        }

        /// <summary>
        /// Reports whether a background watch is currently running. A plain Boolean query
        /// with no failure mode, matching <c>WindowUtils.IsWindowVisible</c>'s shape - no
        /// <c>out string message</c>, since there is nothing here that can fail.
        /// </summary>
        [Category("FileWatch - Watch")]
        [Description("Reports whether a background watch is currently running.")]
        public bool IsWatching()
        {
            lock (_watchLock)
            {
                return _watcher != null;
            }
        }

        /// <summary>Raised when a file or directory is created, while a background watch (<see cref="StartWatching"/>) is running.</summary>
        [Category("FileWatch - Watch")]
        [Description("Raised when a file or directory is created, while a background watch is running.")]
        public event EventHandler<FileWatchChangeEventArgs> Created;

        /// <summary>Raised on a content/attribute/timestamp change, while a background watch (<see cref="StartWatching"/>) is running.</summary>
        [Category("FileWatch - Watch")]
        [Description("Raised on a content/attribute/timestamp change, while a background watch is running.")]
        public event EventHandler<FileWatchChangeEventArgs> Changed;

        /// <summary>Raised when a file or directory is deleted, while a background watch (<see cref="StartWatching"/>) is running.</summary>
        [Category("FileWatch - Watch")]
        [Description("Raised when a file or directory is deleted, while a background watch is running.")]
        public event EventHandler<FileWatchChangeEventArgs> Deleted;

        /// <summary>Raised on a rename, while a background watch (<see cref="StartWatching"/>) is running.</summary>
        [Category("FileWatch - Watch")]
        [Description("Raised on a rename, while a background watch is running.")]
        public event EventHandler<FileWatchRenamedEventArgs> Renamed;

        /// <summary>
        /// Raised if the underlying watcher itself fails (e.g. an internal notification-
        /// buffer overflow) - never raised for an exception thrown by a subscriber's own
        /// handler on <see cref="Created"/>/<see cref="Changed"/>/<see cref="Deleted"/>/
        /// <see cref="Renamed"/>, which is caught and logged instead (see <see cref="StartWatching"/>'s
        /// remarks). The watch has already stopped by the time this fires.
        /// </summary>
        [Category("FileWatch - Watch")]
        [Description("Raised if the underlying watcher itself fails; the watch stops before this fires. Never raised for a subscriber's own handler exception.")]
        public event EventHandler<FileWatchErrorEventArgs> WatchError;

        private void OnNativeCreated(object sender, FileSystemEventArgs e) =>
            RaiseSafely(Created, new FileWatchChangeEventArgs(e.FullPath, FileChangeKind.Created));

        private void OnNativeChanged(object sender, FileSystemEventArgs e) =>
            RaiseSafely(Changed, new FileWatchChangeEventArgs(e.FullPath, FileChangeKind.Changed));

        private void OnNativeDeleted(object sender, FileSystemEventArgs e) =>
            RaiseSafely(Deleted, new FileWatchChangeEventArgs(e.FullPath, FileChangeKind.Deleted));

        private void OnNativeRenamed(object sender, RenamedEventArgs e) =>
            RaiseSafely(Renamed, new FileWatchRenamedEventArgs(e.FullPath, e.OldFullPath));

        private void OnNativeError(object sender, ErrorEventArgs e)
        {
            // Only stop/report for the watcher that actually raised this: a delayed Error
            // callback from an already-replaced watcher (StopWatching immediately followed
            // by a new StartWatching, racing this stale callback) must not tear down or
            // misreport the new, healthy watch. StopWatcherIfCurrent's compare-and-clear
            // happens atomically under _watchLock, so there is no window for it to stop the
            // wrong instance even if another StartWatching runs concurrently with this.
            if (!StopWatcherIfCurrent(sender as FileSystemWatcher))
                return;

            string message = $"StartWatching's background watcher failed unexpectedly: {e.GetException()}";
            RaiseSafely(WatchError, new FileWatchErrorEventArgs(message));
        }

        /// <summary>
        /// Test-only seam: invokes the native Error handling path directly, since a real
        /// <see cref="FileSystemWatcher"/> internal buffer overflow is not reliably
        /// triggerable on demand. Used to verify a stale callback (one whose <paramref name="sender"/>
        /// is no longer the active watcher) is correctly ignored rather than stopping/
        /// misreporting a subsequently-started, healthy watch.
        /// </summary>
        internal void SimulateNativeErrorForTests(object sender, ErrorEventArgs e) => OnNativeError(sender, e);

        /// <summary>
        /// Invokes each subscriber on a multicast event delegate individually, isolating
        /// this component (and every other subscriber) from one subscriber's own handler
        /// exception. A plain <c>handler?.Invoke(...)</c> would not be enough: a multicast
        /// delegate invokes its subscribers in one call, so a throwing handler stops every
        /// subscriber after it in the list from ever running, not just itself. Left
        /// unhandled, the exception would also escape on the watcher's background
        /// thread-pool thread and terminate the host process, since these events do not
        /// fire on the automation's own thread. Logged at debug level, matching
        /// WinEventUtils' own callback-failure fallback - there is no method call in
        /// progress by the time a handler runs, so there is no <c>message</c> output to
        /// report it through.
        /// </summary>
        private void RaiseSafely<TArgs>(EventHandler<TArgs> handler, TArgs args) where TArgs : EventArgs
        {
            if (handler == null)
                return;

            foreach (EventHandler<TArgs> single in handler.GetInvocationList())
            {
                try
                {
                    single(this, args);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"FileWatchUtils: a Watch event handler threw: {ex}");
                }
            }
        }

        /// <summary>
        /// Stops and disposes whichever watcher is currently active. Returns <c>false</c>
        /// if none was running, so <see cref="StopWatching"/> can report "not currently
        /// watching".
        /// </summary>
        private bool StopWatchingCore() => StopWatcherIfCurrent(expected: null);

        /// <summary>
        /// Stops and disposes <paramref name="expected"/> only if it is still the active
        /// watcher (or stops whichever is active when <paramref name="expected"/> is
        /// <c>null</c>). The compare-and-clear against <c>_watcher</c> happens atomically
        /// under <see cref="_watchLock"/>, so a caller that knows which specific instance
        /// it means to stop (<see cref="OnNativeError"/>, guarding against a stale
        /// callback from an already-replaced watcher) cannot race a concurrent
        /// <see cref="StartWatching"/> into stopping the wrong one. Disabling
        /// <c>EnableRaisingEvents</c> also happens inside that same lock, before the
        /// watcher is unlinked from <c>_watcher</c> - otherwise a concurrent
        /// <see cref="StartWatching"/> could start a new watcher while this one might still
        /// raise one more event, producing an overlapping notification from a "stopped"
        /// watch. Never throws: this runs from <see cref="Dispose(bool)"/>, which must not
        /// throw, and from <see cref="OnNativeError"/> on a background thread-pool thread,
        /// where an unhandled exception would terminate the host process - so any teardown
        /// failure here is caught and debug-logged rather than propagated, the same
        /// approach <see cref="RaiseSafely{TArgs}"/> uses for subscriber exceptions.
        /// </summary>
        private bool StopWatcherIfCurrent(FileSystemWatcher expected)
        {
            FileSystemWatcher watcher;
            lock (_watchLock)
            {
                if (expected != null && !ReferenceEquals(expected, _watcher))
                    return false;

                watcher = _watcher;
                if (watcher == null)
                    return false;

                try
                {
                    watcher.EnableRaisingEvents = false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"FileWatchUtils: could not disable the background watcher's raising events: {ex}");
                }

                _watcher = null;
            }

            // No new StartWatching can now observe this watcher as active, and it can no
            // longer raise events - unsubscribing/disposing outside the lock only affects
            // this now-detached instance.
            try
            {
                watcher.Created -= OnNativeCreated;
                watcher.Changed -= OnNativeChanged;
                watcher.Deleted -= OnNativeDeleted;
                watcher.Renamed -= OnNativeRenamed;
                watcher.Error -= OnNativeError;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FileWatchUtils: cleanup of the background watcher failed: {ex}");
            }

            return true;
        }

        #endregion

        #region Actions

        /// <summary>
        /// Moves a file to a new path, optionally overwriting an existing destination.
        /// True atomicity is only guaranteed when source and destination are on the same
        /// volume - a cross-volume move falls back to a non-atomic copy-then-delete. Never
        /// throws.
        /// </summary>
        [Category("FileWatch - Actions")]
        [Description("Moves a file to a new path, optionally overwriting an existing destination. Atomic only on the same volume. Never throws.")]
        public bool AtomicMoveFile(string sourcePath, string destinationPath, bool overwrite, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    message = "A source path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    message = "A destination path is required.";
                    return false;
                }
                if (!File.Exists(sourcePath))
                {
                    message = $"Source file '{sourcePath}' does not exist.";
                    return false;
                }

                File.Move(sourcePath, destinationPath, overwrite);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AtomicMoveFile", ex);
                return false;
            }
        }

        /// <summary>
        /// Replaces <paramref name="destinationPath"/>'s contents with <paramref name="sourcePath"/>'s
        /// via <see cref="File.Replace(string, string, string)"/>, optionally keeping a
        /// backup of the replaced file. Unlike <see cref="AtomicMoveFile"/>, this requires
        /// <paramref name="destinationPath"/> to already exist and both files to be on the
        /// same volume. This is Windows-specific by .NET design - it throws
        /// <see cref="PlatformNotSupportedException"/> (reported as a failure, never an
        /// unhandled throw) on non-Windows runtimes. Never throws.
        /// </summary>
        [Category("FileWatch - Actions")]
        [Description("Replaces an existing file's contents with another file's, optionally keeping a backup. Requires the destination to already exist. Windows-only behavior. Never throws.")]
        public bool ReplaceFile(string sourcePath, string destinationPath, string backupPath, out string message)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    message = "A source path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    message = "A destination path is required.";
                    return false;
                }
                if (!File.Exists(sourcePath))
                {
                    message = $"Source file '{sourcePath}' does not exist.";
                    return false;
                }
                if (!File.Exists(destinationPath))
                {
                    message = $"Destination file '{destinationPath}' does not exist. ReplaceFile requires an existing destination - use AtomicMoveFile for a destination that doesn't exist yet.";
                    return false;
                }

                File.Replace(sourcePath, destinationPath, string.IsNullOrWhiteSpace(backupPath) ? null : backupPath);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ReplaceFile", ex);
                return false;
            }
        }

        /// <summary>
        /// Claims a work file by copying it into an in-progress directory, then deleting
        /// the source. A collision - another instance already claiming this exact source,
        /// or an unrelated file already occupying the destination name - returns
        /// <c>false</c> with a clear message rather than overwriting or auto-renaming.
        /// This is the one method in this component that is actually safe under real
        /// multi-robot concurrency - every other method here observes state and then acts
        /// on it, with an inherent gap another process can exploit in between. Never
        /// throws.
        /// </summary>
        /// <param name="sourcePath">The file to claim.</param>
        /// <param name="inProgressDirectoryPath">The directory to move it into. Must already exist.</param>
        /// <param name="claimedPath">The file's new path on success, or on a specific failure where the claim itself completed but a required cleanup step (deleting the original) did not - see <see cref="ClaimFile"/>'s failure messages. Unset on every other failure.</param>
        /// <param name="message"><c>null</c> on success; a failure reason otherwise. Distinguishes three failure shapes rather than folding them into one: losing the race for this exact source ("already claimed"), an unrelated file already at the destination name, and a copy/cleanup failure after this call had already exclusively secured the source.</param>
        /// <remarks>
        /// <para>
        /// Claims the <b>source</b> first via an exclusively-created sidecar lock file
        /// (<c>sourcePath + ".claiming"</c>, via <c>FileMode.CreateNew</c>), not the
        /// destination - two callers racing to claim the same source into two
        /// <i>different</i> <paramref name="inProgressDirectoryPath"/> values compute two
        /// different destination paths, so a destination-only exclusivity check (an
        /// earlier version of this method) cannot detect that race at all; both could
        /// exclusively create their own destination, both copy the source, and both
        /// report success - the exact duplicate-processing outcome this method exists to
        /// prevent. Locking on the source instead closes that gap regardless of where each
        /// caller intends to put the result.
        /// </para>
        /// <para>
        /// Neither this lock nor the destination claim uses
        /// <see cref="File.Move(string, string, bool)"/> with <c>overwrite: false</c>,
        /// despite that overload's documented "throws if the destination already exists"
        /// contract - measured directly on this repository's target platform, two threads
        /// racing that call against the same destination both report success essentially
        /// every time (a TOCTOU race inside .NET's own implementation, not an OS-level
        /// guarantee it fails to honor), and a further experiment racing it away from a
        /// <i>shared source</i> to two unique destinations was worse still: both calls
        /// reported success with no exception, yet only one destination actually received
        /// the file's content - the other silently never existed. <c>FileMode.CreateNew</c>
        /// does not share either flaw in the same experiments (0 double-successes across
        /// hundreds of trials), since it maps directly to the OS's own atomic
        /// exclusive-create call rather than a separate managed check followed by a
        /// separate move. One residual platform quirk this method does account for: the
        /// losing side of a <c>FileMode.CreateNew</c> race is documented to throw
        /// <see cref="IOException"/>, but was observed here to occasionally throw
        /// <see cref="UnauthorizedAccessException"/> instead in that same race window -
        /// both are treated identically as "lost the race", not just the documented one.
        /// </para>
        /// <para>
        /// Trade-off versus a true rename: claiming now copies bytes rather than
        /// repointing a directory entry, losing a rename's near-instant, whole-file
        /// atomicity for a large file - the same same-volume-only trade-off
        /// <see cref="AtomicMoveFile"/> already documents for its own cross-volume
        /// fallback. A second, narrower trade-off: a hard process crash (not a caught
        /// exception - nothing can run a cleanup step after that) between securing the
        /// source lock and this method's normal completion leaves the
        /// <c>sourcePath + ".claiming"</c> file behind, permanently blocking a legitimate
        /// future claim of that exact source until it is removed by an operator or a
        /// separate maintenance process; a crash between claiming the destination and
        /// finishing the copy similarly leaves a partial/empty file at the destination.
        /// Full crash-safety would need filesystem transactions this cross-platform BCL
        /// component does not have access to - recognizing and clearing either stale
        /// artifact is a documented operational concern, not something this method
        /// resolves on its own.
        /// </para>
        /// </remarks>
        [Category("FileWatch - Actions")]
        [Description("Claims a work file by copying it into an in-progress directory under a source-scoped exclusive lock, then deleting the source. A collision with another claimant or an unrelated existing destination file is reported distinctly. Never throws.")]
        public bool ClaimFile(string sourcePath, string inProgressDirectoryPath, out string claimedPath, out string message)
        {
            claimedPath = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    message = "A source path is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(inProgressDirectoryPath))
                {
                    message = "An in-progress directory path is required.";
                    return false;
                }
                if (!Directory.Exists(inProgressDirectoryPath))
                {
                    message = $"In-progress directory '{inProgressDirectoryPath}' does not exist.";
                    return false;
                }
                if (!File.Exists(sourcePath))
                {
                    message = $"Source file '{sourcePath}' does not exist, or was already claimed by another instance.";
                    return false;
                }

                // Source-scoped exclusive lock (see <remarks>): the real serialization
                // point for this method, regardless of which inProgressDirectoryPath each
                // caller passes.
                string lockPath = sourcePath + ".claiming";
                FileStream lockStream;
                try
                {
                    lockStream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    // Losing this CreateNew race is documented to throw IOException, but
                    // measured directly on this repository's target platform it can
                    // instead surface as UnauthorizedAccessException in the same race
                    // window (an NTFS timing quirk when another handle is mid-create for
                    // this exact path) - both are treated as the same outcome here, not
                    // just the documented one, or this message would occasionally be
                    // wrong about a perfectly ordinary lost race.
                    message = ex is IOException || ex is UnauthorizedAccessException
                        ? $"'{Path.GetFileName(sourcePath)}' is already claimed - another instance is processing it."
                        : $"Could not claim '{Path.GetFileName(sourcePath)}': {ex.Message}";
                    return false;
                }

                try
                {
                    // Exclusively claim the destination name too - a second, independent
                    // collision case from losing the source lock above: an unrelated file
                    // (not from a competing ClaimFile call, which the lock above already
                    // excludes) already occupying this exact destination path.
                    string destination = Path.Combine(inProgressDirectoryPath, Path.GetFileName(sourcePath));
                    bool destinationCreated = false;
                    try
                    {
                        using (var destStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            destinationCreated = true;
                            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                sourceStream.CopyTo(destStream);
                            }
                        }
                    }
                    catch (Exception ex) when (!destinationCreated && (ex is IOException || ex is UnauthorizedAccessException))
                    {
                        // Same CreateNew-race caveat as the source lock above applies
                        // here too: losing this race can surface as either exception type.
                        message = $"'{Path.GetFileName(sourcePath)}' could not be claimed into '{inProgressDirectoryPath}' - a file with that name already exists there.";
                        return false;
                    }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                    {
                        // The destination name was successfully claimed, but its content
                        // could not be populated (the source vanished after this method
                        // already secured the lock above, got locked, access was denied,
                        // etc.) - remove the now-empty/partial claim so it never
                        // permanently blocks a real future claim of the same name. This is
                        // a different, rarer situation than losing the source-level race
                        // above, so it gets its own message rather than being folded into
                        // "already claimed".
                        if (destinationCreated)
                            try { File.Delete(destination); } catch { /* best-effort cleanup */ }
                        message = $"Claimed '{Path.GetFileName(sourcePath)}' but failed to copy its content into '{inProgressDirectoryPath}': {ex.Message}";
                        return false;
                    }

                    try
                    {
                        // Only the sole winner of the source-level lock above ever reaches
                        // this delete - no concurrent racer can also be deleting this same
                        // path, unlike the File.Move-based approach this method used to use.
                        File.Delete(sourcePath);
                    }
                    catch (Exception ex)
                    {
                        // The claim itself is valid and complete - destination has the full
                        // content - but required cleanup (removing the original) failed, so
                        // this is a failure per this suite's compound-result convention even
                        // though the primary work succeeded. claimedPath is still populated
                        // here (unlike every other failure above) so a caller isn't left
                        // unable to find the fully-claimed copy.
                        claimedPath = destination;
                        message = $"Claimed '{Path.GetFileName(sourcePath)}' into '{destination}', but could not delete the original: {ex.Message}";
                        return false;
                    }

                    claimedPath = destination;
                    message = null;
                    return true;
                }
                finally
                {
                    // The lock file is a private implementation detail, never part of the
                    // public contract - remove it regardless of outcome above. Only ever
                    // reached after this call itself created lockStream (a failure to
                    // create it returns early, above), so this never touches a lock
                    // another instance or a stale prior run is still holding.
                    lockStream.Dispose();
                    try { File.Delete(lockPath); } catch { /* best-effort - see <remarks> on stale locks */ }
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ClaimFile", ex);
                return false;
            }
        }

        #endregion

        #region Hash

        /// <summary>Computes the SHA-256 hash of a file's contents, as a lowercase hex string. Streamed - safe for large files. Never throws.</summary>
        [Category("FileWatch - Hash")]
        [Description("Computes the SHA-256 hash of a file's contents, as a lowercase hex string. Never throws.")]
        public bool ComputeFileHashSha256(string path, out string hashHex, out string message)
        {
            hashHex = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A file path is required.";
                    return false;
                }
                if (!File.Exists(path))
                {
                    message = $"File '{path}' does not exist.";
                    return false;
                }

                using var algorithm = SHA256.Create();
                return TryComputeHash(path, algorithm, out hashHex, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ComputeFileHashSha256", ex);
                return false;
            }
        }

        /// <summary>
        /// Computes a file's hash using the named algorithm, as a lowercase hex string -
        /// a power-user escape hatch alongside <see cref="ComputeFileHashSha256"/> for
        /// legacy-system interop. Streamed - safe for large files. Never throws.
        /// </summary>
        /// <param name="path">The file to hash.</param>
        /// <param name="algorithmName">One of "SHA256", "SHA1", "MD5", "SHA384", "SHA512" (case-insensitive).</param>
        /// <param name="hashHex">The computed hash as a lowercase hex string, on success.</param>
        /// <param name="message"><c>null</c> on success; a failure reason otherwise (including an unrecognized <paramref name="algorithmName"/>).</param>
        [Category("FileWatch - Hash")]
        [Description("Computes a file's hash using the named algorithm (SHA256, SHA1, MD5, SHA384, SHA512), as a lowercase hex string. Never throws.")]
        public bool ComputeFileHash(string path, string algorithmName, out string hashHex, out string message)
        {
            hashHex = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A file path is required.";
                    return false;
                }
                if (!File.Exists(path))
                {
                    message = $"File '{path}' does not exist.";
                    return false;
                }
                if (!TryCreateHashAlgorithm(algorithmName, out HashAlgorithm algorithm, out message))
                    return false;

                using (algorithm)
                {
                    return TryComputeHash(path, algorithm, out hashHex, out message);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ComputeFileHash", ex);
                return false;
            }
        }

        /// <summary>Returns <c>true</c> if two files have identical SHA-256 hashes - the direct "detect duplicate/unchanged inputs" convenience. Never throws.</summary>
        [Category("FileWatch - Hash")]
        [Description("Returns True if two files have identical SHA-256 hashes. Never throws.")]
        public bool AreFilesIdenticalByHash(string pathA, string pathB, out bool identical, out string message)
        {
            identical = default;
            message = default;
            try
            {
                if (!ComputeFileHashSha256(pathA, out string hashA, out message))
                    return false;
                if (!ComputeFileHashSha256(pathB, out string hashB, out message))
                    return false;

                identical = string.Equals(hashA, hashB, StringComparison.OrdinalIgnoreCase);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AreFilesIdenticalByHash", ex);
                return false;
            }
        }

        private static bool TryComputeHash(string path, HashAlgorithm algorithm, out string hashHex, out string message)
        {
            hashHex = default;
            message = default;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] hash = algorithm.ComputeHash(stream);
                hashHex = Convert.ToHexString(hash).ToLowerInvariant();
            }
            message = null;
            return true;
        }

        private static bool TryCreateHashAlgorithm(string algorithmName, out HashAlgorithm algorithm, out string message)
        {
            algorithm = default;
            message = default;
            if (string.IsNullOrWhiteSpace(algorithmName))
            {
                message = "An algorithm name is required (SHA256, SHA1, MD5, SHA384, or SHA512).";
                return false;
            }

            switch (algorithmName.Trim().ToUpperInvariant())
            {
                case "SHA256": algorithm = SHA256.Create(); return true;
                case "SHA1": algorithm = SHA1.Create(); return true;
                case "MD5": algorithm = MD5.Create(); return true;
                case "SHA384": algorithm = SHA384.Create(); return true;
                case "SHA512": algorithm = SHA512.Create(); return true;
                default:
                    message = $"Unknown hash algorithm '{algorithmName}'. Valid values: SHA256, SHA1, MD5, SHA384, SHA512.";
                    return false;
            }
        }

        #endregion

        #region Metadata

        /// <summary>Gets a file's size and timestamps as scalar outputs. Never throws.</summary>
        [Category("FileWatch - Metadata")]
        [Description("Gets a file's size and timestamps as scalar outputs. Never throws.")]
        public bool TryGetFileMetadata(string path, out long sizeBytes, out string lastWriteUtcIso8601, out string createdUtcIso8601, out string message)
        {
            sizeBytes = default;
            lastWriteUtcIso8601 = default;
            createdUtcIso8601 = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A file path is required.";
                    return false;
                }
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    message = $"File '{path}' does not exist.";
                    return false;
                }

                sizeBytes = info.Length;
                lastWriteUtcIso8601 = ToIso8601(info.LastWriteTimeUtc);
                createdUtcIso8601 = ToIso8601(info.CreationTimeUtc);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("TryGetFileMetadata", ex);
                return false;
            }
        }

        /// <summary>Gets a file or directory's metadata as a JSON object (includes <c>IsDirectory</c>/<c>Extension</c>, which <see cref="TryGetFileMetadata"/> omits). Never throws.</summary>
        [Category("FileWatch - Metadata")]
        [Description("Gets a file or directory's metadata as a JSON object. Never throws.")]
        public bool GetFileMetadataJson(string path, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "A path is required.";
                    return false;
                }

                FileMetadata metadata;
                if (File.Exists(path))
                    metadata = BuildFileMetadata(new FileInfo(path));
                else if (Directory.Exists(path))
                    metadata = BuildDirectoryMetadata(new DirectoryInfo(path));
                else
                {
                    message = $"'{path}' does not exist.";
                    return false;
                }

                json = JsonSerializer.Serialize(metadata, FileWatchJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetFileMetadataJson", ex);
                return false;
            }
        }

        /// <summary>Lists files in a directory matching a pattern as a JSON array of metadata - pairs naturally with <see cref="WaitForFileMatchingPattern"/>. Never throws.</summary>
        [Category("FileWatch - Metadata")]
        [Description("Lists files in a directory matching a pattern as a JSON array of metadata. Never throws.")]
        public bool GetDirectoryListingJson(string directoryPath, string searchPattern, bool includeSubdirectories, out string json, out string message)
        {
            json = default;
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    message = "A directory path is required.";
                    return false;
                }
                if (!Directory.Exists(directoryPath))
                {
                    message = $"Directory '{directoryPath}' does not exist.";
                    return false;
                }

                string pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern;
                SearchOption option = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var results = new List<FileMetadata>();
                foreach (string filePath in Directory.EnumerateFiles(directoryPath, pattern, option))
                {
                    results.Add(BuildFileMetadata(new FileInfo(filePath)));
                }

                json = JsonSerializer.Serialize(results, FileWatchJson.Options);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("GetDirectoryListingJson", ex);
                return false;
            }
        }

        #endregion

        #region Internal Helpers

        private static string ToIso8601(DateTime utc) =>
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToString("O");

        private static FileMetadata BuildFileMetadata(FileInfo info) => new FileMetadata
        {
            FullPath = info.FullName,
            Name = info.Name,
            Extension = info.Extension ?? string.Empty,
            SizeBytes = info.Length,
            IsDirectory = false,
            CreatedUtcIso8601 = ToIso8601(info.CreationTimeUtc),
            LastWriteUtcIso8601 = ToIso8601(info.LastWriteTimeUtc)
        };

        private static FileMetadata BuildDirectoryMetadata(DirectoryInfo info) => new FileMetadata
        {
            FullPath = info.FullName,
            Name = info.Name,
            Extension = string.Empty,
            SizeBytes = 0,
            IsDirectory = true,
            CreatedUtcIso8601 = ToIso8601(info.CreationTimeUtc),
            LastWriteUtcIso8601 = ToIso8601(info.LastWriteTimeUtc)
        };

        #endregion
    }
}
