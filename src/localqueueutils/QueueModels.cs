using System;

namespace LocalQueueAutomation
{
    internal sealed class QueueManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public string QueueName { get; set; }
        public string Lifetime { get; set; }
        public string RunId { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    internal sealed class QueueItem
    {
        public string Id { get; set; }
        public string BusinessKey { get; set; }
        public string Payload { get; set; }
        public int Priority { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime AvailableUtc { get; set; }
        public int Attempt { get; set; }
        public int MaximumAttempts { get; set; }
        public string LeaseToken { get; set; }
        public DateTime? LeaseExpiresUtc { get; set; }
        public string LastError { get; set; }
        public string ResultJson { get; set; }
        public string SourceFilePath { get; set; }
        public string StoredFilePath { get; set; }
    }
}
