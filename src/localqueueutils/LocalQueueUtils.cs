using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LocalQueueAutomation
{
    /// <summary>Provides a persistent, machine-local work queue for variable-count RPA sub-work without requiring a Pega collection proxy.</summary>
    [Description("Stores variable-count JSON, text, and file work locally and exposes a take/process/complete loop with retry, rejection, deduplication, and crash recovery. Never throws.")]
    public sealed class LocalQueueUtils : Component
    {
        private readonly Dictionary<string, FileStream> locks = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Empty constructor required so Pega Robot Studio can create the component.</summary>
        public LocalQueueUtils() { }
        /// <summary>Standard designer constructor; attaches the component to a container.</summary>
        public LocalQueueUtils(IContainer container) { container?.Add(this); }

        /// <summary>Creates or opens a run-scoped or persistent local queue. Never throws.</summary>
        [Category("Local Queue - Setup")]
        [Description("Creates or opens a Run or Persistent queue beneath LocalApplicationData and acquires its exclusive process lock. Never throws.")]
        public bool CreateQueue(string queueName, QueueLifetime lifetime, string runId, out string queuePath, out bool alreadyExisted, out string message)
        {
            queuePath = null; alreadyExisted = false; message = null;
            try
            {
                if (!LocalQueueCore.TryResolveNew(queueName, lifetime, runId, out queuePath, out message)) return false;
                alreadyExisted = File.Exists(Path.Combine(queuePath, "queue.json"));
                Directory.CreateDirectory(queuePath);
                foreach (string state in LocalQueueCore.States) Directory.CreateDirectory(LocalQueueCore.StatePath(queuePath, state));
                string manifestPath = Path.Combine(queuePath, "queue.json");
                if (!alreadyExisted)
                    LocalQueueCore.WriteAtomic(manifestPath, new QueueManifest { QueueName = queueName, Lifetime = lifetime.ToString(), RunId = runId, CreatedUtc = DateTime.UtcNow });
                else
                {
                    QueueManifest manifest = JsonSerializer.Deserialize<QueueManifest>(File.ReadAllText(manifestPath), LocalQueueCore.JsonOptions);
                    if (manifest == null || manifest.SchemaVersion != 1) { message = "The existing queue has an unsupported or invalid schema."; return false; }
                    if (!string.Equals(manifest.Lifetime, lifetime.ToString(), StringComparison.OrdinalIgnoreCase) || !string.Equals(manifest.RunId ?? "", runId ?? "", StringComparison.Ordinal))
                    { message = "The existing queue ownership does not match the requested lifetime/runId."; return false; }
                }
                return AcquireLock(queuePath, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("CreateQueue", ex); queuePath = null; return false; }
        }

        /// <summary>Opens an existing local queue by path. Never throws.</summary>
        [Category("Local Queue - Setup")]
        [Description("Opens an existing queue by path and acquires its exclusive process lock. Never throws.")]
        public bool OpenQueue(string queuePath, out string message)
        {
            message = null;
            try { return LocalQueueCore.TryValidatePath(queuePath, out string full, out message) && AcquireLock(full, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("OpenQueue", ex); return false; }
        }

        /// <summary>Validates a queue and returns a JSON report. Never throws.</summary>
        [Category("Local Queue - Setup")]
        [Description("Validates an existing queue and returns a JSON report. This does not acquire its process lock. Never throws.")]
        public bool ValidateQueue(string queuePath, out string reportJson, out string message)
        {
            reportJson = null; message = null;
            try
            {
                if (!LocalQueueCore.TryValidatePath(queuePath, out string full, out message)) return false;
                var counts = CountStates(full);
                reportJson = JsonSerializer.Serialize(new { valid = true, path = full, schemaVersion = 1, counts }, LocalQueueCore.JsonOptions);
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("ValidateQueue", ex); return false; }
        }

        /// <summary>Adds one JSON value to the queue. Never throws.</summary>
        [Category("Local Queue - Add")]
        [Description("Adds one valid JSON value to a queue. An active duplicate business key is reported without adding another item. Never throws.")]
        public bool AddJson(string queuePath, string payloadJson, out string itemId, out bool duplicate, out string message, string businessKey = null, int priority = 50, int delaySeconds = 0, int maximumAttempts = 3)
        {
            itemId = null; duplicate = false; message = null;
            try
            {
                using JsonDocument _ = JsonDocument.Parse(payloadJson);
                return AddCore(queuePath, payloadJson, businessKey, priority, delaySeconds, maximumAttempts, null, null, out itemId, out duplicate, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("AddJson", ex); return false; }
        }

        /// <summary>Adds each element of a JSON array as a work item. Never throws.</summary>
        [Category("Local Queue - Add")]
        [Description("Adds every element from a JSON array as a separate queue item. Never throws.")]
        public bool AddJsonArray(string queuePath, string jsonArray, out int addedCount, out string message, int priority = 50, int delaySeconds = 0, int maximumAttempts = 3)
        {
            addedCount = 0; message = null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonArray);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) { message = "jsonArray must contain a JSON array."; return false; }
                foreach (JsonElement element in doc.RootElement.EnumerateArray())
                {
                    if (!AddCore(queuePath, element.GetRawText(), null, priority, delaySeconds, maximumAttempts, null, null, out _, out _, out message)) return false;
                    addedCount++;
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("AddJsonArray", ex); return false; }
        }

        /// <summary>Adds every non-empty text line as a work item. Never throws.</summary>
        [Category("Local Queue - Add")]
        [Description("Adds each non-empty line of text as a separate JSON-string queue item. Never throws.")]
        public bool AddLines(string queuePath, string text, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)
        {
            addedCount = 0; message = null;
            try
            {
                if (text == null) { message = "text is required."; return false; }
                foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    string payload = JsonSerializer.Serialize(line);
                    if (!AddCore(queuePath, payload, null, priority, 0, maximumAttempts, null, null, out _, out _, out message)) return false;
                    addedCount++;
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("AddLines", ex); return false; }
        }

        /// <summary>Adds references to matching local files. Never throws.</summary>
        [Category("Local Queue - Add")]
        [Description("Adds paths of matching local files without copying or moving them. Never throws.")]
        public bool AddFileReferences(string queuePath, string sourceDirectory, string searchPattern, bool recursive, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)
        {
            return AddFilesCore("AddFileReferences", queuePath, sourceDirectory, searchPattern, recursive, false, priority, maximumAttempts, out addedCount, out message);
        }

        /// <summary>Copies matching files into queue storage and adds them as work. Never throws.</summary>
        [Category("Local Queue - Add")]
        [Description("Copies matching local files into queue-owned storage and adds them as work items. Never throws.")]
        public bool ImportFiles(string queuePath, string sourceDirectory, string searchPattern, bool recursive, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)
        {
            return AddFilesCore("ImportFiles", queuePath, sourceDirectory, searchPattern, recursive, true, priority, maximumAttempts, out addedCount, out message);
        }

        /// <summary>Takes and leases the next available item. Never throws.</summary>
        [Category("Local Queue - Process")]
        [Description("Takes the next available item and leases it for exclusive processing. No item is a normal successful check with itemAvailable False. Never throws.")]
        public bool TryTakeNext(string queuePath, out bool itemAvailable, out string itemId, out string payloadJson, out string storedFilePath, out int attempt, out string leaseToken, out string leaseExpiresUtc, out string message, int leaseSeconds = 300)
        {
            itemAvailable = false; itemId = payloadJson = storedFilePath = leaseToken = leaseExpiresUtc = null; attempt = 0; message = null;
            try
            {
                if (!RequireOwned(queuePath, out string full, out message) || !ValidateLease(leaseSeconds, out message)) return false;
                RecoverCore(full, out _, out _);
                DateTime now = DateTime.UtcNow;
                var next = LocalQueueCore.ReadItems(full, "ready", "delayed").Where(x => x.Item.AvailableUtc <= now)
                    .OrderByDescending(x => x.Item.Priority).ThenBy(x => x.Item.AvailableUtc).ThenBy(x => x.Item.CreatedUtc).ThenBy(x => x.Item.Id, StringComparer.Ordinal).FirstOrDefault();
                if (next.Item == null) return true;
                QueueItem item = next.Item;
                item.Attempt++;
                item.LeaseToken = Guid.NewGuid().ToString("N");
                item.LeaseExpiresUtc = now.AddSeconds(leaseSeconds);
                string destination = LocalQueueCore.ItemPath(full, "in-progress", item.Id);
                File.Move(next.Path, destination);
                LocalQueueCore.WriteAtomic(destination, item);
                itemAvailable = true; itemId = item.Id; payloadJson = item.Payload; storedFilePath = item.StoredFilePath; attempt = item.Attempt;
                leaseToken = item.LeaseToken; leaseExpiresUtc = item.LeaseExpiresUtc.Value.ToString("O");
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("TryTakeNext", ex); return false; }
        }

        /// <summary>Renews an in-progress item's lease. Never throws.</summary>
        [Category("Local Queue - Process")]
        [Description("Extends an in-progress item's lease. Never throws.")]
        public bool RenewLease(string queuePath, string itemId, string leaseToken, out string leaseExpiresUtc, out string message, int leaseSeconds = 300)
        {
            leaseExpiresUtc = null; message = null;
            try
            {
                if (!RequireOwned(queuePath, out string full, out message) || !ValidateLease(leaseSeconds, out message)) return false;
                if (!TryReadLeased(full, itemId, leaseToken, out string path, out QueueItem item, out message)) return false;
                item.LeaseExpiresUtc = DateTime.UtcNow.AddSeconds(leaseSeconds); LocalQueueCore.WriteAtomic(path, item);
                leaseExpiresUtc = item.LeaseExpiresUtc.Value.ToString("O"); return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("RenewLease", ex); return false; }
        }

        /// <summary>Marks an in-progress item completed. Never throws.</summary>
        [Category("Local Queue - Process")]
        [Description("Marks a leased item completed and optionally records result JSON. Never throws.")]
        public bool CompleteItem(string queuePath, string itemId, string leaseToken, out string message, string resultJson = null)
        {
            message = null;
            try
            {
                if (resultJson != null) using (JsonDocument.Parse(resultJson)) { }
                if (!RequireOwned(queuePath, out string full, out message) || !TryReadLeased(full, itemId, leaseToken, out string path, out QueueItem item, out message)) return false;
                item.ResultJson = resultJson; item.LeaseToken = null; item.LeaseExpiresUtc = null; LocalQueueCore.WriteAtomic(path, item);
                File.Move(path, LocalQueueCore.ItemPath(full, "completed", item.Id)); return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("CompleteItem", ex); return false; }
        }

        /// <summary>Records failure and retries or rejects an in-progress item. Never throws.</summary>
        [Category("Local Queue - Process")]
        [Description("Records a leased-item failure and either schedules a retry or rejects it after its maximum attempts. Never throws.")]
        public bool RetryItem(string queuePath, string itemId, string leaseToken, string errorMessage, out bool willRetry, out bool rejected, out string message, int delaySeconds = 0)
        {
            willRetry = rejected = false; message = null;
            try
            {
                if (delaySeconds < 0) { message = "delaySeconds must not be negative."; return false; }
                if (!RequireOwned(queuePath, out string full, out message) || !TryReadLeased(full, itemId, leaseToken, out string path, out QueueItem item, out message)) return false;
                item.LastError = LocalQueueCore.TrimError(errorMessage); item.LeaseToken = null; item.LeaseExpiresUtc = null;
                string state;
                if (item.Attempt >= item.MaximumAttempts) { state = "rejected"; rejected = true; }
                else { item.AvailableUtc = DateTime.UtcNow.AddSeconds(delaySeconds); state = delaySeconds == 0 ? "ready" : "delayed"; willRetry = true; }
                LocalQueueCore.WriteAtomic(path, item); File.Move(path, LocalQueueCore.ItemPath(full, state, item.Id)); return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("RetryItem", ex); return false; }
        }

        /// <summary>Rejects an in-progress item immediately. Never throws.</summary>
        [Category("Local Queue - Process")]
        [Description("Rejects a leased item immediately with a reason. Never throws.")]
        public bool RejectItem(string queuePath, string itemId, string leaseToken, string reason, out string message)
        {
            message = null;
            try
            {
                if (!RequireOwned(queuePath, out string full, out message) || !TryReadLeased(full, itemId, leaseToken, out string path, out QueueItem item, out message)) return false;
                item.LastError = LocalQueueCore.TrimError(reason); item.LeaseToken = null; item.LeaseExpiresUtc = null;
                LocalQueueCore.WriteAtomic(path, item); File.Move(path, LocalQueueCore.ItemPath(full, "rejected", item.Id)); return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("RejectItem", ex); return false; }
        }

        /// <summary>Returns scalar counts for every item state. Never throws.</summary>
        [Category("Local Queue - Query")]
        [Description("Returns scalar counts for every work-item state. Never throws.")]
        public bool GetCounts(string queuePath, out int ready, out int delayed, out int inProgress, out int completed, out int rejected, out int corrupt, out string message)
        {
            ready = delayed = inProgress = completed = rejected = corrupt = 0; message = null;
            try
            {
                if (!RequireOwned(queuePath, out string full, out message)) return false;
                Dictionary<string, int> c = CountStates(full); ready = c["ready"]; delayed = c["delayed"]; inProgress = c["in-progress"]; completed = c["completed"]; rejected = c["rejected"]; corrupt = c["corrupt"]; return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("GetCounts", ex); return false; }
        }

        /// <summary>Returns an item's state and JSON metadata. Never throws.</summary>
        [Category("Local Queue - Query")]
        [Description("Returns one item's state and complete metadata as JSON. Not found is a normal result. Never throws.")]
        public bool GetItem(string queuePath, string itemId, out bool found, out string state, out string itemJson, out string message)
        {
            found = false; state = itemJson = null; message = null;
            try
            {
                if (!RequireOwned(queuePath, out string full, out message) || !ValidId(itemId, out message)) return false;
                foreach (string candidate in new[] { "ready", "delayed", "in-progress", "completed", "rejected", "corrupt" })
                {
                    string path = LocalQueueCore.ItemPath(full, candidate, itemId);
                    if (File.Exists(path)) { found = true; state = candidate; itemJson = File.ReadAllText(path); break; }
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("GetItem", ex); return false; }
        }

        /// <summary>Recovers all expired in-progress leases. Never throws.</summary>
        [Category("Local Queue - Recovery")]
        [Description("Recovers expired leases, consuming an attempt and rejecting exhausted items. Never throws.")]
        public bool RecoverExpiredLeases(string queuePath, out int recoveredCount, out int rejectedCount, out string message)
        {
            recoveredCount = rejectedCount = 0; message = null;
            try { return RequireOwned(queuePath, out string full, out message) && RecoverCore(full, out recoveredCount, out rejectedCount); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("RecoverExpiredLeases", ex); return false; }
        }

        /// <summary>Returns a rejected item to ready or delayed state. Never throws.</summary>
        [Category("Local Queue - Recovery")]
        [Description("Returns a rejected item to ready state, optionally resetting its attempt count. Never throws.")]
        public bool RetryRejectedItem(string queuePath, string itemId, bool resetAttemptCount, out string message, int delaySeconds = 0)
        {
            message = null;
            try
            {
                if (delaySeconds < 0) { message = "delaySeconds must not be negative."; return false; }
                if (!RequireOwned(queuePath, out string full, out message) || !ValidId(itemId, out message)) return false;
                string path = LocalQueueCore.ItemPath(full, "rejected", itemId); if (!File.Exists(path)) { message = $"Rejected item '{itemId}' was not found."; return false; }
                QueueItem item = LocalQueueCore.ReadItem(path); if (resetAttemptCount) item.Attempt = 0; item.AvailableUtc = DateTime.UtcNow.AddSeconds(delaySeconds); item.LastError = null;
                LocalQueueCore.WriteAtomic(path, item); File.Move(path, LocalQueueCore.ItemPath(full, delaySeconds == 0 ? "ready" : "delayed", item.Id)); return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("RetryRejectedItem", ex); return false; }
        }

        /// <summary>Deletes completed metadata older than a specified age. Never throws.</summary>
        [Category("Local Queue - Cleanup")]
        [Description("Deletes completed item metadata older than the requested age. Never throws.")]
        public bool DeleteCompletedItems(string queuePath, int olderThanDays, out int removedCount, out string message)
        {
            removedCount = 0; message = null;
            try
            {
                if (olderThanDays < 0) { message = "olderThanDays must not be negative."; return false; }
                if (!RequireOwned(queuePath, out string full, out message)) return false;
                DateTime cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
                foreach (string path in Directory.EnumerateFiles(LocalQueueCore.StatePath(full, "completed"), "*.json"))
                    if (File.GetLastWriteTimeUtc(path) <= cutoff)
                    {
                        QueueItem item = LocalQueueCore.ReadItem(path);
                        if (item.StoredFilePath != null && File.Exists(item.StoredFilePath)) File.Delete(item.StoredFilePath);
                        File.Delete(path); removedCount++;
                    }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("DeleteCompletedItems", ex); return false; }
        }

        /// <summary>Deletes rejected item metadata and queue-owned files older than a specified age. Never throws.</summary>
        [Category("Local Queue - Cleanup")]
        [Description("Deletes rejected item metadata and queue-owned files older than the requested age. Never throws.")]
        public bool DeleteRejectedItems(string queuePath, int olderThanDays, out int removedCount, out string message)
        {
            removedCount = 0; message = null;
            try
            {
                if (olderThanDays < 0) { message = "olderThanDays must not be negative."; return false; }
                if (!RequireOwned(queuePath, out string full, out message)) return false;
                DateTime cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
                foreach (string path in Directory.EnumerateFiles(LocalQueueCore.StatePath(full, "rejected"), "*.json"))
                    if (File.GetLastWriteTimeUtc(path) <= cutoff)
                    {
                        QueueItem item = LocalQueueCore.ReadItem(path);
                        if (item.StoredFilePath != null && File.Exists(item.StoredFilePath)) File.Delete(item.StoredFilePath);
                        File.Delete(path); removedCount++;
                    }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("DeleteRejectedItems", ex); return false; }
        }

        /// <summary>Deletes a queue only when it contains no item metadata or imported files. Never throws.</summary>
        [Category("Local Queue - Cleanup")]
        [Description("Deletes a queue only when every item state and queue-owned file directory is empty. Never throws.")]
        public bool DeleteQueueIfEmpty(string queuePath, out bool deleted, out string message)
        {
            deleted = false; message = null;
            try
            {
                if (!RequireOwned(queuePath, out string full, out message)) return false;
                if (LocalQueueCore.States.Where(s => s != "temp").Any(s => Directory.EnumerateFileSystemEntries(LocalQueueCore.StatePath(full, s)).Any())) return true;
                if (Directory.EnumerateFileSystemEntries(LocalQueueCore.StatePath(full, "temp")).Any()) return true;
                FileStream stream = locks[full]; locks.Remove(full); stream.Dispose();
                Directory.Delete(full, true); deleted = true; return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("DeleteQueueIfEmpty", ex); return false; }
        }

        /// <summary>Deletes an entire queue, requiring explicit confirmation when it contains work. Never throws.</summary>
        [Category("Local Queue - Cleanup")]
        [Description("Deletes an entire queue. A non-empty queue is deleted only when confirmDeleteNonEmpty is True. Referenced source files are never deleted. Never throws.")]
        public bool DeleteQueue(string queuePath, bool confirmDeleteNonEmpty, out bool deleted, out int deletedItemCount, out string message)
        {
            deleted = false; deletedItemCount = 0; message = null;
            try
            {
                if (!RequireOwned(queuePath, out string full, out message)) return false;
                string[] itemStates = { "ready", "delayed", "in-progress", "completed", "rejected", "corrupt" };
                int itemCount = itemStates.Sum(state => Directory.EnumerateFiles(LocalQueueCore.StatePath(full, state), "*.json").Count());
                bool hasOwnedFiles = Directory.EnumerateFileSystemEntries(LocalQueueCore.StatePath(full, "files")).Any() ||
                                     Directory.EnumerateFileSystemEntries(LocalQueueCore.StatePath(full, "temp")).Any();
                if ((itemCount > 0 || hasOwnedFiles) && !confirmDeleteNonEmpty)
                {
                    message = $"Queue contains {itemCount} work item(s) or queue-owned files. Set confirmDeleteNonEmpty to True to delete it.";
                    return false;
                }

                FileStream stream = locks[full];
                locks.Remove(full);
                stream.Dispose();
                Directory.Delete(full, true);
                deleted = true;
                deletedItemCount = itemCount;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("DeleteQueue", ex);
                return false;
            }
        }

        private bool AddFilesCore(string operation, string queuePath, string sourceDirectory, string pattern, bool recursive, bool import, int priority, int maxAttempts, out int count, out string message)
        {
            count = 0; message = null;
            try
            {
                if (!RequireOwned(queuePath, out string full, out message)) return false;
                if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory)) { message = "An existing sourceDirectory is required."; return false; }
                if (string.IsNullOrWhiteSpace(pattern)) { message = "searchPattern is required."; return false; }
                if (priority < 0 || priority > 100) { message = "priority must be between 0 and 100."; return false; }
                if (maxAttempts < 1) { message = "maximumAttempts must be at least 1."; return false; }
                foreach (string source in Directory.EnumerateFiles(sourceDirectory, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                {
                    string stored = null; string id = LocalQueueCore.NewId();
                    if (import) { stored = Path.Combine(LocalQueueCore.StatePath(full, "files"), id + Path.GetExtension(source)); File.Copy(source, stored, false); }
                    string payload = JsonSerializer.Serialize(new { path = import ? stored : Path.GetFullPath(source) });
                    if (!AddCore(full, payload, null, priority, 0, maxAttempts, source, stored, out _, out _, out message, id))
                    {
                        if (stored != null && File.Exists(stored)) File.Delete(stored);
                        return false;
                    }
                    count++;
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(operation, ex); return false; }
        }

        private bool AddCore(string queuePath, string payload, string businessKey, int priority, int delay, int maxAttempts, string source, string stored, out string id, out bool duplicate, out string message, string presetId = null)
        {
            id = null; duplicate = false; message = null;
            if (!RequireOwned(queuePath, out string full, out message)) return false;
            if (priority < 0 || priority > 100) { message = "priority must be between 0 and 100."; return false; }
            if (delay < 0) { message = "delaySeconds must not be negative."; return false; }
            if (maxAttempts < 1) { message = "maximumAttempts must be at least 1."; return false; }
            businessKey = string.IsNullOrWhiteSpace(businessKey) ? null : businessKey.Trim();
            if (businessKey != null)
            {
                var existing = LocalQueueCore.ReadItems(full, "ready", "delayed", "in-progress", "rejected", "completed").FirstOrDefault(x => string.Equals(x.Item.BusinessKey, businessKey, StringComparison.Ordinal));
                if (existing.Item != null) { id = existing.Item.Id; duplicate = true; return true; }
            }
            id = presetId ?? LocalQueueCore.NewId(); DateTime now = DateTime.UtcNow;
            var item = new QueueItem { Id = id, BusinessKey = businessKey, Payload = payload, Priority = priority, CreatedUtc = now, AvailableUtc = now.AddSeconds(delay), MaximumAttempts = maxAttempts, SourceFilePath = source, StoredFilePath = stored };
            LocalQueueCore.WriteAtomic(LocalQueueCore.ItemPath(full, delay == 0 ? "ready" : "delayed", id), item); return true;
        }

        private bool AcquireLock(string path, out string message)
        {
            message = null; string full = Path.GetFullPath(path);
            if (locks.ContainsKey(full)) return true;
            try { locks.Add(full, new FileStream(Path.Combine(full, ".queue.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); return true; }
            catch (IOException) { message = "The queue is already open by another LocalQueueUtils component or process."; return false; }
        }

        private bool RequireOwned(string path, out string full, out string message)
        {
            if (!LocalQueueCore.TryValidatePath(path, out full, out message)) return false;
            if (!locks.ContainsKey(full)) { message = "The queue is not open in this component. Call CreateQueue or OpenQueue first."; return false; }
            return true;
        }

        private static bool ValidId(string id, out string message) { message = null; if (!LocalQueueCore.ValidSegment(id)) { message = "A valid itemId is required."; return false; } return true; }
        private static bool ValidateLease(int seconds, out string message) { message = null; if (seconds < 1 || seconds > 86400) { message = "leaseSeconds must be between 1 and 86400."; return false; } return true; }

        private static bool TryReadLeased(string full, string id, string token, out string path, out QueueItem item, out string message)
        {
            path = null; item = null; message = null;
            if (!ValidId(id, out message) || string.IsNullOrWhiteSpace(token)) { if (message == null) message = "A leaseToken is required."; return false; }
            path = LocalQueueCore.ItemPath(full, "in-progress", id); if (!File.Exists(path)) { message = $"In-progress item '{id}' was not found."; return false; }
            item = LocalQueueCore.ReadItem(path);
            if (!string.Equals(item.LeaseToken, token, StringComparison.Ordinal)) { message = "The lease token is stale or does not belong to this item."; return false; }
            if (item.LeaseExpiresUtc <= DateTime.UtcNow) { message = "The item's lease has expired. Recover it before continuing."; return false; }
            return true;
        }

        private static bool RecoverCore(string full, out int recovered, out int rejected)
        {
            recovered = rejected = 0; DateTime now = DateTime.UtcNow;
            foreach (var entry in LocalQueueCore.ReadItems(full, "in-progress").Where(x => !x.Item.LeaseExpiresUtc.HasValue || x.Item.LeaseExpiresUtc <= now).ToArray())
            {
                QueueItem item = entry.Item; item.LeaseToken = null; item.LeaseExpiresUtc = null; item.LastError = "Lease expired before completion.";
                string state = item.Attempt >= item.MaximumAttempts ? "rejected" : "ready";
                if (state == "rejected") rejected++; else recovered++;
                item.AvailableUtc = now; LocalQueueCore.WriteAtomic(entry.Path, item); File.Move(entry.Path, LocalQueueCore.ItemPath(full, state, item.Id));
            }
            return true;
        }

        private static Dictionary<string, int> CountStates(string full) => new[] { "ready", "delayed", "in-progress", "completed", "rejected", "corrupt" }.ToDictionary(s => s, s => Directory.EnumerateFiles(LocalQueueCore.StatePath(full, s), "*.json").Count());

        /// <summary>Releases every queue ownership lock held by this component.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (FileStream stream in locks.Values) stream.Dispose(); locks.Clear(); }
            base.Dispose(disposing);
        }
    }
}
