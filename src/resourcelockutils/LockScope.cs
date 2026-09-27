namespace ResourceLockAutomation
{
    /// <summary>Who a lock is shared with.</summary>
    public enum LockScope
    {
        /// <summary>Every thread and automation in this Robot Runtime. Held in memory; a lock taken on one thread can be released on another.</summary>
        Process,
        /// <summary>Every robot on this machine, including Server Bots in other sessions under other Windows accounts. Held as files in the lock folder.</summary>
        Machine
    }
}
