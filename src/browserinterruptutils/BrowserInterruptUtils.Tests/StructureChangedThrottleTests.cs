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

        private static long Fresh(long now) => StructureChangedThrottle.InitialState(now, CoalesceMs);
        private static long Last(long state) => StructureChangedThrottle.LastRaised(state);
        private static bool Pending(long state) => StructureChangedThrottle.IsPending(state);

        private static StructureChangedThrottle.Decision Decide(ref long state, long now)
            => StructureChangedThrottle.Decide(ref state, now, CoalesceMs, out _);

        private static StructureChangedThrottle.TrailingResult Consume(ref long state, long now, out int delay)
            => StructureChangedThrottle.TryConsumeTrailing(ref state, now, CoalesceMs, out delay);

        [Fact]
        public void Decide_FirstSignal_Raises()
        {
            long now = 1_000_000L;
            long state = Fresh(now);
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref state, now));
            Assert.Equal(now, Last(state));
            Assert.False(Pending(state));
        }

        [Fact]
        public void Decide_BurstInsideCooldown_ArmsExactlyOneTrailing()
        {
            long now = 1_000_000L;
            long state = Fresh(now);
            Decide(ref state, now);

            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref state, now + 1));
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedAlreadyArmed, Decide(ref state, now + 50));
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedAlreadyArmed, Decide(ref state, now + 249));
            Assert.True(Pending(state));
            Assert.Equal(now, Last(state)); // suppressed signals never advance the cooldown
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
            long state = Fresh(now);
            Decide(ref state, now);
            Decide(ref state, now + 10);

            var r = Consume(ref state, now + CoalesceMs, out _);
            Assert.Equal(StructureChangedThrottle.TrailingResult.Raise, r);
            Assert.Equal(now + CoalesceMs, Last(state)); // the trailing raise starts the next cooldown
            Assert.False(Pending(state));

            // exactly once
            Assert.Equal(StructureChangedThrottle.TrailingResult.Nothing, Consume(ref state, now + CoalesceMs + 1, out _));

            // a signal inside the new cooldown is suppressed and arms a new trailing...
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref state, now + CoalesceMs + 10));
            // ...and one after it raises on the leading edge again
            state = StructureChangedThrottle.Pack(Last(state), false);
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref state, now + 2 * CoalesceMs));
        }

        [Fact]
        public void TryConsumeTrailing_BeforeTheCooldownEnds_RearmsWithRemainingDelay_AndDoesNotRaiseEarly()
        {
            long now = 1_000_000L;
            long state = Fresh(now);
            Decide(ref state, now);
            Decide(ref state, now + 10); // arms

            var r = Consume(ref state, now + 100, out int delay);
            Assert.Equal(StructureChangedThrottle.TrailingResult.Rearm, r);
            Assert.Equal(CoalesceMs - 100, delay);
            Assert.True(Pending(state));   // still pending
            Assert.Equal(now, Last(state));    // not advanced: nothing was raised
        }

        [Fact]
        public void TryConsumeTrailing_AfterANewerRaiseRestartedTheCooldown_Rearms()
        {
            long now = 1_000_000L;
            long state = Fresh(now);
            Decide(ref state, now);
            Decide(ref state, now + 10); // arms, pending=1

            // Timer fires late; a leading raise arrives first and (correctly) clears the pending trailing.
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref state, now + CoalesceMs + 5));
            Assert.False(Pending(state));
            Assert.Equal(StructureChangedThrottle.TrailingResult.Nothing, Consume(ref state, now + CoalesceMs + 6, out _));

            // A signal in the new cooldown arms again; consuming inside it re-arms with the remaining time.
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref state, now + CoalesceMs + 20));
            Assert.Equal(StructureChangedThrottle.TrailingResult.Rearm, Consume(ref state, now + CoalesceMs + 30, out int delay));
            Assert.Equal(CoalesceMs - 25, delay);
        }

        [Fact]
        public void SteadyStream_RaisesAtMostOncePerCooldown_AndEndsWithAFinalTrailing()
        {
            long start = 1_000_000L;
            long state = Fresh(start);
            int raises = 0;
            long lastRaiseAt = long.MinValue;
            long armedDue = -1;

            // A signal every 10ms for 1s, with a simulated timer that fires when due.
            long t = start;
            for (; t <= start + 1000; t += 10)
            {
                if (armedDue >= 0 && t >= armedDue)
                {
                    var r = Consume(ref state, t, out int d);
                    if (r == StructureChangedThrottle.TrailingResult.Raise) { raises++; Assert.True(lastRaiseAt == long.MinValue || t - lastRaiseAt >= CoalesceMs); lastRaiseAt = t; armedDue = -1; }
                    else if (r == StructureChangedThrottle.TrailingResult.Rearm) armedDue = t + d;
                    else armedDue = -1;
                }
                var dec = Decide(ref state, t);
                if (dec == StructureChangedThrottle.Decision.Raise)
                {
                    raises++;
                    Assert.True(lastRaiseAt == long.MinValue || t - lastRaiseAt >= CoalesceMs);
                    lastRaiseAt = t;
                    armedDue = -1;
                }
                else if (dec == StructureChangedThrottle.Decision.SuppressedArmTrailing)
                    armedDue = t + StructureChangedThrottle.DelayUntilCooldownEndsMs(Last(state), t, CoalesceMs);
            }

            // Stream stops; let the timer fire.
            Assert.True(armedDue >= 0, "The last suppressed signals must have armed a final trailing.");
            var final = Consume(ref state, armedDue, out _);
            Assert.Equal(StructureChangedThrottle.TrailingResult.Raise, final);
            raises++;

            Assert.InRange(raises, 4, 6); // ~1000ms / 250ms, plus the guaranteed final
        }

        [Fact]
        public void Decide_ConcurrentSuppressedSignals_ArmExactlyOneTrailing()
        {
            long now = 1_000_000L;
            long state = Fresh(now);
            Decide(ref state, now);

            const int n = 8;
            int armed = 0, already = 0, raised = 0;
            var barrier = new Barrier(n);
            var threads = new Thread[n];
            long st = state;
            for (int i = 0; i < n; i++)
            {
                threads[i] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    switch (StructureChangedThrottle.Decide(ref st, now + 20, CoalesceMs, out _))
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
            long state = Fresh(now);
            Decide(ref state, now);
            Decide(ref state, now + 10);

            const int n = 8;
            int raises = 0;
            var barrier = new Barrier(n);
            var threads = new Thread[n];
            long st = state;
            for (int i = 0; i < n; i++)
            {
                threads[i] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    if (StructureChangedThrottle.TryConsumeTrailing(ref st, now + CoalesceMs, CoalesceMs, out _) == StructureChangedThrottle.TrailingResult.Raise)
                        Interlocked.Increment(ref raises);
                });
                threads[i].Start();
            }
            foreach (var t in threads) t.Join();

            Assert.Equal(1, raises);
            Assert.False(Pending(st));
            Assert.Equal(now + CoalesceMs, Last(st));
        }

        // ------------------------------------------------ lost-trailing race (review finding)

        [Fact]
        public void TryConsumeTrailing_LeadingRaiseAndNewSuppressedSignalBetweenSnapshotAndCommit_DoesNotLoseTheNewTrailing()
        {
            // The exact interleaving from the review comment. The timer (T1) snapshots
            // (old timestamp, pending), then - before it commits - a leading raise restarts the
            // cooldown and clears pending, and a NEW signal is suppressed and arms a fresh trailing
            // (T2). With the old two-field state T1 then cleared the NEW pending bit while its
            // timestamp CAS failed and returned Nothing; T2 then found nothing pending: lost.
            long t0 = 1_000_000L;
            long state = Fresh(t0);
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref state, t0));
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref state, t0 + 10)); // arms T1

            long timerFiresAt = t0 + CoalesceMs;          // T1's cooldown has ended
            long leadingAt = timerFiresAt + 1;
            long suppressedAt = timerFiresAt + 5;
            StructureChangedThrottle.Decision leading = default, suppressed = default;
            int armDelay = 0;
            int hookCalls = 0;

            var r = StructureChangedThrottle.TryConsumeTrailingWithHook(ref state, timerFiresAt, CoalesceMs, out int rearm, () =>
            {
                if (hookCalls++ != 0) return; // interleave once, after T1's first snapshot
                leading = StructureChangedThrottle.Decide(ref state, leadingAt, CoalesceMs, out _);
                suppressed = StructureChangedThrottle.Decide(ref state, suppressedAt, CoalesceMs, out armDelay); // arms T2
            });

            Assert.Equal(StructureChangedThrottle.Decision.Raise, leading);
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, suppressed);
            Assert.Equal(CoalesceMs - 4, armDelay);
            // T1 must not consume/clear the new signal's pending bit: it re-decides from fresh state.
            Assert.Equal(StructureChangedThrottle.TrailingResult.Rearm, r);
            Assert.True(Pending(state));
            Assert.Equal(leadingAt, Last(state));

            // T2 (armed for the end of the new cooldown) delivers the trailing exactly once.
            long t2 = leadingAt + CoalesceMs;
            Assert.Equal(StructureChangedThrottle.TrailingResult.Raise, Consume(ref state, t2, out _));
            Assert.Equal(StructureChangedThrottle.TrailingResult.Nothing, Consume(ref state, t2 + 1, out _));
            Assert.Equal(t2, Last(state));
        }

        [Fact]
        public void Decide_ArmDelay_IsTheRemainingCooldownOfTheSnapshotThatSetPending()
        {
            long now = 1_000_000L;
            long state = Fresh(now);
            StructureChangedThrottle.Decide(ref state, now, CoalesceMs, out _);
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, StructureChangedThrottle.Decide(ref state, now + 40, CoalesceMs, out int delay));
            Assert.Equal(CoalesceMs - 40, delay);
        }

        [Fact]
        public void PackedState_RoundTripsTimestampAndPending_IncludingNegativeSeeds()
        {
            foreach (long ts in new[] { 0L, 1L, -250L, 1_000_000L, (1L << 61), -(1L << 61) })
                foreach (bool p in new[] { false, true })
                {
                    long s = StructureChangedThrottle.Pack(ts, p);
                    Assert.Equal(ts, StructureChangedThrottle.LastRaised(s));
                    Assert.Equal(p, StructureChangedThrottle.IsPending(s));
                }
        }

        [Fact]
        public void Stress_ConcurrentSignalsAndConsumers_NeverLoseOrDuplicateATrailing()
        {
            // Real threads + a shared simulated clock (advanced by a ticker under its own lock).
            // Model of the hook: signals call Decide; SuppressedArmTrailing arms a "timer" (a due
            // time); consumers fire at their due time via TryConsumeTrailing and Rearm re-arms.
            // Checked: (1) once signalling stops and the clock passes the last cooldown, no pending
            // signal remains and the raise count for suppressed signals is exact (never lost);
            // (2) raises are never closer than one cooldown (never duplicated within a cooldown);
            // (3) after each suppressed signal there is a raise within its cooldown.
            for (int round = 0; round < 200; round++)
            {
                long clock = 1_000_000L;
                long state = Fresh(clock);
                long timerDue = -1;          // single simulated timer, like the entry's Timer
                var raiseTimes = new System.Collections.Generic.List<long>();
                var suppressedTimes = new System.Collections.Generic.List<long>();
                var gate = new object();
                int done = 0;

                void ArmTimer(int delay) { lock (gate) { long due = Interlocked.Read(ref clock) + delay; timerDue = due; /* like Timer.Change: last writer wins */ } }
                void Raised(long at) { lock (gate) raiseTimes.Add(at); }

                var signalers = new Thread[3];
                for (int i = 0; i < signalers.Length; i++)
                {
                    int seed = round * 31 + i;
                    signalers[i] = new Thread(() =>
                    {
                        var rnd = new System.Random(seed);
                        for (int k = 0; k < 150; k++)
                        {
                            long now = Interlocked.Read(ref clock);
                            switch (StructureChangedThrottle.Decide(ref state, now, CoalesceMs, out int d))
                            {
                                case StructureChangedThrottle.Decision.Raise: Raised(now); break;
                                case StructureChangedThrottle.Decision.SuppressedArmTrailing:
                                    lock (gate) suppressedTimes.Add(now);
                                    ArmTimer(d); break;
                                default: lock (gate) suppressedTimes.Add(now); break;
                            }
                            if (rnd.Next(4) == 0) Thread.Yield();
                        }
                        Interlocked.Increment(ref done);
                    });
                }

                var ticker = new Thread(() =>
                {
                    var rnd = new System.Random(round);
                    while (Volatile.Read(ref done) < signalers.Length)
                    {
                        Interlocked.Add(ref clock, rnd.Next(1, 40));
                        long due; lock (gate) due = timerDue;
                        long now = Interlocked.Read(ref clock);
                        if (due >= 0 && now >= due)
                        {
                            lock (gate) timerDue = -1;
                            var r = StructureChangedThrottle.TryConsumeTrailing(ref state, now, CoalesceMs, out int rd);
                            if (r == StructureChangedThrottle.TrailingResult.Raise) Raised(now);
                            else if (r == StructureChangedThrottle.TrailingResult.Rearm) ArmTimer(rd);
                        }
                        Thread.Yield();
                    }
                });

                foreach (var t in signalers) t.Start();
                ticker.Start();
                foreach (var t in signalers) t.Join();
                ticker.Join();

                // Drain: signalling stopped; keep firing the timer until quiescent.
                for (int guard = 0; guard < 10; guard++)
                {
                    long now = Interlocked.Add(ref clock, CoalesceMs + 1);
                    bool armed; lock (gate) { armed = timerDue >= 0; timerDue = -1; }
                    if (!Pending(Interlocked.Read(ref state))) break;
                    Assert.True(armed, "A pending trailing exists but no timer is armed: lost trailing (round " + round + ").");
                    var r = StructureChangedThrottle.TryConsumeTrailing(ref state, now, CoalesceMs, out int rd);
                    if (r == StructureChangedThrottle.TrailingResult.Raise) Raised(now);
                    else if (r == StructureChangedThrottle.TrailingResult.Rearm) ArmTimer(rd);
                }
                Assert.False(Pending(Interlocked.Read(ref state)));

                raiseTimes.Sort();
                foreach (long sup in suppressedTimes)
                    Assert.True(raiseTimes.Exists(rt => rt >= sup), "A suppressed signal was never followed by a raise (round " + round + ").");
                for (int i = 1; i < raiseTimes.Count; i++)
                    Assert.True(raiseTimes[i] - raiseTimes[i - 1] >= CoalesceMs,
                        "Two raises inside one cooldown (round " + round + ").");
            }
        }

        [Fact]
        public void Trailing_Logic_DoesNotOverflow_AtExtremeTickValues()
        {
            Assert.Equal(0, StructureChangedThrottle.DelayUntilCooldownEndsMs(long.MinValue, long.MaxValue, CoalesceMs));
            Assert.Equal(CoalesceMs, StructureChangedThrottle.DelayUntilCooldownEndsMs(long.MaxValue, long.MinValue, CoalesceMs));
            Assert.Equal(CoalesceMs - 1, StructureChangedThrottle.DelayUntilCooldownEndsMs(long.MaxValue - 1, long.MaxValue, CoalesceMs));

            long state = StructureChangedThrottle.Pack(long.MinValue >> 1, false);
            Assert.Equal(StructureChangedThrottle.Decision.Raise, Decide(ref state, long.MaxValue >> 1));

            state = StructureChangedThrottle.Pack((long.MaxValue >> 1) - 5, false);
            Assert.Equal(StructureChangedThrottle.Decision.SuppressedArmTrailing, Decide(ref state, long.MaxValue >> 1));
            Assert.Equal(StructureChangedThrottle.TrailingResult.Rearm, Consume(ref state, long.MaxValue >> 1, out int d));
            Assert.Equal(CoalesceMs - 5, d);
        }
    }
}
