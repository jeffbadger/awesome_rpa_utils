using System.Threading;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>
    /// Real (not mirrored/reimplemented) unit tests for <see cref="StructureChangedThrottle"/>,
    /// the pure timestamp/CAS logic extracted out of <see cref="BrowserPopupHookThread"/>'s
    /// per-window <c>StructureChanged</c> coalescing throttle specifically so it could be exercised
    /// here: <see cref="BrowserPopupHookThread"/> itself lives in a file excluded from this
    /// project's local <c>-p:UseWPF=false</c> build (the convenience used to actually *run* tests
    /// on this repo's Linux dev host, which has no <c>Microsoft.WindowsDesktop.App</c> runtime), so
    /// without this extraction none of this logic could be covered by a real, executing test here
    /// at all - only by a standalone scratch reproduction.
    ///
    /// These tests are a regression guard for a critical Task 4 code-review finding: seeding the
    /// per-window last-raised timestamp to <see cref="long.MinValue"/> made the throttle's
    /// "now - last" subtraction overflow (unchecked arithmetic wraps it to a huge negative number,
    /// always less than the coalesce window), which coalesced away the very first
    /// <c>StructureChanged</c> callback for every newly-watched window - and, since a coalesced
    /// callback never advances the timestamp off the sentinel, every later callback for that
    /// window hit the same overflow forever, permanently silencing the notification.
    /// </summary>
    public class StructureChangedThrottleTests
    {
        private const int CoalesceMs = 250;

        [Fact]
        public void ShouldRaise_TheVeryFirstCallbackForANewlyWatchedWindow_RaisesImmediately()
        {
            // Mirrors exactly what WatchWindow does: seed via InitialSeed, then the first real
            // callback arrives at (or shortly after) that same "now".
            long now = 1_000_000L;
            long lastRaisedTicks = StructureChangedThrottle.InitialSeed(now, CoalesceMs);

            bool raised = StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now, CoalesceMs);

            Assert.True(raised, "The first StructureChanged callback for a newly-watched window must always raise.");
            Assert.Equal(now, lastRaisedTicks);
        }

        [Fact]
        public void ShouldRaise_WithTheOldLongMinValueSentinel_DemonstratesTheOverflowBug()
        {
            // This is the exact bug: seeding to long.MinValue (the ORIGINAL, buggy behavior, kept
            // here only to document/guard against ever reintroducing it) makes "now - last"
            // overflow and permanently coalesce away every callback, including the first.
            long now = System.Environment.TickCount64;
            long lastRaisedTicks = long.MinValue;

            long diff = now - lastRaisedTicks; // overflows: wraps to a huge negative number
            Assert.True(diff < CoalesceMs, "Sanity check: the overflow this test documents must actually occur (unchecked build).");

            bool raised = StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now, CoalesceMs);
            Assert.False(raised, "Documents the historical bug: a long.MinValue seed incorrectly coalesces away the first callback.");
        }

        [Fact]
        public void ShouldRaise_ABurstOfCallbacksWithinTheCooldown_OnlyTheFirstRaises()
        {
            long now = 1_000_000L;
            long lastRaisedTicks = StructureChangedThrottle.InitialSeed(now, CoalesceMs);

            Assert.True(StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now, CoalesceMs));

            // A rapid burst of subsequent callbacks, all inside the 250ms cooldown window.
            Assert.False(StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now + 1, CoalesceMs));
            Assert.False(StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now + 50, CoalesceMs));
            Assert.False(StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now + 249, CoalesceMs));
        }

        [Fact]
        public void ShouldRaise_ACallbackAfterTheCooldownWindow_RaisesAgain()
        {
            long now = 1_000_000L;
            long lastRaisedTicks = StructureChangedThrottle.InitialSeed(now, CoalesceMs);

            Assert.True(StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now, CoalesceMs));
            Assert.False(StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now + 100, CoalesceMs));

            bool raisedAgain = StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now + CoalesceMs, CoalesceMs);

            Assert.True(raisedAgain, "A callback at/after the cooldown boundary must raise again.");
            Assert.Equal(now + CoalesceMs, lastRaisedTicks);
        }

        [Fact]
        public void ShouldRaise_TwoConcurrentCallbacksAtTheSameInstant_OnlyOneWins()
        {
            long now = 1_000_000L;
            long lastRaisedTicks = StructureChangedThrottle.InitialSeed(now, CoalesceMs);
            // Prime steady state (not the very first callback) so this exercises the CAS race path,
            // not the seed itself.
            Assert.True(StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, now, CoalesceMs));

            long raceNow = now + CoalesceMs + 1;
            int raisedCount = 0;
            var barrier = new Barrier(2);

            void Attempt()
            {
                barrier.SignalAndWait();
                if (StructureChangedThrottle.ShouldRaise(ref lastRaisedTicks, raceNow, CoalesceMs))
                    Interlocked.Increment(ref raisedCount);
            }

            var t1 = new Thread(Attempt);
            var t2 = new Thread(Attempt);
            t1.Start();
            t2.Start();
            t1.Join();
            t2.Join();

            Assert.Equal(1, raisedCount);
            Assert.Equal(raceNow, lastRaisedTicks);
        }

        [Fact]
        public void InitialSeed_NeverOverflows_ForRealisticTickCount64Values()
        {
            // TickCount64 is milliseconds since boot: always small/non-negative in practice, so
            // "now - coalesceMs" is nowhere near overflowing in either direction.
            Assert.Equal(0L, StructureChangedThrottle.InitialSeed(CoalesceMs, CoalesceMs));
            Assert.Equal(-CoalesceMs, StructureChangedThrottle.InitialSeed(0, CoalesceMs));

            long realistic = System.Environment.TickCount64;
            long seed = StructureChangedThrottle.InitialSeed(realistic, CoalesceMs);
            Assert.True(seed < realistic);
            Assert.True(realistic - seed >= CoalesceMs);
        }
    }
}
