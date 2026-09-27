namespace ResourceLockAutomation
{
    /// <summary>The fixed limits. Every input outside them fails the call and changes nothing.</summary>
    internal static class LockLimits
    {
        internal const int MinLeaseSeconds = 5;
        internal const int MaxLeaseSeconds = 86400;
        internal const int MaxWaitMilliseconds = 3600000;
        internal const int MinCapacity = 1;
        internal const int MaxCapacity = 100;
        internal const int MaxResourceLength = 100;
        internal const int MaxHolderLength = 128;
        internal const int MaxFolderPathLength = 200;
        internal const int MaxHeldPerInstance = 1000;
    }
}
