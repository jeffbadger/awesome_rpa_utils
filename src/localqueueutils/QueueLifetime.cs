namespace LocalQueueAutomation
{
    /// <summary>Controls whether a queue belongs to one run or remains available to unrelated later runs.</summary>
    public enum QueueLifetime
    {
        /// <summary>The queue belongs to one required run ID.</summary>
        Run = 0,
        /// <summary>The queue remains available across unrelated automation runs.</summary>
        Persistent = 1
    }
}
