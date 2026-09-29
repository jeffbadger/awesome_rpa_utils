namespace BrowserInterruptAutomation
{
    /// <summary>What could be established about an element reference.</summary>
    internal enum Liveness
    {
        /// <summary>Resolved to a live element.</summary>
        Alive,

        /// <summary>Definitively gone: its window is destroyed, or a live-resolved/pinned/cached element reported itself unavailable.</summary>
        Dead,

        /// <summary>Nothing reliable can be said (a transient read/provider failure, or a window-less reference that is neither pinned nor cached).</summary>
        Unknown
    }

    /// <summary>The outcome of one <c>Current</c> read on a UI Automation element.</summary>
    internal enum ReadOutcome
    {
        /// <summary>The read succeeded.</summary>
        Ok,

        /// <summary>The read threw <c>ElementNotAvailableException</c>: the element is definitively gone.</summary>
        Unavailable,

        /// <summary>Any other failure (COM error, non-"unavailable" <c>InvalidOperationException</c>, timeout, ...): says nothing about whether the element is open.</summary>
        OtherFailure
    }

    /// <summary>
    /// The single place that decides when a reference is reported dead. UI Automation independent
    /// (compiled on every platform) so the rules are unit-testable. Only two things are definitive:
    /// a destroyed window and a confirmed <see cref="ReadOutcome.Unavailable"/>; every other failure
    /// is <see cref="Liveness.Unknown"/>, which <c>IsAlive</c> reports as alive (a popup that is still
    /// open must never be recorded as dismissed because of a transient provider error).
    /// </summary>
    internal static class LivenessClassifier
    {
        /// <summary>A reference with a window handle is dead iff that window no longer exists.</summary>
        internal static Liveness ClassifyWindow(bool hasWindowHandle, bool isWindow) =>
            hasWindowHandle && !isWindow ? Liveness.Dead : Liveness.Alive;

        /// <summary>Maps one element read to a liveness.</summary>
        internal static Liveness ClassifyRead(ReadOutcome outcome) => outcome switch
        {
            ReadOutcome.Ok => Liveness.Alive,
            ReadOutcome.Unavailable => Liveness.Dead,
            _ => Liveness.Unknown
        };

        /// <summary>A cache entry may be evicted only when its element is confirmed unavailable, never on a non-definitive failure.</summary>
        internal static bool ShouldEvictCacheEntry(ReadOutcome outcome) => outcome == ReadOutcome.Unavailable;

        /// <summary>What <c>IsAlive</c> reports: false only for a definitive <see cref="Liveness.Dead"/>.</summary>
        internal static bool ReportsAlive(Liveness liveness) => liveness != Liveness.Dead;
    }
}
