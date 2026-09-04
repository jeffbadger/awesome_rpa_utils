using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LocalQueueAutomation
{
    internal static class LocalQueueCore
    {
        internal static readonly string[] States = { "ready", "delayed", "in-progress", "completed", "rejected", "corrupt", "temp", "files" };
        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        internal static string BasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwesomeRpaUtils", "Queues");

        internal static bool TryResolveNew(string queueName, QueueLifetime lifetime, string runId, out string path, out string message)
        {
            path = null; message = null;
            if (!ValidSegment(queueName)) { message = "queueName must be a non-empty local directory name without path separators."; return false; }
            if (!Enum.IsDefined(lifetime)) { message = "lifetime is not a defined QueueLifetime value."; return false; }
            if (lifetime == QueueLifetime.Run && !ValidSegment(runId))
            { message = "runId is required for a Run queue and must not contain path separators."; return false; }
            path = lifetime == QueueLifetime.Run
                ? Path.Combine(BasePath, queueName, "runs", runId)
                : Path.Combine(BasePath, queueName, "persistent");
            return true;
        }

        internal static bool TryValidatePath(string path, out string fullPath, out string message)
        {
            fullPath = null; message = null;
            if (string.IsNullOrWhiteSpace(path)) { message = "A queue path is required."; return false; }
            string root = Path.GetFullPath(BasePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { message = $"Queue path must be beneath '{BasePath}'."; return false; }
            if (!File.Exists(Path.Combine(fullPath, "queue.json"))) { message = $"'{fullPath}' is not a LocalQueueUtils queue."; return false; }
            return true;
        }

        internal static bool ValidSegment(string value) => !string.IsNullOrWhiteSpace(value) && value.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0 && value != "." && value != "..";
        internal static string StatePath(string queuePath, string state) => Path.Combine(queuePath, state);
        internal static string ItemPath(string queuePath, string state, string id) => Path.Combine(StatePath(queuePath, state), id + ".json");

        internal static void WriteAtomic(string path, object value)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, path, true);
        }

        internal static QueueItem ReadItem(string path) => JsonSerializer.Deserialize<QueueItem>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"Item file '{path}' contains null JSON.");

        internal static IEnumerable<(string Path, string State, QueueItem Item)> ReadItems(string queuePath, params string[] states)
        {
            foreach (string state in states)
                foreach (string path in Directory.EnumerateFiles(StatePath(queuePath, state), "*.json"))
                {
                    QueueItem item;
                    try { item = ReadItem(path); }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                    {
                        string quarantine = Path.Combine(StatePath(queuePath, "corrupt"), Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N") + ".json");
                        File.Move(path, quarantine);
                        continue;
                    }
                    if (item == null || !ValidSegment(item.Id))
                    {
                        string quarantine = Path.Combine(StatePath(queuePath, "corrupt"), Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N") + ".json");
                        File.Move(path, quarantine);
                        continue;
                    }
                    yield return (path, state, item);
                }
        }

        internal static string NewId() => DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
        internal static string TrimError(string value) => string.IsNullOrEmpty(value) ? value : value.Length <= 4096 ? value : value.Substring(0, 4096);
    }
}
