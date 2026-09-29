using System.Threading;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// The pure timestamp/CAS logic behind <c>BrowserPopupHookThread</c>'s per-window
    /// <c>StructureChanged</c> coalescing throttle (see that type's remarks, "Coalescing happens
    /// here, not in the engine."), pulled out into its own file with no
    /// <c>System.Windows.Automation</c>/WPF dependency so it compiles - and, more importantly, can
    /// be genuinely unit-tested - under both this component's build configurations (default
    /// <c>UseWPF=true</c> and the local <c>-p:UseWPF=false</c> convenience used to actually *run*
    /// tests on a non-Windows host). <c>BrowserPopupHookThread</c>'s own source file is excluded
    /// entirely from the <c>UseWPF=false</c> build (it is the second file, after
    /// <c>UiaBrowserPopupProbe.cs</c>, that genuinely references
    /// <c>System.Windows.Automation</c>), so this logic could not otherwise be exercised by a real
    /// test on this repo's Linux dev host at all. Deliberately plain <c>&lt;c&gt;</c> tags rather
    /// than <c>&lt;see cref&gt;</c> for that type name, since it does not exist in the
    /// <c>UseWPF=false</c> configuration this file is also compiled under.
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
            /// <summary>Suppressed; this is the first suppressed signal of the cooldown, so the caller must arrange exactly one trailing delivery after <see cref="DelayUntilCooldownEndsMs"/>.</summary>
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
        /// Decides what to do with one raw signal at <paramref name="nowTicks"/>. Leading edge: if
        /// the cooldown has ended, wins a CAS on <paramref name="lastRaisedTicks"/> and returns
        /// <see cref="Decision.Raise"/> (clearing any pending trailing, since this raise covers
        /// everything signalled so far). Otherwise (too soon, or lost the CAS to a concurrent raise)
        /// the signal is suppressed: the first one flips <paramref name="trailingPending"/> 0 to 1
        /// and returns <see cref="Decision.SuppressedArmTrailing"/>; the rest coalesce into it
        /// (<see cref="Decision.SuppressedAlreadyArmed"/>). Pure and allocation-free; both fields
        /// are owned by one watch entry and only touched through <see cref="Interlocked"/>.
        /// </summary>
        public static Decision Decide(ref long lastRaisedTicks, ref int trailingPending, long nowTicks, int coalesceMs)
        {
            long last = Interlocked.Read(ref lastRaisedTicks);
            if (DelayUntilCooldownEndsMs(last, nowTicks, coalesceMs) == 0
                && Interlocked.CompareExchange(ref lastRaisedTicks, nowTicks, last) == last)
            {
                Interlocked.Exchange(ref trailingPending, 0);
                return Decision.Raise;
            }

            return Interlocked.CompareExchange(ref trailingPending, 1, 0) == 0
                ? Decision.SuppressedArmTrailing
                : Decision.SuppressedAlreadyArmed;
        }

        /// <summary>
        /// The timer callback's operation. (a) With nothing pending, returns
        /// <see cref="TrailingResult.Nothing"/>. (b) If the cooldown has not ended (a newer raise
        /// restarted it), returns <see cref="TrailingResult.Rearm"/> with the remaining delay and
        /// leaves the trailing pending, so it never raises early. (c) Otherwise claims the pending
        /// trailing with a CAS 1 to 0 (so concurrent consumers raise exactly once), then advances
        /// <paramref name="lastRaisedTicks"/> to <paramref name="nowTicks"/> so the trailing raise
        /// starts the next cooldown. If a leading raise slips in between, that raise already covers
        /// the trailing, so the result is <see cref="TrailingResult.Nothing"/>.
        /// </summary>
        public static TrailingResult TryConsumeTrailing(ref long lastRaisedTicks, ref int trailingPending, long nowTicks, int coalesceMs, out int rearmDelayMs)
        {
            rearmDelayMs = 0;
            if (Interlocked.CompareExchange(ref trailingPending, 1, 1) != 1)
                return TrailingResult.Nothing;

            long last = Interlocked.Read(ref lastRaisedTicks);
            int remaining = DelayUntilCooldownEndsMs(last, nowTicks, coalesceMs);
            if (remaining > 0)
            {
                rearmDelayMs = remaining;
                return TrailingResult.Rearm;
            }

            if (Interlocked.CompareExchange(ref trailingPending, 0, 1) != 1)
                return TrailingResult.Nothing; // another consumer (or a leading raise) claimed it
            return Interlocked.CompareExchange(ref lastRaisedTicks, nowTicks, last) == last
                ? TrailingResult.Raise
                : TrailingResult.Nothing; // a leading raise won the race and covers this trailing
        }
    }
}
