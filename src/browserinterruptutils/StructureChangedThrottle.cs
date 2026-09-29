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
        /// Applies the leading-edge coalescing throttle for one callback: returns <c>true</c>
        /// (and atomically advances <paramref name="lastRaisedTicks"/> to <paramref name="nowTicks"/>)
        /// if at least <paramref name="coalesceMs"/> milliseconds have passed since the last raise,
        /// or <c>false</c> if the callback should be coalesced away (either because it arrived too
        /// soon, or because a concurrent callback just won the race to raise instead).
        /// <paramref name="lastRaisedTicks"/> must have been seeded via <see cref="InitialSeed"/>
        /// (not <see cref="long.MinValue"/>) when the window started being watched, and must only
        /// ever be read/written through <see cref="Interlocked"/> (here and by every other caller),
        /// since this method may be invoked concurrently for the same window.
        /// </summary>
        public static bool ShouldRaise(ref long lastRaisedTicks, long nowTicks, int coalesceMs)
        {
            long last = Interlocked.Read(ref lastRaisedTicks);
            if (nowTicks - last < coalesceMs)
                return false; // coalesced: too soon since the last raise for this window

            // A losing CAS means another callback just won the race and will raise instead.
            return Interlocked.CompareExchange(ref lastRaisedTicks, nowTicks, last) == last;
        }
    }
}
