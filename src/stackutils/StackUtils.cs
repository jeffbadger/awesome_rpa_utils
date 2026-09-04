using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StackAutomation
{
    /// <summary>Provides an instance-local, in-memory LIFO stack for variable-count RPA work.</summary>
    [Description("Stores text, JSON, and local file references in an instance-local, in-memory LIFO stack. Never throws.")]
    public sealed class StackUtils : Component
    {
        private const int DefaultMaximumItems = 10000;
        private const int AbsoluteMaximumItems = 1000000;
        private readonly object syncRoot = new object();
        private readonly List<StackItem> items = new List<StackItem>();
        private int maximumItems = DefaultMaximumItems;
        private bool disposed;

        /// <summary>Empty constructor required so Pega Robot Studio can create the component.</summary>
        public StackUtils() { }

        /// <summary>Standard designer constructor; attaches the component to a container.</summary>
        public StackUtils(IContainer container) { container?.Add(this); }

        /// <summary>Gets or sets the item capacity used by this component instance.</summary>
        [Category("Stack - Configuration")]
        [Description("Maximum number of items held by this component instance. Valid range: 1 through 1,000,000. Default: 10,000.")]
        [DefaultValue(DefaultMaximumItems)]
        public int MaximumItems
        {
            get
            {
                lock (syncRoot)
                {
                    if (disposed) throw new ObjectDisposedException(nameof(StackUtils));
                    return maximumItems;
                }
            }
            set
            {
                lock (syncRoot)
                {
                    if (disposed) throw new ObjectDisposedException(nameof(StackUtils));
                    if (value < 1 || value > AbsoluteMaximumItems)
                        throw new ArgumentOutOfRangeException(nameof(value), "MaximumItems must be between 1 and 1,000,000.");
                    if (value < items.Count)
                        throw new InvalidOperationException("MaximumItems cannot be less than the current item count (" + items.Count + ").");
                    maximumItems = value;
                }
            }
        }

        /// <summary>Sets the maximum number of items accepted by this stack.</summary>
        [Category("Stack - Configuration")]
        [Description("Sets the item capacity from 1 through 1,000,000. It cannot be reduced below the current count. Never throws.")]
        public bool SetMaximumItems(int maximumItems, out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    if (maximumItems < 1 || maximumItems > AbsoluteMaximumItems)
                    { message = "maximumItems must be between 1 and 1,000,000."; return false; }
                    if (maximumItems < items.Count)
                    { message = "maximumItems cannot be less than the current item count (" + items.Count + ")."; return false; }
                    this.maximumItems = maximumItems;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("SetMaximumItems", ex); return false; }
        }

        /// <summary>Returns the current item count.</summary>
        [Category("Stack - Query")]
        [Description("Returns the current number of items. Never throws.")]
        public bool GetCount(out int count, out string message)
        {
            count = 0; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message)) return false; count = items.Count; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("GetCount", ex); return false; }
        }

        /// <summary>Returns the configured item capacity.</summary>
        [Category("Stack - Query")]
        [Description("Returns the configured item capacity. Never throws.")]
        public bool GetMaximumItems(out int maximumItems, out string message)
        {
            maximumItems = 0; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message)) return false; maximumItems = this.maximumItems; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("GetMaximumItems", ex); return false; }
        }

        /// <summary>Removes every item from the stack.</summary>
        [Category("Stack - Remove")]
        [Description("Removes every item and returns the number removed. Never throws.")]
        public bool Clear(out int removedCount, out string message)
        {
            removedCount = 0; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message)) return false; removedCount = items.Count; items.Clear(); return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("Clear", ex); return false; }
        }

        /// <summary>Pushes one text item.</summary>
        [Category("Stack - Push")]
        [Description("Pushes one text value. Empty text is allowed; null is rejected. Never throws.")]
        public bool PushText(string value, out string message)
        {
            message = null;
            try
            {
                if (value == null) { message = "value is required."; return false; }
                return PushPrepared(new[] { new StackItem(StackItemKind.Text, value) }, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("PushText", ex); return false; }
        }

        /// <summary>Validates and pushes one raw JSON value.</summary>
        [Category("Stack - Push")]
        [Description("Validates and pushes one JSON value. Objects, arrays, strings, numbers, booleans, and null are accepted. Never throws.")]
        public bool PushJson(string valueJson, out string message)
        {
            message = null;
            try
            {
                if (valueJson == null) { message = "valueJson is required."; return false; }
                using (JsonDocument.Parse(valueJson)) { }
                return PushPrepared(new[] { new StackItem(StackItemKind.Json, valueJson) }, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("PushJson", ex); return false; }
        }

        /// <summary>Pushes a normalized reference to an existing file.</summary>
        [Category("Stack - Push")]
        [Description("Pushes the normalized absolute path of an existing local file. The file is not copied, moved, or owned by the stack. Never throws.")]
        public bool PushFileReference(string filePath, out string absolutePath, out string message)
        {
            absolutePath = null; message = null;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) { message = "filePath is required."; return false; }
                string fullPath = Path.GetFullPath(filePath);
                if (!File.Exists(fullPath)) { message = "The file does not exist: " + fullPath; return false; }
                if (!PushPrepared(new[] { new StackItem(StackItemKind.FileReference, fullPath) }, out message)) return false;
                absolutePath = fullPath;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("PushFileReference", ex); absolutePath = null; return false; }
        }

        /// <summary>Pushes all elements of a JSON array atomically.</summary>
        [Category("Stack - Push")]
        [Description("Pushes every element of a JSON array in array order as JSON items. The operation is atomic. Never throws.")]
        public bool PushJsonArray(string jsonArray, out int pushedCount, out string message)
        {
            pushedCount = 0; message = null;
            try
            {
                if (jsonArray == null) { message = "jsonArray is required."; return false; }
                using JsonDocument document = JsonDocument.Parse(jsonArray);
                if (document.RootElement.ValueKind != JsonValueKind.Array) { message = "jsonArray must contain a JSON array."; return false; }
                List<StackItem> prepared = document.RootElement.EnumerateArray().Select(element => new StackItem(StackItemKind.Json, element.GetRawText())).ToList();
                if (!PushPrepared(prepared, out message)) return false;
                pushedCount = prepared.Count;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("PushJsonArray", ex); return false; }
        }

        /// <summary>Pushes all non-empty lines atomically.</summary>
        [Category("Stack - Push")]
        [Description("Pushes each non-empty text line in source order. The operation is atomic. Never throws.")]
        public bool PushLines(string text, out int pushedCount, out string message)
        {
            pushedCount = 0; message = null;
            try
            {
                if (text == null) { message = "text is required."; return false; }
                List<StackItem> prepared = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                    .Where(line => line.Length != 0).Select(line => new StackItem(StackItemKind.Text, line)).ToList();
                if (!PushPrepared(prepared, out message)) return false;
                pushedCount = prepared.Count;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("PushLines", ex); return false; }
        }

        /// <summary>Pushes sorted references to all matching files atomically.</summary>
        [Category("Stack - Push")]
        [Description("Pushes matching file references sorted by absolute path. Files are not copied, moved, or owned. The operation is atomic. Never throws.")]
        public bool PushFileReferences(string directoryPath, string searchPattern, bool recursive, out int pushedCount, out string message)
        {
            pushedCount = 0; message = null;
            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath)) { message = "directoryPath is required."; return false; }
                if (string.IsNullOrWhiteSpace(searchPattern)) { message = "searchPattern is required."; return false; }
                string fullDirectory = Path.GetFullPath(directoryPath);
                if (!Directory.Exists(fullDirectory)) { message = "The directory does not exist: " + fullDirectory; return false; }
                SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                List<StackItem> prepared = Directory.GetFiles(fullDirectory, searchPattern, option).Select(Path.GetFullPath)
                    .OrderBy(path => path, StringComparer.Ordinal).Select(path => new StackItem(StackItemKind.FileReference, path)).ToList();
                if (!PushPrepared(prepared, out message)) return false;
                pushedCount = prepared.Count;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("PushFileReferences", ex); return false; }
        }

        /// <summary>Returns the next item without removing it.</summary>
        [Category("Stack - Read")]
        [Description("Returns the next item without removing it. An empty stack is a successful result with itemAvailable False. Never throws.")]
        public bool TryPeek(out bool itemAvailable, out StackItemKind itemKind, out string value, out string message) =>
            TryRead(false, out itemAvailable, out itemKind, out value, out message);

        /// <summary>Removes and returns the next item.</summary>
        [Category("Stack - Remove")]
        [Description("Immediately removes and returns the next item. An empty stack is a successful result with itemAvailable False. Never throws.")]
        public bool TryPop(out bool itemAvailable, out StackItemKind itemKind, out string value, out string message) =>
            TryRead(true, out itemAvailable, out itemKind, out value, out message);

        /// <summary>Returns a JSON snapshot in next-to-pop order.</summary>
        [Category("Stack - Query")]
        [Description("Returns a JSON array of all items in next-to-pop order. Never throws.")]
        public bool GetSnapshotJson(out string snapshotJson, out string message)
        {
            snapshotJson = null; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    snapshotJson = JsonSerializer.Serialize(items.AsEnumerable().Reverse().Select(item => new { kind = item.Kind.ToString(), value = item.Value }));
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("GetSnapshotJson", ex); return false; }
        }

        /// <summary>Clears in-memory stack contents when disposing.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (syncRoot)
                {
                    items.Clear();
                    disposed = true;
                }
            }
            base.Dispose(disposing);
        }

        private bool PushPrepared(IReadOnlyCollection<StackItem> prepared, out string message)
        {
            message = null;
            lock (syncRoot)
            {
                if (!RequireActive(out message)) return false;
                if (prepared.Count > maximumItems - items.Count)
                { message = "The push would exceed the maximum item count of " + maximumItems + "."; return false; }
                items.AddRange(prepared);
                return true;
            }
        }

        private bool TryRead(bool remove, out bool itemAvailable, out StackItemKind itemKind, out string value, out string message)
        {
            itemAvailable = false; itemKind = default; value = null; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    if (items.Count == 0) return true;
                    int index = items.Count - 1;
                    StackItem item = items[index];
                    if (remove) items.RemoveAt(index);
                    itemAvailable = true; itemKind = item.Kind; value = item.Value;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(remove ? "TryPop" : "TryPeek", ex); return false; }
        }

        private bool RequireActive(out string message)
        {
            message = disposed ? "This StackUtils component has been disposed. Drag a new StackUtils component onto the automation surface." : null;
            return !disposed;
        }

        private sealed class StackItem
        {
            internal StackItem(StackItemKind kind, string value) { Kind = kind; Value = value; }
            internal StackItemKind Kind { get; }
            internal string Value { get; }
        }
    }
}
