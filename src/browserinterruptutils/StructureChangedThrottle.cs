using System;
using System.Threading;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// The pure, lock-free state machine behind <c>BrowserPopupHookThread</c>'s per-window
    /// <c>StructureChanged</c> coalescing throttle (see that type's remarks, "Coalescing happens
    /// here, not in the engine."), pulled out into its own file with no
    /// <c>System.Windows.Automation</c>/WPF dependency so it compiles - and, more importantly, can
    /// be genuinely unit-tested - under both this component's build configurations (default
    /// <c>UseWPF=true</c> and the local <c>-p:UseWPF=false</c> convenience used to actually *run*
    /// tests on a non-Windows host). <c>BrowserPopupHookThread</c>'s own source file is excluded
    /// entirely from the <c>UseWPF=false</c> build, so this logic could not otherwise be exercised
    /// by a real test on this repo's Linux dev host at all. Deliberately plain <c>&lt;c&gt;</c>
    /// tags rather than <c>&lt;see cref&gt;</c> for that type name, since it does not exist in the
    /// <c>UseWPF=false</c> configuration this file is also compiled under.
    /// <para>
    /// <b>State.</b> One watched window's whole throttle state is ONE <see cref="long"/> word:
    /// <c>(lastRaisedMs &lt;&lt; 1) | pendingBit</c> (see <see cref="Pack"/>). Every transition
    /// (<see cref="Decide"/>, <see cref="TryConsumeTrailing"/>) is a single compare-and-swap loop
    /// on that word, so a decision is always made from, and committed against, one consistent
    /// snapshot of (timestamp, pending). The earlier design kept the timestamp and the pending flag
    /// in two separate fields, so a timer callback could read the old timestamp, have a leading
    /// raise restart the cooldown and a newly suppressed signal set the pending flag, and then
    /// clear that NEW flag while its timestamp CAS failed - losing the trailing notification (a
    /// review finding). With one word, a consumer whose snapshot went stale fails its CAS and
    /// re-decides from the fresh state (which then still shows pending and a running cooldown, so
    /// it re-arms instead of clearing). A per-window lock was rejected: the CAS loop is equally
    /// simple, allocation-free and cannot block a UIA event thread. The timestamp is milliseconds
    /// since boot, so it needs far fewer than the 63 bits left after the pending bit.
    /// </para>
    /// <para>
    /// <b>Invariants</b>, for arbitrary interleavings of <see cref="Decide"/> and
    /// <see cref="TryConsumeTrailing"/> calls (with non-decreasing time):
    /// (a) the first signal after a cooldown ends raises; (b) the pending bit is set only by a
    /// signal suppressed inside the current cooldown and cleared only by a raise (leading or
    /// trailing), so a signal suppressed after the most recent raise is covered by exactly one
    /// trailing raise no later than that raise's cooldown end, never lost (nothing but a raise
    /// clears the bit) and never duplicated (the raise is the CAS that clears it, and
    /// <see cref="Decision.SuppressedArmTrailing"/> is returned only for the 0-to-1 transition, so
    /// the caller arms exactly one timer per pending episode); (c) a trailing raise stores its own
    /// time as the new timestamp, so it starts the next cooldown; (d) a consumer that runs before
    /// the cooldown ends leaves the state untouched and reports <see cref="TrailingResult.Rearm"/>
    /// with the remaining delay; (e) elapsed-time arithmetic is overflow-safe
    /// (<see cref="DelayUntilCooldownEndsMs"/>) and packing is lossless for |timestamp| &lt; 2^62.
    /// </para>
    /// </summary>
    internal static class StructureChangedThrottle
    {
        /// <summary>
        /// The value a newly-watched window's last-raised timestamp should be seeded to (at
        /// <c>WatchWindow</c> time), given the current <c>Environment.TickCount64</c> and the
        /// coalesce window in milliseconds. Deliberately not <see cref="long.MinValue"/>: that
        /// sentinel makes <see cref="ShouldRaise"/>'s <c>now - last</c> subtraction overflow
        /// (unchecked arithmetic wraps it to a huge negative number, which is always less than
        /// <paramref name="coalesceMs"/>), which would incorrectly coalesce away the very first
        /// callback for every newly watched window - and, because a coalesced callback returns
        /// before ever advancing the timestamp off the sentinel, every later callback for that
        /// window would hit the same overflow forever, permanently silencing the notification.
        /// Seeding to "now - coalesceMs" instead guarantees the first real callback's
        /// "now - last" is already &gt;= <paramref name="coalesceMs"/>, so it raises immediately,
        /// while remaining a genuine, arithmetic-safe <c>TickCount64</c>-shaped value: it is
        /// milliseconds since boot, so it is nowhere near <see cref="long.MinValue"/> and
        /// subtracting a small constant from it cannot overflow in the other direction either.
        /// </summary>
        public static long InitialSeed(long nowTicks, int coalesceMs) => nowTicks - coalesceMs;

        /// <summary>The initial packed state for a newly watched window: seeded per <see cref="InitialSeed"/>, nothing pending.</summary>
        public static long InitialState(long nowTicks, int coalesceMs) => Pack(InitialSeed(nowTicks, coalesceMs), false);

        /// <summary>Packs (last-raised timestamp, pending) into the single state word.</summary>
        public static long Pack(long lastRaisedTicks, bool pending) => unchecked((lastRaisedTicks << 1) | (pending ? 1L : 0L));

        /// <summary>The last-raised timestamp held in a packed state word (arithmetic shift, so negative values round-trip).</summary>
        public static long LastRaised(long state) => state >> 1;

        /// <summary>Whether a packed state word has a trailing notification pending.</summary>
        public static bool IsPending(long state) => (state & 1L) != 0;

        /// <summary>
        /// Applies the plain leading-edge coalescing throttle for one callback: returns <c>true</c>
        /// (and atomically advances <paramref name="lastRaisedTicks"/> to <paramref name="nowTicks"/>)
        /// if at least <paramref name="coalesceMs"/> milliseconds have passed since the last raise,
        /// or <c>false</c> if the callback should be coalesced away. This is the leading-edge
        /// primitive only: it has no memory of a dropped callback, so on its own it can leave a
        /// late change undiscovered. <c>BrowserPopupHookThread</c> therefore uses
        /// <see cref="Decide"/>/<see cref="TryConsumeTrailing"/> (leading edge plus one trailing
        /// notification per burst) instead; this method is retained, with its tests, as the
        /// documented regression guard for the <see cref="long.MinValue"/> seed overflow.
        /// </summary>
        public static bool ShouldRaise(ref long lastRaisedTicks, long nowTicks, int coalesceMs)
        {
            long last = Interlocked.Read(ref lastRaisedTicks);
            if (nowTicks - last < coalesceMs)
                return false; // coalesced: too soon since the last raise for this window

            // A losing CAS means another callback just won the race and will raise instead.
            return Interlocked.CompareExchange(ref lastRaisedTicks, nowTicks, last) == last;
        }

        /// <summary>What <see cref="Decide"/> tells the caller to do with one raw signal.</summary>
        public enum Decision
        {
            /// <summary>Leading edge: raise now (already counted as a raise for the next cooldown).</summary>
            Raise,
            /// <summary>Suppressed; this is the first suppressed signal since the last raise, so the caller must arrange exactly one trailing delivery after the returned delay.</summary>
            SuppressedArmTrailing,
            /// <summary>Suppressed; a trailing delivery is already pending. Nothing more to do.</summary>
            SuppressedAlreadyArmed
        }

        /// <summary>What <see cref="TryConsumeTrailing"/> tells the timer callback to do.</summary>
        public enum TrailingResult
        {
            /// <summary>No trailing is pending (already delivered, or superseded by a leading raise). Do nothing.</summary>
            Nothing,
            /// <summary>The pending trailing was claimed exactly once and counted as a raise: raise now.</summary>
            Raise,
            /// <summary>The cooldown was restarted by a newer raise: re-arm the timer for the returned delay; the trailing stays pending.</summary>
            Rearm
        }

        /// <summary>
        /// Milliseconds from <paramref name="nowTicks"/> until the cooldown that started at
        /// <paramref name="lastRaisedTicks"/> ends: 0 when it has already ended, otherwise in
        /// <c>(0, coalesceMs]</c>. Overflow-safe for any tick values (a backwards clock counts as
        /// a full cooldown remaining).
        /// </summary>
        public static int DelayUntilCooldownEndsMs(long lastRaisedTicks, long nowTicks, int coalesceMs)
        {
            if (nowTicks < lastRaisedTicks)
                return coalesceMs;
            long elapsed = unchecked(nowTicks - lastRaisedTicks);
            if (elapsed < 0 || elapsed >= coalesceMs)
                return 0; // negative here means the subtraction overflowed: an enormous elapsed time
            return coalesceMs - (int)elapsed;
        }

        /// <summary>
        /// Decides what to do with one raw signal at <paramref name="nowTicks"/>, as one CAS loop on
        /// <paramref name="state"/>. If the cooldown has ended, commits (now, not pending) and
        /// returns <see cref="Decision.Raise"/> (the raise covers everything signalled so far).
        /// Otherwise the signal is suppressed: the first one commits the pending bit and returns
        /// <see cref="Decision.SuppressedArmTrailing"/> with <paramref name="armDelayMs"/> computed
        /// from the very snapshot that was committed (so it is the remaining time of the cooldown
        /// this pending bit belongs to); later ones return <see cref="Decision.SuppressedAlreadyArmed"/>.
        /// Pure and allocation-free; the state is only touched through <see cref="Interlocked"/>.
        /// </summary>
        public static Decision Decide(ref long state, long nowTicks, int coalesceMs, out int armDelayMs)
        {
            armDelayMs = 0;
            while (true)
            {
                long s = Interlocked.Read(ref state);
                long last = LastRaised(s);
                int remaining = DelayUntilCooldownEndsMs(last, nowTicks, coalesceMs);
                if (remaining == 0)
                {
                    if (Interlocked.CompareExchange(ref state, Pack(nowTicks, false), s) == s)
                        return Decision.Raise;
                    continue; // state moved under us: re-decide from the fresh snapshot
                }

                if (IsPending(s))
                    return Decision.SuppressedAlreadyArmed;
                if (Interlocked.CompareExchange(ref state, s | 1L, s) == s)
                {
                    armDelayMs = remaining;
                    return Decision.SuppressedArmTrailing;
                }
                // lost a race (a raise, or another suppressed signal): re-decide
            }
        }

        /// <summary>
        /// The timer callback's operation, as one CAS loop on <paramref name="state"/>. (a) With
        /// nothing pending, returns <see cref="TrailingResult.Nothing"/>. (b) If the cooldown has
        /// not ended, returns <see cref="TrailingResult.Rearm"/> with the remaining delay and leaves
        /// the state untouched, so it never raises early. (c) Otherwise commits (now, not pending)
        /// - claiming the pending trailing and starting the next cooldown in one step - and returns
        /// <see cref="TrailingResult.Raise"/>. A CAS that loses to any concurrent transition re-reads
        /// and re-decides, so it can neither clear a pending bit set after its snapshot nor raise
        /// twice.
        /// </summary>
        public static TrailingResult TryConsumeTrailing(ref long state, long nowTicks, int coalesceMs, out int rearmDelayMs)
            => TryConsumeTrailingWithHook(ref state, nowTicks, coalesceMs, out rearmDelayMs, null);

        /// <summary>
        /// Test seam: <paramref name="afterSnapshot"/> (null in production) runs after each state
        /// snapshot and before its commit, letting a test inject the exact interleaving that used to
        /// lose the trailing (a leading raise plus a new suppressed signal between the read and the
        /// commit).
        /// </summary>
        internal static TrailingResult TryConsumeTrailingWithHook(ref long state, long nowTicks, int coalesceMs, out int rearmDelayMs, Action afterSnapshot)
        {
            rearmDelayMs = 0;
            while (true)
            {
                long s = Interlocked.Read(ref state);
                if (!IsPending(s))
                    return TrailingResult.Nothing;

                int remaining = DelayUntilCooldownEndsMs(LastRaised(s), nowTicks, coalesceMs);
                afterSnapshot?.Invoke();
                if (remaining > 0)
                {
                    // Only trust the Rearm verdict if the snapshot is still current; otherwise
                    // re-decide (a raise may have cleared it, or restarted the cooldown).
                    if (Interlocked.Read(ref state) != s)
                        continue;
                    rearmDelayMs = remaining;
                    return TrailingResult.Rearm;
                }

                if (Interlocked.CompareExchange(ref state, Pack(nowTicks, false), s) == s)
                    return TrailingResult.Raise;
            }
        }
    }
}
