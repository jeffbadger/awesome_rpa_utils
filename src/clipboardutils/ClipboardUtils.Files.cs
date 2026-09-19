using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace ClipboardAutomation
{
    public partial class ClipboardUtils
    {
        #region File drop list

        /// <summary>Reads the list of files on the clipboard (what Explorer's Copy or Cut leaves there), as JSON.</summary>
        /// <param name="pathsJson">A JSON array of the file paths (<c>[]</c> if the clipboard holds no file list); empty if this method returns <c>false</c>.</param>
        /// <param name="count">How many files are on the list.</param>
        /// <param name="effect">What pasting the list is asked to do: Copy (also when the clipboard does not say), Move (after Cut), or Link.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success, including when there is no file list; <c>false</c> if the clipboard could not be read. Never throws.</returns>
        [Category("Clipboard - Files")]
        [Description("Reads the file list on the clipboard as a JSON array of paths, with the copy/move effect. No file list is a success with count 0. Returns True on success; never throws.")]
        public bool GetFileDropListJson(out string pathsJson, out int count, out FileDropEffect effect, out string message)
        {
            pathsJson = string.Empty;
            count = 0;
            effect = FileDropEffect.Copy;
            message = default;
            try
            {
                if (!ReadFileDropList(out List<string> paths, out effect, out message))
                    return false;
                pathsJson = JsonSerializer.Serialize(paths, JsonOptions);
                count = paths.Count;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                pathsJson = string.Empty;
                count = 0;
                effect = FileDropEffect.Copy;
                message = NeverThrowsGuard.Failure("GetFileDropListJson", ex);
                return false;
            }
        }

        /// <summary>Reads the list of files on the clipboard as text, one path per line.</summary>
        /// <param name="paths">The paths separated by line breaks (an empty string if the clipboard holds no file list); empty if this method returns <c>false</c>.</param>
        /// <param name="count">How many files are on the list.</param>
        /// <param name="effect">What pasting the list is asked to do: Copy (also when the clipboard does not say), Move (after Cut), or Link.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success, including when there is no file list; <c>false</c> if the clipboard could not be read. Never throws.</returns>
        [Category("Clipboard - Files")]
        [Description("Reads the file list on the clipboard as text, one path per line, with the copy/move effect. No file list is a success with count 0. Returns True on success; never throws.")]
        public bool GetFileDropListText(out string paths, out int count, out FileDropEffect effect, out string message)
        {
            paths = string.Empty;
            count = 0;
            effect = FileDropEffect.Copy;
            message = default;
            try
            {
                if (!ReadFileDropList(out List<string> list, out effect, out message))
                    return false;
                paths = string.Join(Environment.NewLine, list);
                count = list.Count;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                paths = string.Empty;
                count = 0;
                effect = FileDropEffect.Copy;
                message = NeverThrowsGuard.Failure("GetFileDropListText", ex);
                return false;
            }
        }

        /// <summary>
        /// Puts a list of files on the clipboard, as Explorer's Copy or Cut does, so that pasting in Explorer (or any
        /// application that takes files) copies or moves them. Everything else on the clipboard is discarded.
        /// </summary>
        /// <param name="paths">The files and folders, one path per line (line breaks of either kind; blank lines and surrounding quotes are ignored). Relative paths are made absolute.</param>
        /// <param name="effect">Copy, Move (what Cut does) or Link.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <param name="requireExisting"><c>true</c> (the default) to refuse a path that does not exist, since pasting it would fail later and confusingly.</param>
        /// <returns><c>true</c> if the list is on the clipboard; <c>false</c> if it is empty or invalid, a path does not exist, or the clipboard could not be set. Never throws.</returns>
        /// <remarks>Nothing is copied, moved or deleted by this call: only the list is put on the clipboard. A Move takes effect when something pastes it.</remarks>
        [Category("Clipboard - Files")]
        [Description("Puts a list of files (one path per line) on the clipboard for a copy or move paste, like Explorer's Copy or Cut. Returns True on success; never throws.")]
        public bool SetFileDropList(string paths, FileDropEffect effect, out string message, bool requireExisting = true)
        {
            message = default;
            try
            {
                if (paths == null)
                {
                    message = "paths may not be null.";
                    return false;
                }
                return SetFileDropListCore(paths.Split('\n'), effect, requireExisting, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetFileDropList", ex);
                return false;
            }
        }

        /// <summary>Puts a list of files on the clipboard from a JSON array of paths. See <see cref="SetFileDropList"/>.</summary>
        /// <param name="pathsJson">A JSON array of path strings, for example <c>["C:\\Reports\\a.csv","C:\\Reports\\b.csv"]</c>.</param>
        /// <param name="effect">Copy, Move (what Cut does) or Link.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <param name="requireExisting"><c>true</c> (the default) to refuse a path that does not exist.</param>
        /// <returns><c>true</c> if the list is on the clipboard; <c>false</c> if the JSON is not an array of strings, the list is empty or invalid, a path does not exist, or the clipboard could not be set. Never throws.</returns>
        [Category("Clipboard - Files")]
        [Description("Puts a list of files (a JSON array of paths) on the clipboard for a copy or move paste. Returns True on success; never throws.")]
        public bool SetFileDropListJson(string pathsJson, FileDropEffect effect, out string message, bool requireExisting = true)
        {
            message = default;
            try
            {
                if (string.IsNullOrWhiteSpace(pathsJson))
                {
                    message = "pathsJson may not be empty.";
                    return false;
                }

                List<string> paths;
                try
                {
                    paths = JsonSerializer.Deserialize<List<string>>(pathsJson);
                }
                catch (JsonException)
                {
                    message = "pathsJson is not a JSON array of strings.";
                    return false;
                }
                if (paths == null)
                {
                    message = "pathsJson is not a JSON array of strings.";
                    return false;
                }
                return SetFileDropListCore(paths, effect, requireExisting, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetFileDropListJson", ex);
                return false;
            }
        }

        private bool ReadFileDropList(out List<string> paths, out FileDropEffect effect, out string message)
        {
            paths = new List<string>();
            effect = FileDropEffect.Copy;
            if (IsDisposed(out message))
                return false;

            long limit = MaximumBytes;
            if (!TryRunBounded("Reading the clipboard's file list", () =>
                {
                    bool ok = _engine.TryGetFileDropList(limit, out List<string> list, out FileDropEffect kind, out string error);
                    return new FilesResult { Ok = ok, Paths = list, Effect = kind, Error = error };
                }, out FilesResult result, out message))
                return false;
            if (!result.Ok)
            {
                message = result.Error;
                return false;
            }
            paths = result.Paths;
            effect = result.Effect;
            message = null;
            return true;
        }

        private bool SetFileDropListCore(IEnumerable<string> raw, FileDropEffect effect, bool requireExisting, out string message)
        {
            if (IsDisposed(out message))
                return false;
            if (!Enum.IsDefined(typeof(FileDropEffect), effect))
            {
                message = "effect is not a defined FileDropEffect value.";
                return false;
            }
            if (!TryNormalizePaths(raw, requireExisting, out List<string> paths, out message))
                return false;

            if (!TryRunBounded("Setting the clipboard's file list", () =>
                {
                    bool ok = _engine.TrySetFileDropList(paths, effect, out string error);
                    return new SimpleResult { Ok = ok, Error = error };
                }, out SimpleResult result, out message))
                return false;
            message = result.Ok ? null : result.Error;
            return result.Ok;
        }

        /// <summary>
        /// Cleans up a list of paths: trims them and any surrounding quotes, drops blank entries, makes each one
        /// absolute, and (if asked) checks that each exists. Refuses an empty list, one that is too long, or a path
        /// that cannot be one.
        /// </summary>
        internal static bool TryNormalizePaths(IEnumerable<string> raw, bool requireExisting, out List<string> paths, out string message)
        {
            paths = new List<string>();
            message = null;
            var missing = new List<string>();

            foreach (string entry in raw)
            {
                string text = (entry ?? string.Empty).Trim().Trim('"').Trim();
                if (text.Length == 0)
                    continue;

                if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    message = "'" + text + "' is not a valid file path (it contains a character a path cannot hold).";
                    paths = new List<string>();
                    return false;
                }

                string full;
                try
                {
                    full = Path.GetFullPath(text);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    message = "'" + text + "' is not a valid file path.";
                    paths = new List<string>();
                    return false;
                }
                if (text.IndexOfAny(new[] { '*', '?' }) >= 0)
                {
                    message = "'" + text + "' contains a wildcard; give the paths of the files themselves.";
                    paths = new List<string>();
                    return false;
                }

                if (requireExisting && !File.Exists(full) && !Directory.Exists(full))
                    missing.Add(full);
                paths.Add(full);

                if (paths.Count > DropFileList.MaxFiles)
                {
                    message = "A file list may hold at most " + DropFileList.MaxFiles + " paths.";
                    paths = new List<string>();
                    return false;
                }
            }

            if (paths.Count == 0)
            {
                message = "The list of files is empty.";
                return false;
            }
            if (missing.Count > 0)
            {
                message = "These paths do not exist: " + string.Join("; ", missing.GetRange(0, Math.Min(missing.Count, 5)))
                    + (missing.Count > 5 ? "; and " + (missing.Count - 5) + " more" : string.Empty)
                    + ". (Pass requireExisting false to put them on the clipboard anyway.)";
                paths = new List<string>();
                return false;
            }
            return true;
        }

        private sealed class FilesResult
        {
            public bool Ok;
            public List<string> Paths;
            public FileDropEffect Effect;
            public string Error;
        }

        #endregion
    }
}
