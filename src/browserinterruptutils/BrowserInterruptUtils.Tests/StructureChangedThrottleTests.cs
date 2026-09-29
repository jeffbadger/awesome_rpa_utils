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

        // ------------------------------------------------------------ trailing notification

        private static (long last, int pending) Fresh(long now) => (StructureChangedThrottle.InitialSeed(now, CoalesceMs), 0);

        private static StructureChangedThrottle.Decision Decide(ref long last, ref int pending, long now)
            => StructureChangedThrottle.Decide(ref last, ref pending, now, CoalesceMs);

        private static StructureChangedThrottle.TrailingResult Consume(ref long last, ref int pending, long now, out int delay)
            => StructureChangedThrottle.TryConsumeTrailing(ref last, ref pending, now, CoalesceMs, out delay);

        [Fact]
        public void Decide_FirstSignal_Raises()
        {
            long now = 1_000_000L;
            var (last, pending) = Fresh(now);
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref last, ref pending, now));
            Assert.Equal(now, last);
            Assert.Equal(0, pending);
        }

        [Fact]
        public void Decide_BurstInsideCooldown_ArmsExactlyOneTrailing()
        {
            long now = 1_000_000L;
            var (last, pending) = Fresh(now);
            Decide(ref last, ref pending, now);

            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref last, ref pending, now + 1));
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedAlreadyArmed, Decide(ref last, ref pending, now + 50));
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedAlreadyArmed, Decide(ref last, ref pending, now + 249));
            Assert.Equal(1, pending);
            Assert.Equal(now, last); // suppressed signals never advance the cooldown
        }

        [Fact]
        public void DelayUntilCooldownEndsMs_IsTheRemainingTime()
        {
            Assert.Equal(CoalesceMs, StructureChangedThrottle.DelayUntilCooldownEndsMs(1000, 1000, CoalesceMs));
            Assert.Equal(200, StructureChangedThrottle.DelayUntilCooldownEndsMs(1000, 1050, CoalesceMs));
            Assert.Equal(1, StructureChangedThrottle.DelayUntilCooldownEndsMs(1000, 1249, CoalesceMs));
            Assert.Equal(0, StructureChangedThrottle.DelayUntilCooldownEndsMs(1000, 1250, CoalesceMs));
            Assert.Equal(0, StructureChangedThrottle.DelayUntilCooldownEndsMs(1000, 9999, CoalesceMs));
            Assert.Equal(CoalesceMs, StructureChangedThrottle.DelayUntilCooldownEndsMs(1000, 900, CoalesceMs)); // backwards clock
        }

        [Fact]
        public void TryConsumeTrailing_AfterTheCooldown_RaisesOnce_AndALaterSignalStartsAFreshCooldown()
        {
            long now = 1_000_000L;
            var (last, pending) = Fresh(now);
            Decide(ref last, ref pending, now);
            Decide(ref last, ref pending, now + 10);

            var r = Consume(ref last, ref pending, now + CoalesceMs, out _);
            Assert.Equal(StructureChangedThrottle.TrailingResult.Raise, r);
            Assert.Equal(now + CoalesceMs, last); // the trailing raise starts the next cooldown
            Assert.Equal(0, pending);

            // exactly once
            Assert.Equal(StructureChangedThrottle.TrailingResult.Nothing, Consume(ref last, ref pending, now + CoalesceMs + 1, out _));

            // a signal inside the new cooldown is suppressed and arms a new trailing...
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref last, ref pending, now + CoalesceMs + 10));
            // ...and one after it raises on the leading edge again
            pending = 0;
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref last, ref pending, now + 2 * CoalesceMs));
        }

        [Fact]
        public void TryConsumeTrailing_BeforeTheCooldownEnds_RearmsWithRemainingDelay_AndDoesNotRaiseEarly()
        {
            long now = 1_000_000L;
            var (last, pending) = Fresh(now);
            Decide(ref last, ref pending, now);
            Decide(ref last, ref pending, now + 10); // arms

            var r = Consume(ref last, ref pending, now + 100, out int delay);
            Assert.Equal(StructureChangedThrottle.TrailingResult.Rearm, r);
            Assert.Equal(CoalesceMs - 100, delay);
            Assert.Equal(1, pending);   // still pending
            Assert.Equal(now, last);    // not advanced: nothing was raised
        }

        [Fact]
        public void TryConsumeTrailing_AfterANewerRaiseRestartedTheCooldown_Rearms()
        {
            long now = 1_000_000L;
            var (last, pending) = Fresh(now);
            Decide(ref last, ref pending, now);
            Decide(ref last, ref pending, now + 10); // arms, pending=1

            // Timer fires late; a leading raise arrives first and (correctly) clears the pending trailing.
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref last, ref pending, now + CoalesceMs + 5));
            Assert.Equal(0, pending);
            Assert.Equal(StructureChangedThrottle.TrailingResult.Nothing, Consume(ref last, ref pending, now + CoalesceMs + 6, out _));

            // A signal in the new cooldown arms again; consuming inside it re-arms with the remaining time.
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref last, ref pending, now + CoalesceMs + 20));
            Assert.Equal(StructureChangedThrottle.TrailingResult.Rearm, Consume(ref last, ref pending, now + CoalesceMs + 30, out int delay));
            Assert.Equal(CoalesceMs - 25, delay);
        }

        [Fact]
        public void SteadyStream_RaisesAtMostOncePerCooldown_AndEndsWithAFinalTrailing()
        {
            long start = 1_000_000L;
            var (last, pending) = Fresh(start);
            int raises = 0;
            long lastRaiseAt = long.MinValue;
            long armedDue = -1;

            // A signal every 10ms for 1s, with a simulated timer that fires when due.
            long t = start;
            for (; t <= start + 1000; t += 10)
            {
                if (armedDue >= 0 && t >= armedDue)
                {
                    var r = Consume(ref last, ref pending, t, out int d);
                    if (r == StructureChangedThrottle.TrailingResult.Raise) { raises++; Assert.True(lastRaiseAt == long.MinValue || t - lastRaiseAt >= CoalesceMs); lastRaiseAt = t; armedDue = -1; }
                    else if (r == StructureChangedThrottle.TrailingResult.Rearm) armedDue = t + d;
                    else armedDue = -1;
                }
                var dec = Decide(ref last, ref pending, t);
                if (dec == StructureChangedThrottle.Decision.Raise)
                {
                    raises++;
                    Assert.True(lastRaiseAt == long.MinValue || t - lastRaiseAt >= CoalesceMs);
                    lastRaiseAt = t;
                    armedDue = -1;
                }
                else if (dec == StructureChangedThrottle.Decision.SuppressedArmTrailing)
                    armedDue = t + StructureChangedThrottle.DelayUntilCooldownEndsMs(last, t, CoalesceMs);
            }

            // Stream stops; let the timer fire.
            Assert.True(armedDue >= 0, "The last suppressed signals must have armed a final trailing.");
            var final = Consume(ref last, ref pending, armedDue, out _);
            Assert.Equal(StructureChangedThrottle.TrailingResult.Raise, final);
            raises++;

            Assert.InRange(raises, 4, 6); // ~1000ms / 250ms, plus the guaranteed final
        }

        [Fact]
        public void Decide_ConcurrentSuppressedSignals_ArmExactlyOneTrailing()
        {
            long now = 1_000_000L;
            var (last, pending) = Fresh(now);
            Decide(ref last, ref pending, now);

            const int n = 8;
            int armed = 0, already = 0, raised = 0;
            var barrier = new Barrier(n);
            var threads = new Thread[n];
            long l = last; int p = pending;
            for (int i = 0; i < n; i++)
            {
                threads[i] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    switch (StructureChangedThrottle.Decide(ref l, ref p, now + 20, CoalesceMs))
                    {
                        case StructureChangedThrottle.Decision.SuppressedArmTrailing: Interlocked.Increment(ref armed); break;
                        case StructureChangedThrottle.Decision.SuppressedAlreadyArmed: Interlocked.Increment(ref already); break;
                        default: Interlocked.Increment(ref raised); break;
                    }
                });
                threads[i].Start();
            }
            foreach (var t in threads) t.Join();

            Assert.Equal(1, armed);
            Assert.Equal(n - 1, already);
            Assert.Equal(0, raised);
        }

        [Fact]
        public void TryConsumeTrailing_ConcurrentConsumers_RaiseExactlyOnce()
        {
            long now = 1_000_000L;
            var (last, pending) = Fresh(now);
            Decide(ref last, ref pending, now);
            Decide(ref last, ref pending, now + 10);

            const int n = 8;
            int raises = 0;
            var barrier = new Barrier(n);
            var threads = new Thread[n];
            long l = last; int p = pending;
            for (int i = 0; i < n; i++)
            {
                threads[i] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    if (StructureChangedThrottle.TryConsumeTrailing(ref l, ref p, now + CoalesceMs, CoalesceMs, out _) == StructureChangedThrottle.TrailingResult.Raise)
                        Interlocked.Increment(ref raises);
                });
                threads[i].Start();
            }
            foreach (var t in threads) t.Join();

            Assert.Equal(1, raises);
            Assert.Equal(0, p);
            Assert.Equal(now + CoalesceMs, l);
        }

        [Fact]
        public void Trailing_Logic_DoesNotOverflow_AtExtremeTickValues()
        {
            Assert.Equal(0, StructureChangedThrottle.DelayUntilCooldownEndsMs(long.MinValue, long.MaxValue, CoalesceMs));
            Assert.Equal(CoalesceMs, StructureChangedThrottle.DelayUntilCooldownEndsMs(long.MaxValue, long.MinValue, CoalesceMs));
            Assert.Equal(CoalesceMs - 1, StructureChangedThrottle.DelayUntilCooldownEndsMs(long.MaxValue - 1, long.MaxValue, CoalesceMs));

            long last = long.MinValue; int pending = 0;
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref last, ref pending, long.MaxValue));

            last = long.MaxValue - 5; pending = 0;
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref last, ref pending, long.MaxValue));
            Assert.Equal(StructureChangedThrottle.TrailingResult.Rearm, Consume(ref last, ref pending, long.MaxValue, out int d));
            Assert.Equal(CoalesceMs - 5, d);
        }
    }
}
