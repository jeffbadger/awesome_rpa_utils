using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ResourceLockAutomation.Tests
{
    /// <summary>
    /// The Process scope. The lock table is shared by every instance in the process (and so by every test class running in parallel), so each test
    /// uses its own resource names. Expiry is exercised through ProcessLocks with leases of milliseconds; the public methods take whole seconds.
    /// </summary>
    public sealed class ProcessScopeTests
    {
        private static string NewResource() => "r-" + Guid.NewGuid().ToString("N");

        private static string Acquire(ResourceLockUtils c, string resource, string holder = "Robot 1")
        {
            Assert.True(c.TryAcquireLock(LockScope.Process, resource, holder, 60, out bool acquired, out string token, out string current, out string m), m);
            Assert.True(acquired);
            Assert.Null(current);
            Assert.Equal(32, token.Length);
            return token;
        }

        [Fact]
        public void ALock_HasOneHolder_TheOtherIsToldWho_AndAfterReleaseItIsFree()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            string token = Acquire(c, r, "MyServer_1");
            Assert.True(c.TryAcquireLock(LockScope.Process, r, "MyServer_2", 60, out bool acquired, out string other, out string current, out string m), m);
            Assert.Equal((false, (string)null, "MyServer_1"), (acquired, other, current));                // taken is an outcome, not a failure
            Assert.True(c.ReleaseLock(LockScope.Process, r, token, out bool released, out m), m);
            Assert.True(released);
            Acquire(c, r, "MyServer_2");
        }

        [Fact]
        public void ALockTakenOnOneThread_IsReleasedOnAnother()
        {
            // the problem with Robot Studio's Lock: only the acquiring thread can release it
            using var c = new ResourceLockUtils();
            string r = NewResource();
            string token = null;
            var taker = new Thread(() => token = Acquire(c, r));
            taker.Start();
            taker.Join();
            bool released = false;
            string message = null;
            var releaser = new Thread(() => c.ReleaseLock(LockScope.Process, r, token, out released, out message));
            releaser.Start();
            releaser.Join();
            Assert.True(released, message);
            Acquire(c, r);
        }

        [Fact]
        public void LocksAreSharedByEveryInstance_AndResourceNamesIgnoreCase()
        {
            using var first = new ResourceLockUtils();
            using var second = new ResourceLockUtils();
            string r = NewResource();
            string token = Acquire(first, r.ToUpperInvariant(), "Automation A");
            Assert.True(second.TryAcquireLock(LockScope.Process, r, "Automation B", 60, out bool acquired, out _, out string current, out string m), m);
            Assert.Equal((false, "Automation A"), (acquired, current));
            Assert.True(second.ReleaseLock(LockScope.Process, r, token, out bool released, out m), m);   // the token, not the instance, owns the lease
            Assert.True(released);
        }

        [Fact]
        public void OnlyOneOfManyContendingThreads_EverHoldsTheLock()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            int inside = 0, maxInside = 0, entries = 0;
            Parallel.For(0, 16, worker =>
            {
                for (int i = 0; i < 200; i++)
                {
                    Assert.True(c.TryAcquireLock(LockScope.Process, r, "t", 60, out bool acquired, out string token, out _, out string m), m);
                    if (!acquired) continue;
                    int now = Interlocked.Increment(ref inside);
                    InterlockedMax(ref maxInside, now);
                    Interlocked.Increment(ref entries);
                    Thread.SpinWait(50);
                    Interlocked.Decrement(ref inside);
                    Assert.True(c.ReleaseLock(LockScope.Process, r, token, out bool released, out m) && released, m);
                }
            });
            Assert.Equal(1, maxInside);
            Assert.True(entries > 0);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
        }

        [Fact]
        public void Slots_AllowCapacityHolders_AndReportTheCount()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            var tokens = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                Assert.True(c.TryAcquireSlot(LockScope.Process, r, 3, "Robot " + i, 60, out bool acquired, out string token, out int count, out string m), m);
                Assert.Equal((true, i), (acquired, count));
                tokens.Add(token);
            }
            Assert.True(c.TryAcquireSlot(LockScope.Process, r, 3, "Robot 4", 60, out bool full, out string none, out int holders, out string msg), msg);
            Assert.Equal((false, (string)null, 3), (full, none, holders));
            Assert.True(c.GetLockStatus(LockScope.Process, r, out bool held, out string names, out int expires, out int holderCount, out msg), msg);
            Assert.Equal((true, "Robot 1|Robot 2|Robot 3", 3), (held, names, holderCount));
            Assert.InRange(expires, 59, 60);
            Assert.True(c.ReleaseLock(LockScope.Process, r, tokens[1], out bool released, out msg) && released, msg);
            Assert.True(c.TryAcquireSlot(LockScope.Process, r, 3, "Robot 4", 60, out bool acquiredNow, out _, out holders, out msg), msg);
            Assert.Equal((true, 3), (acquiredNow, holders));
        }

        [Fact]
        public void AResource_IsEitherALockOrASlotPool_WithOneCapacity_WhileItIsHeld()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            Assert.True(c.TryAcquireSlot(LockScope.Process, r, 2, "a", 60, out _, out string token, out _, out string m), m);
            Assert.False(c.TryAcquireSlot(LockScope.Process, r, 3, "b", 60, out bool acquired, out string t, out int count, out m));
            Assert.Equal((false, (string)null, 0), (acquired, t, count));
            Assert.Equal("TryAcquireSlot failed: the resource is in use with a capacity of 2; every caller must give the same capacity.", m);
            Assert.False(c.TryAcquireLock(LockScope.Process, r, "b", 60, out _, out _, out _, out m));
            Assert.Contains("in use as a slot pool", m);
            Assert.True(c.ReleaseLock(LockScope.Process, r, token, out _, out m), m);
            Acquire(c, r);                                                                                   // free again: any use is fine
            Assert.False(c.TryAcquireSlot(LockScope.Process, r, 2, "b", 60, out _, out _, out _, out m));
            Assert.Contains("in use as a lock", m);
        }

        [Fact]
        public void AnAbandonedLease_Expires_AndItsHolderLearnsItLostIt()
        {
            object owner = new object();
            string r = NewResource();
            AcquireResult first = ProcessLocks.TryAcquire(r, LockKind.Lock, 1, "crashed robot", 500, owner);
            Assert.True(first.Acquired);
            Assert.False(ProcessLocks.TryAcquire(r, LockKind.Lock, 1, "next", 500, owner).Acquired);
            Thread.Sleep(700);
            AcquireResult second = ProcessLocks.TryAcquire(r, LockKind.Lock, 1, "next", 60000, owner);
            Assert.True(second.Acquired);
            Assert.False(ProcessLocks.Renew(r, first.Token, 60000, out long expires));                       // renewed False: stop using the resource
            Assert.Equal(0, expires);
            Assert.False(ProcessLocks.Release(r, first.Token));                                              // released False: the work may have overlapped
            Assert.True(ProcessLocks.Release(r, second.Token));
        }

        [Fact]
        public void RenewingInTime_KeepsTheLease()
        {
            object owner = new object();
            string r = NewResource();
            AcquireResult held = ProcessLocks.TryAcquire(r, LockKind.Lock, 1, "worker", 1500, owner);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 4; i++)
            {
                Thread.Sleep(500);
                Assert.True(ProcessLocks.Renew(r, held.Token, 1500, out long expires));
                Assert.Equal(1500, expires);
            }
            Assert.True(clock.ElapsedMilliseconds > 1500);                                                   // longer than one lease, still held
            Assert.False(ProcessLocks.TryAcquire(r, LockKind.Lock, 1, "other", 1500, owner).Acquired);
            Assert.True(ProcessLocks.Release(r, held.Token));
        }

        [Fact]
        public void RenewLock_ThroughThePublicMethod_ReportsTheNewLease()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            string token = Acquire(c, r);
            Assert.True(c.RenewLock(LockScope.Process, r, token, 600, out bool renewed, out int expiresInSeconds, out string m), m);
            Assert.Equal((true, 600), (renewed, expiresInSeconds));
            Assert.True(c.RenewLock(LockScope.Process, r, Guid.NewGuid().ToString("N"), 600, out renewed, out expiresInSeconds, out m), m);
            Assert.Equal((false, 0), (renewed, expiresInSeconds));                                           // not this lease: not renewed, not a failure
        }

        [Fact]
        public void AcquireLock_WaitsForARelease_AndWakesAtOnce()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            string token = Acquire(c, r, "first");
            var releaser = new Thread(() => { Thread.Sleep(200); c.ReleaseLock(LockScope.Process, r, token, out _, out _); });
            releaser.Start();
            var clock = Stopwatch.StartNew();
            Assert.True(c.AcquireLock(LockScope.Process, r, "second", 60, 10000, out bool acquired, out string mine, out string current, out string m), m);
            clock.Stop();
            releaser.Join();
            Assert.Equal((true, (string)null), (acquired, current));
            Assert.NotNull(mine);
            Assert.InRange(clock.ElapsedMilliseconds, 50, 5000);                                             // woken by the release, not by the 10 s wait
        }

        [Fact]
        public void AcquireLock_GivesUpWhenTheWaitEnds_AndSaysWhoHasIt()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            Acquire(c, r, "long job");
            var clock = Stopwatch.StartNew();
            Assert.True(c.AcquireLock(LockScope.Process, r, "second", 60, 300, out bool acquired, out string token, out string current, out string m), m);
            Assert.Equal((false, (string)null, "long job"), (acquired, token, current));
            Assert.InRange(clock.ElapsedMilliseconds, 250, 3000);
            Assert.True(c.AcquireLock(LockScope.Process, r, "second", 60, 0, out acquired, out _, out current, out m), m);   // 0: try once
            Assert.Equal((false, "long job"), (acquired, current));
        }

        [Fact]
        public void AWaiter_WakesWhenTheLeaseItWaitsOnExpires()
        {
            object owner = new object();
            string r = NewResource();
            Assert.True(ProcessLocks.TryAcquire(r, LockKind.Lock, 1, "hung robot", 200, owner).Acquired);
            var clock = Stopwatch.StartNew();
            AcquireResult waited = ProcessLocks.Acquire(r, LockKind.Lock, 1, "waiter", 60000, 10000, owner);
            Assert.True(waited.Acquired);
            Assert.InRange(clock.ElapsedMilliseconds, 0, 5000);                                              // woken by the expiry, not by the 10 s wait
            Assert.True(ProcessLocks.Release(r, waited.Token));
        }

        [Fact]
        public void AcquireSlot_WaitsForAFreeSlot()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            Assert.True(c.TryAcquireSlot(LockScope.Process, r, 1, "a", 60, out _, out string token, out _, out string m), m);
            var releaser = new Thread(() => { Thread.Sleep(150); c.ReleaseLock(LockScope.Process, r, token, out _, out _); });
            releaser.Start();
            Assert.True(c.AcquireSlot(LockScope.Process, r, 1, "b", 60, 10000, out bool acquired, out _, out int count, out m), m);
            releaser.Join();
            Assert.Equal((true, 1), (acquired, count));
        }

        [Fact]
        public void ForceRelease_FreesTheResource_AndEveryHolderLearnsItLostTheLease()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            Assert.True(c.TryAcquireSlot(LockScope.Process, r, 2, "a", 60, out _, out string first, out _, out string m), m);
            Assert.True(c.TryAcquireSlot(LockScope.Process, r, 2, "b", 60, out _, out string second, out _, out m), m);
            Assert.True(c.ForceReleaseLock(LockScope.Process, r, true, out int count, out m), m);
            Assert.Equal(2, count);
            Assert.True(c.RenewLock(LockScope.Process, r, first, 60, out bool renewed, out _, out m) && !renewed, m);
            Assert.True(c.ReleaseLock(LockScope.Process, r, second, out bool released, out m) && !released, m);
            Assert.True(c.ForceReleaseLock(LockScope.Process, r, true, out count, out m), m);
            Assert.Equal(0, count);                                                                          // nothing held: nothing to force
        }

        [Fact]
        public void Disposing_ReleasesThatInstancesLocks_AndOnlyThose()
        {
            string mine = NewResource(), theirs = NewResource();
            using var other = new ResourceLockUtils();
            string otherToken = Acquire(other, theirs);
            var c = new ResourceLockUtils();
            Acquire(c, mine);
            c.Dispose();
            Assert.True(other.GetLockStatus(LockScope.Process, mine, out bool held, out _, out _, out _, out string m), m);
            Assert.False(held);
            Assert.True(other.GetLockStatus(LockScope.Process, theirs, out held, out _, out _, out _, out m), m);
            Assert.True(held);
            Assert.True(other.ReleaseLock(LockScope.Process, theirs, otherToken, out _, out m), m);
        }

        [Fact]
        public void AnAcquireThatReachesTheTableAfterDisposal_IsRefused_AndLeavesNothingBehind()
        {
            // The interleaving from review: Ready() admitted the call, then Dispose ran before the call reached the lock table.
            var c = new ResourceLockUtils();
            string r = NewResource();
            c.Dispose();
            AcquireResult late = ProcessLocks.TryAcquire(r, LockKind.Lock, 1, "late", 60000, c);
            Assert.False(late.Acquired);
            Assert.Equal("the component has been disposed", late.Problem);
            Assert.Equal(0, ProcessLocks.HeldBy(c));
            using var other = new ResourceLockUtils();
            Acquire(other, r);                                                                               // nothing blocks the resource
        }

        [Fact]
        public void DisposingAComponent_EndsItsOwnWaits_AtOnce()
        {
            using var holder = new ResourceLockUtils();
            string r = NewResource();
            string token = Acquire(holder, r, "holder");
            var waiter = new ResourceLockUtils();
            bool result = true, acquired = true;
            string message = null;
            var clock = Stopwatch.StartNew();
            var thread = new Thread(() => result = waiter.AcquireLock(LockScope.Process, r, "waiter", 60, 20000, out acquired, out _, out _, out message));
            thread.Start();
            Thread.Sleep(200);                                                                               // the waiter is waiting
            waiter.Dispose();
            Assert.True(thread.Join(5000), "the wait did not end when its component was disposed");
            Assert.InRange(clock.ElapsedMilliseconds, 0, 10000);
            Assert.Equal((false, false), (result, acquired));
            Assert.Equal("AcquireLock failed: the component has been disposed.", message);
            Assert.Equal(0, ProcessLocks.HeldBy(waiter));
            Assert.True(holder.ReleaseLock(LockScope.Process, r, token, out bool released, out string m) && released, m);
        }

        [Fact]
        public void LeaseCounts_FollowEveryWayALeaseEnds()
        {
            object owner = new object();
            string a = NewResource(), b = NewResource(), s = NewResource();
            AcquireResult la = ProcessLocks.TryAcquire(a, LockKind.Lock, 1, "x", 60000, owner);
            ProcessLocks.TryAcquire(b, LockKind.Lock, 1, "x", 60000, owner);
            ProcessLocks.TryAcquire(s, LockKind.Slot, 3, "x", 60000, owner);
            ProcessLocks.TryAcquire(s, LockKind.Slot, 3, "x", 60000, owner);
            Assert.Equal(4, ProcessLocks.HeldBy(owner));
            Assert.True(ProcessLocks.Release(a, la.Token));
            Assert.Equal(3, ProcessLocks.HeldBy(owner));
            Assert.Equal(2, ProcessLocks.ForceRelease(s));
            Assert.Equal(1, ProcessLocks.HeldBy(owner));
            ProcessLocks.ReleaseAllOwnedBy(owner);
            Assert.Equal(0, ProcessLocks.HeldBy(owner));
        }

        [Fact]
        public void ExpiredLeasesThatAreNeverVisitedAgain_AreSwept_AndStopCountingAgainstTheLimit()
        {
            object owner = new object();
            string prefix = NewResource();
            for (int i = 0; i < LockLimits.MaxHeldPerInstance; i++)
                Assert.True(ProcessLocks.TryAcquire(prefix + "-" + i, LockKind.Lock, 1, "one-shot", 300, owner).Acquired);
            Assert.Equal(LockLimits.MaxHeldPerInstance, ProcessLocks.HeldBy(owner));
            Thread.Sleep(500);                                                                               // all expired, none visited
            int entriesBefore = ProcessLocks.EntryCount;
            AcquireResult next = ProcessLocks.TryAcquire(prefix + "-new", LockKind.Lock, 1, "one-shot", 60000, owner);
            Assert.True(next.Acquired, next.Problem);
            Assert.Equal(1, ProcessLocks.HeldBy(owner));
            Assert.True(ProcessLocks.EntryCount <= entriesBefore - LockLimits.MaxHeldPerInstance + 100);     // the stale entries are gone, not just uncounted (slack: other tests run in parallel)
            Assert.True(ProcessLocks.Release(prefix + "-new", next.Token));
        }

        [Fact]
        public void TheStatusOfAFreeResource_IsNotHeld()
        {
            using var c = new ResourceLockUtils();
            Assert.True(c.GetLockStatus(LockScope.Process, NewResource(), out bool held, out string holders, out int expires, out int count, out string m), m);
            Assert.Equal((false, (string)null, 0, 0), (held, holders, expires, count));
        }

        [Fact]
        public void TheLocksJson_ListsHoldersAndLeases_ButNeverTokens()
        {
            using var c = new ResourceLockUtils();
            string r = NewResource();
            string token = Acquire(c, r, "Robot — Bänk");
            Assert.True(c.GetLocksJson(LockScope.Process, out string json, out string m), m);
            Assert.DoesNotContain(token, json);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal("Process", doc.RootElement.GetProperty("scope").GetString());
            JsonElement entry = doc.RootElement.GetProperty("locks").EnumerateArray().Single(e => e.GetProperty("resource").GetString() == r);
            Assert.Equal(("Lock", 1), (entry.GetProperty("kind").GetString(), entry.GetProperty("capacity").GetInt32()));
            JsonElement holder = entry.GetProperty("holders").EnumerateArray().Single();
            Assert.Equal("Robot — Bänk", holder.GetProperty("holder").GetString());
            Assert.InRange(holder.GetProperty("expiresInSeconds").GetInt32(), 59, 60);
            Assert.Contains("Bänk", json);                                                                   // readable, not \u-escaped
        }

        [Fact]
        public void OneInstance_HoldsAtMost1000LocksAndSlots()
        {
            using var c = new ResourceLockUtils();
            string prefix = NewResource();
            for (int i = 0; i < LockLimits.MaxHeldPerInstance; i++)
                Assert.True(c.TryAcquireLock(LockScope.Process, prefix + "-" + i, "bulk", 60, out bool acquired, out _, out _, out string m) && acquired, m);
            Assert.False(c.TryAcquireLock(LockScope.Process, prefix + "-over", "bulk", 60, out _, out _, out _, out string msg));
            Assert.Equal("TryAcquireLock failed: this component already holds 1000 locks and slots; release some first.", msg);
            using var other = new ResourceLockUtils();
            Acquire(other, prefix + "-over");                                                                // the limit is per instance
        }

        [Fact]
        public void MessagesNeverContainTheResourceOrHolder()
        {
            using var c = new ResourceLockUtils();
            string r = "Customer-4711-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Assert.True(c.TryAcquireSlot(LockScope.Process, r, 2, "Secret-Holder", 60, out _, out _, out _, out string m), m);
            Assert.False(c.TryAcquireLock(LockScope.Process, r, "Secret-Holder", 60, out _, out _, out _, out m));
            Assert.False(c.TryAcquireSlot(LockScope.Process, r, 3, "Secret-Holder", 60, out _, out _, out _, out string m2));
            foreach (string text in new[] { m, m2 })
            {
                Assert.DoesNotContain("4711", text);
                Assert.DoesNotContain("Secret", text);
            }
            Assert.True(c.ForceReleaseLock(LockScope.Process, r, true, out _, out m), m);
        }
    }
}
