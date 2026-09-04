using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace LocalQueueAutomation.Tests
{
    public sealed class LocalQueueUtilsTests : IDisposable
    {
        private readonly LocalQueueUtils queue = new LocalQueueUtils();
        private readonly string name = "test-" + Guid.NewGuid().ToString("N");
        private string path;

        public void Dispose()
        {
            queue.Dispose();
            if (path != null)
            {
                string root = Path.Combine(LocalQueueCore.BasePath, name);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private void Create(QueueLifetime lifetime = QueueLifetime.Run)
        {
            Assert.True(queue.CreateQueue(name, lifetime, lifetime == QueueLifetime.Run ? "run-1" : null, out path, out bool existed, out string message), message);
            Assert.False(existed);
        }

        [Fact]
        public void CreateQueue_Run_CreatesExpectedPath()
        {
            Create();
            Assert.Contains(Path.Combine(name, "runs", "run-1"), path);
        }

        [Fact]
        public void AddTakeComplete_RoundTripsPayloadAndCounts()
        {
            Create();
            Assert.True(queue.AddJson(path, "{\"value\":42}", out string id, out bool duplicate, out string message), message);
            Assert.False(duplicate);
            Assert.True(queue.TryTakeNext(path, out bool available, out string takenId, out string payload, out _, out int attempt, out string token, out _, out message), message);
            Assert.True(available); Assert.Equal(id, takenId); Assert.Equal(1, attempt); Assert.Equal(42, JsonDocument.Parse(payload).RootElement.GetProperty("value").GetInt32());
            Assert.True(queue.CompleteItem(path, id, token, out message, "{\"ok\":true}"), message);
            Assert.True(queue.GetCounts(path, out int ready, out _, out int active, out int completed, out _, out _, out message), message);
            Assert.Equal(0, ready); Assert.Equal(0, active); Assert.Equal(1, completed);
        }

        [Fact]
        public void AddJson_DuplicateBusinessKey_DoesNotAddSecondItem()
        {
            Create();
            Assert.True(queue.AddJson(path, "1", out string first, out _, out string message, "key"), message);
            Assert.True(queue.AddJson(path, "2", out string second, out bool duplicate, out message, "key"), message);
            Assert.True(duplicate); Assert.Equal(first, second);
            Assert.True(queue.GetCounts(path, out int ready, out _, out _, out _, out _, out _, out message), message);
            Assert.Equal(1, ready);
        }

        [Fact]
        public void RetryItem_AtMaximumAttempts_RejectsItem()
        {
            Create();
            Assert.True(queue.AddJson(path, "1", out string id, out _, out string message, maximumAttempts: 1), message);
            Assert.True(queue.TryTakeNext(path, out _, out _, out _, out _, out _, out string token, out _, out message), message);
            Assert.True(queue.RetryItem(path, id, token, "bad", out bool retry, out bool rejected, out message), message);
            Assert.False(retry); Assert.True(rejected);
        }

        [Fact]
        public void AddJsonArrayAndLines_AddOneItemPerValue()
        {
            Create();
            Assert.True(queue.AddJsonArray(path, "[1,2,3]", out int jsonCount, out string message), message);
            Assert.True(queue.AddLines(path, "a\r\n\r\nb\n", out int lineCount, out message), message);
            Assert.Equal(3, jsonCount); Assert.Equal(2, lineCount);
        }

        [Fact]
        public void SecondComponent_CannotOpenOwnedQueue()
        {
            Create();
            using var second = new LocalQueueUtils();
            Assert.False(second.OpenQueue(path, out string message));
            Assert.Contains("already open", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void QueueOutsideLocalRoot_IsRejected()
        {
            Assert.False(queue.OpenQueue(Path.GetTempPath(), out string message));
            Assert.Contains("beneath", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryTakeNext_SelectsHighestPriorityFirst()
        {
            Create();
            Assert.True(queue.AddJson(path, "\"low\"", out _, out _, out string message, priority: 1), message);
            Assert.True(queue.AddJson(path, "\"high\"", out _, out _, out message, priority: 100), message);
            Assert.True(queue.TryTakeNext(path, out bool available, out _, out string payload, out _, out _, out _, out _, out message), message);
            Assert.True(available); Assert.Equal("high", JsonSerializer.Deserialize<string>(payload));
        }

        [Fact]
        public void PersistentQueue_ReopensAfterComponentDisposal()
        {
            Create(QueueLifetime.Persistent);
            Assert.True(queue.AddJson(path, "1", out _, out _, out string message), message);
            queue.Dispose();
            using var reopened = new LocalQueueUtils();
            Assert.True(reopened.OpenQueue(path, out message), message);
            Assert.True(reopened.GetCounts(path, out int ready, out _, out _, out _, out _, out _, out message), message);
            Assert.Equal(1, ready);
        }

        [Fact]
        public void CorruptReadyItem_IsQuarantinedWhileTakingNext()
        {
            Create();
            File.WriteAllText(Path.Combine(path, "ready", "broken.json"), "not json");
            Assert.True(queue.TryTakeNext(path, out bool available, out _, out _, out _, out _, out _, out _, out string message), message);
            Assert.False(available);
            Assert.True(queue.GetCounts(path, out _, out _, out _, out _, out _, out int corrupt, out message), message);
            Assert.Equal(1, corrupt);
        }

        [Fact]
        public void DeleteQueue_NonEmptyRequiresConfirmationAndPreservesReferencedFiles()
        {
            Create();
            string sourceDirectory = Directory.CreateTempSubdirectory("localqueue-source-").FullName;
            string sourcePath = Path.Combine(sourceDirectory, "input.txt");
            try
            {
                File.WriteAllText(sourcePath, "source");
                Assert.True(queue.AddFileReferences(path, sourceDirectory, "*.txt", false, out int added, out string message), message);
                Assert.Equal(1, added);

                Assert.False(queue.DeleteQueue(path, false, out bool deleted, out int deletedCount, out message));
                Assert.False(deleted); Assert.Equal(0, deletedCount); Assert.True(Directory.Exists(path));

                Assert.True(queue.DeleteQueue(path, true, out deleted, out deletedCount, out message), message);
                Assert.True(deleted); Assert.Equal(1, deletedCount); Assert.False(Directory.Exists(path));
                Assert.True(File.Exists(sourcePath));
            }
            finally
            {
                if (Directory.Exists(sourceDirectory)) Directory.Delete(sourceDirectory, true);
            }
        }
    }
}
