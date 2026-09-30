using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>The UIA/Win32-independent pid to process-name cache the native sweep and the hook share.</summary>
    public class ProcessIdentityCacheTests
    {
        /// <summary>Scriptable lookups plus call counters and a manual clock.</summary>
        private sealed class Rig
        {
            public long Now;
            public readonly ConcurrentDictionary<int, ProcessIdentity?> Processes = new ConcurrentDictionary<int, ProcessIdentity?>();
            public int FullCalls;
            public int KeyCalls;
            public ProcessIdentityCache Cache;

            public Rig(int maxEntries = ProcessIdentityCache.DefaultMaxEntries)
            {
                Cache = new ProcessIdentityCache(
                    pid => { Interlocked.Increment(ref FullCalls); return Processes.TryGetValue(pid, out var p) ? p : null; },
                    pid => { Interlocked.Increment(ref KeyCalls); return Processes.TryGetValue(pid, out var p) && p.HasValue ? p.Value.CreationKey : (long?)null; },
                    () => Interlocked.Read(ref Now),
                    maxEntries);
            }

            public string Name(int pid) => Cache.TryGetName(pid, out string n) ? n : "<unknown>";
        }

        [Fact]
        public void HitWithinTtl_DoesNotCallEitherLookup()
        {
            var rig = new Rig();
            rig.Processes[10] = new ProcessIdentity("chrome", 111);

            Assert.Equal("chrome", rig.Name(10));
            rig.Now += ProcessIdentityCache.PositiveTtlMs - 1;
            Assert.Equal("chrome", rig.Name(10));

            Assert.Equal(1, rig.FullCalls);
            Assert.Equal(0, rig.KeyCalls);
        }

        [Fact]
        public void ExpiredEntry_RevalidatesWithTheKeyOnly_AndNeverRereadsTheName()
        {
            var rig = new Rig();
            rig.Processes[10] = new ProcessIdentity("chrome", 111);
            rig.Name(10);

            rig.Now += ProcessIdentityCache.PositiveTtlMs;
            Assert.Equal("chrome", rig.Name(10));

            Assert.Equal(1, rig.FullCalls);
            Assert.Equal(1, rig.KeyCalls);

            // The revalidation renewed the trust window: no further lookups until it lapses again.
            rig.Now += ProcessIdentityCache.PositiveTtlMs - 1;
            rig.Name(10);
            Assert.Equal(1, rig.KeyCalls);
            Assert.Equal(1, rig.FullCalls);
        }

        [Fact]
        public void ChangedCreationKey_PidReuse_ReresolvesTheName()
        {
            var rig = new Rig();
            rig.Processes[10] = new ProcessIdentity("chrome", 111);
            Assert.Equal("chrome", rig.Name(10));

            rig.Processes[10] = new ProcessIdentity("notepad", 222); // pid reused by another process
            rig.Now += ProcessIdentityCache.PositiveTtlMs;

            Assert.Equal("notepad", rig.Name(10));
            Assert.Equal(2, rig.FullCalls);
        }

        [Fact]
        public void ProcessGoneAtRevalidation_BecomesUnknownAndIsNegativeCached()
        {
            var rig = new Rig();
            rig.Processes[10] = new ProcessIdentity("chrome", 111);
            rig.Name(10);

            rig.Processes[10] = null;
            rig.Now += ProcessIdentityCache.PositiveTtlMs;

            Assert.False(rig.Cache.TryGetName(10, out string name));
            Assert.Equal(string.Empty, name);
        }

        [Fact]
        public void NullResult_IsNegativeCachedForItsTtl_ThenRetried()
        {
            var rig = new Rig();

            Assert.False(rig.Cache.TryGetName(10, out string name));
            Assert.Equal(string.Empty, name);
            rig.Now += ProcessIdentityCache.NegativeTtlMs - 1;
            Assert.False(rig.Cache.TryGetName(10, out _));
            Assert.Equal(1, rig.FullCalls);

            // The process appears (or access is now granted): picked up once the negative entry lapses.
            rig.Processes[10] = new ProcessIdentity("firefox", 5);
            rig.Now += 1;
            Assert.Equal("firefox", rig.Name(10));
            Assert.Equal(2, rig.FullCalls);
        }

        [Fact]
        public void UnknownKeyEntry_CannotBeCheaplyRevalidated_SoItReresolvesAfterTheTtl()
        {
            var rig = new Rig();
            rig.Processes[10] = new ProcessIdentity("system", ProcessIdentity.UnknownKey);
            rig.Name(10);

            rig.Now += ProcessIdentityCache.PositiveTtlMs;
            rig.Name(10);

            Assert.Equal(2, rig.FullCalls);
            Assert.Equal(0, rig.KeyCalls);
        }

        [Fact]
        public void ThrowingLookups_AreTreatedAsUnknown()
        {
            var cache = new ProcessIdentityCache(_ => throw new InvalidOperationException("x"), _ => throw new InvalidOperationException("y"));
            Assert.False(cache.TryGetName(10, out string name));
            Assert.Equal(string.Empty, name);
        }

        [Fact]
        public void InvalidPid_IsUnknownWithoutLookup()
        {
            var rig = new Rig();
            Assert.False(rig.Cache.TryGetName(0, out _));
            Assert.False(rig.Cache.TryGetName(-4, out _));
            Assert.Equal(0, rig.FullCalls);
            Assert.Equal(0, rig.Cache.Count);
        }

        [Fact]
        public void Size_IsBounded_AndRecentlyUsedEntriesSurvive()
        {
            var rig = new Rig(maxEntries: 4);
            for (int pid = 1; pid <= 4; pid++)
                rig.Processes[pid] = new ProcessIdentity("p" + pid, pid);
            for (int pid = 1; pid <= 4; pid++)
                rig.Name(pid);
            rig.Name(1); // pid 1 is now the most recently used; pid 2 the least

            rig.Processes[5] = new ProcessIdentity("p5", 5);
            rig.Name(5);

            Assert.Equal(4, rig.Cache.Count);
            int calls = rig.FullCalls;
            rig.Name(1); // survived: no lookup
            Assert.Equal(calls, rig.FullCalls);
            rig.Name(2); // evicted: looked up again
            Assert.Equal(calls + 1, rig.FullCalls);

            for (int pid = 100; pid < 200; pid++)
            {
                rig.Processes[pid] = new ProcessIdentity("q", pid);
                rig.Name(pid);
                Assert.True(rig.Cache.Count <= 4);
            }
        }

        [Fact]
        public void FullCache_DropsExpiredEntriesBeforeLiveOnes()
        {
            var rig = new Rig(maxEntries: 2);
            for (int pid = 1; pid <= 3; pid++)
                rig.Processes[pid] = new ProcessIdentity("p" + pid, pid);
            rig.Name(1);                        // t=0, valid until 5000
            rig.Now = 3000;
            rig.Name(2);                        // valid until 8000
            rig.Now = 3500;
            rig.Name(1);                        // pid 1 used last, so plain LRU would evict pid 2
            rig.Now = 5500;                     // pid 1 expired, pid 2 live

            rig.Name(3);                        // full: the expired pid 1 must go, not the live pid 2

            int calls = rig.FullCalls;
            rig.Name(2);
            Assert.Equal(calls, rig.FullCalls);
            Assert.Equal(2, rig.Cache.Count);
        }

        [Fact]
        public void EvictAndClear_ForgetEntries()
        {
            var rig = new Rig();
            rig.Processes[1] = new ProcessIdentity("a", 1);
            rig.Processes[2] = new ProcessIdentity("b", 2);
            rig.Name(1);
            rig.Name(2);

            rig.Cache.Evict(1);
            Assert.Equal(1, rig.Cache.Count);
            rig.Name(1);
            Assert.Equal(3, rig.FullCalls);

            rig.Cache.Clear();
            Assert.Equal(0, rig.Cache.Count);
            rig.Name(2);
            Assert.Equal(4, rig.FullCalls);
        }

        [Fact]
        public void ConcurrentAccess_IsSafe_AndResolvesEachPidABoundedNumberOfTimes()
        {
            const int threads = 16, pids = 50, rounds = 200;
            var rig = new Rig();
            for (int pid = 1; pid <= pids; pid++)
                rig.Processes[pid] = new ProcessIdentity("p" + pid, pid);
            var errors = new ConcurrentBag<string>();

            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
            {
                for (int r = 0; r < rounds; r++)
                    for (int pid = 1; pid <= pids; pid++)
                        if (rig.Name(pid) != "p" + pid)
                            errors.Add("wrong name for " + pid);
            });

            Assert.Empty(errors);
            // Cold-start races may duplicate a lookup, but never more than one per thread per pid, and never per round.
            Assert.InRange(rig.FullCalls, pids, pids * threads);
            Assert.Equal(pids, rig.Cache.Count);
            Assert.Equal(0, rig.KeyCalls); // the clock never moved: nothing expired
        }

        [Theory]
        [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome")]
        [InlineData(@"C:\Windows\SystemApps\msedge.EXE", "msedge")]
        [InlineData(@"D:\tools\my.app.exe", "my.app")]
        [InlineData(@"firefox.exe", "firefox")]
        [InlineData(@"C:\x\noext", "noext")]
        [InlineData(@"C:\x\script.bat", "script.bat")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void NameFromImagePath_MatchesProcessName(string path, string expected)
        {
            Assert.Equal(expected, ProcessNames.NameFromImagePath(path));
        }
    }
}
