namespace WinEventAutomation
{
    /// <summary>
    /// What a subscription's event queue does when it's full and a new event
    /// arrives, set via <see cref="WinEventUtils.SetQueueLimits(int, WinEventOverflowPolicy, out string)"/>.
    /// </summary>
    public enum WinEventOverflowPolicy
    {
        /// <summary>Drops the oldest queued event to make room for the new one.</summary>
        DropOldest,
        /// <summary>Drops the new event, keeping the queue's existing contents.</summary>
        DropNewest,
        /// <summary>
        /// Accepted for compatibility with the string-based overload, but behaves
        /// exactly like <see cref="DropNewest"/>: the WinEvent hook thread must
        /// never block, so a full queue always drops the new event.
        /// </summary>
        Block
    }
}
